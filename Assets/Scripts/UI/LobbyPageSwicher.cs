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
        More
    }

    [Header("--- START GAME ---")]
    [Tooltip("Drag the TriviaDuelManager GameObject here. This lets the Start Game button actually launch the trivia game.")]
    public TriviaDuelManager triviaManager;

    [Tooltip("Drag the TeamDuelManager GameObject here. Lets the Start Game button launch the 2v2 team mode when selected.")]
    public TeamDuelManager teamManager;

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

    [Space(5)]
    [Tooltip("Which page shows up first when the lobby opens.")]
    public LobbyPage defaultPage = LobbyPage.Lobby;

    private void Start()
    {
        ShowDefaultPage();
        RefreshModeHighlight();
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

        // Push the host's mode choice to the network before readying up, so all clients agree.
        // No-ops on non-host clients (SetSelectedMode early-returns unless called on the server).
        TriviaNetworkSync.Instance?.SetSelectedMode(selectedMode);

        if (selectedMode == MatchMode.TeamFour)
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
    public void SelectOneVsOneMode() { selectedMode = MatchMode.OneVsOne; RefreshModeHighlight(); }
    public void SelectTeamFourMode() { selectedMode = MatchMode.TeamFour; RefreshModeHighlight(); }

    private void RefreshModeHighlight()
    {
        bool isOneVsOne = selectedMode == MatchMode.OneVsOne;

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
    public void ShowMainPage()     { ShowLobbyPage(); }
    public void ShowTriviaPage()   { ShowLobbyPage(); }

    public void ShowPage(LobbyPage page)
    {
        SetPageActive(profilePage,  page == LobbyPage.Profile);
        SetPageActive(learningPage, page == LobbyPage.Learning);
        SetPageActive(lobbyPage,    page == LobbyPage.Lobby);
        SetPageActive(morePage,     page == LobbyPage.More);
    }

    private void SetPageActive(GameObject page, bool isActive)
    {
        if (page != null)
            page.SetActive(isActive);
    }
}
