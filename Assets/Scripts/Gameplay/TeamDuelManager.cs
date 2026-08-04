using System.Collections;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

public class TeamDuelManager : MonoBehaviour
{
    internal enum RoundState
    {
        OpenBuzz,
        SoloActiveA,
        SoloActiveB,
        Resolving,
        MatchEnded
    }

    public static TeamDuelManager Instance { get; private set; }

    [Header("--- LOBBY ---")]
    public GameObject lobbyRootObject;
    public LobbyPageSwitcher lobbyPageSwitcher;
    public GameObject teamGameplayRoot;

    [Tooltip("Drag the BottomBar GameObject here. It hides when a match starts and reappears when you return to the lobby.")]
    public GameObject bottomBar;

    public bool returnToLobbyAfterWin = true;
    public float returnToLobbyDelaySeconds = 2f;

    [Header("--- QUESTION & ANSWER BUTTONS ---")]
    public TMP_Text questionText;
    public Button[] answerButtons;
    public AnswerButtonVisual[] answerButtonVisuals;

    [Header("--- 4 PLAYER SLOTS (index 0-3 = slot 1-4) ---")]
    public TMP_Text[] slotNameTexts = new TMP_Text[4];
    public Image[] slotPfpImages = new Image[4];
    public GameObject[] slotActiveIndicators = new GameObject[4];
    public Image[] slotSoloDonuts = new Image[4];

    [Header("--- SCORE DISPLAY ---")]
    public TMP_Text teamAScoreText;
    public TMP_Text teamBScoreText;

    [Header("--- TIMING (in seconds) ---")]
    public float soloTimeSeconds = 3f;
    public float wrongFlashSeconds = 0.5f;
    public float correctResolveSeconds = 1f;
    public float nextRoundDelaySeconds = 0.35f;
    public float inactivityEndSeconds = 60f;

    [Header("--- RULES ---")]
    public int pointsToWin = 9;

    public int LocalAssignedSlot { get; private set; }

    private RoundState roundState = RoundState.MatchEnded;
    private TriviaQuestion currentQuestion;
    private int currentDifficultyLevel = 1;
    private int currentQuestionIndex = -1;
    private int teamAScore;
    private int teamBScore;
    private int activeSlotA = 1;
    private int activeSlotB = 3;
    private int roundNumber;
    private bool triviaRunning;
    private bool inputEnabled;
    private bool isTransitioning;
    private float soloTimer;
    private float inactivityTimer;
    private readonly string[] slotBaseNames = new string[4];

    private Coroutine stateCoroutine;
    private Coroutine returnToLobbyCoroutine;

    private void Awake()
    {
        Instance = this;

        if (questionText != null)
        {
            questionText.enableAutoSizing = true;
            questionText.fontSizeMax = questionText.fontSize;
            questionText.fontSizeMin = 8f;
            questionText.overflowMode = TMPro.TextOverflowModes.Truncate;
        }
    }

    private void Update()
    {
        if (!triviaRunning || roundState == RoundState.MatchEnded)
            return;

        if (IsAuthoritative)
            UpdateRoundTimers();
    }

    private bool IsAuthoritative =>
        NetworkManager.Singleton == null ||
        !NetworkManager.Singleton.IsListening ||
        NetworkManager.Singleton.IsServer;

    private bool IsNetworkedSession =>
        NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;

    private PlayerSideIdentity GetLocalPlayerIdentity()
    {
        if (NetworkManager.Singleton == null || NetworkManager.Singleton.LocalClient == null)
            return null;

        NetworkObject localPlayerObject = NetworkManager.Singleton.LocalClient.PlayerObject;
        return localPlayerObject != null ? localPlayerObject.GetComponent<PlayerSideIdentity>() : null;
    }

    public void RegisterLocalSlot(int slot)
    {
        LocalAssignedSlot = slot;
        RefreshPfpOpacity();
    }

    public void StartTeamTriviaFromLobby()
    {
        if (answerButtons == null || answerButtons.Length != 4 || answerButtonVisuals == null || answerButtonVisuals.Length != 4)
        {
            Debug.LogError("TeamDuelManager: Answer Buttons / Answer Button Visuals must contain exactly 4 entries.");
            return;
        }

        if (IsNetworkedSession)
        {
            GetLocalPlayerIdentity()?.RequestStartTeamTrivia();
            return;
        }

        // 2v2 has no offline mode at all, so with no session there is nothing to start. Falling
        // through here used to open a local match against placeholder team-mates.
        Debug.LogError("StartTeamTriviaFromLobby: not connected to a session, so there is nobody to " +
                       "play with. The host most likely failed to start — check the Console for a " +
                       "transport bind failure on the connect port.");

        if (lobbyPageSwitcher != null)
        {
            lobbyPageSwitcher.waitingScreen?.HideWaiting();
            lobbyPageSwitcher.ShowConnectPage();
        }
    }

    public void BeginMatchAuthoritative()
    {
        // Re-bind every time this mode's match actually starts — the answer buttons are shared
        // with TriviaDuelManager (only one mode is ever active at a time), so whichever mode
        // starts most recently needs to "claim" the click listeners back.
        BindAnswerButtons();

        StopGameplayCoroutines();
        triviaRunning = true;
        inputEnabled = false;
        isTransitioning = false;
        currentQuestion = null;
        currentQuestionIndex = -1;
        roundNumber = 0;
        teamAScore = 0;
        teamBScore = 0;

        if (PlayerIQManager.Instance != null)
            currentDifficultyLevel = PlayerIQManager.Instance.GetLocalDifficultyLevel();

        if (lobbyRootObject != null)
            lobbyRootObject.SetActive(false);

        if (LobbyScreenController.Instance != null)
            LobbyScreenController.Instance.HideLobby();

        if (teamGameplayRoot != null)
            teamGameplayRoot.SetActive(true);

        if (bottomBar != null)
            bottomBar.SetActive(false);

        if (questionText != null)
            questionText.text = string.Empty;

        UpdateScoreUI();

        TriviaNetworkSync.Instance?.BroadcastTeamMatchStarted();
        StartNextRound();
    }

    public void ApplyNetworkedMatchStarted()
    {

        // Match has formed — take down the queue/waiting screen before showing the gameplay UI.
        if (lobbyPageSwitcher != null && lobbyPageSwitcher.waitingScreen != null)
            lobbyPageSwitcher.waitingScreen.HideWaiting();

        BindAnswerButtons();

        if (lobbyRootObject != null)
            lobbyRootObject.SetActive(false);

        if (LobbyScreenController.Instance != null)
            LobbyScreenController.Instance.HideLobby();

        if (teamGameplayRoot != null)
            teamGameplayRoot.SetActive(true);

        if (bottomBar != null)
            bottomBar.SetActive(false);

        if (questionText != null)
            questionText.text = string.Empty;

        ApplyNetworkedRoundState((int)RoundState.OpenBuzz);
    }

    private void StartNextRound()
    {
        if (teamAScore >= pointsToWin)
        {
            EndMatch("Team A Wins!", true, winningTeam: 1);
            return;
        }

        if (teamBScore >= pointsToWin)
        {
            EndMatch("Team B Wins!", true, winningTeam: 2);
            return;
        }

        currentQuestion = TriviaDuelManager.Instance != null
            ? TriviaDuelManager.Instance.GetNextQuestionForDifficulty(currentDifficultyLevel, ref currentQuestionIndex)
            : null;

        if (currentQuestion == null)
        {
            EndMatch("No questions available", false, 0);
            return;
        }

        roundNumber++;
        bool useFirstPairing = (roundNumber % 2) == 1;
        activeSlotA = useFirstPairing ? 1 : 2;
        activeSlotB = useFirstPairing ? 3 : 4;

        UpdateActiveSlotIndicators();
        ResetDonuts();
        RestoreSlotNames();
        UpdateScoreUI();
        ResetInactivityTimer();
        SetButtonsAvailableNormal();
        TriviaNetworkSync.Instance?.BroadcastTeamButtonsAvailable();

        if (TriviaDuelManager.Instance != null)
            TriviaDuelManager.Instance.ApplyQuestionVisualsTo(questionText, answerButtonVisuals, currentQuestion);

        SetState(RoundState.OpenBuzz);
        inputEnabled = true;
        isTransitioning = false;
        PublishNetworkedStateIfServer();
    }

    private void PublishNetworkedStateIfServer()
    {
        TriviaNetworkSync.Instance?.PublishTeamState((int)roundState, currentDifficultyLevel, currentQuestionIndex, activeSlotA, activeSlotB, teamAScore, teamBScore);
    }

    public void ApplyNetworkedScore(int newTeamAScore, int newTeamBScore)
    {
        teamAScore = newTeamAScore;
        teamBScore = newTeamBScore;
        UpdateScoreUI();
    }

    public void ApplyNetworkedRoundState(int newRoundState)
    {
        roundState = (RoundState)newRoundState;

        // The donuts are only redrawn while a solo is ticking, so leaving a solo used to leave the
        // last frame of the ring frozen on screen into the next round. Nothing else ever cleared it:
        // the server stops sending timer updates the moment the solo ends.
        if (roundState != RoundState.SoloActiveA && roundState != RoundState.SoloActiveB)
            soloTimer = 0f;

        UpdateSoloDonuts();
    }

    public void ApplyNetworkedActiveSlots(int newActiveSlotA, int newActiveSlotB)
    {
        activeSlotA = newActiveSlotA;
        activeSlotB = newActiveSlotB;
        UpdateActiveSlotIndicators();
    }

    public void ApplyNetworkedQuestion(int difficultyLevel, int questionIndex)
    {
        if (questionIndex < 0 || TriviaDuelManager.Instance == null)
            return;

        currentDifficultyLevel = difficultyLevel;
        currentQuestionIndex = questionIndex;
        currentQuestion = TriviaDuelManager.Instance.GetQuestionAt(difficultyLevel, questionIndex);

        if (currentQuestion != null)
            TriviaDuelManager.Instance.ApplyQuestionVisualsTo(questionText, answerButtonVisuals, currentQuestion);
    }

    public void ApplySoloTimerVisual(float remaining)
    {
        soloTimer = remaining;
        UpdateSoloDonuts();
    }

    public void ApplyNetworkedTeamPlayerIdentity(int slot, string playerName, Sprite avatarSprite)
    {
        int index = slot - 1;

        if (index < 0 || index >= 4)
            return;

        slotBaseNames[index] = playerName;

        if (slotNameTexts != null && index < slotNameTexts.Length && slotNameTexts[index] != null)
            slotNameTexts[index].text = playerName;

        if (slotPfpImages != null && index < slotPfpImages.Length && slotPfpImages[index] != null)
            slotPfpImages[index].sprite = avatarSprite;

        RefreshPfpOpacity();
    }

    // Dims every player picture except your own, so it's obvious at a glance which slot is you.
    private void RefreshPfpOpacity()
    {
        if (slotPfpImages == null)
            return;

        for (int i = 0; i < slotPfpImages.Length; i++)
        {
            if (slotPfpImages[i] == null)
                continue;

            Color color = slotPfpImages[i].color;
            color.a = (i + 1) == LocalAssignedSlot ? 1f : 0.5f;
            slotPfpImages[i].color = color;
        }
    }

    private void OnAnswerButtonClicked(int answerIndex)
    {
        if (currentQuestion == null)
            return;

        PlayerSideIdentity localIdentity = GetLocalPlayerIdentity();
        localIdentity?.RequestSubmitTeamAnswer(answerIndex);
    }

    public void SubmitAnswerFromNetwork(int slot, int answerIndex)
    {
        if (IsAuthoritative)
            SubmitAnswer(slot, answerIndex);
    }

    private void SubmitAnswer(int slot, int answerIndex)
    {
        if (!IsSlotAllowedToAnswer(slot))
            return;

        if (answerIndex < 0 || answerIndex > 3)
            return;

        ResetInactivityTimer();

        if (answerIndex == currentQuestion.correctAnswerIndex)
            stateCoroutine = StartCoroutine(HandleCorrectAnswer(slot, answerIndex));
        else
            stateCoroutine = StartCoroutine(HandleWrongAnswer(slot, answerIndex));
    }

    private bool IsSlotAllowedToAnswer(int slot)
    {
        if (!triviaRunning || !inputEnabled || isTransitioning)
            return false;

        bool isCurrentlyActiveSlot = slot == activeSlotA || slot == activeSlotB;

        if (!isCurrentlyActiveSlot)
            return false;

        switch (roundState)
        {
            case RoundState.OpenBuzz:
                return true;
            case RoundState.SoloActiveA:
                return slot == activeSlotA;
            case RoundState.SoloActiveB:
                return slot == activeSlotB;
            default:
                return false;
        }
    }

    private IEnumerator HandleCorrectAnswer(int slot, int answerIndex)
    {
        BeginResolve();
        MarkAnswerRight(answerIndex);
        TriviaNetworkSync.Instance?.BroadcastTeamAnswerMarked(answerIndex, true);
        AwardPointToTeamForSlot(slot);
        UpdateScoreUI();
        PublishNetworkedStateIfServer();

        yield return new WaitForSeconds(correctResolveSeconds);

        if (teamAScore >= pointsToWin)
        {
            EndMatch("Team A Wins!", true, winningTeam: 1);
            yield break;
        }

        if (teamBScore >= pointsToWin)
        {
            EndMatch("Team B Wins!", true, winningTeam: 2);
            yield break;
        }

        yield return new WaitForSeconds(nextRoundDelaySeconds);
        stateCoroutine = null;
        StartNextRound();
    }

    private IEnumerator HandleWrongAnswer(int slot, int answerIndex)
    {
        bool wasInSolo = roundState == RoundState.SoloActiveA || roundState == RoundState.SoloActiveB;

        BeginResolve();
        MarkAnswerWrong(answerIndex);
        TriviaNetworkSync.Instance?.BroadcastTeamAnswerMarked(answerIndex, false);

        yield return new WaitForSeconds(wrongFlashSeconds);

        stateCoroutine = null;

        if (wasInSolo)
            EndSoloAndReturnToOpen();
        else
            BeginSoloForSlot(slot == activeSlotA ? activeSlotB : activeSlotA);
    }

    private void BeginResolve()
    {
        inputEnabled = false;
        isTransitioning = true;
        SetState(RoundState.Resolving);
        LockAllButtons();
        TriviaNetworkSync.Instance?.BroadcastTeamLockAllButtons();
    }

    private void BeginSoloForSlot(int soloSlot)
    {
        inputEnabled = true;
        isTransitioning = false;
        soloTimer = soloTimeSeconds;
        ResetInactivityTimer();
        SetButtonsAvailableNormal();
        TriviaNetworkSync.Instance?.BroadcastTeamButtonsAvailable();

        SetState(soloSlot == activeSlotA ? RoundState.SoloActiveA : RoundState.SoloActiveB);
        UpdateSoloDonuts();
        PublishNetworkedStateIfServer();
    }

    private void EndSoloAndReturnToOpen()
    {
        inputEnabled = true;
        isTransitioning = false;
        ResetDonuts();
        SetButtonsAvailableNormal();
        TriviaNetworkSync.Instance?.BroadcastTeamButtonsAvailable();
        ResetInactivityTimer();
        SetState(RoundState.OpenBuzz);
        PublishNetworkedStateIfServer();
    }

    private void UpdateRoundTimers()
    {
        if (isTransitioning)
            return;

        bool runInactivity = roundState == RoundState.OpenBuzz || roundState == RoundState.SoloActiveA || roundState == RoundState.SoloActiveB;

        if (runInactivity)
        {
            inactivityTimer -= Time.deltaTime;

            if (inactivityTimer <= 0f)
            {
                inactivityTimer = 0f;
                EndMatch("Game ended: no attempts for 1 minute", false, 0);
                return;
            }
        }

        if (roundState == RoundState.SoloActiveA || roundState == RoundState.SoloActiveB)
        {
            soloTimer -= Time.deltaTime;
            UpdateSoloDonuts();
            TriviaNetworkSync.Instance?.PublishTeamSoloTimer(soloTimer);

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

    private void AwardPointToTeamForSlot(int slot)
    {
        if (PlayerSideIdentity.TeamForSlot(slot) == 1)
            teamAScore++;
        else
            teamBScore++;
    }

    private void UpdateScoreUI()
    {
        if (teamAScoreText != null)
            teamAScoreText.text = teamAScore.ToString();

        if (teamBScoreText != null)
            teamBScoreText.text = teamBScore.ToString();
    }

    private void RestoreSlotNames()
    {
        for (int i = 0; i < 4; i++)
        {
            if (slotNameTexts != null && i < slotNameTexts.Length && slotNameTexts[i] != null)
                slotNameTexts[i].text = slotBaseNames[i];
        }
    }

    private void UpdateActiveSlotIndicators()
    {
        if (slotActiveIndicators == null)
            return;

        for (int i = 0; i < slotActiveIndicators.Length; i++)
        {
            if (slotActiveIndicators[i] != null)
                slotActiveIndicators[i].SetActive(i == activeSlotA - 1 || i == activeSlotB - 1);
        }
    }

    private void ResetDonuts()
    {
        if (slotSoloDonuts == null)
            return;

        for (int i = 0; i < slotSoloDonuts.Length; i++)
        {
            if (slotSoloDonuts[i] == null)
                continue;

            slotSoloDonuts[i].fillAmount = 1f;
            slotSoloDonuts[i].gameObject.SetActive(false);
        }
    }

    private void UpdateSoloDonuts()
    {
        if (slotSoloDonuts == null)
            return;

        float fill = soloTimeSeconds <= 0f ? 0f : Mathf.Clamp01(soloTimer / soloTimeSeconds);
        int soloSlotIndex = -1;

        if (roundState == RoundState.SoloActiveA)
            soloSlotIndex = activeSlotA - 1;
        else if (roundState == RoundState.SoloActiveB)
            soloSlotIndex = activeSlotB - 1;

        for (int i = 0; i < slotSoloDonuts.Length; i++)
        {
            if (slotSoloDonuts[i] == null)
                continue;

            bool isVisible = i == soloSlotIndex;
            slotSoloDonuts[i].gameObject.SetActive(isVisible);

            if (isVisible)
                slotSoloDonuts[i].fillAmount = fill;
        }
    }

    private void SetState(RoundState newState)
    {
        roundState = newState;
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

    public bool IsMatchRunning => triviaRunning;

    // Server-side counterpart to TriviaDuelManager.AbortMatchForDisconnect. Losing any one of the
    // four players strands the match the same way: the active slot for that player never answers.
    public void AbortMatchForDisconnect(string message)
    {
        if (!triviaRunning)
            return;

        EndMatch(message, false, 0);
        TriviaNetworkSync.Instance?.BroadcastTeamMatchAborted(message);

        if (returnToLobbyCoroutine == null && returnToLobbyAfterWin)
            returnToLobbyCoroutine = StartCoroutine(ReturnToLobbyAfterDelay());
    }

    public void ApplyNetworkedMatchAborted(string message)
    {
        if (questionText != null)
            questionText.text = message;

        if (returnToLobbyCoroutine == null && returnToLobbyAfterWin)
            returnToLobbyCoroutine = StartCoroutine(ReturnToLobbyAfterDelay());
    }

    private void EndMatch(string message, bool hasWinner, int winningTeam)
    {
        StopGameplayCoroutines(false);
        triviaRunning = false;
        inputEnabled = false;
        isTransitioning = false;
        SetState(RoundState.MatchEnded);
        LockAllButtons();
        TriviaNetworkSync.Instance?.BroadcastTeamLockAllButtons();
        ResetDonuts();

        if (questionText != null)
            questionText.text = message;

        if (hasWinner && PlayerIQManager.Instance != null)
            PlayerIQManager.Instance.AdjustLocalIQAfterMatch(PlayerSideIdentity.TeamForSlot(LocalAssignedSlot) == winningTeam);

        PublishNetworkedStateIfServer();
        TriviaNetworkSync.Instance?.BroadcastTeamMatchEnded(message, hasWinner, winningTeam);

        if (hasWinner && returnToLobbyAfterWin)
            returnToLobbyCoroutine = StartCoroutine(ReturnToLobbyAfterDelay());
    }

    public void ApplyNetworkedMatchEnd(string message, bool hasWinner, int winningTeam)
    {
        if (questionText != null)
            questionText.text = message;

        if (hasWinner && PlayerIQManager.Instance != null)
            PlayerIQManager.Instance.AdjustLocalIQAfterMatch(PlayerSideIdentity.TeamForSlot(LocalAssignedSlot) == winningTeam);

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
        triviaRunning = false;

        if (teamGameplayRoot != null)
            teamGameplayRoot.SetActive(false);

        if (bottomBar != null)
            bottomBar.SetActive(true);

        if (lobbyRootObject != null)
            lobbyRootObject.SetActive(true);

        if (lobbyPageSwitcher != null)
            lobbyPageSwitcher.ShowDefaultPage();

        if (LobbyScreenController.Instance != null)
            LobbyScreenController.Instance.ShowLobby();
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

}
