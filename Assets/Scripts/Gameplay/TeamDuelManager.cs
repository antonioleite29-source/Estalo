using System.Collections;
using System.Collections.Generic;
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

    [Tooltip("Colour of the question on the 2v2 board. Applied every time a match opens, so it " +
             "cannot be lost to whatever the label happened to be left at in the scene.")]
    public Color questionColor = Color.white;
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

    [Header("--- SOLO TRANSITION ---")]
    [Tooltip("Played when somebody on YOUR team gets a solo turn. The same list runs backwards on " +
             "the way out, so a solo animates in both directions from one export.")]
    public Sprite[] yourSoloFrames;

    [Tooltip("Played when somebody on the OTHER team gets a solo turn. Leave empty and their solo " +
             "simply does not animate, which is better than borrowing yours and reading as if the " +
             "turn were yours.")]
    public Sprite[] otherSoloFrames;

    [Tooltip("Playback rate for the solo frames.")]
    public float soloFramesPerSecond = 24f;

    [Tooltip("How long the names, avatars, question and answer buttons take to fade up once the " +
             "opening has finished. The board itself is already there — only the pieces on top of " +
             "it fade. Set to 0 for an instant appearance.")]
    public float matchStartUiFadeSeconds = 0.5f;

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

        // Alpha dropped BEFORE the overlay comes down, so the board is never visible at full
        // opacity for even one frame. Reveal makes the objects active; this makes them invisible
        // again immediately, and the fade below is the only thing that brings them up.
        if (matchStartUiFadeSeconds > 0f)
            SetBoardAlpha(0f);

        // Taken down after the reveal, not before: hiding it first would uncover the board while
        // this frame is still on screen, which is the same flicker in the other direction.
        overlay?.HideOverlay();

        if (matchStartUiFadeSeconds > 0f)
            yield return FadeBoard(1f, matchStartUiFadeSeconds);

        matchStartCoroutine = null;
    }

    // A CanvasGroup per element rather than one on the gameplay root, because the root also holds
    // the board artwork -- and the board is the thing the opening animation just finished drawing.
    // Fading it would undo the transition instead of completing it.
    private readonly List<CanvasGroup> boardGroups = new List<CanvasGroup>();

    private List<CanvasGroup> BoardGroups()
    {
        boardGroups.Clear();

        AddGroup(questionText);
        AddGroup(teamAScoreText);
        AddGroup(teamBScoreText);

        foreach (TMP_Text label in slotNameTexts) AddGroup(label);
        foreach (Image pfp in slotPfpImages) AddGroup(pfp);
        foreach (Image donut in slotSoloDonuts) AddGroup(donut);

        if (answerButtons != null)
            foreach (Button button in answerButtons)
                if (button != null)
                    AddGroup(button.gameObject);

        return boardGroups;
    }

    private void AddGroup(Graphic graphic)
    {
        if (graphic != null)
            AddGroup(graphic.gameObject);
    }

    private void AddGroup(GameObject target)
    {
        if (target == null)
            return;

        CanvasGroup group = target.GetComponent<CanvasGroup>();

        // Explicit null check rather than ??: GetComponent returns a fake null that the null
        // coalescing operator does not recognise, so ?? would leave the component unadded.
        if (group == null)
            group = target.AddComponent<CanvasGroup>();

        boardGroups.Add(group);
    }

    private void SetBoardAlpha(float alpha)
    {
        foreach (CanvasGroup group in BoardGroups())
        {
            if (group == null)
                continue;

            group.alpha = alpha;

            // Nothing half-faded should be clickable. An answer button at 20% opacity still takes
            // a press otherwise, and the round has not visually started yet.
            group.blocksRaycasts = alpha >= 1f;
        }
    }

    private IEnumerator FadeBoard(float target, float seconds)
    {
        List<CanvasGroup> groups = BoardGroups();
        float from = groups.Count > 0 && groups[0] != null ? groups[0].alpha : 1f - target;
        float elapsed = 0f;

        // Unscaled, to match the rest of the sequence -- the delay and the frame playback are both
        // unscaled, so a timeScale change cannot desynchronise one part of it from another.
        while (elapsed < seconds)
        {
            elapsed += Time.unscaledDeltaTime;
            SetBoardAlpha(Mathf.Lerp(from, target, Mathf.Clamp01(elapsed / seconds)));
            yield return null;
        }

        SetBoardAlpha(target);
    }

    private void RevealBoard()
    {
        // Captured before any transition can overwrite it, or the board would settle onto whatever
        // frame happened to be showing when it was first asked.
        Image board = ResolveTeamBackground();

        if (board != null && restingBoard == null)
            restingBoard = board.sprite;

        // Every lobby page off. lobbyRootObject is empty in this scene, so relying on it would
        // leave the lobby drawn on top of the board.
        if (lobbyPageSwitcher != null)
            lobbyPageSwitcher.HideAllPages();

        if (lobbyPageSwitcher != null && lobbyPageSwitcher.waitingScreen != null)
            lobbyPageSwitcher.waitingScreen.HideWaiting();

        BindAnswerButtons();
        ApplyDuelButtonTheme();

        if (lobbyRootObject != null)
            lobbyRootObject.SetActive(false);

        if (LobbyScreenController.Instance != null)
            LobbyScreenController.Instance.HideLobby();

        if (teamGameplayRoot != null)
            teamGameplayRoot.SetActive(true);

        // And the 1v1 board away, for the same reason in the other direction.
        if (TriviaDuelManager.Instance != null && TriviaDuelManager.Instance.triviaGameplayRoot != null)
            TriviaDuelManager.Instance.triviaGameplayRoot.SetActive(false);

        if (bottomBar != null)
            bottomBar.SetActive(false);

        // Deliberately NOT blanked. The server publishes the first question as the match forms,
        // which is now around a second before this runs -- the delay and the opening animation sit
        // in between. Clearing it here wiped a question that had already been delivered, and
        // nothing sends it again, so the board stayed empty for the whole round.
        //
        // Re-applied rather than merely left alone: it landed while teamGameplayRoot was still
        // inactive, and TMP components on an inactive object are not guaranteed to have taken the
        // value.
        if (questionText != null)
            questionText.color = questionColor;

        if (currentQuestion != null)
            TriviaDuelManager.Instance?.ApplyQuestionVisualsTo(questionText, answerButtonVisuals, currentQuestion);
        else if (questionText != null)
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
        RoundState previous = roundState;
        roundState = (RoundState)newRoundState;

        PlaySoloTransition(previous, roundState);

        // The donuts are only redrawn while a solo is ticking, so leaving a solo used to leave the
        // last frame of the ring frozen on screen into the next round. Nothing else ever cleared it:
        // the server stops sending timer updates the moment the solo ends.
        if (roundState != RoundState.SoloActiveA && roundState != RoundState.SoloActiveB)
            soloTimer = 0f;

        UpdateSoloDonuts();
    }

    // The board's own artwork, remembered so a transition has something to settle back onto.
    private Sprite restingBoard;
    private Coroutine soloTransition;

    // A solo turn animates in, and unwinds on the way out — the same list played backwards, so one
    // export covers both directions and they can never drift apart.
    private void PlaySoloTransition(RoundState from, RoundState to)
    {
        bool wasSolo = IsSolo(from);
        bool isSolo = IsSolo(to);

        if (wasSolo == isSolo)
            return;

        // Whose turn it is decides which export plays. SoloActiveA means the team A seat is
        // answering, so it is yours only if you are on team A.
        bool soloIsTeamA = (isSolo ? to : from) == RoundState.SoloActiveA;
        bool mine = soloIsTeamA == (PlayerSideIdentity.TeamForSlot(LocalAssignedSlot) == 1);

        Sprite[] frames = mine ? yourSoloFrames : otherSoloFrames;

        if (frames == null || frames.Length == 0)
            return;

        if (soloTransition != null)
            StopCoroutine(soloTransition);

        soloTransition = StartCoroutine(PlayBoardFrames(frames, reversed: !isSolo));
    }

    private static bool IsSolo(RoundState state) =>
        state == RoundState.SoloActiveA || state == RoundState.SoloActiveB;

    // Only the sprite changes. The team board carries its own size and a mirrored x scale that
    // says which end of the table you are sitting at -- touching the transform here would flip the
    // board mid-match, which is what the 1v1 frame helpers would have done if they were reused.
    private IEnumerator PlayBoardFrames(Sprite[] frames, bool reversed)
    {
        Image board = ResolveTeamBackground();

        if (board == null)
            yield break;

        if (restingBoard == null)
            restingBoard = board.sprite;

        float secondsPerFrame = 1f / Mathf.Max(1f, soloFramesPerSecond);

        // Driven by elapsed time rather than by waiting a slice per frame. Waiting rounds up to
        // the next rendered frame and throws the overshoot away, which is how 24 fps plays back at
        // 15 on a 60Hz screen.
        float startedAt = Time.unscaledTime;
        int lastShown = -1;

        while (true)
        {
            int step = Mathf.FloorToInt((Time.unscaledTime - startedAt) / secondsPerFrame);

            if (step >= frames.Length)
                break;

            if (step != lastShown)
            {
                lastShown = step;
                board.sprite = frames[reversed ? frames.Length - 1 - step : step];
            }

            yield return null;
        }

        // Entering a solo HOLDS on the last frame for as long as the solo lasts -- that frame is
        // what the board looks like while somebody is answering alone, and settling straight back
        // to the ordinary board would throw the state away the instant it arrived.
        //
        // Leaving unwinds and settles, because the state being returned to is the ordinary board.
        board.sprite = reversed
            ? restingBoard
            : frames[frames.Length - 1];

        soloTransition = null;
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
            if (slotActiveIndicators[i] == null)
                continue;

            slotActiveIndicators[i].SetActive(i == activeSlotA - 1 || i == activeSlotB - 1);
            TintForTeam(slotActiveIndicators[i].GetComponent<Image>(), i + 1);
        }
    }

    // The square behind a picture says which side that player is on. Blue for your team, red for
    // theirs -- the same two colours the solo animations, the solo rings and the score flash
    // already use, so there is one thing to learn rather than four.
    private void TintForTeam(Image square, int slot)
    {
        if (square == null)
            return;

        square.color = ColourForTeam(PlayerSideIdentity.TeamForSlot(slot));
    }

    private Color ColourForTeam(int team)
    {
        TriviaDuelManager duel = TriviaDuelManager.Instance;

        // Borrowed rather than duplicated: these are Inspector fields on the 1v1 manager and
        // tuning them there should move every use of them at once.
        Color mine = duel != null ? duel.localPlayerOutlineColor : new Color32(128, 179, 200, 255);
        Color theirs = duel != null ? duel.otherSoloDonutColor : new Color32(248, 113, 113, 255);

        return team == PlayerSideIdentity.TeamForSlot(LocalAssignedSlot) ? mine : theirs;
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
                ApplySoloRing(slotSoloDonuts[i], i + 1, fill);
        }
    }

    // The ring, not a square that happens to have a fill value on it.
    //
    // Setting fillAmount on an Image does nothing at all unless its type is Filled and it has been
    // given a fill method -- which is why this was drawing as a plain square while counting down
    // perfectly underneath. The 1v1 rings get this from the scene; the 2v2 ones never did, so it
    // is done here where it cannot be lost.
    private void ApplySoloRing(Image ring, int slot, float fill)
    {
        TriviaDuelManager duel = TriviaDuelManager.Instance;
        bool mine = PlayerSideIdentity.TeamForSlot(slot) == PlayerSideIdentity.TeamForSlot(LocalAssignedSlot);

        if (duel != null)
        {
            Sprite sprite = mine ? duel.mySoloDonutSprite : duel.otherSoloDonutSprite;

            if (sprite != null)
                ring.sprite = sprite;
        }

        ring.color = ColourForTeam(PlayerSideIdentity.TeamForSlot(slot));

        ring.type = Image.Type.Filled;
        ring.fillMethod = Image.FillMethod.Radial360;
        ring.fillOrigin = (int)Image.Origin360.Top;

        // Anticlockwise, matching the 1v1 rings. Two timers unwinding in opposite directions in
        // the same game reads as a bug even when nobody can say why.
        ring.fillClockwise = false;
        ring.preserveAspect = true;

        ring.fillAmount = fill;
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

        // The pieces on the board fade away first, then the opening animation unwinds with the
        // lobby already behind it. Same order as a finished 1v1, which is what stops the lobby
        // ever snapping in.
        if (matchStartUiFadeSeconds > 0f)
            yield return FadeBoard(0f, matchStartUiFadeSeconds);

        yield return ReturnToLobbyTransition();

        // Put the alpha back for the next match, or the following round reveals itself into
        // invisible text.
        SetBoardAlpha(1f);
    }

    // The way out is the way in, backwards: the last frame covers the screen, the lobby is swapped
    // in underneath it, and only then do the frames run. Switching afterwards would spend the whole
    // animation uncovering a finished match instead.
    private IEnumerator ReturnToLobbyTransition()
    {
        TriviaDuelManager overlay = TriviaDuelManager.Instance;

        if (overlay == null || matchStartFrames == null || matchStartFrames.Length == 0)
        {
            ReturnToLobby();
            yield break;
        }

        yield return overlay.PlayOverlayFrames(matchStartFrames, matchStartFramesPerSecond, true,
                                               ReturnToLobby);

        overlay.HideOverlay();
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

    // Borrowed from the 1v1 board rather than owned, the same way the practice screen borrows it.
    //
    // Without a theme an AnswerButtonVisual keeps whatever sprite it already had for every state,
    // so a right answer and a wrong one looked identical here -- all twelve of these visuals have
    // an empty theme in the scene and nothing was ever filling it in. Taken at the start of each
    // match so the two boards cannot drift apart.
    private void ApplyDuelButtonTheme()
    {
        if (answerButtonVisuals == null)
            return;

        ButtonTheme theme = TriviaDuelManager.Instance != null
            ? TriviaDuelManager.Instance.buttonTheme
            : null;

        if (theme == null)
        {
            Debug.LogWarning("TeamDuelManager: no ButtonTheme on the duel manager, so the 2v2 " +
                             "answers cannot show right or wrong.", this);
            return;
        }

        foreach (AnswerButtonVisual visual in answerButtonVisuals)
            if (visual != null)
                visual.ApplyTheme(theme);
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
