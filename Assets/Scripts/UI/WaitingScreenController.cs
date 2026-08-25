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
    public string playersPrefix = "Jogadores: ";

    [Tooltip("Text placed before the live-games number. Clear this if you have a separate label object.")]
    public string liveGamesPrefix = "Partidas em andamento: ";

    [Tooltip("Separator between queued and required, e.g. the \" / \" in \"3 / 4\".")]
    public string playersSeparator = " / ";

    [Header("--- CONTROLS ---")]
    [Tooltip("Leaves the queue and returns to the lobby.")]
    public Button cancelButton;

    [Tooltip("Drag the LobbyPageSwitcher here so Cancel can return to the lobby.")]
    public LobbyPageSwitcher lobbyPageSwitcher;

    [Header("--- TIMEOUT ---")]
    [Tooltip("Give up and return to the lobby after this many seconds with no match. Sitting on a " +
             "waiting screen that never resolves is indistinguishable from the game having frozen, " +
             "which is the same reason this screen exists at all. Set to 0 to wait forever.")]
    public float giveUpAfterSeconds = 60f;

    [Header("--- BACKGROUND ---")]
    [Tooltip("Optional. The Estalo artwork is put behind the waiting room automatically; drag an " +
             "Image here only to place it on a specific object instead of a generated one.")]
    public Image backgroundImage;

    [Header("--- DEBUG ---")]
    [Tooltip("Logs when this screen is shown and hidden, and who hid it. Off by default; tick it if " +
             "the waiting screen ever misbehaves again.")]
    private bool isQueued;

    // When the current queue started, in unscaled time, so the wait is measured in real seconds.
    private float queuedSince;

    // Set the moment a match forms. From then on the numbers are frozen, because the queue they
    // came from has already been emptied.
    private bool matchFound;

    // What is currently on screen, so a redraw only happens when the numbers actually move.
    private string lastPlayersLine;
    private string lastLiveGamesLine;

    private void Awake()
    {
        ApplyBackground();

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

    // The same artwork as the launch screen, behind the queue. The waiting room is the one place
    // in the game that is literally loading something, so it is the one screen where a loading
    // image is telling the truth rather than decorating.
    private void ApplyBackground()
    {
        Sprite art = LoadingScreenController.Artwork;

        if (art == null)
            return;

        GameObject host = waitingRoot != null ? waitingRoot : gameObject;

        if (backgroundImage == null)
        {
            // Generated rather than required in the inspector, and pushed to the back of the
            // sibling order so it sits behind the queue numbers and the Cancel button instead of
            // covering them. Draw order inside a Canvas is sibling order, nothing else.
            GameObject made = new GameObject("WaitingBackground", typeof(RectTransform));
            made.transform.SetParent(host.transform, false);
            made.transform.SetAsFirstSibling();
            backgroundImage = made.AddComponent<Image>();
        }

        backgroundImage.sprite = art;
        backgroundImage.color = Color.white;
        backgroundImage.type = Image.Type.Simple;
        backgroundImage.preserveAspect = false;
        backgroundImage.raycastTarget = false;
        LoadingScreenController.FillScreen(backgroundImage.rectTransform);
    }

    // Called from LobbyPageSwitcher.StartGame() — pressing Start queues the player, it no longer
    // starts a match outright.
    public void ShowWaiting()
    {
        isQueued = true;
        matchFound = false;
        queuedSince = Time.unscaledTime;
        lastPlayersLine = null;
        lastLiveGamesLine = null;
        SetVisible(true);

        // Draw order inside a Canvas is sibling order, and this screen sits earlier than PageArea,
        // so anything left showing there covers it. Moving last on show means it cannot end up
        // behind a page — and it no longer depends on the Hierarchy keeping a particular order.
        transform.SetAsLastSibling();

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

        SetBottomBarVisible(!visible);
    }

    // The nav bar has nowhere useful to go from here — the player is queued, and the only way out
    // is Cancel, which is already on this screen. Leaving it up also lets a tap land on another
    // lobby page while still holding a seat in the queue.
    //
    // Driven from SetVisible so every exit restores it: Cancel, the timeout, and a match forming.
    // A match then hides it again on its own a moment later, which is harmless — one SetActive.
    private void SetBottomBarVisible(bool visible)
    {
        GameObject bar = TriviaDuelManager.Instance != null ? TriviaDuelManager.Instance.bottomBar : null;

        if (bar == null && TeamDuelManager.Instance != null)
            bar = TeamDuelManager.Instance.bottomBar;

        if (bar != null)
            bar.SetActive(visible);
    }

    // Called when the match forms, before the start animation plays.
    //
    // Forming a match takes the players OUT of the queue, so the live count is already back to zero
    // by the time anyone sees it — the screen would read "Players: 0 / 2" for the whole length of
    // the start animation, which looks like the match evaporated at the moment it was found. The
    // numbers are pinned full instead, and the timeout stops counting.
    public void FreezeOnMatchFound()
    {
        matchFound = true;

        if (playersText == null)
            return;

        MatchMode mode = lobbyPageSwitcher != null ? lobbyPageSwitcher.SelectedMode : MatchMode.OneVsOne;
        int required = Matchmaker.RequiredPlayersFor(mode);

        lastPlayersLine = playersPrefix + required + playersSeparator + required;
        playersText.text = lastPlayersLine;
    }

    private void Update()
    {
        if (!isQueued || matchFound)
            return;

        // Leaving the queue properly rather than just hiding the screen: the server is still
        // holding a slot for this player, and abandoning it silently is what leaves a queue
        // reporting people who are no longer there.
        if (giveUpAfterSeconds > 0f && Time.unscaledTime - queuedSince >= giveUpAfterSeconds)
        {
            Debug.Log($"WaitingScreen: no match after {giveUpAfterSeconds:0} seconds, returning to the lobby.", this);

            // Stop the countdown before the animation starts, or the timeout fires again on every
            // frame of it and stacks a transition per frame.
            matchFound = true;

            // Leaves by the same door a finished match does: the start animation, backwards, and
            // the lobby behind it. Falls back to leaving immediately if the manager is missing, so
            // a timeout can never strand the player on the waiting screen.
            if (TriviaDuelManager.Instance != null)
                TriviaDuelManager.Instance.PlayReturnToLobbyTransition(OnCancelClicked);
            else
                OnCancelClicked();

            return;
        }

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
