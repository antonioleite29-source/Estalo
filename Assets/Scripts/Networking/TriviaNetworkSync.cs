using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

// The server's one network face: it runs the queue, owns every live MatchSession, and turns each
// match's decisions into RPCs addressed to that match's own players.
//
// Nothing about a running match travels by NetworkVariable. A NetworkVariable goes to everyone
// connected, which was fine when the server held a single match and is wrong now that it holds
// many -- two matches would overwrite each other's score and question on every client. The three
// that remain are lobby-wide by nature: how many people are queued, and how many games are on.
public class TriviaNetworkSync : NetworkBehaviour, IMatchRouter
{
    public static TriviaNetworkSync Instance { get; private set; }

    // Server-only. NetworkManager.ConnectedClientsIds is not guaranteed to stay in arrival order
    // once anyone disconnects and reconnects, so team slots derived from its iteration order can
    // silently form the wrong pairs. This list is appended and removed explicitly instead.
    private readonly List<ulong> joinOrder = new List<ulong>();

    private void Awake()
    {
        Instance = this;
    }

    // IsServer alone is not enough to decide whether an RPC can be sent. It stays true for a moment
    // after a shutdown, while IsListening has already gone false — so a match started locally right
    // after disconnecting would still try to broadcast and log "Rpc methods can only be invoked
    // after starting the NetworkManager!" for every message it sent.
    private bool CanSendRpc =>
        IsServer && IsSpawned &&
        NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;

    // Client-side state arrives entirely through the targeted RPCs below, addressed to one
    // match's participants. There is nothing for a client to subscribe to here: NetworkVariables
    // are broadcast to everyone, which is exactly wrong once several matches run side by side.
    public override void OnNetworkSpawn()
    {
        Instance = this;

        if (!IsServer)
            return;

        // The host and any clients that beat this object to the spawn are already connected,
        // so seed from the current list before switching to event-driven tracking.
        joinOrder.Clear();

        foreach (ulong existingId in NetworkManager.ConnectedClientsIds)
            joinOrder.Add(existingId);

        NetworkManager.OnClientConnectedCallback += HandleServerClientConnected;
        NetworkManager.OnClientDisconnectCallback += HandleServerClientDisconnected;
    }

    public override void OnNetworkDespawn()
    {
        if (!IsServer || NetworkManager == null)
            return;

        NetworkManager.OnClientConnectedCallback -= HandleServerClientConnected;
        NetworkManager.OnClientDisconnectCallback -= HandleServerClientDisconnected;
    }

    // ---------------------------------------------------------------
    // Concurrent matches: queue, live sessions, and per-match routing
    // ---------------------------------------------------------------

    private readonly Matchmaker matchmaker = new Matchmaker();
    private readonly List<MatchSession> liveMatches = new List<MatchSession>();

    // Broadcast to everyone so a waiting player can see the queue filling up in real time. One
    // count per mode, because each player queues for the mode they chose on their own device —
    // someone waiting for 2v2 should not see the 1v1 queue's numbers.
    public NetworkVariable<int> NetQueuedOneVsOne = new NetworkVariable<int>(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public NetworkVariable<int> NetQueuedTeamFour = new NetworkVariable<int>(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    public NetworkVariable<int> NetLiveMatches = new NetworkVariable<int>(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    // Seconds between server pulses. Frequent enough that a client notices a dead line within a
    // few of them, rare enough to be nothing on the wire -- an empty RPC to everyone connected.
    private const float PulseSeconds = 3f;

    private float nextPulse;

    private void Update()
    {
        if (IsServer)
            PulseIfDue();

        if (!IsServer || liveMatches.Count == 0)
            return;

        float deltaTime = Time.deltaTime;

        for (int i = liveMatches.Count - 1; i >= 0; i--)
        {
            liveMatches[i].Tick(deltaTime);

            if (liveMatches[i].IsFinished)
                liveMatches.RemoveAt(i);
        }

        NetLiveMatches.Value = liveMatches.Count;
    }

    // A heartbeat the client can time.
    //
    // UnityTransport has its own keepalive, but nothing above it ever finds out: a client whose
    // line has quietly died still reports IsListening true, so the reconnect loop -- which only
    // acts when IsListening is false -- sits and waits forever. That is the disconnection that
    // does not recover. This gives NetworkBootstrap something it can miss.
    private void PulseIfDue()
    {
        if (Time.unscaledTime < nextPulse || !CanSendRpc)
            return;

        nextPulse = Time.unscaledTime + PulseSeconds;
        PulseClientRpc();
    }

    [ClientRpc]
    private void PulseClientRpc()
    {
        if (NetworkBootstrap.Instance != null)
            NetworkBootstrap.Instance.NoteServerPulse();
    }

    // Called instead of the old single-match ready gate. Queues the player, then drains the queue
    // into as many complete matches as it can fill.
    public void EnqueueForMatch(ulong clientId, MatchMode requestedMode, int difficultyLevel)
    {
        if (!IsServer)
            return;

        // No queue-wiping on a mode change any more. The queue used to be single-mode, so one
        // player picking 2v2 emptied it and threw everyone already waiting back out. Modes are now
        // tracked per player, and both are drained below.
        matchmaker.Enqueue(clientId, difficultyLevel, requestedMode);
        DrainQueue();
        PublishQueueStatus();
    }

    public void LeaveQueue(ulong clientId)
    {
        if (!IsServer)
            return;

        matchmaker.Remove(clientId);
        PublishQueueStatus();
    }

    // Drains both modes: a 1v1 and a 2v2 can be forming at the same time from different players.
    private void DrainQueue()
    {
        DrainQueueFor(MatchMode.OneVsOne);
        DrainQueueFor(MatchMode.TeamFour);
    }

    private void DrainQueueFor(MatchMode mode)
    {
        List<List<ulong>> groups = matchmaker.FormMatches(mode);

        for (int g = 0; g < groups.Count; g++)
        {
            List<ulong> group = groups[g];
            int difficulty = AverageDifficultyOf(group);

            MatchSession match = new MatchSession(
                matchmaker.TakeNextMatchId(), mode, group, difficulty,
                this, ResolveQuestionSource(), CurrentRules(mode));

            AssignSeatsForMatch(match);
            liveMatches.Add(match);
            match.Begin();
        }

        if (groups.Count > 0)
            NetLiveMatches.Value = liveMatches.Count;
    }

    // Each participant's PlayerSideIdentity carries the seat, so the existing per-client UI
    // (which side am I, which slot am I) keeps working unchanged.
    private void AssignSeatsForMatch(MatchSession match)
    {
        foreach (ulong clientId in match.Participants)
        {
            PlayerSideIdentity identity = FindIdentity(clientId);

            if (identity == null)
                continue;

            int seat = match.GetSeat(clientId);

            // Tag first: MatchId is what tells each client whose name belongs on its screen, and
            // seat numbers restart per match, so an untagged seat change would show the wrong name.
            identity.MatchId.Value = match.MatchId;

            if (match.Mode == MatchMode.TeamFour)
                identity.AssignedSlot.Value = seat;
            else
                identity.AssignedSide.Value = seat;
        }
    }

    // Untag everyone when a match ends, so their identities go back to being lobby-visible
    // instead of staying bound to a match that no longer exists.
    private void ClearMatchTags(MatchSession match)
    {
        foreach (ulong clientId in match.Participants)
        {
            PlayerSideIdentity identity = FindIdentity(clientId);

            if (identity == null)
                continue;

            // Seats must clear too, not just the tag. A leftover seat is what let a player from a
            // previous match push its name into a slot of the next one before its real seat landed.
            identity.MatchId.Value = 0;
            identity.AssignedSide.Value = 0;
            identity.AssignedSlot.Value = 0;
        }
    }

    private int AverageDifficultyOf(IReadOnlyList<ulong> group)
    {
        int total = 0;
        int counted = 0;

        foreach (ulong clientId in group)
        {
            PlayerSideIdentity identity = FindIdentity(clientId);

            if (identity == null)
                continue;

            total += identity.DifficultyLevel.Value;
            counted++;
        }

        return counted == 0 ? 1 : Mathf.Clamp(Mathf.RoundToInt((float)total / counted), 1, 7);
    }

    private PlayerSideIdentity FindIdentity(ulong clientId)
    {
        if (!NetworkManager.ConnectedClients.TryGetValue(clientId, out NetworkClient client))
            return null;

        return client.PlayerObject != null ? client.PlayerObject.GetComponent<PlayerSideIdentity>() : null;
    }

    private IQuestionSource ResolveQuestionSource() => TriviaDuelManager.Instance;

    // Each mode has its own tunables in the Inspector — notably pointsToWin, which is 7 for 1v1
    // and 9 for 2v2. Reading both from TriviaDuelManager would have made every team match end two
    // points early.
    private MatchRules CurrentRules(MatchMode mode)
    {
        if (mode == MatchMode.TeamFour)
        {
            TeamDuelManager team = TeamDuelManager.Instance;

            if (team == null)
                return MatchRules.Default;

            return new MatchRules
            {
                pointsToWin = team.pointsToWin,
                soloTimeSeconds = team.soloTimeSeconds,
                wrongFlashSeconds = team.wrongFlashSeconds,
                correctResolveSeconds = team.correctResolveSeconds,
                nextRoundDelaySeconds = team.nextRoundDelaySeconds,
                inactivityEndSeconds = team.inactivityEndSeconds
            };
        }

        TriviaDuelManager manager = TriviaDuelManager.Instance;

        if (manager == null)
            return MatchRules.Default;

        return new MatchRules
        {
            pointsToWin = manager.pointsToWin,
            soloTimeSeconds = manager.soloTimeSeconds,
            wrongFlashSeconds = manager.wrongFlashSeconds,
            correctResolveSeconds = manager.correctResolveSeconds,
            nextRoundDelaySeconds = manager.nextRoundDelaySeconds,
            inactivityEndSeconds = manager.inactivityEndSeconds
        };
    }

    private void PublishQueueStatus()
    {
        if (!IsServer)
            return;

        NetQueuedOneVsOne.Value = matchmaker.QueuedCountFor(MatchMode.OneVsOne);
        NetQueuedTeamFour.Value = matchmaker.QueuedCountFor(MatchMode.TeamFour);
        NetLiveMatches.Value = liveMatches.Count;
    }

    public MatchSession FindMatchFor(ulong clientId)
    {
        for (int i = 0; i < liveMatches.Count; i++)
            if (liveMatches[i].Contains(clientId))
                return liveMatches[i];

        return null;
    }

    private ClientRpcParams OnlyFor(MatchSession match)
    {
        return new ClientRpcParams
        {
            Send = new ClientRpcSendParams
            {
                TargetClientIds = new List<ulong>(match.Participants).ToArray()
            }
        };
    }

    // --- IMatchRouter: every message goes only to the match's own participants ---
    // These deliberately do NOT skip the server. The host is usually a player too, and under the
    // old single-match design its manager was the authority AND the view; now MatchSession is the
    // sole authority and every client, host included, is purely a view that renders what arrives.

    public void MatchStarted(MatchSession match) =>
        MatchStartedTargetedClientRpc((int)match.Mode, OnlyFor(match));

    public void PublishState(MatchSession match) =>
        MatchStateTargetedClientRpc((int)match.Mode, match.RoundStateValue, match.DifficultyLevel,
            match.QuestionIndex, match.TeamAScore, match.TeamBScore,
            match.ActiveSlotA, match.ActiveSlotB, OnlyFor(match));

    public void PublishSoloTimer(MatchSession match, float remaining) =>
        MatchSoloTimerTargetedClientRpc((int)match.Mode, remaining, OnlyFor(match));

    public void ButtonsAvailable(MatchSession match) =>
        MatchButtonsAvailableTargetedClientRpc((int)match.Mode, OnlyFor(match));

    public void LockAllButtons(MatchSession match) =>
        MatchLockButtonsTargetedClientRpc((int)match.Mode, OnlyFor(match));

    public void AnswerMarked(MatchSession match, int answerIndex, bool wasCorrect, int answeringSeat) =>
        MatchAnswerMarkedTargetedClientRpc((int)match.Mode, answerIndex, wasCorrect, answeringSeat, OnlyFor(match));

    public void MatchEnded(MatchSession match, string message, bool hasWinner, int winningTeam) =>
        MatchEndedTargetedClientRpc((int)match.Mode, NameTheWinner(match, message, hasWinner, winningTeam),
            hasWinner, winningTeam, OnlyFor(match));

    // "Tom ganhou!" rather than "Jogador 1 venceu!".
    //
    // MatchSession decides the rules and knows nothing about who is playing -- deliberately, since
    // that is what lets several matches run at once. The router is the piece that can see both, so
    // putting the name in is its job rather than something MatchSession has to be handed.
    //
    // Falls back to whatever MatchSession wrote if a name cannot be found, so a missing profile
    // costs the flourish and never the announcement.
    private string NameTheWinner(MatchSession match, string message, bool hasWinner, int winningTeam)
    {
        if (!hasWinner)
            return message;

        List<string> winners = new List<string>();

        foreach (ulong clientId in match.Participants)
        {
            int seat = match.GetSeat(clientId);

            // 1v1 seats are the side number outright; team seats are 1-4, paired into two teams.
            int team = match.Mode == MatchMode.TeamFour ? PlayerSideIdentity.TeamForSlot(seat) : seat;

            if (team != winningTeam)
                continue;

            PlayerSideIdentity identity = FindIdentity(clientId);
            string name = identity != null ? identity.PlayerName.Value.ToString() : string.Empty;

            if (!string.IsNullOrWhiteSpace(name))
                winners.Add(name);
        }

        if (winners.Count == 0)
            return message;

        if (winners.Count == 1)
            return winners[0] + " ganhou!";

        // A whole team, so the verb agrees with them rather than with one of them.
        return string.Join(" e ", winners) + " ganharam!";
    }

    // Targeted at the single client who answered, not the whole match: each device logs only its
    // own mistakes.
    public void RecordOwnAnswer(MatchSession match, ulong clientId, int questionIndex, bool wasCorrect)
    {
        if (!CanSendRpc)
            return;

        ClientRpcParams onlySender = new ClientRpcParams
        {
            Send = new ClientRpcSendParams { TargetClientIds = new[] { clientId } }
        };

        RecordOwnAnswerTargetedClientRpc(match.DifficultyLevel, questionIndex, wasCorrect, onlySender);
    }

    [ClientRpc]
    private void RecordOwnAnswerTargetedClientRpc(int difficultyLevel, int questionIndex,
        bool wasCorrect, ClientRpcParams _ = default)
    {
        if (MistakeLogManager.Instance == null || TriviaDuelManager.Instance == null)
            return;

        TriviaQuestion question = TriviaDuelManager.Instance.GetQuestionAt(difficultyLevel, questionIndex);

        if (question != null)
            MistakeLogManager.Instance.RecordAnswer(question, difficultyLevel, wasCorrect);
    }

    [ClientRpc]
    private void MatchStartedTargetedClientRpc(int mode, ClientRpcParams _ = default)
    {
        if ((MatchMode)mode == MatchMode.TeamFour)
            TeamDuelManager.Instance?.ApplyNetworkedMatchStarted();
        else
            TriviaDuelManager.Instance?.ApplyNetworkedMatchStarted();
    }

    [ClientRpc]
    private void MatchStateTargetedClientRpc(int mode, int roundState, int difficulty, int questionIndex,
        int teamAScore, int teamBScore, int activeSlotA, int activeSlotB, ClientRpcParams _ = default)
    {
        if ((MatchMode)mode == MatchMode.TeamFour)
        {
            TeamDuelManager.Instance?.ApplyNetworkedRoundState(roundState);
            TeamDuelManager.Instance?.ApplyNetworkedActiveSlots(activeSlotA, activeSlotB);
            TeamDuelManager.Instance?.ApplyNetworkedQuestion(difficulty, questionIndex);
            TeamDuelManager.Instance?.ApplyNetworkedScore(teamAScore, teamBScore);
            return;
        }

        TriviaDuelManager.Instance?.ApplyNetworkedRoundState(roundState);
        TriviaDuelManager.Instance?.ApplyNetworkedQuestion(difficulty, questionIndex);
        TriviaDuelManager.Instance?.ApplyNetworkedScore(teamAScore, teamBScore);
    }

    [ClientRpc]
    private void MatchSoloTimerTargetedClientRpc(int mode, float remaining, ClientRpcParams _ = default)
    {
        if ((MatchMode)mode == MatchMode.TeamFour)
            TeamDuelManager.Instance?.ApplySoloTimerVisual(remaining);
        else
            TriviaDuelManager.Instance?.ApplySoloTimerVisual(remaining);
    }

    [ClientRpc]
    private void MatchButtonsAvailableTargetedClientRpc(int mode, ClientRpcParams _ = default)
    {
        if ((MatchMode)mode == MatchMode.TeamFour)
            TeamDuelManager.Instance?.SetButtonsAvailableNormal();
        else
            TriviaDuelManager.Instance?.SetButtonsAvailableNormal();
    }

    [ClientRpc]
    private void MatchLockButtonsTargetedClientRpc(int mode, ClientRpcParams _ = default)
    {
        if ((MatchMode)mode == MatchMode.TeamFour)
            TeamDuelManager.Instance?.LockAllButtons();
        else
            TriviaDuelManager.Instance?.LockAllButtons();
    }

    [ClientRpc]
    private void MatchAnswerMarkedTargetedClientRpc(int mode, int answerIndex, bool wasCorrect,
        int answeringSeat, ClientRpcParams _ = default)
    {
        // answeringSeat was arriving and being dropped, so every player heard the sound for
        // their OWN right or wrong answer whenever anybody in the match answered. The seat is the
        // only thing in the message that says whose result this is.
        if ((MatchMode)mode == MatchMode.TeamFour)
        {
            TeamDuelManager team = TeamDuelManager.Instance;

            if (team == null)
                return;

            bool mineTeam = answeringSeat == team.LocalAssignedSlot;

            if (wasCorrect) team.MarkAnswerRight(answerIndex, mineTeam);
            else team.MarkAnswerWrong(answerIndex, mineTeam);
            return;
        }

        TriviaDuelManager duel = TriviaDuelManager.Instance;

        if (duel == null)
            return;

        bool mine = answeringSeat == duel.LocalAssignedSide;

        if (wasCorrect) duel.MarkAnswerRight(answerIndex, mine);
        else duel.MarkAnswerWrong(answerIndex, mine);
    }

    [ClientRpc]
    private void MatchEndedTargetedClientRpc(int mode, string message, bool hasWinner, int winningTeam,
        ClientRpcParams _ = default)
    {
        if ((MatchMode)mode == MatchMode.TeamFour)
            TeamDuelManager.Instance?.ApplyNetworkedMatchEnd(message, hasWinner, winningTeam);
        else
            TriviaDuelManager.Instance?.ApplyNetworkedMatchEnd(message, hasWinner, winningTeam);
    }

    private void HandleServerClientConnected(ulong clientId)
    {
        if (!joinOrder.Contains(clientId))
            joinOrder.Add(clientId);
    }

    // Routed per match, so an answer only ever affects the match its sender is actually in.
    public void SubmitAnswerFromClient(ulong clientId, int answerIndex)
    {
        if (!IsServer)
            return;

        FindMatchFor(clientId)?.SubmitAnswer(clientId, answerIndex);
    }

    private void HandleServerClientDisconnected(ulong clientId)
    {
        joinOrder.Remove(clientId);
        matchmaker.Remove(clientId);

        MatchSession match = FindMatchFor(clientId);

        if (match != null)
        {
            ClearMatchTags(match);
            match.AbortForDisconnect(clientId);
            liveMatches.Remove(match);
        }

        PublishQueueStatus();
    }

    public void MarkClientReady(ulong clientId)
    {
        MarkClientReadyForMode(clientId, MatchMode.OneVsOne);
    }

    // Pressing Start no longer starts "the" match — it joins the queue. The matchmaker then forms
    // as many concurrent matches as the queue can fill, grouping players by difficulty level.
    public void MarkClientReadyForMode(ulong clientId, MatchMode requestedMode)
    {
        if (!IsServer)
            return;

        PlayerSideIdentity identity = FindIdentity(clientId);
        int difficultyLevel = identity != null ? identity.DifficultyLevel.Value : 1;

        EnqueueForMatch(clientId, requestedMode, difficultyLevel);

        // Kept (not a temp diagnostic): on a phone this one line via `adb logcat -s Unity` is the
        // only way to see why a match did or didn't form.
        Debug.Log("Matchmaking: client " + clientId + " joined the " + requestedMode + " queue at level " +
                  difficultyLevel + ". queued=" + matchmaker.QueuedCount +
                  ", needs " + matchmaker.PlayersStillNeeded(requestedMode) + " more, live matches=" +
                  liveMatches.Count);
    }
}
