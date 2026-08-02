using System.Collections.Generic;
using Unity.Netcode;

public class TriviaNetworkSync : NetworkBehaviour
{
    public static TriviaNetworkSync Instance { get; private set; }

    private readonly HashSet<ulong> readyClientIds = new HashSet<ulong>();
    private MatchMode pendingMode = MatchMode.OneVsOne;

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

    public override void OnNetworkSpawn()
    {
        Instance = this;

        if (IsServer)
            return;

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
        if (IsServer)
            SetButtonsAvailableClientRpc();
    }

    public void BroadcastLockAllButtons()
    {
        if (IsServer)
            LockAllButtonsClientRpc();
    }

    public void BroadcastMatchEnded(string message, bool hasWinner, int winnerSide)
    {
        if (IsServer)
            NotifyMatchEndedClientRpc(message, hasWinner, winnerSide);
    }

    public void BroadcastMatchStarted()
    {
        if (IsServer)
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
        if (IsServer)
            SetTeamButtonsAvailableClientRpc();
    }

    public void BroadcastTeamLockAllButtons()
    {
        if (IsServer)
            LockTeamButtonsClientRpc();
    }

    public void BroadcastTeamMatchEnded(string message, bool hasWinner, int winningTeam)
    {
        if (IsServer)
            NotifyTeamMatchEndedClientRpc(message, hasWinner, winningTeam);
    }

    public void BroadcastTeamMatchStarted()
    {
        if (IsServer)
            TeamMatchStartedClientRpc();
    }

    public void SetSelectedMode(MatchMode mode)
    {
        if (!IsServer)
            return;

        NetSelectedMode.Value = (int)mode;
    }

    public void MarkClientReady(ulong clientId)
    {
        MarkClientReadyForMode(clientId, MatchMode.OneVsOne);
    }

    public void MarkClientReadyForMode(ulong clientId, MatchMode requestedMode)
    {
        if (!IsServer)
            return;

        // A stale ready-up for a different mode than what's currently being gated must not
        // accidentally complete this gate — reset and start tracking the new mode instead.
        if (requestedMode != pendingMode)
        {
            pendingMode = requestedMode;
            readyClientIds.Clear();
        }

        readyClientIds.Add(clientId);
        int requiredCount = requestedMode == MatchMode.TeamFour ? 4 : 2;
        int connectedCount = NetworkManager.ConnectedClientsIds.Count;

        if (connectedCount >= requiredCount && readyClientIds.Count >= requiredCount)
        {
            readyClientIds.Clear();

            if (requestedMode == MatchMode.TeamFour)
            {
                AssignTeamSlotsInJoinOrder();
                TeamDuelManager.Instance?.BeginMatchAuthoritative();
            }
            else
            {
                TriviaDuelManager.Instance?.BeginMatchAuthoritative();
            }
        }
    }

    private void AssignTeamSlotsInJoinOrder()
    {
        int joinIndex = 0;

        foreach (ulong clientId in NetworkManager.ConnectedClientsIds)
        {
            NetworkClient client = NetworkManager.ConnectedClients[clientId];
            PlayerSideIdentity identity = client.PlayerObject != null ? client.PlayerObject.GetComponent<PlayerSideIdentity>() : null;

            if (identity != null)
                identity.AssignedSlot.Value = PlayerSideIdentity.SlotForJoinIndex(joinIndex);

            joinIndex++;
        }
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
