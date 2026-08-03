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

    // This device's own difficulty level (1-7, from PlayerIQManager). Owner-written so the server
    // can group players of similar skill — previously the server used its OWN local IQ for
    // everyone, so the host's level silently decided the difficulty for the whole match.
    public NetworkVariable<int> DifficultyLevel = new NetworkVariable<int>(
        1,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Owner);

    // Which match this player belongs to, 0 when not in one. Needed because every client holds a
    // PlayerSideIdentity for *every* connected player, and side/slot numbers restart per match —
    // match 1 and match 2 both have a "side 1". Without this tag, all of them push their name into
    // the same two UI slots and the last one to arrive wins.
    public NetworkVariable<int> MatchId = new NetworkVariable<int>(
        0,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

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
        // Deliberately no side at spawn. Under the old single-match design the host took side 1 and
        // everyone else side 2, which is wrong the moment two matches run at once: a match made of
        // two non-host players would have both of them carrying a stale side 2, and whichever
        // pushed last owned both name slots. Seats now come only from AssignSeatsForMatch, and 0
        // means "not seated yet" — TryPushIdentity refuses to push until a real seat arrives.
        AssignedSide.OnValueChanged += (_, _) => TryPushIdentity();
        PlayerName.OnValueChanged += (_, _) => { TryPushIdentity(); TryPushTeamIdentity(); };
        AvatarIndex.OnValueChanged += (_, _) => { TryPushIdentity(); TryPushTeamIdentity(); };
        AssignedSlot.OnValueChanged += (_, _) => TryPushTeamIdentity();

        // MatchId decides whether *any* identity belongs on this screen, and the local player's own
        // MatchId may arrive after or before everyone else's. Rather than depend on arrival order,
        // re-evaluate every identity on this client whenever any of them changes match.
        MatchId.OnValueChanged += (_, _) => RefreshAllIdentities();

        if (IsOwner)
        {
            // Stays subscribed for the whole session: seats reset to 0 between matches, so the
            // owner has to re-register its side every time a new match seats it.
            AssignedSide.OnValueChanged += HandleAssignedSideChanged;

            if (AssignedSide.Value != 0)
                RegisterWithDuelManager(AssignedSide.Value);

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

        if (PlayerIQManager.Instance != null)
            DifficultyLevel.Value = PlayerIQManager.Instance.GetLocalDifficultyLevel();
    }

    private static void RefreshAllIdentities()
    {
        PlayerSideIdentity[] all = FindObjectsByType<PlayerSideIdentity>(FindObjectsInactive.Include, FindObjectsSortMode.None);

        for (int i = 0; i < all.Length; i++)
        {
            all[i].TryPushIdentity();
            all[i].TryPushTeamIdentity();
        }
    }

    // True when this player is in the same match as the player sitting at this device — the only
    // case where their name and avatar belong on this screen.
    private bool SharesMatchWithLocalPlayer()
    {
        if (NetworkManager.Singleton == null || NetworkManager.Singleton.LocalClient == null)
            return true;

        NetworkObject localPlayerObject = NetworkManager.Singleton.LocalClient.PlayerObject;
        PlayerSideIdentity localIdentity = localPlayerObject != null
            ? localPlayerObject.GetComponent<PlayerSideIdentity>()
            : null;

        // Before any match is formed both sides read 0, so identities still show in the lobby.
        if (localIdentity == null)
            return true;

        return MatchId.Value == localIdentity.MatchId.Value;
    }

    private void TryPushIdentity()
    {
        if (AssignedSide.Value == 0)
            return;

        if (!SharesMatchWithLocalPlayer())
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

        if (!SharesMatchWithLocalPlayer())
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
        if (newValue != 0)
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
        // Routed by client id rather than by side: with several matches running at once, "side 1"
        // is only meaningful inside one particular match.
        TriviaNetworkSync.Instance?.SubmitAnswerFromClient(OwnerClientId, answerIndex);
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

    public void RequestLeaveQueue()
    {
        LeaveQueueServerRpc();
    }

    [ServerRpc(RequireOwnership = false)]
    private void LeaveQueueServerRpc(ServerRpcParams rpcParams = default)
    {
        TriviaNetworkSync.Instance?.LeaveQueue(rpcParams.Receive.SenderClientId);
    }

    public void RequestSubmitTeamAnswer(int answerIndex)
    {
        SubmitTeamAnswerServerRpc(answerIndex);
    }

    [ServerRpc]
    private void SubmitTeamAnswerServerRpc(int answerIndex, ServerRpcParams rpcParams = default)
    {
        TriviaNetworkSync.Instance?.SubmitAnswerFromClient(OwnerClientId, answerIndex);
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
