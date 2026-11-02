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
        {
            joinOrder.Add(existingId);
            AttachAccount(existingId);
        }

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
    public void EnqueueForMatch(ulong clientId, MatchMode requestedMode, int difficultyLevel,
        GameRuleSet ruleSet)
    {
        if (!IsServer)
            return;

        // No queue-wiping on a mode change any more. The queue used to be single-mode, so one
        // player picking 2v2 emptied it and threw everyone already waiting back out. Modes are now
        // tracked per player, and both are drained below.
        matchmaker.Enqueue(clientId, difficultyLevel, requestedMode, ruleSet);
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
    // Every mode against every rule set: a 1v1 Classico, a 1v1 Sem perdao and a 2v2 Relampago can
    // all be forming at the same time, and nobody crosses from one to another.
    private void DrainQueue()
    {
        foreach (GameRuleSet ruleSet in GameRuleSets.All)
        {
            DrainQueueFor(MatchMode.OneVsOne, ruleSet);
            DrainQueueFor(MatchMode.TeamFour, ruleSet);
        }
    }

    private void DrainQueueFor(MatchMode mode, GameRuleSet ruleSet)
    {
        List<List<ulong>> groups = matchmaker.FormMatches(mode, ruleSet);

        for (int g = 0; g < groups.Count; g++)
        {
            List<ulong> group = groups[g];
            int difficulty = AverageDifficultyOf(group);

            MatchSession match = new MatchSession(
                matchmaker.TakeNextMatchId(), mode, group, difficulty,
                this, ResolveQuestionSource(), GameRuleSets.Apply(ruleSet, CurrentRules(mode), mode));

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

        foreach (ulong clientId in group)
            total += LevelFor(clientId);

        return group.Count == 0 ? 1 : Mathf.Clamp(Mathf.RoundToInt((float)total / group.Count), 1, 7);
    }

    // From the server's own copy of the player's IQ. The level the phone reports is only a fallback
    // for a client with no account, which should not happen once connection approval has run.
    private int LevelFor(ulong clientId)
    {
        PlayerAccounts.Account account = PlayerAccounts.For(clientId);

        if (account != null)
            return PlayerIQManager.LevelForIQ(account.iq);

        PlayerSideIdentity identity = FindIdentity(clientId);
        return identity != null ? identity.DifficultyLevel.Value : 1;
    }

    // ---------------------------------------------------------------
    // Accounts: the server's copy of every player's IQ and record
    // ---------------------------------------------------------------

    private void AttachAccount(ulong clientId)
    {
        PlayerAccounts.Account account = PlayerAccounts.Attach(clientId);

        // The same account opened on a second device: the one that was already playing is sent away.
        // The newcomer is the one the player is holding, so it keeps the account.
        if (PlayerAccounts.TryFindOtherDevice(account, clientId, out ulong other))
            SendAway(other);

        SendAccount(clientId);

        // A match may be sitting paused waiting for exactly this account to come back.
        if (account != null && IsAnyMatchWaitingFor(account.id))
            StartCoroutine(SeatBackIntoHeldMatch(clientId, account.id));
    }

    private bool IsAnyMatchWaitingFor(string accountId)
    {
        for (int i = 0; i < liveMatches.Count; i++)
            if (liveMatches[i].IsWaitingForAPlayer)
                return true;

        return false;
    }

    // Waits for this client's player object to spawn before giving the seat back: the seat is held
    // on PlayerSideIdentity, and on a reconnect the identity lands a moment after the connection.
    private System.Collections.IEnumerator SeatBackIntoHeldMatch(ulong clientId, string accountId)
    {
        for (int attempt = 0; attempt < 40 && FindIdentity(clientId) == null; attempt++)
            yield return new WaitForSecondsRealtime(0.1f);

        if (!IsServer || FindIdentity(clientId) == null)
            yield break;

        for (int i = 0; i < liveMatches.Count; i++)
        {
            MatchSession match = liveMatches[i];

            if (!match.TryReturn(accountId, clientId))
                continue;

            AssignSeatsForMatch(match);
            match.ResumeAfterReturn();
            Debug.Log($"TriviaNetworkSync: client {clientId} came back and sat down again in match {match.MatchId}.");
            yield break;
        }
    }

    // Tells a device its account has been opened somewhere else, then drops it. The message goes
    // first, because a disconnect on its own looks like the Wi-Fi.
    private void SendAway(ulong clientId)
    {
        if (CanSendRpc)
            AccountOpenedElsewhereClientRpc(OnlyTo(clientId));

        PlayerAccounts.Detach(clientId);

        // A beat for the message to leave before the socket closes under it.
        StartCoroutine(DisconnectShortly(clientId));
    }

    private System.Collections.IEnumerator DisconnectShortly(ulong clientId)
    {
        yield return new WaitForSecondsRealtime(0.4f);

        if (IsServer && NetworkManager.Singleton != null &&
            NetworkManager.Singleton.ConnectedClients.ContainsKey(clientId))
        {
            NetworkManager.Singleton.DisconnectClient(clientId);
        }
    }

    [ClientRpc]
    private void AccountOpenedElsewhereClientRpc(ClientRpcParams _ = default)
    {
        PlayerStats.NoteAccountOpenedElsewhere();
    }

    // Every participant's record changes here, on the server, from the result the server itself
    // decided. Nothing a phone says about who won is involved.
    private void RecordResults(MatchSession match, int winningTeam)
    {
        foreach (ulong clientId in match.Participants)
        {
            int seat = match.GetSeat(clientId);
            int team = match.Mode == MatchMode.TeamFour ? PlayerSideIdentity.TeamForSlot(seat) : seat;

            PlayerSideIdentity identity = FindIdentity(clientId);
            string playerName = identity != null ? identity.PlayerName.Value.ToString() : null;

            PlayerAccounts.RecordMatch(clientId, team == winningTeam, playerName);
        }

        PlayerAccounts.Save();

        foreach (ulong clientId in match.Participants)
            SendAccount(clientId);
    }

    // pushProfile is true only just after a login: the account's own name and face go down to the
    // phone, because the player signed in to get their account back and that includes who they are.
    // Every other time the phone owns them and they come up, which is what keeps a rename working.
    private void SendAccount(ulong clientId, bool pushProfile = false)
    {
        PlayerAccounts.Account account = PlayerAccounts.For(clientId);

        if (account == null || !CanSendRpc)
            return;

        PlayerSideIdentity identity = FindIdentity(clientId);

        if (!pushProfile)
        {
            // Kept current for the leaderboard file; saved with the next change that saves.
            string playerName = identity != null ? identity.PlayerName.Value.ToString() : string.Empty;

            if (!string.IsNullOrWhiteSpace(playerName))
                account.name = playerName;

            if (identity != null && identity.AvatarIndex.Value >= 0)
                account.avatarIndex = identity.AvatarIndex.Value;
        }

        (int rank, int size) = PlayerAccounts.RankOf(account);

        AccountTargetedClientRpc(PlayerAccounts.TakeUnsentKey(clientId), account.iq, account.bestIQ,
            account.played, account.won, account.lost, account.firstSeen, account.claimed, rank, size,
            account.username ?? string.Empty,
            pushProfile ? account.name ?? string.Empty : string.Empty,
            pushProfile ? account.avatarIndex : -1,
            OnlyTo(clientId));
    }

    private static ClientRpcParams OnlyTo(ulong clientId) =>
        new ClientRpcParams { Send = new ClientRpcSendParams { TargetClientIds = new[] { clientId } } };

    [ClientRpc]
    private void AccountTargetedClientRpc(string key, float iq, float bestIQ, int played, int won, int lost,
        long firstSeen, bool claimed, int rank, int size, string username, string accountName, int avatarIndex,
        ClientRpcParams _ = default)
    {
        PlayerStats.ApplyServerAccount(key, iq, bestIQ, played, won, lost, firstSeen, claimed, rank, size,
            username, accountName, avatarIndex);
    }

    // A Unity login, checked against Unity's own keys before it is tied to anything. The player id is
    // taken from inside the verified token, never from the phone.
    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void LinkLoginServerRpc(string accessToken, string username, RpcParams rpcParams = default)
    {
        StartCoroutine(VerifyAndLink(rpcParams.Receive.SenderClientId, accessToken, username));
    }

    private System.Collections.IEnumerator VerifyAndLink(ulong clientId, string accessToken, string username)
    {
        string unityPlayerId = null;
        yield return UnityLoginVerifier.Verify(accessToken, id => unityPlayerId = id);

        // Left while the check was running.
        if (PlayerAccounts.For(clientId) == null || !CanSendRpc)
            yield break;

        bool accepted = !string.IsNullOrEmpty(unityPlayerId);

        if (accepted)
        {
            PlayerAccounts.LinkLogin(clientId, unityPlayerId, username);
            PlayerAccounts.Save();

            // Signing in here takes the account off whichever device had it open.
            if (PlayerAccounts.TryFindOtherDevice(PlayerAccounts.For(clientId), clientId, out ulong other))
                SendAway(other);

            // The whole point of signing in on a second phone: the record, the name and the face that
            // belong to the account come back down to it.
            SendAccount(clientId, pushProfile: true);
        }

        LoginResultTargetedClientRpc(accepted, OnlyTo(clientId));
    }

    [ClientRpc]
    private void LoginResultTargetedClientRpc(bool accepted, ClientRpcParams _ = default)
    {
        PlayerStats.ApplyLoginResult(accepted);
    }

    // Both of these act on the sender's own account and nobody else's: the account is found from who
    // sent the message, never from anything inside it.
    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void RequestAccountServerRpc(RpcParams rpcParams = default)
    {
        SendAccount(rpcParams.Receive.SenderClientId);
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void ClaimStartingRecordServerRpc(float iq, int played, int won, long firstPlayed,
        RpcParams rpcParams = default)
    {
        ulong sender = rpcParams.Receive.SenderClientId;

        PlayerAccounts.Claim(sender, iq, played, won, firstPlayed);
        SendAccount(sender);
    }

    // The hint the player wrote for their own password, kept with their account so it is there on a
    // new phone -- which is the only place it is any use.
    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void SetPasswordHintServerRpc(string hint, RpcParams rpcParams = default)
    {
        PlayerAccounts.SetHint(rpcParams.Receive.SenderClientId, hint);
    }

    // Asked for by username, before signing in, because a player who remembered their password would
    // not be asking. Only the hint goes back, never anything else about the account.
    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void RequestPasswordHintServerRpc(string username, RpcParams rpcParams = default)
    {
        if (!CanSendRpc)
            return;

        PasswordHintTargetedClientRpc(username ?? string.Empty,
            PlayerAccounts.HintForUsername(username), OnlyTo(rpcParams.Receive.SenderClientId));
    }

    [ClientRpc]
    private void PasswordHintTargetedClientRpc(string username, string hint, ClientRpcParams _ = default)
    {
        PlayerStats.ApplyPasswordHint(username, hint);
    }

    // Deleting the sender's own account, and nobody else's. Whose account it is comes from who sent
    // the message; there is nothing in it to point at anyone.
    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void DeleteAccountServerRpc(RpcParams rpcParams = default)
    {
        ulong sender = rpcParams.Receive.SenderClientId;

        if (!PlayerAccounts.Delete(sender) || !CanSendRpc)
            return;

        AccountDeletedClientRpc(OnlyTo(sender));

        // The key that phone holds now points at nothing, so the line is dropped: reconnecting with
        // what it has left opens a fresh account rather than the deleted one.
        StartCoroutine(DisconnectShortly(sender));
    }

    [ClientRpc]
    private void AccountDeletedClientRpc(ClientRpcParams _ = default)
    {
        PlayerStats.ForgetEverything();
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

    public void MatchEnded(MatchSession match, string message, bool hasWinner, int winningTeam)
    {
        MatchEndedTargetedClientRpc((int)match.Mode, NameTheWinner(match, message, hasWinner, winningTeam),
            hasWinner, winningTeam, OnlyFor(match));

        if (hasWinner)
            RecordResults(match, winningTeam);
    }

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

    public void MatchHeld(MatchSession match, bool held, int secondsLeft)
    {
        if (CanSendRpc)
            MatchHeldTargetedClientRpc((int)match.Mode, held, secondsLeft, OnlyFor(match));
    }

    [ClientRpc]
    private void MatchHeldTargetedClientRpc(int mode, bool held, int secondsLeft, ClientRpcParams _ = default)
    {
        string message = held
            ? $"Um jogador caiu. Esperando {secondsLeft}s..."
            : "Voltou! Continuando...";

        if ((MatchMode)mode == MatchMode.TeamFour)
            TeamDuelManager.Instance?.ApplyNetworkedMatchHeld(held, message);
        else
            TriviaDuelManager.Instance?.ApplyNetworkedMatchHeld(held, message);
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

        AttachAccount(clientId);
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
            // A phone that drops mid-match gets its seat kept for MatchSession.WaitForReturnSeconds,
            // because losing signal for a moment is the most ordinary thing that happens on a phone
            // and it used to end the match for everybody. Only when the seat cannot be kept, or
            // nobody comes back in time, does the match end.
            PlayerAccounts.Account account = PlayerAccounts.For(clientId);

            if (!match.HoldForMissing(clientId, account != null ? account.id : null))
            {
                ClearMatchTags(match);
                match.AbortForDisconnect(clientId);
                liveMatches.Remove(match);
            }
        }

        PlayerAccounts.Detach(clientId);
        PublishQueueStatus();
    }

    public void MarkClientReady(ulong clientId, GameRuleSet ruleSet)
    {
        MarkClientReadyForMode(clientId, MatchMode.OneVsOne, ruleSet);
    }

    // Pressing Start no longer starts "the" match — it joins the queue. The matchmaker then forms
    // as many concurrent matches as the queue can fill, grouping players by difficulty level.
    public void MarkClientReadyForMode(ulong clientId, MatchMode requestedMode, GameRuleSet ruleSet)
    {
        if (!IsServer)
            return;

        PlayerSideIdentity identity = FindIdentity(clientId);
        int difficultyLevel = identity != null ? identity.DifficultyLevel.Value : 1;

        EnqueueForMatch(clientId, requestedMode, difficultyLevel, ruleSet);

        // Kept (not a temp diagnostic): on a phone this one line via `adb logcat -s Unity` is the
        // only way to see why a match did or didn't form.
        Debug.Log("Matchmaking: client " + clientId + " joined the " + requestedMode + " " +
                  GameRuleSets.NameOf(ruleSet) + " queue at level " + difficultyLevel +
                  ". queued=" + matchmaker.QueuedCount +
                  ", needs " + matchmaker.PlayersStillNeeded(requestedMode, ruleSet) + " more, live matches=" +
                  liveMatches.Count);
    }
}
