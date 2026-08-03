using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

// Shown from the moment a player presses Start until their match is formed. Matchmaking groups
// people by skill level, so a player can wait a little for a suitable opponent — without this
// screen that wait is indistinguishable from the game having frozen.
public class WaitingScreenController : MonoBehaviour
{
    [Header("--- ROOT ---")]
    [Tooltip("The panel to show while queued. Leave empty to show/hide this GameObject itself.")]
    public GameObject waitingRoot;

    [Header("--- TEXT ---")]
    [Tooltip("How many players are queued out of the group size, e.g. \"Players: 3 / 4\".")]
    public TMP_Text playersText;

    [Tooltip("How many matches are already under way, e.g. \"Live games: 2\".")]
    public TMP_Text liveGamesText;

    [Header("--- LABELS ---")]
    [Tooltip("Text placed before the player numbers. Clear this if you have a separate label object.")]
    public string playersPrefix = "Players: ";

    [Tooltip("Text placed before the live-games number. Clear this if you have a separate label object.")]
    public string liveGamesPrefix = "Live games: ";

    [Tooltip("Separator between queued and required, e.g. the \" / \" in \"3 / 4\".")]
    public string playersSeparator = " / ";

    [Header("--- CONTROLS ---")]
    [Tooltip("Leaves the queue and returns to the lobby.")]
    public Button cancelButton;

    [Tooltip("Drag the LobbyPageSwitcher here so Cancel can return to the lobby.")]
    public LobbyPageSwitcher lobbyPageSwitcher;

    private bool isQueued;

    private void Awake()
    {
        if (cancelButton != null)
            cancelButton.onClick.AddListener(OnCancelClicked);

        SetVisible(false);
    }

    // Called from LobbyPageSwitcher.StartGame() — pressing Start queues the player, it no longer
    // starts a match outright.
    public void ShowWaiting()
    {
        isQueued = true;
        SetVisible(true);
        Refresh();
    }

    // Called when the match actually forms, or when the player cancels out of the queue.
    public void HideWaiting()
    {
        isQueued = false;
        SetVisible(false);
    }

    private void SetVisible(bool visible)
    {
        if (waitingRoot != null)
            waitingRoot.SetActive(visible);
        else
            gameObject.SetActive(visible);
    }

    private void Update()
    {
        if (isQueued)
            Refresh();
    }

    private void Refresh()
    {
        TriviaNetworkSync sync = TriviaNetworkSync.Instance;

        if (sync == null || !sync.IsSpawned)
            return;

        if (playersText != null)
        {
            playersText.text = playersPrefix
                             + sync.NetQueuedPlayers.Value
                             + playersSeparator
                             + sync.NetRequiredPlayers.Value;
        }

        if (liveGamesText != null)
            liveGamesText.text = liveGamesPrefix + sync.NetLiveMatches.Value;
    }

    private void OnCancelClicked()
    {
        NetworkObject playerObject = NetworkManager.Singleton != null && NetworkManager.Singleton.LocalClient != null
            ? NetworkManager.Singleton.LocalClient.PlayerObject
            : null;

        playerObject?.GetComponent<PlayerSideIdentity>()?.RequestLeaveQueue();

        HideWaiting();

        if (lobbyPageSwitcher != null)
            lobbyPageSwitcher.ShowLobbyPage();
    }
}
