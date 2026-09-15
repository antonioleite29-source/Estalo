using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

// A lesson from the learning path -- or a "Praticar meus erros" set -- played on the real duel board
// against the Treinador.
//
// A lesson teaches first and asks second: a few cards explaining the idea, then questions until the
// player has got ten right. A wrong answer shows the right one and puts the question back a couple
// of places later, so nobody finishes a lesson by being walked past what they got wrong.
//
// It runs on a GameObject of its own, as FirstRunTutorial does, and that is not a style choice. The
// first thing a session does is switch every lobby page off, and the Learning page is one of them.
// Unity stops every coroutine on an object the moment it is deactivated, so while this ran on
// PracticeQuizController -- which sits under the Learning page -- tapping a lesson hid the lobby and
// then did nothing at all.
public class LessonSession : MonoBehaviour
{
    // Read by ConnectPageController, which would otherwise drop the lobby back over the board when
    // the server connection comes back in the middle of a lesson.
    public static bool IsRunning => current != null;

    private static LessonSession current;

    private const string CoachName = "Treinador";

    private const float RightPauseSeconds = 0.8f;

    // Longer than a right answer: this is the moment the correct one is on screen to be learned.
    private const float WrongPauseSeconds = 2f;

    private const float FinishedSeconds = 4f;

    // How many other questions come before a wrong one is asked again -- soon enough that the right
    // answer is still fresh, late enough that it is not simply the screen they just left.
    private const int RetryAfter = 2;

    private TriviaDuelManager duel;

    // A lesson teaches and then plays until CorrectToPass; a practice set is played through once.
    private LessonLadder.Lesson lesson;
    private int lessonIndex = -1;
    private List<TriviaQuestion> pool;
    private List<TriviaQuestion> practiceSet;

    private int right;
    private int wrong;
    private bool lastAnswerCorrect;

    private bool awaitingTap;
    private int tapped = -1;
    private bool exitRequested;
    private bool reviewRequested;

    private GameObject topBar;

    // While the teaching cards are up, the outermost answer buttons are Voltar and Próximo.
    private const int BackButton = 0;
    private int NextButton => duel.answerButtonVisuals.Length - 1;

    // --- starting -------------------------------------------------------

    public static void StartLesson(int index)
    {
        LessonLadder.Lesson lesson = LessonLadder.At(index);

        if (lesson == null)
        {
            Debug.LogError("LessonSession: there are no lessons. Is Assets/Resources/Lessons.json missing?");
            return;
        }

        List<TriviaQuestion> questions = LessonLadder.BuildPool(lesson);

        if (questions.Count == 0)
        {
            Debug.LogWarning($"LessonSession: lesson {lesson.Number} ({lesson.topic}, level {lesson.level}) " +
                             "has no questions.");
            return;
        }

        LessonSession session = Create();

        if (session == null)
            return;

        session.lesson = lesson;
        session.lessonIndex = index;
        session.pool = questions;
        session.StartCoroutine(session.Run());
    }

    public static void StartPractice(List<TriviaQuestion> questions)
    {
        if (questions == null || questions.Count == 0)
        {
            Debug.LogWarning("LessonSession: no questions to practise.");
            return;
        }

        LessonSession session = Create();

        if (session == null)
            return;

        session.practiceSet = new List<TriviaQuestion>(questions);
        session.StartCoroutine(session.Run());
    }

    private static LessonSession Create()
    {
        // A second tap while a session is already opening would stack two of them on one board.
        if (current != null || FirstRunTutorial.IsRunning)
            return null;

        TriviaDuelManager duel = TriviaDuelManager.Instance;

        if (duel == null)
        {
            Debug.LogError("LessonSession: no TriviaDuelManager, so there is no board to play on.");
            return null;
        }

        if (duel.answerButtonVisuals == null || duel.answerButtonVisuals.Length < 2)
        {
            Debug.LogError("LessonSession: the duel board has no answer buttons to play with.");
            return null;
        }

        GameObject host = new GameObject("LessonSession");
        DontDestroyOnLoad(host);

        current = host.AddComponent<LessonSession>();
        current.duel = duel;
        return current;
    }

    // --- the session ----------------------------------------------------

    private IEnumerator Run()
    {
        // Every lobby page off. lobbyRootObject is empty in this scene, so relying on it would leave
        // the lobby drawn on top of the board.
        if (duel.lobbyPageSwitcher != null)
            duel.lobbyPageSwitcher.HideAllPages();

        TriviaDuelManager.HideTeamBoard();

        yield return duel.MatchStartSequence();

        DressBoard();
        BindButtons();
        BuildTopBar();

        if (lesson != null)
        {
            yield return Teach(false);

            if (!exitRequested)
                yield return PlayUntilPassed();
        }
        else
        {
            yield return PlayThrough();
        }

        if (!exitRequested)
            yield return ShowFinished();

        Leave();
    }

    // The lesson's cards, one at a time. Reviewing from the middle of the questions comes through
    // here too: there is no goal card at the end, and backing out of the first card goes back to the
    // question rather than out of the lesson.
    private IEnumerator Teach(bool reviewing)
    {
        List<string> pages = BuildPages(reviewing);
        int page = 0;

        SetTopBarVisible(false);

        while (page < pages.Count)
        {
            SetQuestionText(pages[page]);

            bool first = page == 0;
            bool last = page == pages.Count - 1;

            ShowNavigation(first && !reviewing ? "Sair" : "Voltar",
                           last ? (reviewing ? "Continuar" : "Começar") : "Próximo");

            yield return WaitForTap();

            if (tapped == NextButton)
            {
                page++;
            }
            else if (first)
            {
                if (!reviewing)
                    exitRequested = true;

                break;
            }
            else
            {
                page--;
            }
        }

        SetTopBarVisible(true);
    }

    private List<string> BuildPages(bool reviewing)
    {
        List<string> pages = new List<string>();
        string heading = $"<b>Lição {lesson.Number}: {lesson.title}</b>\n";
        string[] cards = lesson.cards ?? new string[0];

        for (int i = 0; i < cards.Length; i++)
            pages.Add(i == 0 ? heading + cards[i] : cards[i]);

        if (!reviewing)
            pages.Add($"Agora é com você! Acerte {LessonLadder.CorrectToPass} perguntas para concluir a lição.\n" +
                      "Errou? Tudo bem: a pergunta volta mais tarde.");

        if (pages.Count == 0)
            pages.Add(heading);

        return pages;
    }

    private IEnumerator PlayUntilPassed()
    {
        List<TriviaQuestion> queue = new List<TriviaQuestion>();
        TriviaQuestion previous = null;

        while (right < LessonLadder.CorrectToPass)
        {
            // Refilled rather than ended when it runs dry: a player still short of ten after the
            // whole pool goes round again, reshuffled, and never opening on the question just asked.
            if (queue.Count == 0)
            {
                queue.AddRange(pool);
                LessonLadder.Shuffle(queue);

                if (queue.Count > 1 && queue[0] == previous)
                    (queue[0], queue[1]) = (queue[1], queue[0]);
            }

            TriviaQuestion question = queue[0];
            queue.RemoveAt(0);
            previous = question;

            yield return Ask(question);

            if (exitRequested)
                yield break;

            if (!lastAnswerCorrect)
                queue.Insert(Mathf.Min(RetryAfter, queue.Count), question);
        }
    }

    private IEnumerator PlayThrough()
    {
        foreach (TriviaQuestion question in practiceSet)
        {
            yield return Ask(question);

            if (exitRequested)
                yield break;
        }
    }

    private IEnumerator Ask(TriviaQuestion question)
    {
        while (true)
        {
            ShowQuestion(question);
            yield return WaitForTap();

            if (exitRequested)
                yield break;

            if (!reviewRequested)
                break;

            // Back to the cards, then the same question again from the top.
            reviewRequested = false;
            yield return Teach(true);
        }

        int chosen = tapped;
        lastAnswerCorrect = chosen == question.correctAnswerIndex;

        if (lastAnswerCorrect)
            right++;
        else
            wrong++;

        // Tagged as Practice: getting a question right here retires it from the practice pool, but
        // it must NOT move the topic statistics. "Seu ponto fraco" is a verdict about how the player
        // performs in real games, and practice sets come from those weak topics -- counting them
        // would let a player practise their way out of a weakness they still have in matches.
        if (MistakeLogManager.Instance != null)
            MistakeLogManager.Instance.RecordAnswer(question, lesson != null ? lesson.level : 1,
                lastAnswerCorrect, MistakeLogManager.AnswerSource.Practice);

        // The right answer is always shown, and on a wrong answer beside the one they picked -- a
        // board that only says "wrong" teaches nothing.
        AnswerButtonVisual[] visuals = duel.answerButtonVisuals;

        for (int i = 0; i < visuals.Length; i++)
        {
            if (visuals[i] == null)
                continue;

            if (i == question.correctAnswerIndex)
                visuals[i].SetPressedRightState();
            else if (i == chosen)
                visuals[i].SetPressedWrongState();
            else
                visuals[i].SetLockedPressedState();
        }

        UpdateScores();

        yield return new WaitForSecondsRealtime(lastAnswerCorrect ? RightPauseSeconds : WrongPauseSeconds);
    }

    private IEnumerator ShowFinished()
    {
        SetTopBarVisible(false);

        string message;

        if (lesson != null)
        {
            bool unlocksNext = lessonIndex == LessonLadder.Completed && lessonIndex + 1 < LessonLadder.Count;
            bool wasLast = lessonIndex + 1 >= LessonLadder.Count;

            LessonLadder.MarkDone(lessonIndex);

            message = $"Lição concluída!\n{right} acertos e {wrong} erros.\n" +
                      (wasLast ? "Você terminou todas as lições!"
                       : unlocksNext ? "A próxima lição foi desbloqueada."
                       : "Mandou bem!");
        }
        else
        {
            message = $"Você acertou {right} de {practiceSet.Count}.\n{WeakestLine()}";
        }

        SetQuestionText(message);

        foreach (AnswerButtonVisual visual in duel.answerButtonVisuals)
            if (visual != null)
                visual.SetDisabledState();

        yield return new WaitForSecondsRealtime(FinishedSeconds);
    }

    // Out the same door a finished match leaves by, but back onto the learning path rather than the
    // lobby, so the lesson that was just unlocked is the first thing on screen.
    private void Leave()
    {
        UnbindButtons();

        if (topBar != null)
            Destroy(topBar);

        TriviaDuelManager board = duel;

        board.PlayReturnToLobbyTransition(() =>
        {
            board.PrepareForLobby(false);

            if (board.lobbyPageSwitcher != null)
                board.lobbyPageSwitcher.ShowLearningPage();
        });

        Destroy(gameObject);
    }

    private void OnDestroy()
    {
        if (current == this)
            current = null;

        if (topBar != null)
            Destroy(topBar);
    }

    // --- the board ------------------------------------------------------

    private void DressBoard()
    {
        duel.SetTriviaUiVisible(true);
        duel.ShowOpenBuzzBackground();

        // Side 1, the same side the tutorial seats you on, so the blue ring is around your own
        // avatar rather than the coach's.
        duel.RegisterLocalPlayerSide(1);

        if (duel.bottomBar != null)
            duel.bottomBar.SetActive(false);

        PlayerProfileManager profile = PlayerProfileManager.Instance;

        if (profile != null)
        {
            int mine = profile.GetLocalAvatarIndex();
            int avatars = profile.availableAvatars != null ? profile.availableAvatars.Length : 0;

            duel.ApplyNetworkedPlayerIdentity(1, profile.GetLocalName(), profile.GetAvatarSprite(mine));

            // The coach must not be wearing your face. With one avatar in the gallery there is no
            // other choice, and matching beats an empty slot.
            int coach = avatars > 1 ? (mine + 1 + Random.Range(0, avatars - 1)) % avatars : mine;
            duel.ApplyNetworkedPlayerIdentity(2, CoachName, profile.GetAvatarSprite(coach));
        }

        if (duel.leftSoloDonut != null)
            duel.leftSoloDonut.gameObject.SetActive(false);

        if (duel.rightSoloDonut != null)
            duel.rightSoloDonut.gameObject.SetActive(false);

        // Without the duel's theme an AnswerButtonVisual keeps whatever sprite it already had for
        // every state, so a right answer and a wrong one look identical.
        if (duel.buttonTheme != null)
        {
            foreach (AnswerButtonVisual visual in duel.answerButtonVisuals)
                if (visual != null)
                    visual.ApplyTheme(duel.buttonTheme);
        }

        right = 0;
        wrong = 0;
        UpdateScores();
    }

    private void ShowQuestion(TriviaQuestion question)
    {
        duel.ApplyQuestionVisualsTo(duel.questionText, duel.answerButtonVisuals, question);

        foreach (AnswerButtonVisual visual in duel.answerButtonVisuals)
            if (visual != null)
                visual.SetAvailableState();

        UpdateScores();
    }

    // Only the outer two buttons are live while the cards are up; the middle ones go blank and dead.
    private void ShowNavigation(string back, string next)
    {
        AnswerButtonVisual[] visuals = duel.answerButtonVisuals;

        for (int i = 0; i < visuals.Length; i++)
        {
            if (visuals[i] == null)
                continue;

            if (i == BackButton || i == NextButton)
            {
                visuals[i].SetLabel(i == NextButton ? next : back);
                visuals[i].SetAvailableState();
            }
            else
            {
                visuals[i].SetLabel(string.Empty);
                visuals[i].SetDisabledState();
            }
        }
    }

    // Right on your side, wrong on the coach's.
    private void UpdateScores()
    {
        if (duel.team1ScoreText != null)
            duel.team1ScoreText.text = right.ToString();

        if (duel.team2ScoreText != null)
            duel.team2ScoreText.text = wrong.ToString();
    }

    private void SetQuestionText(string text)
    {
        if (duel.questionText == null)
            return;

        duel.questionText.text = text;

        if (duel.questionText.gameObject.activeInHierarchy)
            duel.questionText.ForceMeshUpdate();
    }

    private IEnumerator WaitForTap()
    {
        tapped = -1;
        awaitingTap = true;

        while (awaitingTap && !exitRequested && !reviewRequested)
            yield return null;

        awaitingTap = false;
    }

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
            button.onClick.AddListener(() => OnTap(index));
        }
    }

    // Left empty rather than pointing anywhere: TriviaDuelManager rebinds them for itself when a real
    // match starts, and a listener left behind here would answer for a lesson that has gone.
    private void UnbindButtons()
    {
        if (duel == null || duel.answerButtons == null)
            return;

        foreach (Button button in duel.answerButtons)
            if (button != null)
                button.onClick.RemoveAllListeners();
    }

    private void OnTap(int index)
    {
        if (!awaitingTap)
            return;

        tapped = index;
        awaitingTap = false;
    }

    // --- Sair and Rever lição -------------------------------------------

    // Two small buttons along the top of the board while questions are up: leave the lesson, or go
    // back over the cards and then carry on. Built here rather than in the scene because they only
    // exist for as long as a lesson does. Parented to the gameplay root, so they are hidden with the
    // board if anything else takes the screen.
    private void BuildTopBar()
    {
        Transform parent = duel.triviaGameplayRoot != null ? duel.triviaGameplayRoot.transform
                         : duel.questionText != null ? duel.questionText.canvas.transform
                         : null;

        if (parent == null)
            return;

        topBar = new GameObject("LessonTopBar", typeof(RectTransform));

        RectTransform bar = (RectTransform)topBar.transform;
        bar.SetParent(parent, false);
        bar.anchorMin = new Vector2(0f, 0.915f);
        bar.anchorMax = new Vector2(1f, 0.965f);
        bar.offsetMin = Vector2.zero;
        bar.offsetMax = Vector2.zero;
        bar.SetAsLastSibling();

        AddBarButton(bar, "Sair", 0.03f, 0.25f, () => { if (awaitingTap) exitRequested = true; });

        if (lesson != null)
            AddBarButton(bar, "Rever lição", 0.72f, 0.97f, () => { if (awaitingTap) reviewRequested = true; });

        SetTopBarVisible(false);
    }

    private void AddBarButton(RectTransform bar, string text, float xMin, float xMax, UnityAction onClick)
    {
        GameObject go = new GameObject(text, typeof(RectTransform), typeof(Image), typeof(Button));

        RectTransform rect = (RectTransform)go.transform;
        rect.SetParent(bar, false);
        rect.anchorMin = new Vector2(xMin, 0f);
        rect.anchorMax = new Vector2(xMax, 1f);
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        Image background = go.GetComponent<Image>();
        background.color = new Color(0f, 0f, 0f, 0.45f);

        Button button = go.GetComponent<Button>();
        button.targetGraphic = background;
        button.onClick.AddListener(onClick);

        GameObject labelObject = new GameObject("Label", typeof(RectTransform));
        RectTransform labelRect = (RectTransform)labelObject.transform;
        labelRect.SetParent(rect, false);
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = Vector2.zero;
        labelRect.offsetMax = Vector2.zero;

        TextMeshProUGUI label = labelObject.AddComponent<TextMeshProUGUI>();

        if (duel.questionText != null && duel.questionText.font != null)
            label.font = duel.questionText.font;

        label.text = text;
        label.alignment = TextAlignmentOptions.Center;
        label.color = Color.white;
        label.enableAutoSizing = true;
        label.fontSizeMin = TextFitsItsBox.SmallestSize;
        label.fontSizeMax = 48f;
        label.raycastTarget = false;
    }

    private void SetTopBarVisible(bool visible)
    {
        if (topBar != null)
            topBar.SetActive(visible);
    }

    // --- words ----------------------------------------------------------

    // Portuguese labels for the topic slugs stored in the question file, so the results say
    // "subtração" rather than "subtracao".
    private static readonly Dictionary<string, string> TopicNames = new Dictionary<string, string>
    {
        { "adicao", "adição" },
        { "subtracao", "subtração" },
        { "multiplicacao", "multiplicação" },
        { "divisao", "divisão" },
        { "potencias", "potências" },
        { "algebra", "álgebra" },
        { "sequencias", "sequências" },
        { "logica", "lógica" },
        { "analogias", "analogias" },
        { "classificacao", "classificação" },
        { "probabilidade", "probabilidade" },
        { "geometria", "geometria" },
        { "raciocinio", "raciocínio" }
    };

    private static string WeakestLine()
    {
        MistakeLogManager log = MistakeLogManager.Instance;
        string weakest = log != null ? log.GetWeakestTopic() : string.Empty;

        if (string.IsNullOrEmpty(weakest))
        {
            // No verdict yet rather than a wrong one: topic stats come from real matches, and a
            // player who has only practised has not given us anything to judge.
            return log != null && log.HasMatchHistory
                ? "Nenhum ponto fraco claro ainda. Continue jogando."
                : "Jogue algumas partidas para descobrir seu ponto fraco.";
        }

        return "Seu ponto fraco nas partidas: " +
               (TopicNames.TryGetValue(weakest, out string name) ? name : weakest);
    }
}
