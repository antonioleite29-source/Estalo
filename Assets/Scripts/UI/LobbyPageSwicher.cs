using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;

public class LobbyPageSwitcher : MonoBehaviour
{
    public enum LobbyPage
    {
        Profile,
        Learning,
        Lobby,
        More,
        Connect
    }

    [Header("--- START GAME ---")]
    [Tooltip("Drag the TriviaDuelManager GameObject here. This lets the Start Game button actually launch the trivia game.")]
    public TriviaDuelManager triviaManager;

    [Tooltip("Drag the TeamDuelManager GameObject here. Lets the Start Game button launch the 2v2 team mode when selected.")]
    public TeamDuelManager teamManager;

    [Tooltip("Optional but recommended: the waiting/queue screen shown between pressing Start and " +
             "the match actually forming. Without it, a queued player sees a blank screen.")]
    public WaitingScreenController waitingScreen;

    private MatchMode selectedMode = MatchMode.OneVsOne;

    [Tooltip("Optional: a highlight/checkmark object shown only while 1v1 is selected.")]
    public GameObject oneVsOneHighlight;

    [Tooltip("Optional: a highlight/checkmark object shown only while 2v2 is selected.")]
    public GameObject teamFourHighlight;

    [Header("--- MODE BUTTON COLORS ---")]
    [Tooltip("The 1v1 button's own Image component, so it can change color when selected.")]
    public Image oneVsOneButtonImage;

    [Tooltip("The 2v2 button's own Image component, so it can change color when selected.")]
    public Image teamFourButtonImage;

    [Tooltip("Color applied to whichever mode button is currently selected.")]
    public Color selectedModeColor = new Color32(34, 184, 207, 255);

    [Tooltip("Color applied to whichever mode button is NOT currently selected.")]
    public Color unselectedModeColor = Color.white;

    [Header("--- LOBBY PAGES ---")]
    [Tooltip("The GameObject for the Profile page. Drag it here.")]
    public GameObject profilePage;

    [Tooltip("The GameObject for the Learning page. Drag it here.")]
    public GameObject learningPage;

    [FormerlySerializedAs("triviaPage")]
    [Tooltip("The GameObject for the main Lobby page (where the Start button is). Drag it here.")]
    public GameObject lobbyPage;

    [Tooltip("The GameObject for the More / Settings page. Drag it here.")]
    public GameObject morePage;

    [Tooltip("The GameObject for the Connect page (Host / Join by address). Drag it here.")]
    public GameObject connectPage;

    [Space(5)]
    [Tooltip("Which page shows up first when the lobby opens.")]
    public LobbyPage defaultPage = LobbyPage.Lobby;

    [Tooltip("In a build, open the Connect page first regardless of Default Page — no mode can start " +
             "without a session. The Editor keeps using Default Page, since it auto-connects on Play.")]
    public bool forceConnectPageInBuild = true;

    private void Start()
    {
        ShowStartupPage();
        RefreshModeHighlight();
    }

    private void ShowStartupPage()
    {
        if (forceConnectPageInBuild && !Application.isEditor && connectPage != null)
        {
            ShowConnectPage();
            return;
        }

        ShowDefaultPage();
    }

    // ---------------------------------------------------------------
    // Call this from the Start Game button in the Inspector (onClick)
    // ---------------------------------------------------------------
    public void StartGame()
    {
        // Hide every lobby page so nothing bleeds through into the game screen
        SetPageActive(profilePage,  false);
        SetPageActive(learningPage, false);
        SetPageActive(lobbyPage,    false);
        SetPageActive(morePage,     false);
        SetPageActive(connectPage,  false);

        // Push the host's mode choice to the network before readying up, so all clients agree.
        // No-ops on non-host clients (SetSelectedMode early-returns unless called on the server).
        TriviaNetworkSync.Instance?.SetSelectedMode(selectedMode);

        // Then ready up for whatever the SERVER says the mode is, not this device's own toggle.
        // Each client keeps its own selectedMode field, so without this a client whose toggle
        // still said 1v1 would ready up for 1v1 while the host readied for 2v2 — and the ready
        // gate treats a mode change as a reset, so the two kept wiping each other out.
        MatchMode modeToStart = ResolveAuthoritativeMode();

        // Pressing Start now joins a queue rather than starting a match outright, so show the
        // waiting screen immediately. The match may need to wait for a suitable opponent.
        if (waitingScreen != null)
            waitingScreen.ShowWaiting();

        if (modeToStart == MatchMode.TeamFour)
        {
            if (teamManager != null)
                teamManager.StartTeamTriviaFromLobby();
            else
                Debug.LogWarning("LobbyPageSwitcher: No TeamDuelManager assigned! Drag it into the 'Team Manager' slot in the Inspector.");
        }
        else
        {
            if (triviaManager != null)
                triviaManager.StartTriviaFromLobby();
            else
                Debug.LogWarning("LobbyPageSwitcher: No TriviaDuelManager assigned! Drag it into the 'Trivia Manager' slot in the Inspector.");
        }
    }

    // ---------------------------------------------------------------
    // Mode selection — wire these to the 1v1 / 2v2 toggle buttons in the Inspector
    // ---------------------------------------------------------------
    public void SelectOneVsOneMode() { SelectMode(MatchMode.OneVsOne); }
    public void SelectTeamFourMode() { SelectMode(MatchMode.TeamFour); }

    private void SelectMode(MatchMode mode)
    {
        selectedMode = mode;

        // The host owns the choice for the whole room; pushing it here (rather than only when
        // Start is pressed) lets every client's highlight update as soon as the host picks.
        TriviaNetworkSync.Instance?.SetSelectedMode(mode);

        RefreshModeHighlight();
    }

    // The server's NetSelectedMode is the single source of truth once a session exists. Falls back
    // to this device's own toggle for local/offline play, where there is no server to ask.
    private MatchMode ResolveAuthoritativeMode()
    {
        TriviaNetworkSync sync = TriviaNetworkSync.Instance;

        if (sync != null && sync.IsSpawned)
            return (MatchMode)sync.NetSelectedMode.Value;

        return selectedMode;
    }

    private void RefreshModeHighlight()
    {
        // Show the room's actual mode, not this device's toggle — otherwise a client can sit
        // looking at a highlighted "1v1" while the host has already put everyone into 2v2.
        bool isOneVsOne = ResolveAuthoritativeMode() == MatchMode.OneVsOne;

        if (oneVsOneHighlight != null)
            oneVsOneHighlight.SetActive(isOneVsOne);

        if (teamFourHighlight != null)
            teamFourHighlight.SetActive(!isOneVsOne);

        if (oneVsOneButtonImage != null)
            oneVsOneButtonImage.color = isOneVsOne ? selectedModeColor : unselectedModeColor;

        if (teamFourButtonImage != null)
            teamFourButtonImage.color = isOneVsOne ? unselectedModeColor : selectedModeColor;
    }

    // ---------------------------------------------------------------
    // Page switching — wire these to your nav buttons in the Inspector
    // ---------------------------------------------------------------
    public void ShowDefaultPage()  { ShowPage(defaultPage); }
    public void ShowProfilePage()  { ShowPage(LobbyPage.Profile); }
    public void ShowLearningPage() { ShowPage(LobbyPage.Learning); }
    public void ShowLobbyPage()    { ShowPage(LobbyPage.Lobby); }
    public void ShowMorePage()     { ShowPage(LobbyPage.More); }
    public void ShowConnectPage()  { ShowPage(LobbyPage.Connect); }
    public void ShowMainPage()     { ShowLobbyPage(); }
    public void ShowTriviaPage()   { ShowLobbyPage(); }

    public void ShowPage(LobbyPage page)
    {
        SetPageActive(profilePage,  page == LobbyPage.Profile);
        SetPageActive(learningPage, page == LobbyPage.Learning);
        SetPageActive(lobbyPage,    page == LobbyPage.Lobby);
        SetPageActive(morePage,     page == LobbyPage.More);
        SetPageActive(connectPage,  page == LobbyPage.Connect);
    }

    private void SetPageActive(GameObject page, bool isActive)
    {
        if (page != null)
            page.SetActive(isActive);
    }
}
