using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

public class PlayerSideIdentity : NetworkBehaviour
{
    public NetworkVariable<int> AssignedSide = new NetworkVariable<int>(
        0,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    public NetworkVariable<FixedString64Bytes> PlayerName = new NetworkVariable<FixedString64Bytes>(
        default,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Owner);

    public NetworkVariable<int> AvatarIndex = new NetworkVariable<int>(
        0,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Owner);

    // 1-4, used only by the 4-player team mode. Separate from AssignedSide (1-2, used only by 1v1) —
    // assigned by TriviaNetworkSync once 4 players are ready for team mode, not here at spawn time,
    // since we don't know yet whether this session intends 1v1 or team mode.
    public NetworkVariable<int> AssignedSlot = new NetworkVariable<int>(
        0,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    private static readonly int[] JoinOrderToSlot = { 1, 3, 2, 4 };

    public static int SlotForJoinIndex(int joinIndex) => JoinOrderToSlot[Mathf.Clamp(joinIndex, 0, 3)];

    public static int TeamForSlot(int slot) => (slot == 1 || slot == 2) ? 1 : 2;

    public override void OnNetworkSpawn()
    {
        if (IsServer)
            AssignedSide.Value = OwnerClientId == NetworkManager.ServerClientId ? 1 : 2;

        AssignedSide.OnValueChanged += (_, _) => TryPushIdentity();
        PlayerName.OnValueChanged += (_, _) => { TryPushIdentity(); TryPushTeamIdentity(); };
        AvatarIndex.OnValueChanged += (_, _) => { TryPushIdentity(); TryPushTeamIdentity(); };
        AssignedSlot.OnValueChanged += (_, _) => TryPushTeamIdentity();

        if (IsOwner)
        {
            if (AssignedSide.Value != 0)
                RegisterWithDuelManager(AssignedSide.Value);
            else
                AssignedSide.OnValueChanged += HandleAssignedSideChanged;

            PushLocalProfile();
        }

        TryPushIdentity();
        TryPushTeamIdentity();
    }

    // Re-call this whenever the local player changes their name/avatar on the Profile page —
    // OnNetworkSpawn only captures whatever was saved as of connection time, which is typically
    // before the player has had a chance to pick anything for this session.
    public void PushLocalProfile()
    {
        if (!IsOwner || PlayerProfileManager.Instance == null)
            return;

        PlayerName.Value = PlayerProfileManager.Instance.GetLocalName();
        AvatarIndex.Value = PlayerProfileManager.Instance.GetLocalAvatarIndex();
    }

    private void TryPushIdentity()
    {
        if (AssignedSide.Value == 0)
            return;

        string playerName = PlayerName.Value.ToString();

        if (string.IsNullOrEmpty(playerName))
            return;

        Sprite avatarSprite = PlayerProfileManager.Instance != null
            ? PlayerProfileManager.Instance.GetAvatarSprite(AvatarIndex.Value)
            : null;

        TriviaDuelManager.Instance?.ApplyNetworkedPlayerIdentity(AssignedSide.Value, playerName, avatarSprite);
    }

    private void TryPushTeamIdentity()
    {
        if (AssignedSlot.Value == 0)
            return;

        string playerName = PlayerName.Value.ToString();

        if (string.IsNullOrEmpty(playerName))
            return;

        Sprite avatarSprite = PlayerProfileManager.Instance != null
            ? PlayerProfileManager.Instance.GetAvatarSprite(AvatarIndex.Value)
            : null;

        TeamDuelManager.Instance?.ApplyNetworkedTeamPlayerIdentity(AssignedSlot.Value, playerName, avatarSprite);

        if (IsOwner)
            TeamDuelManager.Instance?.RegisterLocalSlot(AssignedSlot.Value);
    }

    public override void OnNetworkDespawn()
    {
        AssignedSide.OnValueChanged -= HandleAssignedSideChanged;
    }

    private void HandleAssignedSideChanged(int previousValue, int newValue)
    {
        AssignedSide.OnValueChanged -= HandleAssignedSideChanged;
        RegisterWithDuelManager(newValue);
    }

    private void RegisterWithDuelManager(int side)
    {
        if (TriviaDuelManager.Instance != null)
            TriviaDuelManager.Instance.RegisterLocalPlayerSide(side);
    }

    public void RequestSubmitAnswer(int answerIndex)
    {
        SubmitAnswerServerRpc(answerIndex);
    }

    [ServerRpc]
    private void SubmitAnswerServerRpc(int answerIndex, ServerRpcParams rpcParams = default)
    {
        if (TriviaDuelManager.Instance != null)
            TriviaDuelManager.Instance.SubmitAnswerFromNetwork(AssignedSide.Value, answerIndex);
    }

    public void RequestStartTrivia()
    {
        RequestStartTriviaServerRpc();
    }

    [ServerRpc(RequireOwnership = false)]
    private void RequestStartTriviaServerRpc(ServerRpcParams rpcParams = default)
    {
        TriviaNetworkSync.Instance?.MarkClientReady(rpcParams.Receive.SenderClientId);
    }

    public void RequestSubmitTeamAnswer(int answerIndex)
    {
        SubmitTeamAnswerServerRpc(answerIndex);
    }

    [ServerRpc]
    private void SubmitTeamAnswerServerRpc(int answerIndex, ServerRpcParams rpcParams = default)
    {
        if (TeamDuelManager.Instance != null)
            TeamDuelManager.Instance.SubmitAnswerFromNetwork(AssignedSlot.Value, answerIndex);
    }

    public void RequestStartTeamTrivia()
    {
        RequestStartTeamTriviaServerRpc();
    }

    [ServerRpc(RequireOwnership = false)]
    private void RequestStartTeamTriviaServerRpc(ServerRpcParams rpcParams = default)
    {
        TriviaNetworkSync.Instance?.MarkClientReadyForMode(rpcParams.Receive.SenderClientId, MatchMode.TeamFour);
    }
}
