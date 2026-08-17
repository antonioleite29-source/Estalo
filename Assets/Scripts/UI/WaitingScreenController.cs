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

    [Header("--- DEBUG ---")]
    [Tooltip("Logs when this screen is shown and hidden, and who hid it. Off by default; tick it if " +
             "the waiting screen ever misbehaves again.")]
    public bool logHideCalls;

    private bool isQueued;

    // What is currently on screen, so a redraw only happens when the numbers actually move.
    private string lastPlayersLine;
    private string lastLiveGamesLine;

    private void Awake()
    {
        if (cancelButton != null)
            cancelButton.onClick.AddListener(OnCancelClicked);

        // Only hide when nobody has asked for this screen yet.
        //
        // This object starts inactive, so Awake has not run by the time the player presses Start.
        // SetActive(true) inside ShowWaiting runs Awake synchronously, before ShowWaiting has even
        // returned — so hiding unconditionally here switched the screen straight back off, and it
        // only ever appeared on the second press, once Awake could no longer fire.
        if (!isQueued)
            SetVisible(false);
    }

    // Called from LobbyPageSwitcher.StartGame() — pressing Start queues the player, it no longer
    // starts a match outright.
    public void ShowWaiting()
    {
        isQueued = true;
        lastPlayersLine = null;
        lastLiveGamesLine = null;
        SetVisible(true);

        // Draw order inside a Canvas is sibling order, and this screen sits earlier than PageArea,
        // so anything left showing there covers it. Moving last on show means it cannot end up
        // behind a page — and it no longer depends on the Hierarchy keeping a particular order.
        transform.SetAsLastSibling();

        if (logHideCalls)
        {
            GameObject shown = waitingRoot != null ? waitingRoot : gameObject;
            Debug.Log($"WaitingScreen: ShowWaiting() ran. '{shown.name}' activeSelf={shown.activeSelf} " +
                      $"activeInHierarchy={shown.activeInHierarchy} parent='{(transform.parent != null ? transform.parent.name : "none")}' " +
                      $"parentActive={(transform.parent == null || transform.parent.gameObject.activeInHierarchy)}", this);
        }

        Refresh();
    }

    // Called when the match actually forms, or when the player cancels out of the queue.
    public void HideWaiting()
    {
        // Temporary: the screen has been disappearing while a player is still queued, and this is
        // the only method that takes it down, so the stack trace on this line names whoever did it.
        if (logHideCalls && isQueued)
            Debug.Log("WaitingScreen: HideWaiting() called while still queued.", this);

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

        // Assigning text rebuilds the canvas even when the string is identical, and this runs every
        // frame while queued — on a single canvas holding the whole game that was a full rebuild of
        // 133 renderers per frame to redraw a number that changes once every few seconds.
        if (playersText != null)
        {
            // The queue for the mode THIS player picked. There is one queue per mode, so a player
            // waiting for 2v2 must not be shown how many people are lined up for 1v1.
            MatchMode mode = lobbyPageSwitcher != null
                ? lobbyPageSwitcher.SelectedMode
                : MatchMode.OneVsOne;

            int queued = mode == MatchMode.TeamFour
                ? sync.NetQueuedTeamFour.Value
                : sync.NetQueuedOneVsOne.Value;

            string line = playersPrefix + queued + playersSeparator + Matchmaker.RequiredPlayersFor(mode);

            if (line != lastPlayersLine)
            {
                lastPlayersLine = line;
                playersText.text = line;
            }
        }

        if (liveGamesText != null)
        {
            string line = liveGamesPrefix + sync.NetLiveMatches.Value;

            if (line != lastLiveGamesLine)
            {
                lastLiveGamesLine = line;
                liveGamesText.text = line;
            }
        }
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
