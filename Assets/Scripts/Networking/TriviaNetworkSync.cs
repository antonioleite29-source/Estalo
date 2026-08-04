using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public class TriviaNetworkSync : NetworkBehaviour, IMatchRouter
{
    public static TriviaNetworkSync Instance { get; private set; }

    private readonly HashSet<ulong> readyClientIds = new HashSet<ulong>();

    // Server-only. NetworkManager.ConnectedClientsIds is not guaranteed to stay in arrival order
    // once anyone disconnects and reconnects, so team slots derived from its iteration order can
    // silently form the wrong pairs. This list is appended and removed explicitly instead.
    private readonly List<ulong> joinOrder = new List<ulong>();

    public NetworkVariable<int> NetSelectedMode = new NetworkVariable<int>((int)MatchMode.OneVsOne, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    public NetworkVariable<int> NetRoundState = new NetworkVariable<int>(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public NetworkVariable<int> NetCurrentDifficultyLevel = new NetworkVariable<int>(1, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public NetworkVariable<int> NetCurrentQuestionIndex = new NetworkVariable<int>(-1, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public NetworkVariable<int> NetCurrentDuelIndex = new NetworkVariable<int>(-1, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public NetworkVariable<int> NetTeam1Score = new NetworkVariable<int>(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public NetworkVariable<int> NetTeam2Score = new NetworkVariable<int>(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public NetworkVariable<float> NetSoloTimer = new NetworkVariable<float>(0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    // --- 4-player team mode state (kept fully separate from the 1v1 NetworkVariables above) ---
    public NetworkVariable<int> NetTeamRoundState = new NetworkVariable<int>(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public NetworkVariable<int> NetTeamDifficultyLevel = new NetworkVariable<int>(1, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public NetworkVariable<int> NetTeamQuestionIndex = new NetworkVariable<int>(-1, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public NetworkVariable<int> NetActiveSlotA = new NetworkVariable<int>(1, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public NetworkVariable<int> NetActiveSlotB = new NetworkVariable<int>(3, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public NetworkVariable<int> NetTeamScoreA = new NetworkVariable<int>(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public NetworkVariable<int> NetTeamScoreB = new NetworkVariable<int>(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public NetworkVariable<float> NetTeamSoloTimer = new NetworkVariable<float>(0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

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

    public override void OnNetworkSpawn()
    {
        Instance = this;

        if (IsServer)
        {
            // The host and any clients that beat this object to the spawn are already connected,
            // so seed from the current list before switching to event-driven tracking.
            joinOrder.Clear();

            foreach (ulong existingId in NetworkManager.ConnectedClientsIds)
                joinOrder.Add(existingId);

            NetworkManager.OnClientConnectedCallback += HandleServerClientConnected;
            NetworkManager.OnClientDisconnectCallback += HandleServerClientDisconnected;
            return;
        }

        NetRoundState.OnValueChanged += (_, newValue) => TriviaDuelManager.Instance?.ApplyNetworkedRoundState(newValue);
        NetCurrentQuestionIndex.OnValueChanged += (_, newValue) => TriviaDuelManager.Instance?.ApplyNetworkedQuestion(NetCurrentDifficultyLevel.Value, newValue);
        NetCurrentDuelIndex.OnValueChanged += (_, newValue) => TriviaDuelManager.Instance?.ApplyNetworkedDuelIndex(newValue);
        NetTeam1Score.OnValueChanged += (_, newValue) => TriviaDuelManager.Instance?.ApplyNetworkedScore(newValue, NetTeam2Score.Value);
        NetTeam2Score.OnValueChanged += (_, newValue) => TriviaDuelManager.Instance?.ApplyNetworkedScore(NetTeam1Score.Value, newValue);
        NetSoloTimer.OnValueChanged += (_, newValue) => TriviaDuelManager.Instance?.ApplySoloTimerVisual(newValue);

        // NetworkVariables only deliver OnValueChanged for changes *after* spawn — the values
        // already in place at spawn time (e.g. RoundState.OpenBuzz == 0, the variable's own
        // default) never fire a change event, so explicitly catch up to the current state here.
        TriviaDuelManager.Instance?.ApplyNetworkedRoundState(NetRoundState.Value);
        TriviaDuelManager.Instance?.ApplyNetworkedDuelIndex(NetCurrentDuelIndex.Value);
        TriviaDuelManager.Instance?.ApplyNetworkedQuestion(NetCurrentDifficultyLevel.Value, NetCurrentQuestionIndex.Value);
        TriviaDuelManager.Instance?.ApplyNetworkedScore(NetTeam1Score.Value, NetTeam2Score.Value);

        NetTeamRoundState.OnValueChanged += (_, newValue) => TeamDuelManager.Instance?.ApplyNetworkedRoundState(newValue);
        NetTeamQuestionIndex.OnValueChanged += (_, newValue) => TeamDuelManager.Instance?.ApplyNetworkedQuestion(NetTeamDifficultyLevel.Value, newValue);
        NetActiveSlotA.OnValueChanged += (_, newValue) => TeamDuelManager.Instance?.ApplyNetworkedActiveSlots(newValue, NetActiveSlotB.Value);
        NetActiveSlotB.OnValueChanged += (_, newValue) => TeamDuelManager.Instance?.ApplyNetworkedActiveSlots(NetActiveSlotA.Value, newValue);
        NetTeamScoreA.OnValueChanged += (_, newValue) => TeamDuelManager.Instance?.ApplyNetworkedScore(newValue, NetTeamScoreB.Value);
        NetTeamScoreB.OnValueChanged += (_, newValue) => TeamDuelManager.Instance?.ApplyNetworkedScore(NetTeamScoreA.Value, newValue);
        NetTeamSoloTimer.OnValueChanged += (_, newValue) => TeamDuelManager.Instance?.ApplySoloTimerVisual(newValue);

        TeamDuelManager.Instance?.ApplyNetworkedRoundState(NetTeamRoundState.Value);
        TeamDuelManager.Instance?.ApplyNetworkedActiveSlots(NetActiveSlotA.Value, NetActiveSlotB.Value);
        TeamDuelManager.Instance?.ApplyNetworkedQuestion(NetTeamDifficultyLevel.Value, NetTeamQuestionIndex.Value);
        TeamDuelManager.Instance?.ApplyNetworkedScore(NetTeamScoreA.Value, NetTeamScoreB.Value);
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

    public NetworkVariable<int> NetQueuedPlayers = new NetworkVariable<int>(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public NetworkVariable<int> NetPlayersNeeded = new NetworkVariable<int>(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public NetworkVariable<int> NetLiveMatches = new NetworkVariable<int>(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    // Group size for the mode currently being queued (2 for 1v1, 4 for 2v2), so the waiting screen
    // can show "3 / 4" without having to know the rules itself.
    public NetworkVariable<int> NetRequiredPlayers = new NetworkVariable<int>(2, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private void Update()
    {
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

        NetQueuedPlayers.Value = matchmaker.QueuedCount;
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
        MatchEndedTargetedClientRpc((int)match.Mode, message, hasWinner, winningTeam, OnlyFor(match));

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
        if ((MatchMode)mode == MatchMode.TeamFour)
        {
            if (wasCorrect) TeamDuelManager.Instance?.MarkAnswerRight(answerIndex);
            else TeamDuelManager.Instance?.MarkAnswerWrong(answerIndex);
            return;
        }

        if (wasCorrect) TriviaDuelManager.Instance?.MarkAnswerRight(answerIndex);
        else TriviaDuelManager.Instance?.MarkAnswerWrong(answerIndex);
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

    public void PublishState(int roundState, int difficultyLevel, int questionIndex, int duelIndex, int team1Score, int team2Score)
    {
        if (!IsServer)
            return;

        NetRoundState.Value = roundState;
        NetCurrentDifficultyLevel.Value = difficultyLevel;
        NetCurrentQuestionIndex.Value = questionIndex;
        NetCurrentDuelIndex.Value = duelIndex;
        NetTeam1Score.Value = team1Score;
        NetTeam2Score.Value = team2Score;
    }

    public void PublishSoloTimer(float remaining)
    {
        if (!IsServer)
            return;

        NetSoloTimer.Value = remaining;
    }

    public void PublishTeamState(int roundState, int difficultyLevel, int questionIndex, int activeSlotA, int activeSlotB, int teamAScore, int teamBScore)
    {
        if (!IsServer)
            return;

        NetTeamRoundState.Value = roundState;
        NetTeamDifficultyLevel.Value = difficultyLevel;
        NetTeamQuestionIndex.Value = questionIndex;
        NetActiveSlotA.Value = activeSlotA;
        NetActiveSlotB.Value = activeSlotB;
        NetTeamScoreA.Value = teamAScore;
        NetTeamScoreB.Value = teamBScore;
    }

    public void PublishTeamSoloTimer(float remaining)
    {
        if (!IsServer)
            return;

        NetTeamSoloTimer.Value = remaining;
    }

    public void BroadcastAnswerMarked(int answerIndex, bool wasCorrect)
    {
        if (!IsServer)
            return;

        if (wasCorrect)
            MarkAnswerRightClientRpc(answerIndex);
        else
            MarkAnswerWrongClientRpc(answerIndex);
    }

    public void BroadcastButtonsAvailable()
    {
        if (CanSendRpc)
            SetButtonsAvailableClientRpc();
    }

    public void BroadcastLockAllButtons()
    {
        if (CanSendRpc)
            LockAllButtonsClientRpc();
    }

    public void BroadcastMatchEnded(string message, bool hasWinner, int winnerSide)
    {
        if (CanSendRpc)
            NotifyMatchEndedClientRpc(message, hasWinner, winnerSide);
    }

    public void BroadcastMatchStarted()
    {
        if (CanSendRpc)
            MatchStartedClientRpc();
    }

    public void BroadcastTeamAnswerMarked(int answerIndex, bool wasCorrect)
    {
        if (!IsServer)
            return;

        if (wasCorrect)
            MarkTeamAnswerRightClientRpc(answerIndex);
        else
            MarkTeamAnswerWrongClientRpc(answerIndex);
    }

    public void BroadcastTeamButtonsAvailable()
    {
        if (CanSendRpc)
            SetTeamButtonsAvailableClientRpc();
    }

    public void BroadcastTeamLockAllButtons()
    {
        if (CanSendRpc)
            LockTeamButtonsClientRpc();
    }

    public void BroadcastTeamMatchEnded(string message, bool hasWinner, int winningTeam)
    {
        if (CanSendRpc)
            NotifyTeamMatchEndedClientRpc(message, hasWinner, winningTeam);
    }

    public void BroadcastTeamMatchStarted()
    {
        if (CanSendRpc)
            TeamMatchStartedClientRpc();
    }

    public void BroadcastMatchAborted(string message)
    {
        if (CanSendRpc)
            NotifyMatchAbortedClientRpc(message);
    }

    public void BroadcastTeamMatchAborted(string message)
    {
        if (CanSendRpc)
            NotifyTeamMatchAbortedClientRpc(message);
    }

    public void SetSelectedMode(MatchMode mode)
    {
        if (!IsServer)
            return;

        NetSelectedMode.Value = (int)mode;
    }

    // Any player may pick the mode, not just the host. NetSelectedMode is server-write, so a client
    // tapping 2v2 used to set only its own local field — ResolveAuthoritativeMode then read the
    // server's unchanged value and the highlight snapped back, making the button look broken.
    public void RequestSelectedMode(MatchMode mode)
    {
        if (IsServer)
        {
            NetSelectedMode.Value = (int)mode;
            return;
        }

        RequestSelectedModeServerRpc((int)mode);
    }

    [ServerRpc(RequireOwnership = false)]
    private void RequestSelectedModeServerRpc(int mode)
    {
        NetSelectedMode.Value = mode;
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

    [ClientRpc]
    private void MatchStartedClientRpc()
    {
        if (IsServer)
            return;

        TriviaDuelManager.Instance?.ApplyNetworkedMatchStarted();
    }

    [ClientRpc]
    private void MarkAnswerRightClientRpc(int answerIndex)
    {
        if (IsServer)
            return;

        TriviaDuelManager.Instance?.MarkAnswerRight(answerIndex);
    }

    [ClientRpc]
    private void MarkAnswerWrongClientRpc(int answerIndex)
    {
        if (IsServer)
            return;

        TriviaDuelManager.Instance?.MarkAnswerWrong(answerIndex);
    }

    [ClientRpc]
    private void SetButtonsAvailableClientRpc()
    {
        if (IsServer)
            return;

        TriviaDuelManager.Instance?.SetButtonsAvailableNormal();
    }

    [ClientRpc]
    private void LockAllButtonsClientRpc()
    {
        if (IsServer)
            return;

        TriviaDuelManager.Instance?.LockAllButtons();
    }

    [ClientRpc]
    private void NotifyMatchEndedClientRpc(string message, bool hasWinner, int winnerSide)
    {
        if (IsServer)
            return;

        TriviaDuelManager.Instance?.ApplyNetworkedMatchEnd(message, hasWinner, winnerSide);
    }

    [ClientRpc]
    private void NotifyMatchAbortedClientRpc(string message)
    {
        if (IsServer)
            return;

        TriviaDuelManager.Instance?.ApplyNetworkedMatchAborted(message);
    }

    [ClientRpc]
    private void NotifyTeamMatchAbortedClientRpc(string message)
    {
        if (IsServer)
            return;

        TeamDuelManager.Instance?.ApplyNetworkedMatchAborted(message);
    }

    [ClientRpc]
    private void TeamMatchStartedClientRpc()
    {
        if (IsServer)
            return;

        TeamDuelManager.Instance?.ApplyNetworkedMatchStarted();
    }

    [ClientRpc]
    private void MarkTeamAnswerRightClientRpc(int answerIndex)
    {
        if (IsServer)
            return;

        TeamDuelManager.Instance?.MarkAnswerRight(answerIndex);
    }

    [ClientRpc]
    private void MarkTeamAnswerWrongClientRpc(int answerIndex)
    {
        if (IsServer)
            return;

        TeamDuelManager.Instance?.MarkAnswerWrong(answerIndex);
    }

    [ClientRpc]
    private void SetTeamButtonsAvailableClientRpc()
    {
        if (IsServer)
            return;

        TeamDuelManager.Instance?.SetButtonsAvailableNormal();
    }

    [ClientRpc]
    private void LockTeamButtonsClientRpc()
    {
        if (IsServer)
            return;

        TeamDuelManager.Instance?.LockAllButtons();
    }

    [ClientRpc]
    private void NotifyTeamMatchEndedClientRpc(string message, bool hasWinner, int winningTeam)
    {
        if (IsServer)
            return;

        TeamDuelManager.Instance?.ApplyNetworkedMatchEnd(message, hasWinner, winningTeam);
    }
}
