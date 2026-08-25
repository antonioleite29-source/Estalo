using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// The very first time Estalo is ever opened, on this device, it opens into a match instead of the
// lobby — one with nobody on the other side.
//
// It runs on the real board rather than on cards of its own: the same background, the same answer
// buttons, the same score positions, the same solo ring. A player who is told how the game works
// on a mock-up has to recognise it again later; a player who is told on the board itself has
// already seen everything by the time they play for real.
//
// Nothing here touches TriviaDuelManager's state machine. It drives the same widgets directly,
// which is why a tutorial cannot leave a half-started match behind if it is interrupted.
public class FirstRunTutorial : MonoBehaviour
{
    // Per virtual player: MPPM clones share this machine's PlayerPrefs, so without the suffix
    // testing the tutorial once would mark it done for every clone.
    private static string SeenKey => "EstaloFirstRunDone" + NetworkBootstrap.GetLocalProfileSuffix();

    private const string PlayerName = "Novo Jogador";
    private const string OpponentName = "Treinador";

    private const float CardSeconds = 7f;
    private const int TestQuestions = 7;

    private TriviaDuelManager duel;
    private int opponentAvatarIndex;
    private int answeredCorrectlyRaw;
    private int answeredCorrectly;

    // Set while a question is on screen and waiting for a tap.
    private bool awaitingAnswer;
    private int chosenAnswer = -1;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Begin()
    {
        if (NetworkBootstrap.IsDedicatedServerBuild)
            return;

        if (PlayerPrefs.GetInt(SeenKey, 0) == 1)
            return;

        GameObject host = new GameObject("FirstRunTutorial");
        DontDestroyOnLoad(host);

        FirstRunTutorial tutorial = host.AddComponent<FirstRunTutorial>();
        tutorial.StartCoroutine(tutorial.Run());
    }

    private IEnumerator Run()
    {
        // Behind the launch screen, so the board is already dressed by the time the logo fades off
        // it rather than assembling itself in front of the player.
        yield return new WaitForSecondsRealtime(LoadingScreenController.HoldSeconds + LoadingScreenController.FadeSeconds);

        duel = TriviaDuelManager.Instance;

        if (duel == null)
        {
            Debug.LogWarning("FirstRunTutorial: no TriviaDuelManager, so the first-run match cannot run.");
            Finish();
            yield break;
        }

        GiveThemAName();
        OpenBoard();

        yield return PlayIntroCards();
        yield return PlayTestRound();

        ShowResult();
        yield return new WaitForSecondsRealtime(6f);

        Finish();
    }

    // --- setup ----------------------------------------------------------

    // A name and a face before the first card, so "you are the blue team" has something on screen
    // to point at. Both are only filled in if the player has genuinely never set them.
    private void GiveThemAName()
    {
        PlayerProfileManager profile = PlayerProfileManager.Instance;

        if (profile == null)
            return;

        if (profile.GetLocalName() == "Player" || string.IsNullOrWhiteSpace(profile.GetLocalName()))
            profile.SetLocalName(PlayerName);

        int avatarCount = profile.availableAvatars != null ? profile.availableAvatars.Length : 0;

        if (avatarCount > 0)
        {
            int mine = Random.Range(0, avatarCount);
            profile.SetLocalAvatarIndex(mine);

            // The coach must not be wearing the player's face. With one avatar in the gallery there
            // is no other choice, and matching is better than an empty slot.
            opponentAvatarIndex = avatarCount > 1
                ? (mine + Random.Range(1, avatarCount)) % avatarCount
                : mine;
        }
    }

    private void OpenBoard()
    {
        // Side 1 is the left of the board and the side the blue ring marks as yours. Card two tells
        // them they are always blue, so this has to be true before that card appears.
        duel.RegisterLocalPlayerSide(1);

        if (duel.lobbyRootObject != null)
            duel.lobbyRootObject.SetActive(false);

        if (duel.lobbyPageSwitcher != null && duel.lobbyPageSwitcher.waitingScreen != null)
            duel.lobbyPageSwitcher.waitingScreen.HideWaiting();

        if (LobbyScreenController.Instance != null)
            LobbyScreenController.Instance.HideLobby();

        if (duel.triviaGameplayRoot != null)
            duel.triviaGameplayRoot.SetActive(true);

        if (duel.bottomBar != null)
            duel.bottomBar.SetActive(false);

        if (duel.gameBackground != null)
            duel.gameBackground.enabled = true;

        PlayerProfileManager profile = PlayerProfileManager.Instance;

        SetName(duel.leftPlayerNameText, profile != null ? profile.GetLocalName() : PlayerName);
        SetName(duel.rightPlayerNameText, OpponentName);

        if (profile != null)
        {
            SetAvatar(duel.leftPlayerPfpImage, profile.GetAvatarSprite(profile.GetLocalAvatarIndex()));
            SetAvatar(duel.rightPlayerPfpImage, profile.GetAvatarSprite(opponentAvatarIndex));
        }

        SetScores(0, 0);
        HideRings();

        // Blank and locked: the buttons belong to the explanation until the test round starts, and
        // a live-looking button nobody may press is worse than an obviously dead one.
        SetAnswerLabels(null);
        LockButtons();
    }

    // --- the six cards --------------------------------------------------

    private IEnumerator PlayIntroCards()
    {
        int soloSeconds = Mathf.Max(1, Mathf.RoundToInt(duel.soloTimeSeconds));

        yield return Card("Bem-vindo ao Estalo! Um jogo de trivia que torna o aprendizado " +
                          "realmente legal e competitivo. Preste muita atenção nas instruções.");

        yield return Card("Você sempre será do time azul, e os pontos do seu time são os do seu lado.");

        yield return Card("Seu objetivo é acertar as perguntas antes do seu oponente. " +
                          "Quem fizer " + duel.pointsToWin + " pontos primeiro ganha!", ShowScoreClimb());

        yield return Card("Mas se você errar uma pergunta, seu oponente terá " + soloSeconds +
                          " segundos para responder sozinho — e você não pode.", ShowOpponentSolo());

        yield return Card("Se ele errar ou o tempo acabar, o jogo continua.");

        yield return Card("Para você entender direito, vamos fazer uma rodada de teste!");
    }

    // Every card holds for the same beat whatever it is doing, so the tutorial has a rhythm rather
    // than a series of different-length pauses.
    private IEnumerator Card(string message, IEnumerator alongside = null)
    {
        if (duel.questionText != null)
            duel.questionText.text = message;

        if (alongside == null)
        {
            yield return new WaitForSecondsRealtime(CardSeconds);
            yield break;
        }

        // The demonstration runs inside the card's seven seconds, not after them.
        Coroutine running = StartCoroutine(alongside);
        yield return new WaitForSecondsRealtime(CardSeconds);

        if (running != null)
            StopCoroutine(running);
    }

    // Card three: the scores climb so "first to seven" is something they watch happen rather than
    // a number in a sentence. Reset afterwards, because the real match starts level.
    private IEnumerator ShowScoreClimb()
    {
        yield return new WaitForSecondsRealtime(2f);

        const float climbSeconds = 4f;
        float elapsed = 0f;

        while (elapsed < climbSeconds)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / climbSeconds);

            // The opponent trails rather than matching, so it reads as a race being won.
            SetScores(Mathf.RoundToInt(t * duel.pointsToWin),
                      Mathf.RoundToInt(t * (duel.pointsToWin - 3)));
            yield return null;
        }

        yield return new WaitForSecondsRealtime(0.6f);
        SetScores(0, 0);
    }

    // Card four: the opponent's ring, counting down exactly as it will in a real solo turn.
    private IEnumerator ShowOpponentSolo()
    {
        yield return new WaitForSecondsRealtime(1.5f);

        Image ring = duel.rightSoloDonut;

        if (ring == null)
            yield break;

        if (duel.otherSoloDonutSprite != null)
            ring.sprite = duel.otherSoloDonutSprite;

        ring.color = duel.otherSoloDonutColor;
        ring.fillAmount = 1f;
        ring.gameObject.SetActive(true);

        float remaining = duel.soloTimeSeconds;

        while (remaining > 0f)
        {
            remaining -= Time.unscaledDeltaTime;
            ring.fillAmount = Mathf.Clamp01(remaining / duel.soloTimeSeconds);
            yield return null;
        }

        ring.gameObject.SetActive(false);
    }

    // --- the seven questions --------------------------------------------

    // One question per difficulty level, so the test actually spans the range it is measuring
    // rather than sampling the same band seven times.
    private IEnumerator PlayTestRound()
    {
        BindButtons();

        for (int level = 1; level <= TestQuestions; level++)
        {
            TriviaQuestion question = PickQuestion(level);

            if (question == null)
                continue;

            duel.ApplyQuestionVisualsTo(duel.questionText, duel.answerButtonVisuals, question);
            SetScores(answeredCorrectly, 0);
            UnlockButtons();

            chosenAnswer = -1;
            awaitingAnswer = true;

            // No clock. The starting difficulty is decided by this round, and hurrying somebody
            // through the one measurement it rests on measures nerve instead of knowledge.
            while (awaitingAnswer)
                yield return null;

            bool right = chosenAnswer == question.correctAnswerIndex;

            if (right)
            {
                answeredCorrectlyRaw += level;
                answeredCorrectly++;
                duel.answerButtonVisuals[chosenAnswer].SetPressedRightState();
            }
            else
            {
                duel.answerButtonVisuals[chosenAnswer].SetPressedWrongState();

                // Showing the right one matters more here than in a match: this is the only round
                // where nobody else answers it, so an unanswered question teaches nothing.
                if (question.correctAnswerIndex >= 0 && question.correctAnswerIndex < duel.answerButtonVisuals.Length)
                    duel.answerButtonVisuals[question.correctAnswerIndex].SetPressedRightState();
            }

            yield return new WaitForSecondsRealtime(1.4f);
            LockButtons();
            yield return new WaitForSecondsRealtime(0.25f);
        }

        SetAnswerLabels(null);
        LockButtons();
    }

    private TriviaQuestion PickQuestion(int level)
    {
        int poolSize = duel.GetPoolSize(level);

        if (poolSize <= 0)
            return null;

        return duel.GetQuestionAt(level, Random.Range(0, poolSize));
    }

    // --- the result -----------------------------------------------------

    // Weighted by difficulty: a level-7 question answered right is worth seven times a level-1, so
    // the raw total runs 0 to 28 with 14 as an average showing.
    //
    // IQ = 100 + 2.5 x (raw - 14) puts that average at 100 and a perfect round at 135, which is
    // roughly the first percentile. Reaching the game's ceiling of 150 off seven questions would
    // be flattery: 150 is meant to be about one person in two thousand.
    private void ShowResult()
    {
        int iq = Mathf.Clamp(Mathf.RoundToInt(100f + 2.5f * (answeredCorrectlyRaw - 14f)),
                             Mathf.RoundToInt(PlayerIQManager.IQ_MIN),
                             Mathf.RoundToInt(PlayerIQManager.IQ_MAX));

        if (PlayerIQManager.Instance != null)
            PlayerIQManager.Instance.SetLocalIQ(iq);

        int level = PlayerIQManager.Instance != null ? PlayerIQManager.Instance.GetLocalDifficultyLevel() : 1;

        if (duel.questionText != null)
        {
            duel.questionText.text = $"Você acertou {answeredCorrectly} de {TestQuestions}.\n" +
                                     $"Seu IQ inicial é {iq}, nível {level} de 7.\n" +
                                     "As perguntas vão se ajustar a você a cada partida. Boa sorte!";
        }

        SetScores(0, 0);
        HideRings();
    }

    private void Finish()
    {
        PlayerPrefs.SetInt(SeenKey, 1);
        PlayerPrefs.Save();

        UnbindButtons();

        // Back through the manager, so the lobby is restored exactly the way it is after a real
        // match rather than by this class guessing which objects to switch on.
        if (duel != null)
            duel.PrepareForLobby(true);

        Destroy(gameObject);
    }

    // --- driving the shared board ---------------------------------------

    private void BindButtons()
    {
        if (duel.answerButtons == null)
            return;

        for (int i = 0; i < duel.answerButtons.Length; i++)
        {
            int index = i;
            Button button = duel.answerButtons[i];

            if (button == null)
                continue;

            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(() => OnAnswer(index));
        }
    }

    private void UnbindButtons()
    {
        if (duel == null || duel.answerButtons == null)
            return;

        // Left empty rather than pointing anywhere: TriviaDuelManager rebinds them for itself when
        // a real match starts, and a listener left behind here would answer for the tutorial.
        foreach (Button button in duel.answerButtons)
            if (button != null)
                button.onClick.RemoveAllListeners();
    }

    private void OnAnswer(int index)
    {
        if (!awaitingAnswer)
            return;

        chosenAnswer = index;
        awaitingAnswer = false;
    }

    private void UnlockButtons()
    {
        foreach (AnswerButtonVisual visual in duel.answerButtonVisuals)
            if (visual != null)
                visual.SetAvailableState();
    }

    private void LockButtons()
    {
        foreach (AnswerButtonVisual visual in duel.answerButtonVisuals)
            if (visual != null)
                visual.SetDisabledState();
    }

    private void SetAnswerLabels(string text)
    {
        if (duel.answerButtonVisuals == null)
            return;

        foreach (AnswerButtonVisual visual in duel.answerButtonVisuals)
            if (visual != null)
                visual.SetLabel(text ?? string.Empty);
    }

    private void SetScores(int mine, int theirs)
    {
        if (duel.team1ScoreText != null)
            duel.team1ScoreText.text = mine.ToString();

        if (duel.team2ScoreText != null)
            duel.team2ScoreText.text = theirs.ToString();
    }

    private void HideRings()
    {
        if (duel.leftSoloDonut != null)
            duel.leftSoloDonut.gameObject.SetActive(false);

        if (duel.rightSoloDonut != null)
            duel.rightSoloDonut.gameObject.SetActive(false);
    }

    private static void SetName(TMP_Text label, string value)
    {
        if (label != null)
            label.text = value;
    }

    private static void SetAvatar(Image image, Sprite sprite)
    {
        if (image != null && sprite != null)
            image.sprite = sprite;
    }
}
