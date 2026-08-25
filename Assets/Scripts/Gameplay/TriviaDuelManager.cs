using System.Collections;
using System.Collections.Generic;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.UI;

[System.Serializable]
public class TriviaQuestion
{
    [TextArea(2, 4)]
    public string question;
    public string[] answers = new string[4];

    [Range(0, 3)]
    public int correctAnswerIndex;

    // What skill this question tests: adicao, divisao, sequencias, logica and so on. Mistakes can
    // only be counted without it; with it they can be explained, which is what makes the practice
    // set personal rather than just "questions you got wrong".
    public string topic;
}

[System.Serializable]
public class DuelPlayer
{
    public string playerName;
    public Sprite playerPfp;

    [Range(1, 2)]
    public int teamId = 1;
}

[System.Serializable]
public class DuelPair
{
    public DuelPlayer leftPlayer;
    public DuelPlayer rightPlayer;
}

public class TriviaDuelManager : MonoBehaviour, IQuestionSource
{
    internal enum RoundState
    {
        OpenBuzz,
        SoloLeft,
        SoloRight,
        Resolving,
        MatchEnded
    }

    public enum BackgroundAnimation
    {
        None,
        Fade,
        Pop,
        SlideFromLeft,
        SlideFromRight,

        // Play a sequence of images instead of moving one. This is what hand-drawn transitions
        // exported out of Adobe Animate as a PNG sequence use — the frames themselves carry the
        // motion, so nothing here slides, scales or fades them.
        Frames
    }

    public enum BackgroundLoopAnimation
    {
        None,
        SlowZoom,
        Float,
        Pulse,
        ScrollUpRepeat
    }

    [System.Serializable]
    public class StateBackgroundVisual
    {
        public Sprite backgroundSprite;
        public BackgroundAnimation animation = BackgroundAnimation.Fade;
        public float animationSeconds = 0.25f;

        [Tooltip("Only used when animation is Frames. The exported sequence, in order — select " +
                 "them all in the Project window and drag them in together. The same list is the " +
                 "way IN to this state and, played backwards, the way OUT of it, so a state that " +
                 "has frames animates in both directions and one that has none animates in " +
                 "neither. Nothing needs setting on the state you leave towards.")]
        public Sprite[] frames;

        [Tooltip("Only used when animation is Frames. Match what the timeline was authored at in " +
                 "Animate (24 by default). animationSeconds is ignored: the frame count and this " +
                 "rate decide how long it runs.")]
        public float framesPerSecond = 24f;

        public BackgroundLoopAnimation loopAnimation = BackgroundLoopAnimation.None;
        public float loopSeconds = 4f;
        public float loopStrength = 0.04f;
        public float scrollSpeed = 0.08f;
        public Color mainTextColor = new Color32(248, 250, 252, 255);
        public Color secondaryTextColor = new Color32(226, 232, 240, 255);
    }

    [Header("--- YOUR SCREEN SIDE ---")]
    [Tooltip("Set this to 1 if you are Player 1 (left side of the screen), or 2 if you are Player 2 (right side). This flips the background direction for your view.")]
    [Range(1, 2)] public int localViewPlayerSide = 1;

    [Tooltip("If ON, the lobby screen shows when the game starts. If OFF, the trivia begins immediately.")]
    public bool showLobbyOnStart = true;

    [Tooltip("If ON, this device controls both sides locally (hotseat/offline debug mode: 1/2 keys and WASD/JKL switch sides). Turn OFF for real networked play, where each client only controls its own assigned side.")]
    public bool localPassAndPlayMode = false;

    private static TriviaDuelManager instance;

    // Resolves itself if the static has been lost. A script recompile while the game is running
    // reloads the domain, which clears every static but does NOT call Awake again — so this went
    // null mid-session and everything that reaches the manager through it started failing.
    public static TriviaDuelManager Instance
    {
        get
        {
            if (instance == null)
                instance = FindAnyObjectByType<TriviaDuelManager>(FindObjectsInactive.Include);

            return instance;
        }
        private set => instance = value;
    }

    public int LocalAssignedSide { get; private set; }

    [Header("--- LOBBY ---")]
    [Tooltip("Drag the root GameObject of your lobby UI here (the parent that contains all lobby screens). When the trivia starts, this gets hidden.")]
    public GameObject lobbyRootObject;

    [Tooltip("Drag the LobbyPageSwitcher component here so the game can show the correct lobby page when returning from a match.")]
    public LobbyPageSwitcher lobbyPageSwitcher;

    [Tooltip("Drag the root GameObject of your trivia game UI here. This gets shown when the game starts and hidden in the lobby.")]
    public GameObject triviaGameplayRoot;

    [Tooltip("Drag the BottomBar GameObject here. It hides when a match starts and reappears when you return to the lobby.")]
    public GameObject bottomBar;

    [Tooltip("If ON, the game automatically returns to the lobby after a team wins.")]
    public bool returnToLobbyAfterWin = true;

    [Tooltip("How many seconds to wait on the win screen before going back to the lobby.")]
    public float returnToLobbyDelaySeconds = 2f;

    [Header("--- BACKGROUND IMAGE ---")]
    [Tooltip("Drag the background Image component here. This is the image that changes color and animation depending on the game state.")]
    public Image gameBackground;

    [Header("--- BACKGROUND FOR EACH GAME STATE ---")]
    [Tooltip("Background shown during Open Buzz — when both players can buzz in to answer.")]
    public StateBackgroundVisual openBuzzBackground = new StateBackgroundVisual
    {
        animation = BackgroundAnimation.Pop,
        loopAnimation = BackgroundLoopAnimation.ScrollUpRepeat,
        scrollSpeed = 0.08f
    };

    [Tooltip("Background shown when it is YOUR solo turn (the other player answered wrong and now you have a timer to answer).")]
    public StateBackgroundVisual yourSoloBackground = new StateBackgroundVisual { animation = BackgroundAnimation.SlideFromLeft };

    [Tooltip("Background shown when it is the OTHER PLAYER's solo turn.")]
    public StateBackgroundVisual otherPlayerSoloBackground = new StateBackgroundVisual { animation = BackgroundAnimation.SlideFromRight };

    [Tooltip("Background shown when the match ends (someone wins or time runs out).")]
    public StateBackgroundVisual matchEndedBackground = new StateBackgroundVisual { animation = BackgroundAnimation.Fade };

    [Header("--- QUESTION & ANSWER BUTTONS ---")]
    [Tooltip("Drag the Text (TMP) component that displays the question here.")]
    public TMP_Text questionText;

    [Tooltip("Drag all 4 answer Button components here in order: A, B, C, D.")]
    public Button[] answerButtons;

    [Tooltip("Drag all 4 AnswerButtonVisual components here in order: A, B, C, D.")]
    public AnswerButtonVisual[] answerButtonVisuals;

    [Header("--- PLAYER NAMES AND PICTURES ---")]
    [Tooltip("Text component that shows the left player's name.")]
    public TMP_Text leftPlayerNameText;

    [Tooltip("Text component that shows the right player's name.")]
    public TMP_Text rightPlayerNameText;

    [Tooltip("Image component that shows the left player's profile picture.")]
    public Image leftPlayerPfpImage;

    [Tooltip("Image component that shows the right player's profile picture.")]
    public Image rightPlayerPfpImage;

    [Header("--- SOLO TIMER RINGS ---")]
    [Tooltip("The ring/donut Image on the left side that counts down during a solo. Drag it here.")]
    public Image leftSoloDonut;

    [Tooltip("The ring/donut Image on the right side that counts down during a solo. Drag it here.")]
    public Image rightSoloDonut;

    [Tooltip("The sprite shown on the ring when it is YOUR solo turn.")]
    public Sprite mySoloDonutSprite;

    [Tooltip("The sprite shown on the ring when it is the OTHER player's solo turn.")]
    public Sprite otherSoloDonutSprite;

    [Header("--- LOCAL PLAYER OUTLINE ---")]
    [Tooltip("Ring drawn around whichever avatar belongs to the person holding the device. Sampled " +
             "from the blue half of the board art (#80B3C8) so it reads as 'this side is yours' " +
             "rather than as a decoration.")]
    public Color localPlayerOutlineColor = new Color32(128, 179, 200, 255);

    [Tooltip("Thickness of that ring, in UI units.")]
    public float localPlayerOutlineWidth = 12f;

    [Header("--- MATCH START ANIMATION ---")]
    [Tooltip("Full-screen Image that sits ABOVE the waiting screen and is switched off the rest of " +
             "the time. The match-start animation plays here, which is what lets it run while the " +
             "player is still in the waiting room and the gameplay page is still hidden.")]
    public Image matchStartOverlay;

    [Tooltip("The exported sequence to play when a match forms. Leave empty and the match opens " +
             "immediately, exactly as it did before.")]
    public Sprite[] matchStartFrames;

    [Tooltip("Playback rate for the match-start frames.")]
    public float matchStartFramesPerSecond = 24f;

    [Tooltip("Beat between the match forming and the animation starting. A moment of the waiting " +
             "screen still being there is what makes the cut read as 'found them' rather than as " +
             "the screen glitching.")]
    public float matchStartDelaySeconds = 0.2f;

    [Tooltip("How long the names, avatars, question and answer buttons take to fade up once the " +
             "start animation has finished. The board itself is already there — only the pieces on " +
             "top of it fade. Set to 0 for the old instant appearance.")]
    public float matchStartUiFadeSeconds = 0.5f;

    [Tooltip("Color of the ring when it is your solo turn.")]
    public Color mySoloDonutColor = new Color32(248, 250, 252, 255);

    [Tooltip("Color of the ring when it is the other player's solo turn.")]
    public Color otherSoloDonutColor = new Color32(248, 113, 113, 255);

    [Header("--- SCORE DISPLAY ---")]
    [Tooltip("Text component that shows Team 1's current score.")]
    public TMP_Text team1ScoreText;

    [Tooltip("Text component that shows Team 2's current score.")]
    public TMP_Text team2ScoreText;

    [Header("--- BUTTON APPEARANCE ---")]
    [Tooltip("The ButtonTheme ScriptableObject that controls how the answer buttons look (normal, hovered, correct answer, wrong answer). Create one via Right-click > Create > Trivia > Button Theme.")]
    public ButtonTheme buttonTheme;

    [Header("--- QUESTIONS ---")]
    [Tooltip("(Optional) Drag the TriviaQuestions.txt file here directly. If left empty, the game loads it from the Resources folder automatically.")]
    public TextAsset questionDocument;

    [Tooltip("The filename (without .tsv) of the question file inside Assets/Resources. Default is 'TriviaQuestions'. Only used if Question Document above is empty.")]
    public string questionDocumentResourceName = "TriviaQuestions";

    [Tooltip("(Optional) Add trivia questions directly here instead of using a TSV file. Leave empty if you are using a TSV file.")]
    public TriviaQuestion[] questions;

    [Tooltip("Set up your player matchups here. Each Duel Pair is two players who face each other. Questions rotate through these pairs.")]
    public DuelPair[] duelPairs;

    [Header("--- TIMING (in seconds) ---")]
    [Tooltip("How many seconds a player gets to answer alone after the other player answers wrong (their Solo window).")]
    public float soloTimeSeconds = 3f;

    [Tooltip("How long the wrong answer flash stays on screen before the other player's solo begins.")]
    public float wrongFlashSeconds = 0.5f;

    [Tooltip("How long the correct answer is shown on screen before moving to the next question.")]
    public float correctResolveSeconds = 1f;

    [Tooltip("Extra delay after a correct answer before the next question loads.")]
    public float nextRoundDelaySeconds = 0.35f;

    [Tooltip("If nobody answers for this many seconds, the game ends automatically. Default is 60 seconds.")]
    public float inactivityEndSeconds = 60f;

    [Header("--- RULES ---")]
    [Tooltip("The first team to reach this many points wins the match.")]
    public int pointsToWin = 7;

    [Header("--- FALLBACK COLORS (used when no background image is set) ---")]
    [Tooltip("Default background color when no image is assigned to any state.")]
    public Color defaultBackgroundColor = new Color32(91, 92, 235, 255);

    [Tooltip("Background color during Open Buzz, used only if no background image is set.")]
    public Color openBuzzColor = new Color32(20, 184, 166, 255);

    [Tooltip("Background color during your Solo turn, used only if no background image is set.")]
    public Color yourSoloColor = new Color32(34, 184, 207, 255);

    [Tooltip("Background color during the other player's Solo turn, used only if no background image is set.")]
    public Color otherPlayerSoloColor = new Color32(255, 107, 107, 255);

    private static readonly Color LightTextColor = new Color32(248, 250, 252, 255);
    private static readonly Color LightSecondaryTextColor = new Color32(226, 232, 240, 255);
    private static readonly Color DarkTextColor = new Color32(15, 23, 42, 255);
    private static readonly Color DarkSecondaryTextColor = new Color32(51, 65, 85, 255);

    private readonly List<TriviaQuestion>[] questionsByDifficulty = new List<TriviaQuestion>[7];
    private bool hasRetriedQuestionLoad;

    private RoundState roundState = RoundState.MatchEnded;
    private TriviaQuestion currentQuestion;
    private int currentDifficultyLevel = 1;
    private int currentQuestionIndex = -1;
    private int currentDuelIndex = -1;
    private int team1Score;
    private int team2Score;
    private int debugMouseOwner = 1;
    private bool inputEnabled;
    private bool triviaRunning;
    private bool isTransitioning;
    private float soloTimer;
    private float inactivityTimer;
    private string leftBaseName = string.Empty;
    private string rightBaseName = string.Empty;

    private Coroutine stateCoroutine;
    private Coroutine backgroundTransitionCoroutine;
    private Coroutine matchStartCoroutine;

    // The visual currently on screen. A state's exit animation belongs to the state being LEFT, not
    // the one being entered — otherwise every state you can arrive at would need to know which
    // states might precede it, and Open Buzz would need solo's frames just to get out of a solo.
    private StateBackgroundVisual activeBackgroundVisual;

    // Which side won the match just ended, or 0 for no winner (an abandoned match). Read only by
    // the match-ended background, to decide whose solo animation introduces the end screen.
    private int lastWinnerSide;
    private Coroutine backgroundLoopCoroutine;
    private Coroutine returnToLobbyCoroutine;

    // How far off-centre a sliding background starts, in the UI's authored pixels.
    private const float SlideDistance = 80f;

    private Transform scrollingLayer;
    private Image backgroundUnderlay;
    private Image scrollingBackgroundA;
    private Image scrollingBackgroundB;
    private Image scrollingBackgroundC;
    private Sprite scrollingSprite;
    private RectTransform backgroundRect;

    private void Awake()
    {
        Instance = this;

        for (int i = 0; i < questionsByDifficulty.Length; i++)
            questionsByDifficulty[i] = new List<TriviaQuestion>();

        backgroundRect = gameBackground != null ? gameBackground.rectTransform : null;

        LoadQuestions();
        ApplyButtonThemeToAll(buttonTheme);
        NormalizeBackgroundSettings();

        if (questionText != null)
        {
            questionText.enableAutoSizing = true;
            questionText.fontSizeMax = questionText.fontSize;
            questionText.fontSizeMin = 8f;
            questionText.overflowMode = TMPro.TextOverflowModes.Truncate;
        }
    }

    private void Start()
    {
        if (!ValidateSetup())
            return;

        if (showLobbyOnStart)
            PrepareForLobby();
        else
            StartTriviaFromLobby();
    }

    private void Update()
    {
        if (localPassAndPlayMode)
            HandleDebugMouseOwnerKeys();

        HandleDifficultyKeys();

        if (!triviaRunning || roundState == RoundState.MatchEnded)
            return;

        if (localPassAndPlayMode)
            HandleKeyboardAnswers();

        if (IsAuthoritative)
            UpdateRoundTimers();
    }

    private bool IsAuthoritative =>
        localPassAndPlayMode ||
        NetworkManager.Singleton == null ||
        !NetworkManager.Singleton.IsListening ||
        NetworkManager.Singleton.IsServer;

    private bool ValidateSetup()
    {
        if (!HasAnyQuestionSource())
        {
            Debug.LogError("No questions available. Assign questions in the Inspector or add Assets/Resources/TriviaQuestions.tsv.");
            return false;
        }

        if (answerButtons == null || answerButtons.Length != 4)
        {
            Debug.LogError("Answer Buttons must contain exactly 4 buttons.");
            return false;
        }

        if (answerButtonVisuals == null || answerButtonVisuals.Length != 4)
        {
            Debug.LogError("Answer Button Visuals must contain exactly 4 visuals.");
            return false;
        }

        // With showLobbyOnStart on and no lobby wired up, PrepareForLobby() hides the gameplay UI
        // and then has nothing to show in its place — leaving the player on a blank screen with no
        // Start button, so the match can never begin. This used to fail completely silently.
        if (showLobbyOnStart && lobbyRootObject == null && lobbyPageSwitcher == null)
        {
            Debug.LogError(
                "Show Lobby On Start is enabled but neither 'Lobby Root Object' nor 'Lobby Page Switcher' " +
                "is assigned on " + name + ". There will be no lobby and no Start button, so no match can " +
                "ever start. Assign them in the Inspector.", this);
            return false;
        }

        return true;
    }

    private void BindAnswerButtons()
    {
        if (answerButtons == null)
            return;

        for (int i = 0; i < answerButtons.Length; i++)
        {
            int answerIndex = i;
            Button button = answerButtons[i];

            if (button == null)
                continue;

            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(() => OnAnswerButtonClicked(answerIndex));
        }
    }

    // openLobbyPage separates the two reasons this runs. On startup the app should open on whatever
    // Default Page says. Coming back from a match it should not: Default Page is Profile in this
    // scene, so finishing a game dumped the player on their profile instead of the screen with the
    // Start button on it.
    public void PrepareForLobby(bool openLobbyPage = false)
    {
        StopGameplayCoroutines();
        triviaRunning = false;
        inputEnabled = false;
        isTransitioning = false;
        currentQuestion = null;
        roundState = RoundState.MatchEnded;

        SetTriviaUiVisible(false);
        ResetDonuts();
        RestorePlayerNames();
        StopBackgroundMotion();
        DisableScrollingBackground();

        if (gameBackground != null)
            gameBackground.enabled = false;

        if (triviaGameplayRoot != null)
            triviaGameplayRoot.SetActive(false);

        if (bottomBar != null)
            bottomBar.SetActive(true);

        if (lobbyRootObject != null)
            lobbyRootObject.SetActive(true);

        if (lobbyPageSwitcher != null)
        {
            if (openLobbyPage)
                lobbyPageSwitcher.ShowLobbyPage();
            else
                lobbyPageSwitcher.ShowDefaultPage();
        }

        if (LobbyScreenController.Instance != null)
            LobbyScreenController.Instance.ShowLobby();

        foreach (IQBarController bar in FindObjectsByType<IQBarController>())
            bar.RefreshFromManager();
    }

    private bool IsNetworkedSession =>
        !localPassAndPlayMode && NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;

    public void StartTriviaFromLobby()
    {
        if (!ValidateSetup())
            return;

        if (IsNetworkedSession)
        {
            PlayerSideIdentity identity = GetLocalPlayerIdentity();

            if (identity == null)
            {
                Debug.LogError("StartTriviaFromLobby: no local PlayerSideIdentity, so this device " +
                               "cannot ready up. The player prefab is missing the component or has not spawned yet.");
                return;
            }

            identity.RequestStartTrivia();
            return;
        }

        // Only a deliberate offline game may start without a server. Otherwise this is a networked
        // session whose host failed to come up — a leaked port, a dropped connection — and starting
        // anyway produced a local match against a placeholder opponent that looked like the game
        // working, which is far worse than saying nothing happened.
        if (!localPassAndPlayMode)
        {
            Debug.LogError("StartTriviaFromLobby: not connected to a session, so there is nobody to " +
                           "play against. The host most likely failed to start — check the Console " +
                           "for a transport bind failure on the connect port.");

            if (lobbyPageSwitcher != null)
            {
                lobbyPageSwitcher.waitingScreen?.HideWaiting();
                lobbyPageSwitcher.ShowConnectPage();
            }

            return;
        }

        BeginMatchAuthoritative();
    }

    public void BeginMatchAuthoritative()
    {
        // Re-bind every time this mode's match actually starts, not just once at Awake — the answer
        // buttons are shared with TeamDuelManager (only one mode is ever active at a time), so
        // whichever mode starts most recently needs to "claim" the click listeners back.
        BindAnswerButtons();

        StopGameplayCoroutines();
        triviaRunning = true;
        showLobbyOnStart = false;
        inputEnabled = false;
        isTransitioning = false;
        currentQuestion = null;
        currentQuestionIndex = -1;
        currentDuelIndex = -1;
        team1Score = 0;
        team2Score = 0;

        if (PlayerIQManager.Instance != null)
            currentDifficultyLevel = PlayerIQManager.Instance.GetLocalDifficultyLevel();

        if (lobbyRootObject != null)
            lobbyRootObject.SetActive(false);

        if (LobbyScreenController.Instance != null)
            LobbyScreenController.Instance.HideLobby();

        if (triviaGameplayRoot != null)
            triviaGameplayRoot.SetActive(true);

        if (bottomBar != null)
            bottomBar.SetActive(false);

        if (questionText != null)
            questionText.text = string.Empty;

        SetTriviaUiVisible(true);
        UpdateScoreUI();
        ResetDonuts();

        if (gameBackground != null)
            gameBackground.enabled = true;

        StartNextRound();
    }

    public void ApplyNetworkedMatchStarted()
    {
        BindAnswerButtons();

        // Cleared before the board is shown. ApplyNetworkedScore decides who scored by comparing
        // against these, so a 7 left over from the last match would swallow the first six points
        // of this one -- they would show on screen and make no sound at all.
        team1Score = 0;
        team2Score = 0;

        if (matchStartCoroutine != null)
            StopCoroutine(matchStartCoroutine);

        matchStartCoroutine = StartCoroutine(MatchStartSequence());
    }

    // Holds the player on the waiting screen for a beat, plays the match-start animation over it,
    // and only then swaps to the gameplay page. The reveal is deliberately last: doing it first and
    // animating afterwards would show the board for a frame before the animation covered it, which
    // is the flicker this sequence exists to avoid.
    // Internal so the first-run tutorial can open on the same animation a real match does, and
    // wait for it, instead of keeping a second copy that drifts.
    internal IEnumerator MatchStartSequence()
    {
        // Pin the waiting screen at "found" before anything else. The queue these players came from
        // is already empty, so left live it would count itself down to 0 / 2 while the start
        // animation played over it.
        if (lobbyPageSwitcher != null && lobbyPageSwitcher.waitingScreen != null)
            lobbyPageSwitcher.waitingScreen.FreezeOnMatchFound();

        if (matchStartDelaySeconds > 0f)
            yield return new WaitForSecondsRealtime(matchStartDelaySeconds);

        if (matchStartOverlay != null && matchStartFrames != null && matchStartFrames.Length > 0)
        {
            // Configured while still INACTIVE, then switched on. Setting up an object that is
            // already visible lets it render once in whatever state it was left in — which is what
            // produced the couple of stretched frames at the start: the overlay appeared, and the
            // rect, sprite and scale only reached their right values on the following frame.
            PrepareFrameSurface(matchStartOverlay, matchStartFrames[0]);

            matchStartOverlay.gameObject.SetActive(true);
            matchStartOverlay.transform.SetAsLastSibling();

            yield return PlayFramesOn(matchStartOverlay, matchStartFrames, matchStartFramesPerSecond, false);
        }

        RevealGameplayAfterMatchStart();

        // Alpha is dropped BEFORE the overlay comes down, so the UI is never visible at full
        // opacity for even one frame. Reveal makes the objects active; this makes them invisible
        // again immediately, and the fade below is the only thing that brings them up.
        List<CanvasGroup> fadeGroups = matchStartUiFadeSeconds > 0f ? CollectTriviaUiFadeGroups() : null;

        if (fadeGroups != null)
            SetFadeGroupsAlpha(fadeGroups, 0f);

        // Taken down after the reveal, not before: hiding it first would uncover the board while
        // this frame is still on screen, which is the same flicker in the other direction.
        if (matchStartOverlay != null)
            matchStartOverlay.gameObject.SetActive(false);

        if (fadeGroups != null)
            yield return FadeInTriviaUi(fadeGroups, matchStartUiFadeSeconds);

        matchStartCoroutine = null;
    }

    // A CanvasGroup per element rather than one on the gameplay root, because the root also holds
    // the background — and the background is the thing the start animation just finished drawing.
    // Fading it would undo the transition instead of completing it.
    private List<CanvasGroup> CollectTriviaUiFadeGroups()
    {
        List<CanvasGroup> groups = new List<CanvasGroup>();

        AddFadeGroup(groups, questionText);
        AddFadeGroup(groups, leftPlayerNameText);
        AddFadeGroup(groups, rightPlayerNameText);
        AddFadeGroup(groups, leftPlayerPfpImage);
        AddFadeGroup(groups, rightPlayerPfpImage);
        AddFadeGroup(groups, leftSoloDonut);
        AddFadeGroup(groups, rightSoloDonut);
        AddFadeGroup(groups, team1ScoreText);
        AddFadeGroup(groups, team2ScoreText);

        if (answerButtons != null)
        {
            for (int i = 0; i < answerButtons.Length; i++)
            {
                if (answerButtons[i] != null)
                    AddFadeGroup(groups, answerButtons[i].gameObject);
            }
        }

        return groups;
    }

    private void AddFadeGroup(List<CanvasGroup> groups, Graphic graphic)
    {
        if (graphic != null)
            AddFadeGroup(groups, graphic.gameObject);
    }

    private void AddFadeGroup(List<CanvasGroup> groups, GameObject target)
    {
        if (target == null)
            return;

        CanvasGroup group = target.GetComponent<CanvasGroup>();

        // Explicit null check rather than ??: GetComponent returns a fake null that the null
        // coalescing operator does not recognise, so ?? would leave the component unadded.
        if (group == null)
            group = target.AddComponent<CanvasGroup>();

        groups.Add(group);
    }

    private void SetFadeGroupsAlpha(List<CanvasGroup> groups, float alpha)
    {
        for (int i = 0; i < groups.Count; i++)
        {
            if (groups[i] == null)
                continue;

            groups[i].alpha = alpha;

            // Nothing half-faded should be clickable. An answer button at 20% opacity still takes
            // a press otherwise, and the round has not visually started yet.
            groups[i].blocksRaycasts = alpha >= 1f;
        }
    }

    private IEnumerator FadeInTriviaUi(List<CanvasGroup> groups, float seconds)
    {
        float elapsed = 0f;

        // Unscaled, to match the rest of the start sequence — the delay and the frame playback are
        // both unscaled, so a timeScale change cannot desynchronise one part of it from another.
        while (elapsed < seconds)
        {
            elapsed += Time.unscaledDeltaTime;
            SetFadeGroupsAlpha(groups, Mathf.Clamp01(elapsed / seconds));
            yield return null;
        }

        SetFadeGroupsAlpha(groups, 1f);
    }

    private void RevealGameplayAfterMatchStart()
    {
        // The player has been sitting on the waiting screen since they pressed Start; their match
        // has now formed, so take it down before revealing the gameplay UI underneath.
        if (lobbyPageSwitcher != null && lobbyPageSwitcher.waitingScreen != null)
            lobbyPageSwitcher.waitingScreen.HideWaiting();

        if (lobbyRootObject != null)
            lobbyRootObject.SetActive(false);

        if (LobbyScreenController.Instance != null)
            LobbyScreenController.Instance.HideLobby();

        if (triviaGameplayRoot != null)
            triviaGameplayRoot.SetActive(true);

        if (bottomBar != null)
            bottomBar.SetActive(false);

        SetTriviaUiVisible(true);
        ResetDonuts();

        // Deliberately NOT blanked here any more.
        //
        // The server publishes the first question as the match forms, which is now a second or so
        // BEFORE this runs — the delay and the start animation sit in between. Clearing the text at
        // this point wiped a question that had already been delivered, and nothing sends it again,
        // so the board stayed empty for the whole round.
        //
        // Re-applied rather than merely left alone: the question landed while triviaGameplayRoot
        // was still inactive, and TMP components on an inactive object are not guaranteed to have
        // taken the value. Setting it again once everything is visible costs nothing and does not
        // depend on that.
        if (currentQuestion != null)
        {
            ApplyQuestionToButtons(currentQuestion);

            if (questionText != null)
                questionText.text = currentQuestion.question;
        }
        else if (questionText != null)
        {
            questionText.text = string.Empty;
        }

        if (gameBackground != null)
            gameBackground.enabled = true;

        // A fresh match always opens on OpenBuzz. The server publishes that state as the match
        // forms, which is before this runs, so nothing else is coming — apply it explicitly rather
        // than waiting for a message that has already been and gone.
        ApplyNetworkedRoundState((int)RoundState.OpenBuzz);
    }

    private void StartNextRound()
    {
        if (team1Score >= pointsToWin)
        {
            EndMatch("Jogador 1 venceu!", true, winnerSide: 1);
            return;
        }

        if (team2Score >= pointsToWin)
        {
            EndMatch("Jogador 2 venceu!", true, winnerSide: 2);
            return;
        }

        currentQuestion = GetNextQuestionForCurrentDifficulty();

        if (currentQuestion == null)
        {
            EndMatch("Sem perguntas disponíveis", false);
            return;
        }

        if (!IsNetworkedSession && duelPairs != null && duelPairs.Length > 0)
        {
            currentDuelIndex = (currentDuelIndex + 1) % duelPairs.Length;
            ApplyCurrentDuelUI();
        }
        ApplyQuestionToButtons(currentQuestion);
        ResetDonuts();
        RestorePlayerNames();
        UpdateScoreUI();
        ResetInactivityTimer();
        SetButtonsAvailableNormal();

        if (questionText != null)
            questionText.text = currentQuestion.question;

        SetState(RoundState.OpenBuzz);
        inputEnabled = true;
        isTransitioning = false;
    }

    public void ApplyNetworkedScore(int newTeam1Score, int newTeam2Score)
    {
        // Which number went up decides which sound plays. Comparing against what was on screen a
        // moment ago is the only way to tell: the server sends both scores every time, so the
        // message itself does not say who scored.
        //
        // Only ever a rise. A match starting, or the board being caught up after a reconnection,
        // sets these to values that are not a point being won by anyone.
        bool mineWentUp = newTeam1Score > team1Score;
        bool theirsWentUp = newTeam2Score > team2Score;

        team1Score = newTeam1Score;
        team2Score = newTeam2Score;
        UpdateScoreUI();

        if (!mineWentUp && !theirsWentUp)
            return;

        bool byLocalPlayer = localViewPlayerSide == (mineWentUp ? 1 : 2);

        MatchSounds.PlayScored(byLocalPlayer);
        FlashScore(mineWentUp ? team1ScoreText : team2ScoreText, byLocalPlayer);
    }

    public void ApplyNetworkedRoundState(int newRoundState)
    {
        roundState = (RoundState)newRoundState;
        RefreshBackgroundForCurrentState();

        // The donut is only redrawn while a solo is ticking, so leaving a solo used to leave the last
        // frame of the ring frozen on screen into the next round. Nothing else ever cleared it: the
        // server stops sending timer updates the moment the solo ends.
        if (roundState != RoundState.SoloLeft && roundState != RoundState.SoloRight)
            soloTimer = 0f;

        UpdateSoloDonut();
    }

    public void ApplyNetworkedQuestion(int difficultyLevel, int questionIndex)
    {
        if (questionIndex < 0)
            return;

        currentDifficultyLevel = difficultyLevel;
        currentQuestionIndex = questionIndex;

        List<TriviaQuestion> pool = GetQuestionPool(difficultyLevel);

        if (pool == null || questionIndex >= pool.Count)
            return;

        currentQuestion = pool[questionIndex];
        ApplyQuestionToButtons(currentQuestion);

        if (questionText != null)
            questionText.text = currentQuestion.question;
    }

    public void ApplySoloTimerVisual(float remaining)
    {
        soloTimer = remaining;
        UpdateSoloDonut();
    }

    public void RegisterLocalPlayerSide(int side)
    {
        LocalAssignedSide = side;
        localViewPlayerSide = side;
        RefreshBackgroundForCurrentState();
        RefreshPfpOpacity();
    }

    private PlayerSideIdentity GetLocalPlayerIdentity()
    {
        if (NetworkManager.Singleton == null || NetworkManager.Singleton.LocalClient == null)
            return null;

        NetworkObject localPlayerObject = NetworkManager.Singleton.LocalClient.PlayerObject;
        return localPlayerObject != null ? localPlayerObject.GetComponent<PlayerSideIdentity>() : null;
    }

    private void OnAnswerButtonClicked(int answerIndex)
    {
        if (currentQuestion == null)
            return;

        if (localPassAndPlayMode)
        {
            if (!inputEnabled || isTransitioning)
                return;

            SubmitAnswer(debugMouseOwner, answerIndex);
            return;
        }

        // inputEnabled/isTransitioning are only maintained by the authoritative state machine,
        // which only runs server-side — a pure client can't rely on them, so just forward the
        // request and let the server's IsPlayerAllowedToAnswer be the real gate.
        PlayerSideIdentity localIdentity = GetLocalPlayerIdentity();
        localIdentity?.RequestSubmitAnswer(answerIndex);
    }

    private void SubmitAnswer(int playerSide, int answerIndex)
    {
        if (!IsPlayerAllowedToAnswer(playerSide))
            return;

        if (answerIndex < 0 || answerIndex > 3)
            return;

        ResetInactivityTimer();

        if (answerIndex == currentQuestion.correctAnswerIndex)
            stateCoroutine = StartCoroutine(HandleCorrectAnswer(playerSide, answerIndex));
        else
            stateCoroutine = StartCoroutine(HandleWrongAnswer(playerSide, answerIndex));
    }

    private bool IsPlayerAllowedToAnswer(int playerSide)
    {
        if (!triviaRunning || !inputEnabled || isTransitioning)
            return false;

        switch (roundState)
        {
            case RoundState.OpenBuzz:
                return true;
            case RoundState.SoloLeft:
                return playerSide == 1;
            case RoundState.SoloRight:
                return playerSide == 2;
            default:
                return false;
        }
    }

    private IEnumerator HandleCorrectAnswer(int playerSide, int answerIndex)
    {
        BeginResolve();
        MarkAnswerRight(answerIndex);
        AwardPointToPlayerSide(playerSide);
        UpdateScoreUI();

        if (playerSide == 1 && leftPlayerNameText != null)
            leftPlayerNameText.text = "ACERTOU!";

        if (playerSide == 2 && rightPlayerNameText != null)
            rightPlayerNameText.text = "ACERTOU!";

        yield return new WaitForSeconds(correctResolveSeconds);

        if (team1Score >= pointsToWin)
        {
            EndMatch("Jogador 1 venceu!", true, winnerSide: 1);
            yield break;
        }

        if (team2Score >= pointsToWin)
        {
            EndMatch("Jogador 2 venceu!", true, winnerSide: 2);
            yield break;
        }

        yield return new WaitForSeconds(nextRoundDelaySeconds);
        stateCoroutine = null;
        StartNextRound();
    }

    private IEnumerator HandleWrongAnswer(int playerSide, int answerIndex)
    {
        // Capture state before BeginResolve() changes it
        bool wasInSolo = roundState == RoundState.SoloLeft || roundState == RoundState.SoloRight;

        BeginResolve();
        MarkAnswerWrong(answerIndex);

        if (playerSide == 1 && leftPlayerNameText != null)
            leftPlayerNameText.text = "ERROU";

        if (playerSide == 2 && rightPlayerNameText != null)
            rightPlayerNameText.text = "ERROU";

        yield return new WaitForSeconds(wrongFlashSeconds);

        stateCoroutine = null;

        // If already in a solo, end it and return to open buzz — don't start another solo (infinite loop bug)
        if (wasInSolo)
            EndSoloAndReturnToOpen();
        else
            BeginSoloForPlayer(playerSide == 1 ? 2 : 1);
    }

    private void BeginResolve()
    {
        inputEnabled = false;
        isTransitioning = true;
        SetState(RoundState.Resolving);
        LockAllButtons();
    }

    private void BeginSoloForPlayer(int soloPlayerSide)
    {
        inputEnabled = true;
        isTransitioning = false;
        soloTimer = soloTimeSeconds;
        ResetInactivityTimer();
        SetButtonsAvailableNormal();

        if (soloPlayerSide == 1)
        {
            if (leftPlayerNameText != null)
                leftPlayerNameText.text = leftBaseName + " - SOLO";

            if (rightPlayerNameText != null)
                rightPlayerNameText.text = rightBaseName;

            SetState(RoundState.SoloLeft);
        }
        else
        {
            if (leftPlayerNameText != null)
                leftPlayerNameText.text = leftBaseName;

            if (rightPlayerNameText != null)
                rightPlayerNameText.text = rightBaseName + " - SOLO";

            SetState(RoundState.SoloRight);
        }

        UpdateSoloDonut();
    }

    private void EndSoloAndReturnToOpen()
    {
        inputEnabled = true;
        isTransitioning = false;
        ResetDonuts();
        RestorePlayerNames();
        SetButtonsAvailableNormal();
        ResetInactivityTimer();
        SetState(RoundState.OpenBuzz);
    }

    private void HandleKeyboardAnswers()
    {
        if (Keyboard.current == null || !inputEnabled || isTransitioning)
            return;

        if (Keyboard.current.aKey.wasPressedThisFrame) SubmitAnswer(1, 0);
        if (Keyboard.current.sKey.wasPressedThisFrame) SubmitAnswer(1, 1);
        if (Keyboard.current.dKey.wasPressedThisFrame) SubmitAnswer(1, 2);
        if (Keyboard.current.fKey.wasPressedThisFrame) SubmitAnswer(1, 3);

        if (Keyboard.current.jKey.wasPressedThisFrame) SubmitAnswer(2, 0);
        if (Keyboard.current.kKey.wasPressedThisFrame) SubmitAnswer(2, 1);
        if (Keyboard.current.lKey.wasPressedThisFrame) SubmitAnswer(2, 2);
        if (Keyboard.current.semicolonKey.wasPressedThisFrame) SubmitAnswer(2, 3);
    }

    private void HandleDebugMouseOwnerKeys()
    {
        if (Keyboard.current == null)
            return;

        if (Keyboard.current.digit1Key.wasPressedThisFrame)
        {
            debugMouseOwner = 1;
            localViewPlayerSide = 1;
            RefreshBackgroundForCurrentState();
            RefreshPfpOpacity();
        }

        if (Keyboard.current.digit2Key.wasPressedThisFrame)
        {
            debugMouseOwner = 2;
            localViewPlayerSide = 2;
            RefreshBackgroundForCurrentState();
            RefreshPfpOpacity();
        }
    }

    private void HandleDifficultyKeys()
    {
        if (Keyboard.current == null)
            return;

        for (int keyNumber = 3; keyNumber <= 9; keyNumber++)
        {
            KeyControl key = GetDigitKey(keyNumber);

            if (key != null && key.wasPressedThisFrame)
            {
                currentDifficultyLevel = Mathf.Clamp(keyNumber - 2, 1, 7);
                currentQuestionIndex = -1;
                break;
            }
        }
    }

    private KeyControl GetDigitKey(int keyNumber)
    {
        switch (keyNumber)
        {
            case 3: return Keyboard.current.digit3Key;
            case 4: return Keyboard.current.digit4Key;
            case 5: return Keyboard.current.digit5Key;
            case 6: return Keyboard.current.digit6Key;
            case 7: return Keyboard.current.digit7Key;
            case 8: return Keyboard.current.digit8Key;
            case 9: return Keyboard.current.digit9Key;
            default: return null;
        }
    }

    private void UpdateRoundTimers()
    {
        if (isTransitioning)
            return;

        bool runInactivity = roundState == RoundState.OpenBuzz || roundState == RoundState.SoloLeft || roundState == RoundState.SoloRight;

        if (runInactivity)
        {
            inactivityTimer -= Time.deltaTime;

            if (inactivityTimer <= 0f)
            {
                inactivityTimer = 0f;
                EndMatch("Partida encerrada: ninguém respondeu por 1 minuto", false);
                return;
            }
        }

        if (roundState == RoundState.SoloLeft || roundState == RoundState.SoloRight)
        {
            soloTimer -= Time.deltaTime;
            UpdateSoloDonut();

            if (soloTimer <= 0f)
            {
                soloTimer = 0f;
                EndSoloAndReturnToOpen();
            }
        }
    }

    private void ResetInactivityTimer()
    {
        inactivityTimer = inactivityEndSeconds;
    }

    private void ApplyCurrentDuelUI()
    {
        DuelPair pair = duelPairs[currentDuelIndex];
        leftBaseName = pair.leftPlayer != null ? pair.leftPlayer.playerName : string.Empty;
        rightBaseName = pair.rightPlayer != null ? pair.rightPlayer.playerName : string.Empty;

        if (leftPlayerPfpImage != null)
            leftPlayerPfpImage.sprite = pair.leftPlayer != null ? pair.leftPlayer.playerPfp : null;

        if (rightPlayerPfpImage != null)
            rightPlayerPfpImage.sprite = pair.rightPlayer != null ? pair.rightPlayer.playerPfp : null;

        // Local hotseat/offline testing: show the chosen profile on your own side, keep the
        // placeholder duel-pair data on the other side (there's only one local profile per device).
        if (PlayerProfileManager.Instance != null)
        {
            string localName = PlayerProfileManager.Instance.GetLocalName();
            Sprite localAvatar = PlayerProfileManager.Instance.GetAvatarSprite(PlayerProfileManager.Instance.GetLocalAvatarIndex());

            if (localViewPlayerSide == 1)
            {
                leftBaseName = localName;

                if (leftPlayerPfpImage != null)
                    leftPlayerPfpImage.sprite = localAvatar;
            }
            else
            {
                rightBaseName = localName;

                if (rightPlayerPfpImage != null)
                    rightPlayerPfpImage.sprite = localAvatar;
            }
        }

        RestorePlayerNames();
        RefreshPfpOpacity();
    }

    public void ApplyNetworkedPlayerIdentity(int side, string playerName, Sprite avatarSprite)
    {
        if (side == 1)
        {
            leftBaseName = playerName;

            if (leftPlayerPfpImage != null)
                leftPlayerPfpImage.sprite = avatarSprite;
        }
        else
        {
            rightBaseName = playerName;

            if (rightPlayerPfpImage != null)
                rightPlayerPfpImage.sprite = avatarSprite;
        }

        RestorePlayerNames();
        RefreshPfpOpacity();
    }

    // Dims every player picture except your own, so it's obvious at a glance which side is you.
    private void RefreshPfpOpacity()
    {
        SetPfpOpacity(leftPlayerPfpImage, localViewPlayerSide == 1);
        SetPfpOpacity(rightPlayerPfpImage, localViewPlayerSide == 2);

        ApplyLocalPlayerOutline(leftPlayerPfpImage, localViewPlayerSide == 1);
        ApplyLocalPlayerOutline(rightPlayerPfpImage, localViewPlayerSide == 2);
    }

    // Public so TeamDuelManager can mark its own slots without a second copy of this: 2v2 owns four
    // avatars instead of two, but "the local one gets a blue ring" is the same rule.
    public void ApplyLocalPlayerOutline(Graphic target, bool isLocalPlayer)
    {
        if (target == null)
            return;

        Outline outline = target.GetComponent<Outline>();

        // Explicit null check, not ??: GetComponent hands back a fake null that ?? treats as a real
        // object, which would leave the component unadded and the ring silently missing.
        if (outline == null)
        {
            if (!isLocalPlayer)
                return;   // nothing to turn off, and no reason to add a component to hide it

            outline = target.gameObject.AddComponent<Outline>();
        }

        outline.effectColor = localPlayerOutlineColor;
        outline.effectDistance = new Vector2(localPlayerOutlineWidth, localPlayerOutlineWidth);
        outline.useGraphicAlpha = false;   // stays solid while the avatar itself is dimmed to 50%
        outline.enabled = isLocalPlayer;
    }

    // Team mode has no background system of its own, so it cannot flip the board by changing
    // roundState the way 1v1 does. This lets it set which side the local player is looking from and
    // re-apply the current background immediately — RefreshBackgroundForCurrentState would bail,
    // because it checks triviaGameplayRoot, which is inactive for the whole of a 2v2 match.
    public void SetLocalViewSideForTeams(int side)
    {
        if (side != 1 && side != 2)
            return;

        LocalAssignedSide = side;
        localViewPlayerSide = side;

        if (activeBackgroundVisual != null)
            ApplyStateBackground(activeBackgroundVisual, defaultBackgroundColor, animateExit: false);
    }

    private void SetPfpOpacity(Image pfpImage, bool isLocalPlayer)
    {
        if (pfpImage == null)
            return;

        Color color = pfpImage.color;
        color.a = isLocalPlayer ? 1f : 0.5f;
        pfpImage.color = color;
    }

    private void RestorePlayerNames()
    {
        if (leftPlayerNameText != null)
            leftPlayerNameText.text = leftBaseName;

        if (rightPlayerNameText != null)
            rightPlayerNameText.text = rightBaseName;
    }

    private void AwardPointToPlayerSide(int playerSide)
    {
        int teamId = GetTeamIdForPlayerSide(playerSide);

        if (teamId == 1)
            team1Score++;
        else
            team2Score++;
    }

    private int GetTeamIdForPlayerSide(int playerSide)
    {
        // Player 1 = Team 1, Player 2 = Team 2.
        // DuelPlayer.teamId defaults to 1 for all players, which caused both sides to always score to Team 1.
        return Mathf.Clamp(playerSide, 1, 2);
    }

    // The number that changed pulses once, in the colour of whoever it belongs to. Once, not
    // repeatedly: the tutorial pulses five times because it is pointing something out, and a
    // score that keeps flashing during a match is a warning light rather than a reward.
    //
    // Blue for you, red for them -- the same two colours the board and the solo rings already use,
    // so nothing here introduces a third meaning.
    internal void FlashScore(TMP_Text score, bool byLocalPlayer)
    {
        if (score == null)
            return;

        if (scoreFlashCoroutine != null)
            StopCoroutine(scoreFlashCoroutine);

        scoreFlashCoroutine = StartCoroutine(FlashScoreRoutine(
            score, byLocalPlayer ? localPlayerOutlineColor : otherSoloDonutColor));
    }

    private Coroutine scoreFlashCoroutine;

    private IEnumerator FlashScoreRoutine(TMP_Text score, Color flash)
    {
        // Read now rather than caching it: ApplyStateBackground recolours the labels whenever the
        // round state changes, so whatever it is at this moment is what it should return to.
        Color original = score.color;

        const float frameRate = 24f;
        const float seconds = 0.55f;

        float elapsed = 0f;
        int lastStep = -1;

        while (elapsed < seconds)
        {
            elapsed += Time.unscaledDeltaTime;

            // Stepped at 24 fps to match the exported transitions. A smooth per-frame fade beside
            // hand-drawn 24 fps artwork reads as belonging to different software.
            int step = Mathf.FloorToInt(elapsed * frameRate);

            if (step != lastStep)
            {
                lastStep = step;
                float t = Mathf.Sin(Mathf.Clamp01(step / frameRate / seconds) * Mathf.PI);
                score.color = Color.Lerp(original, flash, t);
            }

            yield return null;
        }

        score.color = original;
        scoreFlashCoroutine = null;
    }

    private void UpdateScoreUI()
    {
        if (team1ScoreText != null)
            team1ScoreText.text = team1Score.ToString();

        if (team2ScoreText != null)
            team2ScoreText.text = team2Score.ToString();
    }

    private void ApplyQuestionToButtons(TriviaQuestion questionData)
    {
        if (answerButtonVisuals == null || questionData == null || questionData.answers == null)
            return;

        for (int i = 0; i < answerButtonVisuals.Length && i < questionData.answers.Length; i++)
        {
            if (answerButtonVisuals[i] != null)
                answerButtonVisuals[i].SetLabel(questionData.answers[i]);
        }
    }

    // Lets a client-side caller (e.g. TeamDuelManager.ApplyNetworkedQuestion) deterministically
    // resolve the same question the server picked, from the synced difficulty+index pair alone.
    internal TriviaQuestion GetQuestionAt(int difficultyLevel, int questionIndex)
    {
        List<TriviaQuestion> pool = GetQuestionPool(difficultyLevel);

        if (pool == null || questionIndex < 0 || questionIndex >= pool.Count)
            return null;

        return pool[questionIndex];
    }

    // Every question at every level, for the Learning tab to build practice sets from. Practice is
    // not limited to the player's current difficulty: the point is the topics they get wrong, and a
    // question they missed at level 5 is still the one worth revisiting.
    public List<TriviaQuestion> GetAllQuestions()
    {
        List<TriviaQuestion> all = new List<TriviaQuestion>();

        if (questionsByDifficulty == null)
            return all;

        for (int level = 0; level < questionsByDifficulty.Length; level++)
        {
            List<TriviaQuestion> pool = questionsByDifficulty[level];

            if (pool != null)
                all.AddRange(pool);
        }

        return all;
    }

    internal void ApplyQuestionVisualsTo(TMP_Text targetQuestionText, AnswerButtonVisual[] targetVisuals, TriviaQuestion questionData)
    {
        if (targetQuestionText != null && questionData != null)
            targetQuestionText.text = questionData.question;

        if (targetVisuals == null || questionData == null || questionData.answers == null)
            return;

        for (int i = 0; i < targetVisuals.Length && i < questionData.answers.Length; i++)
        {
            if (targetVisuals[i] != null)
                targetVisuals[i].SetLabel(questionData.answers[i]);
        }
    }

    // Internal rather than private: the first-run tutorial dresses this same board without going
    // through the match state machine, and duplicating the list of things to switch on is how one
    // of them gets forgotten and a board comes up half empty.
    internal void SetTriviaUiVisible(bool isVisible)
    {
        SetGraphicVisible(questionText, isVisible);
        SetGraphicVisible(leftPlayerNameText, isVisible);
        SetGraphicVisible(rightPlayerNameText, isVisible);
        SetGraphicVisible(leftPlayerPfpImage, isVisible);
        SetGraphicVisible(rightPlayerPfpImage, isVisible);
        SetGraphicVisible(leftSoloDonut, isVisible);
        SetGraphicVisible(rightSoloDonut, isVisible);
        SetGraphicVisible(team1ScoreText, isVisible);
        SetGraphicVisible(team2ScoreText, isVisible);

        if (answerButtons == null)
            return;

        for (int i = 0; i < answerButtons.Length; i++)
        {
            if (answerButtons[i] != null)
                answerButtons[i].gameObject.SetActive(isVisible);
        }
    }

    private void SetGraphicVisible(Graphic graphic, bool isVisible)
    {
        if (graphic != null)
            graphic.gameObject.SetActive(isVisible);
    }

    private void ApplyButtonThemeToAll(ButtonTheme theme)
    {
        if (answerButtonVisuals == null)
            return;

        for (int i = 0; i < answerButtonVisuals.Length; i++)
        {
            if (answerButtonVisuals[i] != null)
                answerButtonVisuals[i].ApplyTheme(theme);
        }
    }

    internal void SetButtonsAvailableNormal()
    {
        if (answerButtonVisuals == null)
            return;

        for (int i = 0; i < answerButtonVisuals.Length; i++)
        {
            if (answerButtonVisuals[i] != null)
                answerButtonVisuals[i].SetAvailableState();
        }
    }

    internal void LockAllButtons()
    {
        if (answerButtonVisuals == null)
            return;

        for (int i = 0; i < answerButtonVisuals.Length; i++)
        {
            if (answerButtonVisuals[i] != null)
                answerButtonVisuals[i].SetDisabledState();
        }
    }

    // byLocalPlayer decides whether this device makes a noise about it. The button turning green
    // is news for everyone in the match; the sound is only for the person who answered.
    internal void MarkAnswerRight(int answerIndex, bool byLocalPlayer = true)
    {
        if (answerButtonVisuals == null || answerIndex < 0 || answerIndex >= answerButtonVisuals.Length)
            return;

        // Opposite the C that plays on a wrong one. This lands a moment after the tap that caused
        // it, so the two together read as a question and its answer rather than as two clicks.
        if (byLocalPlayer)
            MatchSounds.PlayCorrect();

        if (answerButtonVisuals[answerIndex] != null)
            answerButtonVisuals[answerIndex].SetPressedRightState();
    }

    internal void MarkAnswerWrong(int answerIndex, bool byLocalPlayer = true)
    {
        if (answerButtonVisuals == null || answerIndex < 0 || answerIndex >= answerButtonVisuals.Length)
            return;

        // The root of the scale: settled and final, nothing owed. It lands a moment after the tap
        // that caused it, so the pair reads as a question and its answer rather than two clicks.
        if (byLocalPlayer)
        {
            DeviceFeedback.Vibrate(DeviceFeedback.Strength.Bump);
            ButtonClickSound.Play(ButtonClickSound.Note.C);
        }

        if (answerButtonVisuals[answerIndex] != null)
            answerButtonVisuals[answerIndex].SetPressedWrongState();
    }

    private void ResetDonuts()
    {
        if (leftSoloDonut != null)
        {
            leftSoloDonut.fillAmount = 1f;
            leftSoloDonut.gameObject.SetActive(false);
        }

        if (rightSoloDonut != null)
        {
            rightSoloDonut.fillAmount = 1f;
            rightSoloDonut.gameObject.SetActive(false);
        }
    }

    private void UpdateSoloDonut()
    {
        float fill = soloTimeSeconds <= 0f ? 0f : Mathf.Clamp01(soloTimer / soloTimeSeconds);

        if (roundState == RoundState.SoloLeft)
        {
            ApplySoloDonutVisual(leftSoloDonut, 1, fill, true);
            ApplySoloDonutVisual(rightSoloDonut, 2, 1f, false);
        }
        else if (roundState == RoundState.SoloRight)
        {
            ApplySoloDonutVisual(leftSoloDonut, 1, 1f, false);
            ApplySoloDonutVisual(rightSoloDonut, 2, fill, true);
        }
        else
        {
            ResetDonuts();
        }
    }

    private void ApplySoloDonutVisual(Image donut, int playerSide, float fillAmount, bool isVisible)
    {
        if (donut == null)
            return;

        donut.gameObject.SetActive(isVisible);

        if (!isVisible)
            return;

        bool isMySolo = playerSide == localViewPlayerSide;

        if (isMySolo && mySoloDonutSprite != null)
            donut.sprite = mySoloDonutSprite;
        else if (!isMySolo && otherSoloDonutSprite != null)
            donut.sprite = otherSoloDonutSprite;

        donut.color = isMySolo ? mySoloDonutColor : otherSoloDonutColor;
        donut.fillAmount = fillAmount;
    }

    public bool IsMatchRunning => triviaRunning;

    private void EndMatch(string message, bool hasWinner, int winnerSide = 0)
    {
        lastWinnerSide = hasWinner ? winnerSide : 0;

        StopGameplayCoroutines(false);
        triviaRunning = false;
        inputEnabled = false;
        isTransitioning = false;
        SetState(RoundState.MatchEnded);
        LockAllButtons();
        ResetDonuts();

        if (questionText != null)
            questionText.text = message;

        if (leftPlayerNameText != null)
            leftPlayerNameText.text = string.Empty;

        if (rightPlayerNameText != null)
            rightPlayerNameText.text = string.Empty;

        if (hasWinner && PlayerIQManager.Instance != null)
            PlayerIQManager.Instance.AdjustLocalIQAfterMatch(winnerSide == localViewPlayerSide);

        // NOT gated on hasWinner any more. A match that ends without one — the inactivity
        // timeout, or an opponent abandoning — used to skip this entirely and leave the player
        // parked on the end screen with no way back except quitting. Whether to return is the
        // returnToLobbyAfterWin preference; whether somebody won is beside the point.
        if (returnToLobbyAfterWin)
            returnToLobbyCoroutine = StartCoroutine(ReturnToLobbyAfterDelay());
    }

    public void ApplyNetworkedMatchEnd(string message, bool hasWinner, int winnerSide)
    {
        lastWinnerSide = hasWinner ? winnerSide : 0;

        // Nothing on a match that simply ran out of players or time: there is no result to
        // announce, and a defeat sting for somebody's Wi-Fi dropping is a lie.
        if (hasWinner)
            MatchSounds.PlayEnded(winnerSide == localViewPlayerSide);

        if (questionText != null)
            questionText.text = message;

        if (leftPlayerNameText != null)
            leftPlayerNameText.text = string.Empty;

        if (rightPlayerNameText != null)
            rightPlayerNameText.text = string.Empty;

        if (hasWinner && PlayerIQManager.Instance != null)
            PlayerIQManager.Instance.AdjustLocalIQAfterMatch(winnerSide == localViewPlayerSide);

        // NOT gated on hasWinner any more. A match that ends without one — the inactivity
        // timeout, or an opponent abandoning — used to skip this entirely and leave the player
        // parked on the end screen with no way back except quitting. Whether to return is the
        // returnToLobbyAfterWin preference; whether somebody won is beside the point.
        if (returnToLobbyAfterWin)
            returnToLobbyCoroutine = StartCoroutine(ReturnToLobbyAfterDelay());
    }

    // The way out is the way in, run backwards: the results sit for their delay, the pieces on the
    // board fade away, the start animation unwinds, and only then does the lobby appear. Doing it
    // in this order means the player never sees the board empty or the lobby snap in.
    private IEnumerator ReturnToLobbyAfterDelay()
    {
        yield return new WaitForSeconds(Mathf.Max(0f, returnToLobbyDelaySeconds));

        // Fade the names, avatars, question and buttons back out. Same groups, same duration as the
        // way in, so the two halves of the match are symmetrical.
        if (matchStartUiFadeSeconds > 0f)
        {
            List<CanvasGroup> fadeGroups = CollectTriviaUiFadeGroups();
            yield return FadeOutTriviaUi(fadeGroups, matchStartUiFadeSeconds);
        }

        // Then the start animation in reverse, and the lobby behind it.
        returnToLobbyCoroutine = null;
        yield return ReturnToLobbyTransition(ReturnToLobby);

        // The UI was faded to zero on the way out and its objects are switched off with the
        // gameplay root, so alpha is put back for the next match — otherwise the following round
        // would reveal itself into invisible text.
        SetFadeGroupsAlpha(CollectTriviaUiFadeGroups(), 1f);
    }

    // Giving up in the queue leaves by the same door as finishing a match, so the animation lives
    // in one place. Called by WaitingScreenController when its timeout fires; there is no UI fade
    // in that case because the gameplay UI was never shown.
    public void PlayReturnToLobbyTransition(System.Action returnToLobby)
    {
        StartCoroutine(ReturnToLobbyTransition(returnToLobby));
    }

    private IEnumerator ReturnToLobbyTransition(System.Action returnToLobby)
    {
        if (matchStartOverlay != null && matchStartFrames != null && matchStartFrames.Length > 0)
        {
            // Prepared on the LAST frame, since this runs backwards — the same reason the way in
            // prepares on the first. Configuring an object that is already visible is what caused
            // the stretched frames at the start.
            PrepareFrameSurface(matchStartOverlay, matchStartFrames[matchStartFrames.Length - 1]);

            matchStartOverlay.gameObject.SetActive(true);
            matchStartOverlay.transform.SetAsLastSibling();

            // The lobby is put up BEFORE the animation plays, not after it finishes. That last
            // frame covers the whole screen, so the swap happens completely hidden behind it, and
            // the frames then open onto the lobby. Switching afterwards meant the animation spent
            // its whole length uncovering the finished match instead.
            returnToLobby?.Invoke();

            // Showing the lobby activates its root, which can put it above the overlay in the
            // draw order. Re-asserted so the animation stays on top of the thing it is revealing.
            matchStartOverlay.transform.SetAsLastSibling();

            yield return PlayFramesOn(matchStartOverlay, matchStartFrames, matchStartFramesPerSecond, true);

            // Hidden only once the frames are done — by now the lobby has been up behind it the
            // whole time, so there is nothing left to uncover.
            matchStartOverlay.gameObject.SetActive(false);
            yield break;
        }

        // No animation configured: nothing to hide behind, so this is just the plain switch.
        returnToLobby?.Invoke();
    }

    private IEnumerator FadeOutTriviaUi(List<CanvasGroup> groups, float seconds)
    {
        float elapsed = 0f;

        while (elapsed < seconds)
        {
            elapsed += Time.unscaledDeltaTime;
            SetFadeGroupsAlpha(groups, 1f - Mathf.Clamp01(elapsed / seconds));
            yield return null;
        }

        SetFadeGroupsAlpha(groups, 0f);
    }

    private void ReturnToLobby()
    {
        showLobbyOnStart = true;
        PrepareForLobby(true);
    }

    private void StopGameplayCoroutines(bool stopBackground = true)
    {
        if (stateCoroutine != null)
        {
            StopCoroutine(stateCoroutine);
            stateCoroutine = null;
        }

        if (returnToLobbyCoroutine != null)
        {
            StopCoroutine(returnToLobbyCoroutine);
            returnToLobbyCoroutine = null;
        }

        if (!stopBackground)
            return;

        StopBackgroundMotion();
        DisableScrollingBackground();
    }

    private void SetState(RoundState newState)
    {
        roundState = newState;
        RefreshBackgroundForCurrentState();
    }

    private void RefreshBackgroundForCurrentState()
    {
        // Several network sync paths (initial connection, catch-up sync on spawn) can reach this
        // before the gameplay panel has ever been shown — its TMP text components are inactive and
        // uninitialized at that point, and touching properties like outlineWidth on them throws
        // inside TextMeshPro itself. Safe to skip: this gets called again once the panel actually
        // becomes visible (BeginMatchAuthoritative/ApplyNetworkedMatchStarted).
        if (triviaGameplayRoot != null && !triviaGameplayRoot.activeInHierarchy)
            return;

        switch (roundState)
        {
            case RoundState.OpenBuzz:
                ApplyStateBackground(openBuzzBackground, openBuzzColor);
                break;

            case RoundState.SoloLeft:
                ApplyStateBackground(localViewPlayerSide == 1 ? yourSoloBackground : otherPlayerSoloBackground,
                    localViewPlayerSide == 1 ? yourSoloColor : otherPlayerSoloColor);
                break;

            case RoundState.SoloRight:
                ApplyStateBackground(localViewPlayerSide == 2 ? yourSoloBackground : otherPlayerSoloBackground,
                    localViewPlayerSide == 2 ? yourSoloColor : otherPlayerSoloColor);
                break;

            case RoundState.MatchEnded:
                // animateExit is off here: the win is already being announced by the winner's solo
                // animation playing forwards, and unwinding the previous state first would show two
                // sequences back to back for one event.
                ApplyStateBackground(BuildMatchEndVisual(), defaultBackgroundColor, animateExit: false);
                break;
        }
    }

    // On a win, the transition into the end screen is the winning side's own solo animation played
    // forwards. Rather than a second animation system, this borrows that visual's frames and hands
    // them to the normal Frames path, which settles onto the match-ended background afterwards
    // exactly as it would after any other sequence.
    // Team mode has no background system of its own: TeamDuelManager owns its own root, slots and
    // scores, but shares this one background Image — the same arrangement that already has it
    // borrowing question visuals through ApplyQuestionVisualsTo. This is its way in.
    //
    // It calls ApplyStateBackground directly rather than RefreshBackgroundForCurrentState, because
    // that method bails when triviaGameplayRoot is inactive, and during a team match it always is.
    public void PlayWinnerBackground(int winnerSide)
    {
        lastWinnerSide = winnerSide;
        ApplyStateBackground(BuildMatchEndVisual(), defaultBackgroundColor, animateExit: false);
    }

    private StateBackgroundVisual BuildMatchEndVisual()
    {
        StateBackgroundVisual winnerSolo = lastWinnerSide == 0 ? null
            : lastWinnerSide == localViewPlayerSide ? yourSoloBackground
            : otherPlayerSoloBackground;

        // No winner, or that side has no animation exported yet: the end screen behaves as before.
        if (!HasFrames(winnerSolo))
            return matchEndedBackground;

        return new StateBackgroundVisual
        {
            backgroundSprite = matchEndedBackground.backgroundSprite,
            animation = BackgroundAnimation.Frames,
            animationSeconds = matchEndedBackground.animationSeconds,
            frames = winnerSolo.frames,
            framesPerSecond = winnerSolo.framesPerSecond,
            loopAnimation = matchEndedBackground.loopAnimation,
            loopSeconds = matchEndedBackground.loopSeconds,
            loopStrength = matchEndedBackground.loopStrength,
            scrollSpeed = matchEndedBackground.scrollSpeed,
            mainTextColor = matchEndedBackground.mainTextColor,
            secondaryTextColor = matchEndedBackground.secondaryTextColor
        };
    }

    private void ApplyStateBackground(StateBackgroundVisual visual, Color fallbackColor, bool animateExit = true)
    {
        ApplyVisualTextColors(visual, fallbackColor);

        // A visual with frames but no still sprite is legitimate — the match-ended slot has no
        // background of its own and is introduced entirely by the winner's animation, which then
        // holds on its final frame.
        if (visual == null || gameBackground == null || (visual.backgroundSprite == null && !HasFrames(visual)))
        {
            SetSolidBackground(fallbackColor);
            return;
        }

        if (visual.loopAnimation == BackgroundLoopAnimation.ScrollUpRepeat)
        {
            StartScrollingBackground(visual);
            return;
        }

        StopBackgroundMotion();
        DisableScrollingBackground();

        StateBackgroundVisual outgoing = animateExit ? activeBackgroundVisual : null;
        activeBackgroundVisual = visual;

        backgroundTransitionCoroutine = StartCoroutine(AnimateStaticBackground(visual, outgoing));
    }

    // The background a round opens on, applied without going through the round state machine.
    //
    // The tutorial dresses this board itself, and the sprite sitting on the Image in the scene is
    // whatever was last dragged onto it -- not necessarily the one Open Buzz is configured with.
    // Left alone, the first thing a new player ever sees is an out-of-date background.
    internal void ShowOpenBuzzBackground(bool animateExit = false)
    {
        ApplyStateBackground(openBuzzBackground, openBuzzColor, animateExit);
    }

    // The board as it looks while the OTHER player is taking a solo turn: their transition frames
    // play in, and leaving it plays them back out. Which visual that is depends on which side the
    // local player is on, exactly as RefreshBackgroundForCurrentState decides it.
    internal void ShowOpponentSoloBackground()
    {
        ApplyStateBackground(otherPlayerSoloBackground, otherPlayerSoloColor);
    }

    private static bool HasFrames(StateBackgroundVisual visual)
    {
        return visual != null && visual.frames != null && visual.frames.Length > 0;
    }

    // One sequence player for both directions. Reverse is an index flip rather than a second loop,
    // so the way out can never drift out of step with the way in.
    private IEnumerator PlayFrames(StateBackgroundVisual visual, bool reversed)
    {
        yield return PlayFramesOn(gameBackground, visual.frames, visual.framesPerSecond, reversed);
    }

    // Puts an Image into the exact state frame playback needs, including its first sprite. Split
    // out so it can be done BEFORE the object is shown; anything visible while being configured
    // renders at least one frame in the wrong shape.
    private void PrepareFrameSurface(Image target, Sprite firstFrame)
    {
        if (target == null)
            return;

        target.enabled = true;
        target.color = Color.white;
        target.type = Image.Type.Simple;
        target.preserveAspect = false;

        if (firstFrame != null)
            target.sprite = firstFrame;

        StretchImageToFill(target.rectTransform);
        target.rectTransform.anchoredPosition = Vector2.zero;
        target.rectTransform.localScale = GetMirroredScale(Vector3.one);
    }

    // Drives any Image, not just the background, so the match-start animation can play on an
    // overlay above the waiting screen while the gameplay page is still hidden behind it.
    private IEnumerator PlayFramesOn(Image target, Sprite[] frames, float framesPerSecond, bool reversed)
    {
        if (target == null || frames == null || frames.Length == 0)
            yield break;

        float secondsPerFrame = 1f / Mathf.Max(1f, framesPerSecond);

        PrepareFrameSurface(target, frames[reversed ? frames.Length - 1 : 0]);

        // Driven by total elapsed time, not by waiting a fixed slice per frame.
        //
        // Waiting per frame is what made 24 fps play back at 15. Each step waited until at least
        // secondsPerFrame had passed, which on a 60Hz display rounds UP to the next rendered frame:
        // a 41.7 ms step becomes 50 ms, and the 8.3 ms of overshoot is thrown away rather than
        // carried into the next step. Seven frames of that is a third longer than asked for, and
        // worse whenever the editor dips under 60.
        //
        // Asking the clock which frame is due instead keeps the TOTAL duration exact and skips a
        // frame when the display cannot keep up, which is what any video player does.
        float startedAt = Time.unscaledTime;
        int lastShown = -1;

        while (true)
        {
            // Unscaled, so a transition still plays at the right speed if anything ever pauses the
            // game by setting Time.timeScale.
            int step = Mathf.FloorToInt((Time.unscaledTime - startedAt) / secondsPerFrame);

            if (step >= frames.Length)
                break;

            if (step != lastShown)
            {
                lastShown = step;
                target.sprite = frames[reversed ? frames.Length - 1 - step : step];
            }

            yield return null;
        }
    }

    private IEnumerator AnimateStaticBackground(StateBackgroundVisual visual, StateBackgroundVisual outgoing)
    {
        // Leaving a state that has frames runs those frames backwards first, so a solo animates out
        // the way it animated in, using the one export. Open Buzz has no frames, so arriving there
        // adds nothing of its own — the whole transition is the solo's own animation unwinding.
        if (outgoing != null && outgoing != visual && HasFrames(outgoing))
            yield return PlayFrames(outgoing, reversed: true);

        // The outgoing sprite stays on screen underneath for the length of the transition. The new
        // one fades in from alpha 0, and with nothing behind it that used to mean fading up from
        // whatever the camera clears to — a black flash on every state change.
        ShowBackgroundUnderlay(gameBackground.sprite);

        gameBackground.enabled = true;
        gameBackground.sprite = visual.backgroundSprite;
        gameBackground.type = Image.Type.Simple;
        gameBackground.preserveAspect = false;
        StretchImageToFill(gameBackground.rectTransform);

        // Frames carry their own motion, so none of the slide/scale/fade machinery below applies.
        // Handled before any of it rather than as another case in the switch, because every one of
        // those effects would fight the artwork instead of adding to it.
        if (visual.animation == BackgroundAnimation.Frames && !HasFrames(visual))
        {
            // Silently falling through to the slide is how a background slot sits misconfigured for
            // an afternoon: the animation still plays, just not the one that was asked for.
            Debug.LogWarning("Background: this state is set to Frames but has no frames assigned, " +
                             "so the old slide/fade played instead. Drag the exported sprites into " +
                             "its Frames list.", this);
        }

        if (HasFrames(visual) && visual.animation == BackgroundAnimation.Frames)
        {
            yield return PlayFrames(visual, reversed: false);

            // Settle on the still background the state uses for the rest of the round, so the
            // sequence reads as a transition INTO something rather than the last frame sticking.
            gameBackground.sprite = visual.backgroundSprite;

            HideBackgroundUnderlay();
            StartBackgroundLoop(visual);
            yield break;
        }

        float duration = Mathf.Max(0.01f, visual.animationSeconds);
        float startScale = 1f;

        bool isSlide = visual.animation == BackgroundAnimation.SlideFromLeft
                    || visual.animation == BackgroundAnimation.SlideFromRight;

        Vector2 startOffset = Vector2.zero;

        switch (visual.animation)
        {
            case BackgroundAnimation.Pop:
                startScale = 0.94f;
                break;
            case BackgroundAnimation.SlideFromLeft:
                startOffset = new Vector2(-SlideDistance, 0f);
                break;
            case BackgroundAnimation.SlideFromRight:
                startOffset = new Vector2(SlideDistance, 0f);
                break;
        }

        // Player 2 sees the board mirrored, and anchoredPosition is in the parent's space, so
        // unlike the artwork it is not flipped by GetMirroredScale. Without this the background
        // slides in from the side opposite the player it belongs to.
        startOffset = new Vector2(localViewPlayerSide == 2 ? -startOffset.x : startOffset.x,
                                  startOffset.y);

        RectTransform rect = gameBackground.rectTransform;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            float eased = 1f - Mathf.Pow(1f - t, 3f);

            // A slide keeps full opacity: the outgoing background is right behind it, so there is
            // no gap to hide and fading only muddies the movement.
            gameBackground.color = isSlide ? Color.white : new Color(1f, 1f, 1f, eased);
            rect.anchoredPosition = Vector2.Lerp(startOffset, Vector2.zero, eased);
            rect.localScale = GetMirroredScale(Vector3.one * Mathf.Lerp(startScale, 1f, eased));

            yield return null;
        }

        gameBackground.color = Color.white;
        rect.anchoredPosition = Vector2.zero;
        rect.localScale = GetMirroredScale(Vector3.one);
        HideBackgroundUnderlay();
        StartBackgroundLoop(visual);
        backgroundTransitionCoroutine = null;
    }

    // Sits directly behind gameBackground holding the sprite being replaced, so a fade or a slide
    // always has the old background underneath it rather than empty screen.
    private void ShowBackgroundUnderlay(Sprite outgoingSprite)
    {
        if (gameBackground == null || outgoingSprite == null)
            return;

        if (backgroundUnderlay == null)
        {
            backgroundUnderlay = CreateScrollingImage("BackgroundUnderlay",
                gameBackground.rectTransform.parent, gameBackground.rectTransform.GetSiblingIndex());
        }

        // Re-seated every time: the scrolling backgrounds are inserted and removed around it, so a
        // sibling index chosen once will not stay directly behind gameBackground.
        backgroundUnderlay.rectTransform.SetSiblingIndex(gameBackground.rectTransform.GetSiblingIndex());
        backgroundUnderlay.sprite = outgoingSprite;
        backgroundUnderlay.color = Color.white;
        backgroundUnderlay.enabled = true;
        StretchImageToFill(backgroundUnderlay.rectTransform);
        backgroundUnderlay.rectTransform.localScale = GetMirroredScale(Vector3.one);
        backgroundUnderlay.rectTransform.anchoredPosition = Vector2.zero;
    }

    private void HideBackgroundUnderlay()
    {
        if (backgroundUnderlay != null)
            backgroundUnderlay.enabled = false;
    }

    private void SetSolidBackground(Color color)
    {
        StopBackgroundMotion();
        DisableScrollingBackground();

        if (gameBackground == null)
            return;

        gameBackground.enabled = true;
        gameBackground.sprite = null;
        gameBackground.color = color;
        // StretchImageToFill rather than only normalising the scale. The background in the scene is
        // 78.6 x 85.2 with a localScale of 15 x 30, which is what makes it screen sized — so setting
        // the scale to 1 on its own collapses it to 78 pixels until something else happens to
        // stretch it. That is where the white edges came from: every path that normalised the scale
        // without fixing the rect left the image the wrong size, and only the ones that went on to
        // call this recovered. Rect and scale are now always changed together.
        StretchImageToFill(gameBackground.rectTransform);
        gameBackground.rectTransform.anchoredPosition = Vector2.zero;
    }

    private void StartBackgroundLoop(StateBackgroundVisual visual)
    {
        if (visual == null || visual.loopAnimation == BackgroundLoopAnimation.None || visual.loopAnimation == BackgroundLoopAnimation.ScrollUpRepeat)
            return;

        backgroundLoopCoroutine = StartCoroutine(LoopStaticBackground(visual));
    }

    private IEnumerator LoopStaticBackground(StateBackgroundVisual visual)
    {
        RectTransform rect = gameBackground.rectTransform;
        float duration = Mathf.Max(0.5f, visual.loopSeconds);
        Vector3 baseScale = Vector3.one;

        while (true)
        {
            float cycle = Mathf.PingPong(Time.time / duration, 1f);

            switch (visual.loopAnimation)
            {
                case BackgroundLoopAnimation.SlowZoom:
                    rect.localScale = GetMirroredScale(baseScale * (1f + visual.loopStrength * cycle));
                    break;

                case BackgroundLoopAnimation.Float:
                    rect.anchoredPosition = new Vector2(0f, Mathf.Lerp(-20f, 20f, cycle) * visual.loopStrength * 10f);
                    rect.localScale = GetMirroredScale(baseScale);
                    break;

                case BackgroundLoopAnimation.Pulse:
                    rect.localScale = GetMirroredScale(baseScale * (1f + visual.loopStrength * 0.5f * cycle));
                    gameBackground.color = new Color(1f, 1f, 1f, Mathf.Lerp(0.9f, 1f, cycle));
                    break;
            }

            yield return null;
        }
    }

    private void StartScrollingBackground(StateBackgroundVisual visual)
    {
        EnsureScrollingBackground();

        if (scrollingSprite != visual.backgroundSprite)
        {
            scrollingSprite = visual.backgroundSprite;
            ConfigureScrollingImage(scrollingBackgroundA, scrollingSprite);
            ConfigureScrollingImage(scrollingBackgroundB, scrollingSprite);
            ConfigureScrollingImage(scrollingBackgroundC, scrollingSprite);
        }

        StopBackgroundMotion();

        if (gameBackground != null)
            gameBackground.enabled = false;

        SetScrollingBackgroundVisible(true);
        ResetScrollingPositions();
        backgroundLoopCoroutine = StartCoroutine(LoopScrollingBackground(visual));
    }

    private IEnumerator LoopScrollingBackground(StateBackgroundVisual visual)
    {
        float speed = Mathf.Max(0.01f, visual.scrollSpeed);

        while (true)
        {
            float height = GetBackgroundHeight();
            float delta = height * speed * Time.deltaTime;
            MoveScrollingImage(scrollingBackgroundA.rectTransform, delta, height);
            MoveScrollingImage(scrollingBackgroundB.rectTransform, delta, height);
            MoveScrollingImage(scrollingBackgroundC.rectTransform, delta, height);
            yield return null;
        }
    }

    private void MoveScrollingImage(RectTransform rect, float delta, float height)
    {
        Vector2 position = rect.anchoredPosition;
        position.y += delta;

        if (position.y >= height)
            position.y -= height * 3f;

        // Deliberately written every frame. Skipping the write when the rounded pixel is unchanged
        // looks like a saving, but the next frame reads this value back — so at a scroll speed
        // slower than half a pixel per frame the position could never advance and the background
        // would stall. Isolating this on its own canvas is what makes the per-frame write cheap.
        rect.anchoredPosition = new Vector2(0f, Mathf.Round(position.y));
    }

    private void EnsureScrollingBackground()
    {
        if (gameBackground == null)
            return;

        Transform parent = gameBackground.transform.parent;
        int siblingIndex = gameBackground.transform.GetSiblingIndex();

        if (scrollingBackgroundA == null)
            scrollingBackgroundA = CreateScrollingImage("ScrollingBackgroundA", parent, siblingIndex);

        if (scrollingBackgroundB == null)
            scrollingBackgroundB = CreateScrollingImage("ScrollingBackgroundB", parent, siblingIndex);

        if (scrollingBackgroundC == null)
            scrollingBackgroundC = CreateScrollingImage("ScrollingBackgroundC", parent, siblingIndex);
    }

    private Image CreateScrollingImage(string objectName, Transform parent, int siblingIndex)
    {
        GameObject go = new GameObject(objectName, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(ScrollingLayer(parent, siblingIndex), false);

        Image image = go.GetComponent<Image>();
        image.type = Image.Type.Simple;
        image.preserveAspect = false;
        image.color = Color.white;
        StretchImageToFill(image.rectTransform);
        return image;
    }

    // The scrolling background moves three full-screen images EVERY frame for as long as a round
    // lasts. Unity rebuilds a canvas whole, so on the single canvas this scene uses that meant
    // re-batching all ~133 renderers every frame — the lobby, the practice panels and every page,
    // none of which had changed. Giving the scroll its own nested canvas confines that rebuild to
    // the three images that actually moved.
    private Transform ScrollingLayer(Transform parent, int siblingIndex)
    {
        if (scrollingLayer != null)
            return scrollingLayer;

        Transform existing = parent.Find("ScrollingBackgroundLayer");

        if (existing != null)
        {
            scrollingLayer = existing;
            return scrollingLayer;
        }

        GameObject layer = new GameObject("ScrollingBackgroundLayer", typeof(RectTransform), typeof(Canvas));
        layer.transform.SetParent(parent, false);
        layer.transform.SetSiblingIndex(siblingIndex);

        StretchImageToFill(layer.GetComponent<RectTransform>());

        scrollingLayer = layer.transform;
        return scrollingLayer;
    }

    private void ConfigureScrollingImage(Image image, Sprite sprite)
    {
        if (image == null)
            return;

        image.sprite = sprite;
        image.enabled = sprite != null;
        StretchImageToFill(image.rectTransform);
        image.rectTransform.localScale = GetMirroredScale(Vector3.one);
    }

    private void ResetScrollingPositions()
    {
        float height = GetBackgroundHeight();
        SetScrollingPosition(scrollingBackgroundA, 0f);
        SetScrollingPosition(scrollingBackgroundB, -height);
        SetScrollingPosition(scrollingBackgroundC, -height * 2f);
    }

    private void SetScrollingPosition(Image image, float y)
    {
        if (image == null)
            return;

        StretchImageToFill(image.rectTransform);
        image.rectTransform.localScale = GetMirroredScale(Vector3.one);
        image.rectTransform.anchoredPosition = new Vector2(0f, y);
    }

    private float GetBackgroundHeight()
    {
        if (backgroundRect != null && backgroundRect.rect.height > 0f)
            return backgroundRect.rect.height;

        return Screen.height;
    }

    private void StopBackgroundMotion()
    {
        if (backgroundTransitionCoroutine != null)
        {
            StopCoroutine(backgroundTransitionCoroutine);
            backgroundTransitionCoroutine = null;
        }

        if (backgroundLoopCoroutine != null)
        {
            StopCoroutine(backgroundLoopCoroutine);
            backgroundLoopCoroutine = null;
        }

        if (gameBackground != null)
        {
            gameBackground.color = Color.white;
            // StretchImageToFill rather than only normalising the scale. The background in the scene is
            // 78.6 x 85.2 with a localScale of 15 x 30, which is what makes it screen sized — so setting
            // the scale to 1 on its own collapses it to 78 pixels until something else happens to
            // stretch it. That is where the white edges came from: every path that normalised the scale
            // without fixing the rect left the image the wrong size, and only the ones that went on to
            // call this recovered. Rect and scale are now always changed together.
            StretchImageToFill(gameBackground.rectTransform);
            gameBackground.rectTransform.anchoredPosition = Vector2.zero;
        }

        // A transition cut short leaves the outgoing sprite behind the new one; without this it
        // would stay there for the rest of the match.
        HideBackgroundUnderlay();
    }

    private void DisableScrollingBackground()
    {
        SetScrollingBackgroundVisible(false);
        scrollingSprite = null;
    }

    private void SetScrollingBackgroundVisible(bool isVisible)
    {
        if (scrollingBackgroundA != null) scrollingBackgroundA.gameObject.SetActive(isVisible);
        if (scrollingBackgroundB != null) scrollingBackgroundB.gameObject.SetActive(isVisible);
        if (scrollingBackgroundC != null) scrollingBackgroundC.gameObject.SetActive(isVisible);
    }

    private void StretchImageToFill(RectTransform rect)
    {
        if (rect == null)
            return;

        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        rect.localScale = GetMirroredScale(Vector3.one);
    }

    private Vector3 GetMirroredScale(Vector3 scale)
    {
        // Both sides are mirrored from what they used to be: side 1 is now negated and side 2 is
        // not, where it was the other way round. The two sides stay opposite each other, so the
        // board still reads correctly for whoever is looking at it — the whole thing is just
        // flipped. One place, no per-background switch, nothing to remember to tick.
        return new Vector3(localViewPlayerSide == 2 ? Mathf.Abs(scale.x) : -Mathf.Abs(scale.x), scale.y, scale.z);
    }

    private void ApplyVisualTextColors(StateBackgroundVisual visual, Color fallbackColor)
    {
        Color mainColor = visual != null ? SafeColor(visual.mainTextColor, LightTextColor) : GetReadableTextColor(fallbackColor, true);
        Color secondaryColor = visual != null ? SafeColor(visual.secondaryTextColor, LightSecondaryTextColor) : GetReadableTextColor(fallbackColor, false);

        ApplyTextColor(questionText, mainColor);
        ApplyTextColor(leftPlayerNameText, secondaryColor);
        ApplyTextColor(rightPlayerNameText, secondaryColor);
        ApplyTextColor(team1ScoreText, mainColor);
        ApplyTextColor(team2ScoreText, mainColor);
    }

    private void ApplyTextColor(TMP_Text text, Color color)
    {
        if (text == null)
            return;

        text.color = color;

        // outlineWidth reaches into the font material, and TextMeshPro only creates that once the
        // component has been enabled at least once. At the end of a 2v2 these trivia text objects
        // have never been shown -- team mode draws on its own root -- so this threw an NRE from
        // inside TMP, which surfaced as "Unhandled RPC exception!" and took the whole match-end
        // message down with it. Colour still applies; only the outline needs a live material.
        if (!text.gameObject.activeInHierarchy || text.fontSharedMaterial == null)
            return;

        text.outlineWidth = 0.12f;
        text.outlineColor = GetContrastRatio(color, DarkTextColor) >= GetContrastRatio(color, LightTextColor) ? DarkTextColor : LightTextColor;
    }

    private Color SafeColor(Color color, Color fallback)
    {
        if (color.a <= 0.01f)
            return fallback;

        return color;
    }

    private Color GetReadableTextColor(Color backgroundColor, bool mainText)
    {
        float contrastToLight = GetContrastRatio(backgroundColor, LightTextColor);
        float contrastToDark = GetContrastRatio(backgroundColor, DarkTextColor);

        if (mainText)
            return contrastToLight >= contrastToDark ? LightTextColor : DarkTextColor;

        return contrastToLight >= contrastToDark ? LightSecondaryTextColor : DarkSecondaryTextColor;
    }

    private float GetContrastRatio(Color first, Color second)
    {
        float firstLum = GetRelativeLuminance(first);
        float secondLum = GetRelativeLuminance(second);
        float lighter = Mathf.Max(firstLum, secondLum);
        float darker = Mathf.Min(firstLum, secondLum);
        return (lighter + 0.05f) / (darker + 0.05f);
    }

    private float GetRelativeLuminance(Color color)
    {
        float r = Linearize(color.r);
        float g = Linearize(color.g);
        float b = Linearize(color.b);
        return 0.2126f * r + 0.7152f * g + 0.0722f * b;
    }

    private float Linearize(float channel)
    {
        return channel <= 0.03928f ? channel / 12.92f : Mathf.Pow((channel + 0.055f) / 1.055f, 2.4f);
    }

    private void NormalizeBackgroundSettings()
    {
        if (openBuzzBackground.mainTextColor.a <= 0f) openBuzzBackground.mainTextColor = LightTextColor;
        if (openBuzzBackground.secondaryTextColor.a <= 0f) openBuzzBackground.secondaryTextColor = LightSecondaryTextColor;
        if (yourSoloBackground.mainTextColor.a <= 0f) yourSoloBackground.mainTextColor = LightTextColor;
        if (yourSoloBackground.secondaryTextColor.a <= 0f) yourSoloBackground.secondaryTextColor = LightSecondaryTextColor;
        if (otherPlayerSoloBackground.mainTextColor.a <= 0f) otherPlayerSoloBackground.mainTextColor = LightTextColor;
        if (otherPlayerSoloBackground.secondaryTextColor.a <= 0f) otherPlayerSoloBackground.secondaryTextColor = LightSecondaryTextColor;
        if (matchEndedBackground.mainTextColor.a <= 0f) matchEndedBackground.mainTextColor = LightTextColor;
        if (matchEndedBackground.secondaryTextColor.a <= 0f) matchEndedBackground.secondaryTextColor = LightSecondaryTextColor;
    }

    private void LoadQuestions()
    {
        EnsureQuestionPools();

        for (int i = 0; i < questionsByDifficulty.Length; i++)
            questionsByDifficulty[i].Clear();

        TextAsset source = questionDocument;

        if (source == null && !string.IsNullOrWhiteSpace(questionDocumentResourceName))
            source = Resources.Load<TextAsset>(questionDocumentResourceName);

        if (source != null)
            LoadQuestionDocument(source.text);

        if (!HasAnyQuestionSource() && questions != null)
        {
            for (int i = 0; i < questions.Length; i++)
            {
                if (IsValidQuestion(questions[i]))
                    questionsByDifficulty[0].Add(questions[i]);
            }
        }
    }

    private void LoadQuestionDocument(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return;

        string[] lines = text.Split('\n');

        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i].Trim();

            if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#") || line.StartsWith("difficulty\t"))
                continue;

            string[] columns = line.Split('\t');

            // 8 columns since the topic was added: difficulty, topic, question, 4 answers, index.
            // Files still in the old 7-column shape are read too, so an older TSV does not silently
            // load zero questions.
            bool hasTopicColumn = columns.Length >= 8;
            int expectedColumns = hasTopicColumn ? 8 : 7;

            if (columns.Length < expectedColumns)
                continue;

            if (!int.TryParse(columns[0], out int difficulty))
                continue;

            int topicOffset = hasTopicColumn ? 1 : 0;

            if (!int.TryParse(columns[6 + topicOffset], out int correctAnswerIndex))
                continue;

            TriviaQuestion questionData = new TriviaQuestion
            {
                question = columns[1 + topicOffset],
                answers = new[]
                {
                    columns[2 + topicOffset], columns[3 + topicOffset],
                    columns[4 + topicOffset], columns[5 + topicOffset]
                },
                correctAnswerIndex = Mathf.Clamp(correctAnswerIndex, 0, 3),
                topic = hasTopicColumn ? columns[1] : "geral"
            };

            if (!IsValidQuestion(questionData))
                continue;

            questionsByDifficulty[Mathf.Clamp(difficulty, 1, 7) - 1].Add(questionData);
        }
    }

    // --- IQuestionSource, so a server-side MatchSession can pick questions without touching UI ---

    public int GetPoolSize(int difficultyLevel)
    {
        List<TriviaQuestion> pool = GetQuestionPool(difficultyLevel);
        return pool != null ? pool.Count : 0;
    }

    public int GetCorrectAnswerIndex(int difficultyLevel, int questionIndex)
    {
        List<TriviaQuestion> pool = GetQuestionPool(difficultyLevel);

        if (pool == null || questionIndex < 0 || questionIndex >= pool.Count)
            return -1;

        return pool[questionIndex].correctAnswerIndex;
    }

    private TriviaQuestion GetNextQuestionForCurrentDifficulty()
    {
        List<TriviaQuestion> pool = GetQuestionPool(currentDifficultyLevel);

        if (pool == null || pool.Count == 0)
            return null;

        if (pool.Count == 1)
        {
            currentQuestionIndex = 0;
            return pool[0];
        }

        int next;
        int attempts = 0;
        do
        {
            next = Random.Range(0, pool.Count);
            attempts++;
        }
        while (next == currentQuestionIndex && attempts < 10);

        currentQuestionIndex = next;
        return pool[currentQuestionIndex];
    }

    private List<TriviaQuestion> GetQuestionPool(int difficulty)
    {
        int index = Mathf.Clamp(difficulty, 1, 7) - 1;

        EnsureQuestionPools();

        if (questionsByDifficulty[index].Count > 0)
            return questionsByDifficulty[index];

        if (questionsByDifficulty[0].Count > 0)
            return questionsByDifficulty[0];

        return null;
    }

    // The pools are built in Awake, but a script recompile while the game is running reloads the
    // domain: private fields are rebuilt from their initialisers and Awake is NOT called again, so
    // the array comes back with seven null entries and the next question lookup throws. Cheap to
    // re-check, and it keeps an Editor recompile mid-session from breaking Start.
    private void EnsureQuestionPools()
    {
        for (int i = 0; i < questionsByDifficulty.Length; i++)
            if (questionsByDifficulty[i] == null)
                questionsByDifficulty[i] = new List<TriviaQuestion>();
    }

    private bool HasAnyQuestionSource()
    {
        EnsureQuestionPools();

        if (AnyPoolHasQuestions())
            return true;

        // Same reload case: the pools survive as empty lists, so the questions themselves are gone
        // too. Reading them again costs one file parse and only ever happens once.
        if (!hasRetriedQuestionLoad)
        {
            hasRetriedQuestionLoad = true;
            LoadQuestions();
        }

        return AnyPoolHasQuestions();
    }

    private bool AnyPoolHasQuestions()
    {
        for (int i = 0; i < questionsByDifficulty.Length; i++)
        {
            if (questionsByDifficulty[i].Count > 0)
                return true;
        }

        return false;
    }

    private bool IsValidQuestion(TriviaQuestion questionData)
    {
        return questionData != null &&
               !string.IsNullOrWhiteSpace(questionData.question) &&
               questionData.answers != null &&
               questionData.answers.Length >= 4;
    }
}
