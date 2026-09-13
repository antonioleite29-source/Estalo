using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// The Learning page's way into the board: lesson nodes and the "Praticar meus erros" button both
// come through here, and both are handed straight to LessonSession.
//
// Nothing is played from this object any more. It sits under the Learning page, and starting a
// lesson switches that page off -- which stopped every coroutine this used to run, so the lobby
// disappeared and the lesson never came. LessonSession runs on an object of its own instead.
public class PracticeQuizController : MonoBehaviour
{
    // The old practice page. Nothing draws on it now -- practice happens on the duel board -- but
    // PracticePageBuilder still builds and wires these, so they stay rather than break that tool.
    [Header("--- ROOT ---")]
    [Tooltip("The old quiz panel. Kept hidden; practice runs on the duel board.")]
    public GameObject quizRoot;

    [Tooltip("The old results panel. Kept hidden; results are shown on the duel board.")]
    public GameObject resultsRoot;

    [Header("--- OLD PAGE (unused) ---")]
    public TMP_Text questionText;
    public TMP_Text progressText;
    public Button[] answerButtons = new Button[4];
    public TMP_Text[] answerLabels = new TMP_Text[4];
    public AnswerButtonVisual[] answerVisuals = new AnswerButtonVisual[4];
    public TMP_Text scoreText;
    public TMP_Text weakestTopicText;
    public TMP_Text breakdownText;
    public Button doneButton;

    [Header("--- SETTINGS ---")]
    [Tooltip("How many questions a \"Praticar meus erros\" set contains. Lessons go until " +
             "LessonLadder.CorrectToPass answers are right instead.")]
    public int questionsPerSet = 10;

    private void Awake()
    {
        if (doneButton != null)
            doneButton.onClick.AddListener(Hide);

        Hide();
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
        LessonSession.StartPractice(MistakeLogManager.Instance.BuildPracticeSet(questionsPerSet, all));
    }

    // Started from a node on the learning path.
    public void StartLesson(int lessonIndex)
    {
        LessonSession.StartLesson(lessonIndex);
    }

    public void Hide()
    {
        if (quizRoot != null)
            quizRoot.SetActive(false);

        if (resultsRoot != null)
            resultsRoot.SetActive(false);
    }
}
