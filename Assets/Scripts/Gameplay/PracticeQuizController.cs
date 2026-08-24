using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// The single-player half of the Learning tab: a short quiz, then a score and a plain-language note
// about what this player is weakest at.
//
// Deliberately NOT routed through the duel state machine. There is no opponent, no buzz-in race, no
// solo turn and no server — running it through TriviaDuelManager would mean threading a "there is
// nobody else" case through every branch of a state machine built entirely around two sides.
public class PracticeQuizController : MonoBehaviour
{
    [Header("--- ROOT ---")]
    [Tooltip("The quiz panel. Shown while answering, hidden when the results appear.")]
    public GameObject quizRoot;

    [Tooltip("The results panel, shown when the last question is answered.")]
    public GameObject resultsRoot;

    [Header("--- QUIZ ---")]
    public TMP_Text questionText;

    [Tooltip("Progress through the set, e.g. \"3 / 10\".")]
    public TMP_Text progressText;

    [Tooltip("The four answer buttons, in the same order as the answers in the question data.")]
    public Button[] answerButtons = new Button[4];

    [Tooltip("The label inside each answer button, in the same order.")]
    public TMP_Text[] answerLabels = new TMP_Text[4];

    [Tooltip("The AnswerButtonVisual on each answer button, in the same order. Left empty these are " +
             "found automatically. This is what the duel screen uses, so practice looks the same.")]
    public AnswerButtonVisual[] answerVisuals = new AnswerButtonVisual[4];

    [Header("--- RESULTS ---")]
    [Tooltip("Final score, e.g. \"8 / 10\".")]
    public TMP_Text scoreText;

    [Tooltip("Which topic this player is weakest at, in words.")]
    public TMP_Text weakestTopicText;

    [Tooltip("Per-topic accuracy for the topics in this set.")]
    public TMP_Text breakdownText;

    public Button doneButton;

    [Header("--- SETTINGS ---")]
    [Tooltip("How many questions a practice set contains.")]
    public int questionsPerSet = 10;

    [Tooltip("How long a wrong answer stays highlighted before moving on.")]
    public float wrongAnswerPauseSeconds = 1.2f;

    [Tooltip("Colours matching the duel screen, so practice looks like the real game.")]
    public Color correctColor = new Color(0.30f, 0.75f, 0.38f);
    public Color wrongColor = new Color(0.85f, 0.30f, 0.30f);
    public Color neutralColor = Color.white;

    private readonly List<TriviaQuestion> set = new List<TriviaQuestion>();
    private readonly Dictionary<string, int> askedByTopic = new Dictionary<string, int>();
    private readonly Dictionary<string, int> rightByTopic = new Dictionary<string, int>();

    private int index;
    private int score;
    private bool answering;
    private float advanceAt;
    private bool waitingToAdvance;

    // Portuguese labels for the topic slugs stored in the question file, so the results screen says
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
        { "fracoes", "frações" },
        { "porcentagem", "porcentagem" },
        { "raciocinio", "raciocínio" },
        { "geral", "geral" }
    };

    private void Awake()
    {
        for (int i = 0; i < answerButtons.Length; i++)
        {
            int captured = i;

            if (answerButtons[i] != null)
                answerButtons[i].onClick.AddListener(() => OnAnswerClicked(captured));
        }

        if (doneButton != null)
            doneButton.onClick.AddListener(Hide);

        if (answerVisuals == null || answerVisuals.Length != answerButtons.Length)
            answerVisuals = new AnswerButtonVisual[answerButtons.Length];

        for (int i = 0; i < answerButtons.Length; i++)
            if (answerVisuals[i] == null && answerButtons[i] != null)
                answerVisuals[i] = answerButtons[i].GetComponent<AnswerButtonVisual>();

        // Done here rather than left to the Inspector so it holds for whatever objects are dragged
        // into these slots, including ones built by hand.
        FitToItsBox(questionText, 0.35f);

        for (int i = 0; i < answerLabels.Length; i++)
            FitToItsBox(answerLabels[i], 0.4f);

        // "Seu ponto fraco nas partidas: multiplicação" is the longest line on the results screen and
        // its length depends on the topic name, so it needs the most room to shrink into.
        FitToItsBox(weakestTopicText, 0.3f);

        // The breakdown grows a line per topic, so it shrinks to fit vertically as well as across.
        FitToItsBox(breakdownText, 0.3f);
        FitToItsBox(scoreText, 0.5f);
        FitToItsBox(progressText, 0.5f);

        SetActive(quizRoot, false);
        SetActive(resultsRoot, false);
    }

    // Long questions must shrink to stay inside their box instead of running off the screen, which
    // is how the duel screen's question already behaves.
    private static void FitToItsBox(TMP_Text label, float minimumSizeRatio)
    {
        if (label == null)
            return;

        label.textWrappingMode = TextWrappingModes.Normal;
        label.overflowMode = TextOverflowModes.Truncate;

        // Whatever size the object was authored at becomes the ceiling; auto-sizing only ever
        // shrinks from there, so nothing suddenly renders bigger than it was designed to be.
        if (!label.enableAutoSizing)
        {
            label.fontSizeMax = label.fontSize;
            label.enableAutoSizing = true;
        }

        label.fontSizeMin = Mathf.Max(8f, label.fontSizeMax * minimumSizeRatio);
    }

    // A TMP object that was activated in this same frame has not run its own initialisation yet, so
    // the text it is handed never reaches the mesh and the label draws blank. Tapping a button
    // happened to force a rebuild, which is why the answers only appeared once you touched one.
    private static void SetText(TMP_Text label, string value)
    {
        if (label == null)
            return;

        label.text = value;

        if (label.gameObject.activeInHierarchy)
            label.ForceMeshUpdate();
    }

    // Wire this to the "practice my mistakes" button at the bottom of the Learning page.
    public void StartPersonalisedPractice()
    {
        if (MistakeLogManager.Instance == null || TriviaDuelManager.Instance == null)
        {
            Debug.LogError("PracticeQuizController: needs MistakeLogManager and TriviaDuelManager " +
                           "in the scene. Practice cannot be built without the question bank.");
            return;
        }

        List<TriviaQuestion> all = TriviaDuelManager.Instance.GetAllQuestions();
        Begin(MistakeLogManager.Instance.BuildPracticeSet(questionsPerSet, all));
    }

    private void Begin(List<TriviaQuestion> questions)
    {
        set.Clear();
        askedByTopic.Clear();
        rightByTopic.Clear();

        if (questions != null)
            set.AddRange(questions);

        if (set.Count == 0)
        {
            Debug.LogWarning("PracticeQuizController: no questions to practise.");
            return;
        }

        index = 0;
        score = 0;
        waitingToAdvance = false;

        ApplyDuelButtonTheme();

        SetActive(resultsRoot, false);
        SetActive(quizRoot, true);

        ShowCurrentQuestion();
    }

    // Practice deliberately borrows the duel screen's theme rather than owning one. Without a theme
    // an AnswerButtonVisual keeps whatever sprite it already had for every state, so a right answer
    // and a wrong one looked identical. Taken at the start of each set so the two screens cannot
    // drift apart.
    private void ApplyDuelButtonTheme()
    {
        ButtonTheme theme = TriviaDuelManager.Instance != null
            ? TriviaDuelManager.Instance.buttonTheme
            : null;

        if (theme == null)
        {
            Debug.LogWarning("PracticeQuizController: TriviaDuelManager has no Button Theme, so the " +
                             "practice buttons cannot show right/wrong. Assign one on the duel manager.");
            return;
        }

        for (int i = 0; i < answerVisuals.Length; i++)
            if (answerVisuals[i] != null)
                answerVisuals[i].ApplyTheme(theme);
    }

    private void Update()
    {
        if (!waitingToAdvance || Time.unscaledTime < advanceAt)
            return;

        waitingToAdvance = false;
        index++;

        if (index >= set.Count)
            ShowResults();
        else
            ShowCurrentQuestion();
    }

    private void ShowCurrentQuestion()
    {
        TriviaQuestion question = set[index];
        answering = true;

        SetText(questionText, question.question);
        SetText(progressText, (index + 1) + " / " + set.Count);

        for (int i = 0; i < answerButtons.Length; i++)
        {
            if (answerLabels.Length > i)
                SetText(answerLabels[i], i < question.answers.Length ? question.answers[i] : string.Empty);

            if (answerButtons[i] == null)
                continue;

            answerButtons[i].interactable = true;

            // Where an AnswerButtonVisual exists it owns the look completely — it turns Unity's own
            // colour transition off and drives the sprite itself. Tinting the Image here as well
            // would fight it, which is what left these buttons blank until they were touched.
            if (answerVisuals[i] != null)
            {
                answerVisuals[i].SetAvailableState();
                continue;
            }

            Image background = answerButtons[i].GetComponent<Image>();

            if (background != null)
                background.color = neutralColor;
        }
    }

    private void OnAnswerClicked(int answerIndex)
    {
        if (!answering || index >= set.Count)
            return;

        answering = false;

        TriviaQuestion question = set[index];
        bool wasCorrect = answerIndex == question.correctAnswerIndex;
        string topic = string.IsNullOrEmpty(question.topic) ? "geral" : question.topic;

        askedByTopic[topic] = askedByTopic.TryGetValue(topic, out int asked) ? asked + 1 : 1;

        if (wasCorrect)
        {
            score++;
            rightByTopic[topic] = rightByTopic.TryGetValue(topic, out int right) ? right + 1 : 1;
        }

        // Tagged as Practice: getting a question right here retires it from the practice pool, but
        // it must NOT move the topic statistics. "Seu ponto fraco" is a verdict about how the player
        // performs in real games, and practice sets come from those weak topics — counting them
        // would let a player practise their way out of a weakness they still have in matches.
        MistakeLogManager.Instance?.RecordAnswer(question, 1, wasCorrect,
            MistakeLogManager.AnswerSource.Practice);

        Paint(answerIndex, wasCorrect);

        // On a wrong answer also show which one was right — a practice set that only says "wrong"
        // teaches nothing.
        if (!wasCorrect)
            Paint(question.correctAnswerIndex, true);

        for (int i = 0; i < answerButtons.Length; i++)
        {
            if (answerButtons[i] == null)
                continue;

            answerButtons[i].interactable = false;

            // The two buttons Paint just marked keep their result colour; the rest go to the same
            // locked look the duel screen uses between rounds.
            if (answerVisuals[i] != null && i != answerIndex && i != question.correctAnswerIndex)
                answerVisuals[i].SetLockedPressedState();
        }

        advanceAt = Time.unscaledTime + (wasCorrect ? wrongAnswerPauseSeconds * 0.5f : wrongAnswerPauseSeconds);
        waitingToAdvance = true;
    }

    private void Paint(int answerIndex, bool asCorrect)
    {
        if (answerIndex < 0 || answerIndex >= answerButtons.Length || answerButtons[answerIndex] == null)
            return;

        if (answerVisuals[answerIndex] != null)
        {
            if (asCorrect)
                answerVisuals[answerIndex].SetPressedRightState();
            else
                answerVisuals[answerIndex].SetPressedWrongState();

            return;
        }

        Image background = answerButtons[answerIndex].GetComponent<Image>();

        if (background != null)
            background.color = asCorrect ? correctColor : wrongColor;
    }

    private void ShowResults()
    {
        SetActive(quizRoot, false);
        SetActive(resultsRoot, true);

        SetText(scoreText, score + " / " + set.Count);
        SetText(weakestTopicText, BuildWeakestLine());
        SetText(breakdownText, BuildBreakdown());
    }

    private string BuildWeakestLine()
    {
        string weakest = MistakeLogManager.Instance != null
            ? MistakeLogManager.Instance.GetWeakestTopic()
            : string.Empty;

        if (string.IsNullOrEmpty(weakest))
        {
            // No verdict yet rather than a wrong one: topic stats come from real matches, and a
            // player who has only practised has not given us anything to judge.
            return MistakeLogManager.Instance != null && MistakeLogManager.Instance.HasMatchHistory
                ? "Nenhum ponto fraco claro ainda. Continue jogando."
                : "Jogue algumas partidas para descobrir seu ponto fraco.";
        }

        return "Seu ponto fraco nas partidas: " + Pretty(weakest);
    }

    private string BuildBreakdown()
    {
        System.Text.StringBuilder builder = new System.Text.StringBuilder();

        foreach (KeyValuePair<string, int> entry in askedByTopic)
        {
            rightByTopic.TryGetValue(entry.Key, out int right);
            builder.AppendLine($"{Pretty(entry.Key)}: {right} / {entry.Value}");
        }

        return builder.ToString().TrimEnd();
    }

    private static string Pretty(string topic) =>
        TopicNames.TryGetValue(topic, out string name) ? name : topic;

    public void Hide()
    {
        SetActive(quizRoot, false);
        SetActive(resultsRoot, false);
    }

    private static void SetActive(GameObject target, bool active)
    {
        if (target != null)
            target.SetActive(active);
    }
}
