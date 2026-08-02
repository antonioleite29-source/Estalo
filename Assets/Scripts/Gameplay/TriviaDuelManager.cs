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

public class TriviaDuelManager : MonoBehaviour
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
        SlideFromRight
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

    public static TriviaDuelManager Instance { get; private set; }

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

    [Tooltip("Color of the ring when it is your solo turn.")]
    public Color mySoloDonutColor = new Color32(248, 250, 252, 255);

    [Tooltip("Color of the ring when it is the other player's solo turn.")]
    public Color otherSoloDonutColor = new Color32(248, 113, 113, 255);

    [Header("--- SCORE DISPLAY ---")]
    [Tooltip("Text component that shows Team 1's current score.")]
    public TMP_Text team1ScoreText;

    [Tooltip("Text component that shows Team 2's current score.")]
    public TMP_Text team2ScoreText;

    [Tooltip("DEBUG ONLY — shows which player controls the mouse and the active difficulty level. Remove or hide this text before publishing your game.")]
    public TMP_Text debugMouseOwnerText;

    [Header("--- BUTTON APPEARANCE ---")]
    [Tooltip("The ButtonTheme ScriptableObject that controls how the answer buttons look (normal, hovered, correct answer, wrong answer). Create one via Right-click > Create > Trivia > Button Theme.")]
    public ButtonTheme buttonTheme;

    [Header("--- QUESTIONS ---")]
    [Tooltip("(Optional) Drag the TriviaQuestions.txt file here directly. If left empty, the game loads it from the Resources folder automatically.")]
    public TextAsset questionDocument;

    [Tooltip("The filename (without .tsv) of the question file inside Assets/Resources. Default is 'TriviaQuestions'. Only used if Question Document above is empty.")]
    public string questionDocumentResourceName = "TriviaQuestions";

    [Tooltip("(Optional) Images to show on the Learning page in the lobby. Drag your learning/study images here.")]
    public Sprite[] learningPageSprites;

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
    private Coroutine backgroundLoopCoroutine;
    private Coroutine returnToLobbyCoroutine;

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

    public void PrepareForLobby()
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
            lobbyPageSwitcher.ShowDefaultPage();

        if (LobbyScreenController.Instance != null)
            LobbyScreenController.Instance.ShowLobby();

        foreach (IQBarController bar in FindObjectsByType<IQBarController>(FindObjectsSortMode.None))
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
            GetLocalPlayerIdentity()?.RequestStartTrivia();
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
        UpdateDebugMouseOwnerUI();
        ResetDonuts();

        if (gameBackground != null)
            gameBackground.enabled = true;

        TriviaNetworkSync.Instance?.BroadcastMatchStarted();
        StartNextRound();
    }

    public void ApplyNetworkedMatchStarted()
    {
        BindAnswerButtons();

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
        UpdateDebugMouseOwnerUI();
        ResetDonuts();

        if (gameBackground != null)
            gameBackground.enabled = true;

        // A fresh match always opens on OpenBuzz, which is also RoundState's/the NetworkVariable's
        // own default value — so NetRoundState.OnValueChanged never fires for this transition.
        // Apply it explicitly here instead of relying on that callback.
        ApplyNetworkedRoundState((int)RoundState.OpenBuzz);
    }

    public void StartTriviaFromGameManagerButton()
    {
        StartTriviaFromLobby();
    }

    public void ReturnToLobbyFromGameManagerButton()
    {
        ReturnToLobby();
    }

    public Sprite[] GetLearningPageSprites()
    {
        List<Sprite> sprites = new List<Sprite>();

        if (learningPageSprites != null)
        {
            for (int i = 0; i < learningPageSprites.Length; i++)
            {
                if (learningPageSprites[i] != null)
                    sprites.Add(learningPageSprites[i]);
            }
        }

        if (sprites.Count == 0 && openBuzzBackground.backgroundSprite != null)
            sprites.Add(openBuzzBackground.backgroundSprite);

        return sprites.ToArray();
    }

    private void StartNextRound()
    {
        if (team1Score >= pointsToWin)
        {
            EndMatch("Team 1 Wins!", true, winnerSide: 1);
            return;
        }

        if (team2Score >= pointsToWin)
        {
            EndMatch("Team 2 Wins!", true, winnerSide: 2);
            return;
        }

        currentQuestion = GetNextQuestionForCurrentDifficulty();

        if (currentQuestion == null)
        {
            EndMatch("No questions available", false);
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
        UpdateDebugMouseOwnerUI();
        ResetInactivityTimer();
        SetButtonsAvailableNormal();
        TriviaNetworkSync.Instance?.BroadcastButtonsAvailable();

        if (questionText != null)
            questionText.text = currentQuestion.question;

        SetState(RoundState.OpenBuzz);
        inputEnabled = true;
        isTransitioning = false;
        PublishNetworkedStateIfServer();
    }

    private void PublishNetworkedStateIfServer()
    {
        TriviaNetworkSync.Instance?.PublishState((int)roundState, currentDifficultyLevel, currentQuestionIndex, currentDuelIndex, team1Score, team2Score);
    }

    public void ApplyNetworkedDuelIndex(int duelIndex)
    {
        if (duelPairs == null || duelIndex < 0 || duelIndex >= duelPairs.Length)
            return;

        currentDuelIndex = duelIndex;
        ApplyCurrentDuelUI();
    }

    public void ApplyNetworkedScore(int newTeam1Score, int newTeam2Score)
    {
        team1Score = newTeam1Score;
        team2Score = newTeam2Score;
        UpdateScoreUI();
    }

    public void ApplyNetworkedRoundState(int newRoundState)
    {
        roundState = (RoundState)newRoundState;
        RefreshBackgroundForCurrentState();
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
        UpdateDebugMouseOwnerUI();
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

    public void SubmitAnswerFromNetwork(int playerSide, int answerIndex)
    {
        if (IsAuthoritative)
            SubmitAnswer(playerSide, answerIndex);
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
        TriviaNetworkSync.Instance?.BroadcastAnswerMarked(answerIndex, true);
        AwardPointToPlayerSide(playerSide);
        UpdateScoreUI();
        PublishNetworkedStateIfServer();

        if (playerSide == 1 && leftPlayerNameText != null)
            leftPlayerNameText.text = "ACERTOU!";

        if (playerSide == 2 && rightPlayerNameText != null)
            rightPlayerNameText.text = "ACERTOU!";

        yield return new WaitForSeconds(correctResolveSeconds);

        if (team1Score >= pointsToWin)
        {
            EndMatch("Team 1 Wins!", true, winnerSide: 1);
            yield break;
        }

        if (team2Score >= pointsToWin)
        {
            EndMatch("Team 2 Wins!", true, winnerSide: 2);
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
        TriviaNetworkSync.Instance?.BroadcastAnswerMarked(answerIndex, false);

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
        TriviaNetworkSync.Instance?.BroadcastLockAllButtons();
    }

    private void BeginSoloForPlayer(int soloPlayerSide)
    {
        inputEnabled = true;
        isTransitioning = false;
        soloTimer = soloTimeSeconds;
        ResetInactivityTimer();
        SetButtonsAvailableNormal();
        TriviaNetworkSync.Instance?.BroadcastButtonsAvailable();

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
        PublishNetworkedStateIfServer();
    }

    private void EndSoloAndReturnToOpen()
    {
        inputEnabled = true;
        isTransitioning = false;
        ResetDonuts();
        RestorePlayerNames();
        SetButtonsAvailableNormal();
        TriviaNetworkSync.Instance?.BroadcastButtonsAvailable();
        ResetInactivityTimer();
        SetState(RoundState.OpenBuzz);
        PublishNetworkedStateIfServer();
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
            UpdateDebugMouseOwnerUI();
            RefreshBackgroundForCurrentState();
            RefreshPfpOpacity();
        }

        if (Keyboard.current.digit2Key.wasPressedThisFrame)
        {
            debugMouseOwner = 2;
            localViewPlayerSide = 2;
            UpdateDebugMouseOwnerUI();
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
                UpdateDebugMouseOwnerUI();
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
                EndMatch("Game ended: no attempts for 1 minute", false);
                return;
            }
        }

        if (roundState == RoundState.SoloLeft || roundState == RoundState.SoloRight)
        {
            soloTimer -= Time.deltaTime;
            UpdateSoloDonut();
            TriviaNetworkSync.Instance?.PublishSoloTimer(soloTimer);

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

    private void DeductPointFromPlayerSide(int playerSide)
    {
        int teamId = GetTeamIdForPlayerSide(playerSide);

        if (teamId == 1)
            team1Score--;
        else
            team2Score--;
    }

    private int GetTeamIdForPlayerSide(int playerSide)
    {
        // Player 1 = Team 1, Player 2 = Team 2.
        // DuelPlayer.teamId defaults to 1 for all players, which caused both sides to always score to Team 1.
        return Mathf.Clamp(playerSide, 1, 2);
    }

    private void UpdateScoreUI()
    {
        if (team1ScoreText != null)
            team1ScoreText.text = team1Score.ToString();

        if (team2ScoreText != null)
            team2ScoreText.text = team2Score.ToString();
    }

    private void UpdateDebugMouseOwnerUI()
    {
        if (debugMouseOwnerText != null)
            debugMouseOwnerText.text = string.Empty;
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

    // Lets other managers (e.g. TeamDuelManager) reuse the loaded question pool without duplicating
    // TSV parsing. lastQuestionIndex is the caller's own repeat-avoidance state, kept separate from
    // this manager's currentQuestionIndex so the two modes don't interfere with each other.
    internal TriviaQuestion GetNextQuestionForDifficulty(int difficultyLevel, ref int lastQuestionIndex)
    {
        List<TriviaQuestion> pool = GetQuestionPool(difficultyLevel);

        if (pool == null || pool.Count == 0)
            return null;

        if (pool.Count == 1)
        {
            lastQuestionIndex = 0;
            return pool[0];
        }

        int next;
        int attempts = 0;
        do
        {
            next = Random.Range(0, pool.Count);
            attempts++;
        }
        while (next == lastQuestionIndex && attempts < 10);

        lastQuestionIndex = next;
        return pool[lastQuestionIndex];
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

    private void SetTriviaUiVisible(bool isVisible)
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
        SetGraphicVisible(debugMouseOwnerText, isVisible);

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

    internal void MarkAnswerRight(int answerIndex)
    {
        if (answerButtonVisuals == null || answerIndex < 0 || answerIndex >= answerButtonVisuals.Length)
            return;

        if (answerButtonVisuals[answerIndex] != null)
            answerButtonVisuals[answerIndex].SetPressedRightState();
    }

    internal void MarkAnswerWrong(int answerIndex)
    {
        if (answerButtonVisuals == null || answerIndex < 0 || answerIndex >= answerButtonVisuals.Length)
            return;

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

    private void EndMatch(string message, bool hasWinner, int winnerSide = 0)
    {
        StopGameplayCoroutines(false);
        triviaRunning = false;
        inputEnabled = false;
        isTransitioning = false;
        SetState(RoundState.MatchEnded);
        LockAllButtons();
        TriviaNetworkSync.Instance?.BroadcastLockAllButtons();
        ResetDonuts();

        if (questionText != null)
            questionText.text = message;

        if (leftPlayerNameText != null)
            leftPlayerNameText.text = string.Empty;

        if (rightPlayerNameText != null)
            rightPlayerNameText.text = string.Empty;

        if (hasWinner && PlayerIQManager.Instance != null)
            PlayerIQManager.Instance.AdjustLocalIQAfterMatch(winnerSide == localViewPlayerSide);

        PublishNetworkedStateIfServer();
        TriviaNetworkSync.Instance?.BroadcastMatchEnded(message, hasWinner, winnerSide);

        if (hasWinner && returnToLobbyAfterWin)
            returnToLobbyCoroutine = StartCoroutine(ReturnToLobbyAfterDelay());
    }

    public void ApplyNetworkedMatchEnd(string message, bool hasWinner, int winnerSide)
    {
        if (questionText != null)
            questionText.text = message;

        if (leftPlayerNameText != null)
            leftPlayerNameText.text = string.Empty;

        if (rightPlayerNameText != null)
            rightPlayerNameText.text = string.Empty;

        if (hasWinner && PlayerIQManager.Instance != null)
            PlayerIQManager.Instance.AdjustLocalIQAfterMatch(winnerSide == localViewPlayerSide);

        if (hasWinner && returnToLobbyAfterWin)
            returnToLobbyCoroutine = StartCoroutine(ReturnToLobbyAfterDelay());
    }

    private IEnumerator ReturnToLobbyAfterDelay()
    {
        yield return new WaitForSeconds(Mathf.Max(0f, returnToLobbyDelaySeconds));
        returnToLobbyCoroutine = null;
        ReturnToLobby();
    }

    private void ReturnToLobby()
    {
        showLobbyOnStart = true;
        PrepareForLobby();
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
                ApplyStateBackground(matchEndedBackground, defaultBackgroundColor);
                break;
        }
    }

    private void ApplyStateBackground(StateBackgroundVisual visual, Color fallbackColor)
    {
        ApplyVisualTextColors(visual, fallbackColor);

        if (visual == null || visual.backgroundSprite == null || gameBackground == null)
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

        backgroundTransitionCoroutine = StartCoroutine(AnimateStaticBackground(visual));
    }

    private IEnumerator AnimateStaticBackground(StateBackgroundVisual visual)
    {
        gameBackground.enabled = true;
        gameBackground.sprite = visual.backgroundSprite;
        gameBackground.type = Image.Type.Simple;
        gameBackground.preserveAspect = false;
        StretchImageToFill(gameBackground.rectTransform);

        float duration = Mathf.Max(0.01f, visual.animationSeconds);
        Vector2 startOffset = Vector2.zero;
        float startScale = 1f;

        switch (visual.animation)
        {
            case BackgroundAnimation.Pop:
                startScale = 0.94f;
                break;
            case BackgroundAnimation.SlideFromLeft:
                startOffset = new Vector2(-80f, 0f);
                break;
            case BackgroundAnimation.SlideFromRight:
                startOffset = new Vector2(80f, 0f);
                break;
        }

        RectTransform rect = gameBackground.rectTransform;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            float eased = 1f - Mathf.Pow(1f - t, 3f);

            if (visual.animation == BackgroundAnimation.Fade || visual.animation == BackgroundAnimation.Pop || visual.animation == BackgroundAnimation.SlideFromLeft || visual.animation == BackgroundAnimation.SlideFromRight)
            {
                gameBackground.color = new Color(1f, 1f, 1f, eased);
                rect.anchoredPosition = Vector2.Lerp(startOffset, Vector2.zero, eased);
                rect.localScale = GetMirroredScale(Vector3.one * Mathf.Lerp(startScale, 1f, eased));
            }
            else
            {
                gameBackground.color = Color.white;
                rect.anchoredPosition = Vector2.zero;
                rect.localScale = GetMirroredScale(Vector3.one);
            }

            yield return null;
        }

        gameBackground.color = Color.white;
        rect.anchoredPosition = Vector2.zero;
        rect.localScale = GetMirroredScale(Vector3.one);
        StartBackgroundLoop(visual);
        backgroundTransitionCoroutine = null;
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
        StretchImageToFill(gameBackground.rectTransform);
        gameBackground.rectTransform.localScale = GetMirroredScale(Vector3.one);
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
        go.transform.SetParent(parent, false);
        go.transform.SetSiblingIndex(siblingIndex);

        Image image = go.GetComponent<Image>();
        image.type = Image.Type.Simple;
        image.preserveAspect = false;
        image.color = Color.white;
        StretchImageToFill(image.rectTransform);
        return image;
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
            gameBackground.rectTransform.localScale = GetMirroredScale(Vector3.one);
            gameBackground.rectTransform.anchoredPosition = Vector2.zero;
        }
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
        return new Vector3(localViewPlayerSide == 2 ? -Mathf.Abs(scale.x) : Mathf.Abs(scale.x), scale.y, scale.z);
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
        ApplyTextColor(debugMouseOwnerText, secondaryColor);
    }

    private void ApplyTextColor(TMP_Text text, Color color)
    {
        if (text == null)
            return;

        text.color = color;
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

            if (columns.Length < 7)
                continue;

            if (!int.TryParse(columns[0], out int difficulty))
                continue;

            if (!int.TryParse(columns[6], out int correctAnswerIndex))
                continue;

            TriviaQuestion questionData = new TriviaQuestion
            {
                question = columns[1],
                answers = new[] { columns[2], columns[3], columns[4], columns[5] },
                correctAnswerIndex = Mathf.Clamp(correctAnswerIndex, 0, 3)
            };

            if (!IsValidQuestion(questionData))
                continue;

            questionsByDifficulty[Mathf.Clamp(difficulty, 1, 7) - 1].Add(questionData);
        }
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

        if (questionsByDifficulty[index].Count > 0)
            return questionsByDifficulty[index];

        if (questionsByDifficulty[0].Count > 0)
            return questionsByDifficulty[0];

        return null;
    }

    private bool HasAnyQuestionSource()
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
