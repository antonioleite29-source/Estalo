using System.Collections;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

// The 2v2 screen, and only the screen. Every rule -- who may answer, who scores, when a solo
// starts, when the match ends -- belongs to MatchSession on the server, which sends the result to
// this match's four players through TriviaNetworkSync. Everything here is an Apply*/Register*
// method that draws what arrived.
//
// It used to hold a full copy of the rules as well, run locally. That copy became unreachable the
// moment matchmaking started running several matches at once, and a second set of rules that
// nobody executes is a second set of rules to keep in step -- so it is gone.
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

    [Tooltip("The board image behind 2v2 gameplay. This is NOT the same object as the 1v1 " +
             "background — team mode has its own root with its own Image — which is why flipping " +
             "the 1v1 one had no effect here. Leave empty and the first Image directly under Team " +
             "Gameplay Root is used.")]
    public Image teamBackground;

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

    [Header("--- MATCH START ANIMATION ---")]
    [Tooltip("The exported sequence played when a 2v2 forms. Leave empty and the board opens " +
             "immediately, exactly as it did before. It plays on the same overlay the 1v1 uses, " +
             "which is why there is no second overlay slot here.")]
    public Sprite[] matchStartFrames;

    [Tooltip("Playback rate for those frames. Match what the timeline was authored at.")]
    public float matchStartFramesPerSecond = 24f;

    [Tooltip("Beat between the match forming and the animation starting. A moment of the waiting " +
             "screen still being there is what makes the cut read as 'found them'.")]
    public float matchStartDelaySeconds = 0.2f;

    public int LocalAssignedSlot { get; private set; }

    private RoundState roundState = RoundState.MatchEnded;
    private TriviaQuestion currentQuestion;
    private int teamAScore;
    private int teamBScore;
    private int activeSlotA = 1;
    private int activeSlotB = 3;
    private float soloTimer;
    private readonly string[] slotBaseNames = new string[4];

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

        // Slots 1 and 2 are Team A, 3 and 4 are Team B.
        int team = PlayerSideIdentity.TeamForSlot(slot);

        // Still handed to TriviaDuelManager so anything keyed off "which side am I" agrees across
        // modes, but the flip itself has to be done here: that call mirrors the 1v1 background,
        // which is on a different root and is not on screen during a team match.
        TriviaDuelManager.Instance?.SetLocalViewSideForTeams(team == 1 ? 2 : 1);

        ApplyTeamBackgroundSide(team);
    }

    private Image ResolveTeamBackground()
    {
        if (teamBackground != null)
            return teamBackground;

        if (teamGameplayRoot == null)
            return null;

        // The board sits first under the root, ahead of the answer buttons and the player slots.
        // Cached once found, so this walk happens at most once per session.
        foreach (Transform child in teamGameplayRoot.transform)
        {
            Image candidate = child.GetComponent<Image>();

            if (candidate != null)
            {
                teamBackground = candidate;
                return candidate;
            }
        }

        return null;
    }

    // Mirrors the 2v2 board so each player sees it from their own end.
    private void ApplyTeamBackgroundSide(int team)
    {
        Image background = ResolveTeamBackground();

        if (background == null)
        {
            Debug.LogWarning("TeamDuelManager: no team background found, so the board cannot be " +
                             "flipped per side. Assign Team Background on this component.", this);
            return;
        }

        Vector3 scale = background.rectTransform.localScale;

        // Only the SIGN of x is touched. The magnitude is whatever the scene set it to — the 1v1
        // background is sized by a 15 x 30 localScale, and normalising that to 1 is what collapsed
        // it to 78 pixels once already.
        float x = Mathf.Abs(scale.x);

        background.rectTransform.localScale = new Vector3(team == 1 ? x : -x, scale.y, scale.z);
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

    public void ApplyNetworkedMatchStarted()
    {
        // Same reasoning as the 1v1 board: ApplyNetworkedScore reads a rise off these, so leftovers
        // from the last match would swallow the first points of this one.
        teamAScore = 0;
        teamBScore = 0;

        if (matchStartCoroutine != null)
            StopCoroutine(matchStartCoroutine);

        matchStartCoroutine = StartCoroutine(MatchStartSequence());
    }

    private Coroutine matchStartCoroutine;

    // The board is revealed underneath the animation rather than before it. Doing it the other way
    // shows the board for a frame before the overlay covers it, which is the flicker this exists
    // to avoid.
    private IEnumerator MatchStartSequence()
    {
        // Pin the waiting screen at "found" first. The queue these four came from is already
        // empty, so left live it would count itself down to 0 / 4 while the opening plays over it.
        if (lobbyPageSwitcher != null && lobbyPageSwitcher.waitingScreen != null)
            lobbyPageSwitcher.waitingScreen.FreezeOnMatchFound();

        if (matchStartDelaySeconds > 0f)
            yield return new WaitForSecondsRealtime(matchStartDelaySeconds);

        TriviaDuelManager overlay = TriviaDuelManager.Instance;

        if (overlay != null && matchStartFrames != null && matchStartFrames.Length > 0)
            yield return overlay.PlayOverlayFrames(matchStartFrames, matchStartFramesPerSecond, false);

        RevealBoard();

        // Taken down after the reveal, not before: hiding it first would uncover the board while
        // this frame is still on screen, which is the same flicker in the other direction.
        overlay?.HideOverlay();

        matchStartCoroutine = null;
    }

    private void RevealBoard()
    {
        // Every lobby page off. lobbyRootObject is empty in this scene, so relying on it would
        // leave the lobby drawn on top of the board.
        if (lobbyPageSwitcher != null)
            lobbyPageSwitcher.HideAllPages();

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

    public void ApplyNetworkedScore(int newTeamAScore, int newTeamBScore)
    {
        // Same reasoning as the 1v1 board: the server sends both scores, so only the rise says
        // who scored, and only a rise is a point at all.
        bool teamAScored = newTeamAScore > teamAScore;
        bool teamBScored = newTeamBScore > teamBScore;

        teamAScore = newTeamAScore;
        teamBScore = newTeamBScore;
        UpdateScoreUI();

        if (!teamAScored && !teamBScored)
            return;

        int scoringTeam = teamAScored ? 1 : 2;
        bool byMyTeam = PlayerSideIdentity.TeamForSlot(LocalAssignedSlot) == scoringTeam;

        MatchSounds.PlayScored(byMyTeam);

        // Borrowed rather than reimplemented, so the pulse is the same length and the same two
        // colours in both modes and only has to be tuned in one place.
        TriviaDuelManager.Instance?.FlashScore(teamAScored ? teamAScoreText : teamBScoreText, byMyTeam);
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

            bool isLocal = (i + 1) == LocalAssignedSlot;

            Color color = slotPfpImages[i].color;
            color.a = isLocal ? 1f : 0.5f;
            slotPfpImages[i].color = color;

            // Borrowed from TriviaDuelManager rather than reimplemented, so the ring is the same
            // colour and thickness in both modes and only has to be tuned in one place.
            TriviaDuelManager.Instance?.ApplyLocalPlayerOutline(slotPfpImages[i], isLocal);
        }
    }

    private void OnAnswerButtonClicked(int answerIndex)
    {
        if (currentQuestion == null)
            return;

        PlayerSideIdentity localIdentity = GetLocalPlayerIdentity();
        localIdentity?.RequestSubmitTeamAnswer(answerIndex);
    }

    private void UpdateScoreUI()
    {
        if (teamAScoreText != null)
            teamAScoreText.text = teamAScore.ToString();

        if (teamBScoreText != null)
            teamBScoreText.text = teamBScore.ToString();
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
            ButtonClickSound.Play(ButtonClickSound.Note.C);

        if (answerButtonVisuals[answerIndex] != null)
            answerButtonVisuals[answerIndex].SetPressedWrongState();
    }

    public void ApplyNetworkedMatchEnd(string message, bool hasWinner, int winningTeam)
    {
        // The winning team's own solo animation introduces the end screen, the same way it does
        // in 1v1 — team mode has no background system of its own and borrows that one.
        if (hasWinner)
            TriviaDuelManager.Instance?.PlayWinnerBackground(winningTeam);

        if (questionText != null)
            questionText.text = message;

        if (hasWinner)
            MatchSounds.PlayEnded(PlayerSideIdentity.TeamForSlot(LocalAssignedSlot) == winningTeam);

        if (hasWinner && PlayerIQManager.Instance != null)
            PlayerIQManager.Instance.AdjustLocalIQAfterMatch(PlayerSideIdentity.TeamForSlot(LocalAssignedSlot) == winningTeam);

        // NOT gated on hasWinner any more. A match that ends without one — the inactivity
        // timeout, or an opponent abandoning — used to skip this entirely and leave the player
        // parked on the end screen with no way back except quitting. Whether to return is the
        // returnToLobbyAfterWin preference; whether somebody won is beside the point.
        if (returnToLobbyAfterWin)
            StartCoroutine(ReturnToLobbyAfterDelay());
    }

    private IEnumerator ReturnToLobbyAfterDelay()
    {
        yield return new WaitForSeconds(Mathf.Max(0f, returnToLobbyDelaySeconds));
        ReturnToLobby();
    }

    private void ReturnToLobby()
    {
        if (teamGameplayRoot != null)
            teamGameplayRoot.SetActive(false);

        if (bottomBar != null)
            bottomBar.SetActive(true);

        if (lobbyRootObject != null)
            lobbyRootObject.SetActive(true);

        // The Lobby page by name, not Default Page — that one is Profile, and a finished 2v2 has
        // no business ending up there.
        if (lobbyPageSwitcher != null)
            lobbyPageSwitcher.ShowLobbyPage();

        if (LobbyScreenController.Instance != null)
            LobbyScreenController.Instance.ShowLobby();
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
