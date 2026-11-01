using System.Collections;
using System.Collections.Generic;
using TMPro;
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

    [Tooltip("Always open on the Connect page, in the Editor as well as in a build. No mode can " +
             "start without a session, so this is the only page that is useful first. Untick to " +
             "fall back to Default Page.")]
    public bool forceConnectPageInBuild = true;

    private void Start()
    {
        ReleaseModeButtonTints();
        ShowStartupPage();
        RefreshModeHighlight();
    }

    // Hands the mode buttons' colour over to this script.
    //
    // Both are Buttons set to Color Tint whose Target Graphic is the very Image below tints, so
    // Unity's own Selectable rewrote that colour on every state change -- press, release, the
    // pointer leaving -- and put back its Normal white a frame after the selection was applied.
    // The two were fighting over one property and Unity always got the last word, which is why
    // the highlight looked like it did nothing.
    //
    // Transition.None is what AnswerButtonVisual already does for the same reason: a button whose
    // look is decided by game state cannot also be driven by pointer state.
    private void ReleaseModeButtonTints()
    {
        ReleaseTint(oneVsOneButtonImage);
        ReleaseTint(teamFourButtonImage);
    }

    private static void ReleaseTint(Image buttonImage)
    {
        if (buttonImage == null)
            return;

        Button button = buttonImage.GetComponent<Button>();

        if (button != null)
            button.transition = Selectable.Transition.None;
    }

    private void ShowStartupPage()
    {
        // With an always-on server configured there is nothing to ask: nobody hosts and nobody
        // types an address, so the Connect page has no job and showing it would be a screen the
        // player has to dismiss before a game that is already online lets them play.
        NetworkBootstrap bootstrap = NetworkBootstrap.Instance;

        if (bootstrap != null && bootstrap.autoConnectToServer &&
            !string.IsNullOrWhiteSpace(bootstrap.serverAddress))
        {
            ShowLobbyPage();
            return;
        }

        // No longer build-only. Keeping the Editor on Default Page meant the one screen that has to
        // be tested — the one every real player sees first — was the one never seen while working.
        if (forceConnectPageInBuild && connectPage != null)
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
        HideAllPages();

        // This device's own choice, and only this device's. The mode was briefly server-wide so
        // that clients could change it at all — but that made one phone tapping 2v2 switch every
        // other phone too, as though they were one device. Players queue independently now.
        MatchMode modeToStart = selectedMode;

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

    // Switches off every lobby page, which is what actually makes the lobby disappear.
    //
    // Not lobbyRootObject: that field is empty in this scene and always has been, so anything
    // relying on it to clear the lobby silently does nothing and leaves the pages drawn on top of
    // whatever was revealed underneath.
    public void HideAllPages()
    {
        SetPageActive(profilePage,  false);
        SetPageActive(learningPage, false);
        SetPageActive(lobbyPage,    false);
        SetPageActive(morePage,     false);
        SetPageActive(connectPage,  false);
    }

    // ---------------------------------------------------------------
    // Mode selection — wire these to the 1v1 / 2v2 toggle buttons in the Inspector
    // ---------------------------------------------------------------
    public void SelectOneVsOneMode() { SelectMode(MatchMode.OneVsOne); }
    public void SelectTeamFourMode() { SelectMode(MatchMode.TeamFour); }

    private void SelectMode(MatchMode mode)
    {
        // These two buttons make their own noise rather than taking the automatic one, because
        // what they should play depends on whether anything actually changed -- and a pointer
        // handler on the button has no way of knowing that.
        ButtonClickSound.Play(mode == selectedMode
            ? ButtonClickSound.Note.D      // already chosen: a shrug
            : ButtonClickSound.Note.E);    // a real choice: the neutral tap

        selectedMode = mode;
        RefreshModeHighlight();
    }

    // What this player is playing, and what the waiting screen should show a queue for.
    public MatchMode SelectedMode => selectedMode;

    private void RefreshModeHighlight()
    {
        bool isOneVsOne = selectedMode == MatchMode.OneVsOne;

        if (oneVsOneHighlight != null)
            oneVsOneHighlight.SetActive(isOneVsOne);

        if (teamFourHighlight != null)
            teamFourHighlight.SetActive(!isOneVsOne);

        ApplyModeTint(oneVsOneButtonImage, isOneVsOne);
        ApplyModeTint(teamFourButtonImage, !isOneVsOne);
    }

    // The label moves with the button, not just the button art. Tinting only the background left
    // "1x1" and "2x2" reading identically whichever was chosen, so the one piece of the control a
    // player actually looks at carried none of the state.
    private void ApplyModeTint(Image buttonImage, bool isSelected)
    {
        if (buttonImage == null)
            return;

        Color tint = isSelected ? selectedModeColor : unselectedModeColor;
        buttonImage.color = tint;

        // Searched from the button rather than wired in the Inspector: the label is always a child
        // of the button it belongs to, and one less slot is one less thing to leave empty.
        TMP_Text label = buttonImage.GetComponentInChildren<TMP_Text>(true);

        if (label != null)
            label.color = tint;
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
    public void ShowTriviaPage()   { ShowLobbyPage(); }

    [Header("--- PAGE TRANSITION ---")]
    [Tooltip("Seconds for a page to slide in when a bottom tab is tapped. 0 switches instantly.")]
    [Range(0f, 0.6f)]
    public float slideSeconds = 0.22f;

    // Where each page sits when it is the one on screen, remembered the first time it is moved, so a
    // transition that is interrupted can always put it back.
    private readonly Dictionary<RectTransform, Vector2> homePositions = new Dictionary<RectTransform, Vector2>();
    private Coroutine slideCoroutine;
    private LobbyPage shownPage;
    private bool hasShownPage;

    // Left to right, the order of the bottom bar: Profile, Learning, Trivia, More. A tab to the right
    // of the current one slides in from the right, like Clash Royale's tabs, so the movement matches
    // where the tap was.
    private static int TabOrderOf(LobbyPage page)
    {
        switch (page)
        {
            case LobbyPage.Profile:  return 0;
            case LobbyPage.Learning: return 1;
            case LobbyPage.Lobby:    return 2;
            case LobbyPage.More:     return 3;
            case LobbyPage.Connect:  return 3;
            default:                 return 0;
        }
    }

    public void ShowPage(LobbyPage page)
    {
        // Hide everything first, then show the one we want. Written this way rather than as one
        // SetActive per page with a == test, because two fields are allowed to point at the SAME
        // object — Connect and More share a page so the Connect UI inherits More's background.
        // With per-field tests, whichever line came last would win and switch it straight back off.
        GameObject incoming = PageObject(page);
        GameObject outgoing = hasShownPage ? PageObject(shownPage) : null;

        bool animate = slideSeconds > 0f && hasShownPage && incoming != null && outgoing != null &&
                       incoming != outgoing && outgoing.activeSelf && gameObject.activeInHierarchy;

        // Whatever a half-finished transition left behind, before anything is switched off.
        if (slideCoroutine != null)
        {
            StopCoroutine(slideCoroutine);
            slideCoroutine = null;
            ResetPagePositions();
        }

        shownPage = page;
        hasShownPage = true;

        if (!animate)
        {
            SetPageActive(profilePage,  false);
            SetPageActive(learningPage, false);
            SetPageActive(lobbyPage,    false);
            SetPageActive(morePage,     false);
            SetPageActive(connectPage,  false);

            SetPageActive(incoming, true);
            GameMusic.PlayLobby();
            return;
        }

        slideCoroutine = StartCoroutine(SlidePages(outgoing, incoming,
            TabOrderOf(page) >= TabOrderOf(PageOf(outgoing)) ? 1f : -1f));

        GameMusic.PlayLobby();
    }

    // Which page a given object is, so the direction can be worked out from the object that is on
    // screen rather than from a second field that could disagree with it.
    private LobbyPage PageOf(GameObject pageObject)
    {
        if (pageObject == profilePage)  return LobbyPage.Profile;
        if (pageObject == learningPage) return LobbyPage.Learning;
        if (pageObject == lobbyPage)    return LobbyPage.Lobby;
        if (pageObject == connectPage)  return LobbyPage.Connect;
        return LobbyPage.More;
    }

    // The incoming page comes in from `direction` (1 = from the right), the outgoing one leaves the
    // other way, both moving together. Unscaled time, so it still runs if anything pauses the game.
    private IEnumerator SlidePages(GameObject outgoing, GameObject incoming, float direction)
    {
        RectTransform from = outgoing.transform as RectTransform;
        RectTransform to = incoming.transform as RectTransform;

        if (from == null || to == null)
        {
            SetPageActive(outgoing, false);
            SetPageActive(incoming, true);
            slideCoroutine = null;
            yield break;
        }

        Remember(from);
        Remember(to);

        SetPageActive(incoming, true);

        float width = ((RectTransform)transform).rect.width;

        if (width <= 1f)
            width = Screen.width;

        Vector2 fromHome = homePositions[from];
        Vector2 toHome = homePositions[to];

        to.anchoredPosition = toHome + new Vector2(width * direction, 0f);

        float elapsed = 0f;

        while (elapsed < slideSeconds)
        {
            elapsed += Time.unscaledDeltaTime;

            // Fast at first and settling at the end, which is what makes it read as a swipe rather
            // than a slide at constant speed.
            float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / slideSeconds));

            to.anchoredPosition = toHome + new Vector2(width * direction * (1f - t), 0f);
            from.anchoredPosition = fromHome - new Vector2(width * direction * t, 0f);

            yield return null;
        }

        to.anchoredPosition = toHome;
        from.anchoredPosition = fromHome;

        SetPageActive(profilePage,  false);
        SetPageActive(learningPage, false);
        SetPageActive(lobbyPage,    false);
        SetPageActive(morePage,     false);
        SetPageActive(connectPage,  false);

        SetPageActive(incoming, true);
        slideCoroutine = null;
    }

    private void Remember(RectTransform page)
    {
        if (!homePositions.ContainsKey(page))
            homePositions[page] = page.anchoredPosition;
    }

    private void ResetPagePositions()
    {
        foreach (KeyValuePair<RectTransform, Vector2> entry in homePositions)
        {
            if (entry.Key != null)
                entry.Key.anchoredPosition = entry.Value;
        }
    }

    private GameObject PageObject(LobbyPage page)
    {
        switch (page)
        {
            case LobbyPage.Profile:  return profilePage;
            case LobbyPage.Learning: return learningPage;
            case LobbyPage.Lobby:    return lobbyPage;
            case LobbyPage.More:     return morePage;
            case LobbyPage.Connect:  return connectPage;
            default:                 return null;
        }
    }

    private void SetPageActive(GameObject page, bool isActive)
    {
        if (page != null)
            page.SetActive(isActive);
    }
}
