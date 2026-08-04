using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

// Builds the practice quiz and results screens into the Learning page and wires them to a
// PracticeQuizController. Generated rather than assembled by hand so the whole thing can be rebuilt
// if the scene is damaged, and so the wiring is reviewable in git rather than living only in a
// serialised file nobody can read.
//
// Safe to re-run: any previously generated panel is removed first.
public static class PracticePageBuilder
{
    private const string PanelName = "PracticeArea";

    [MenuItem("Trivia Duel/Setup/Build Practice Page")]
    public static void Build()
    {
        LobbyPageSwitcher switcher = Object.FindAnyObjectByType<LobbyPageSwitcher>(FindObjectsInactive.Include);

        if (switcher == null || switcher.learningPage == null)
        {
            Debug.LogError("Need a LobbyPageSwitcher with its Learning Page assigned. Open " +
                           "ProjectCapstone and check the Inspector.");
            return;
        }

        Transform parent = switcher.learningPage.transform;

        // Re-runnable: drop any panel a previous run left, wherever it ended up.
        PracticeQuizController[] previous = Object.FindObjectsByType<PracticeQuizController>(
            FindObjectsInactive.Include);

        foreach (PracticeQuizController old in previous)
        {
            if (old == null)
                continue;

            if (Selection.activeGameObject != null &&
                Selection.activeGameObject.transform.IsChildOf(old.transform))
            {
                Selection.activeGameObject = null;
            }

            Undo.DestroyObjectImmediate(old.gameObject);
        }

        GameObject area = new GameObject(PanelName, typeof(RectTransform));
        Undo.RegisterCreatedObjectUndo(area, "Build Practice Page");
        area.transform.SetParent(parent, false);
        Stretch(area.GetComponent<RectTransform>());

        PracticeQuizController controller = area.AddComponent<PracticeQuizController>();

        BuildStartButton(area.transform, controller);
        BuildQuizPanel(area.transform, controller);
        BuildResultsPanel(area.transform, controller);

        EditorUtility.SetDirty(controller);
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(area.scene);
        Selection.activeGameObject = area;

        Debug.Log("Practice page built inside the Learning page and wired to PracticeQuizController. " +
                  "Save the scene (Cmd+S) to keep it.", area);
    }

    // The entry point from your design: a button at the bottom of the Learning tab that switches
    // from the level path into practice built from this player's own mistakes.
    private static void BuildStartButton(Transform parent, PracticeQuizController controller)
    {
        GameObject go = MakeChild(parent, "PractiseMistakesButton");
        RectTransform rect = go.GetComponent<RectTransform>();

        // Anchored to the bottom edge so it sits below the level path however tall the screen is.
        rect.anchorMin = new Vector2(0.5f, 0f);
        rect.anchorMax = new Vector2(0.5f, 0f);
        rect.pivot = new Vector2(0.5f, 0f);
        rect.anchoredPosition = new Vector2(0f, 220f);
        rect.sizeDelta = new Vector2(820f, 130f);

        Image background = go.AddComponent<Image>();
        background.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
        background.type = Image.Type.Sliced;
        background.color = new Color(0.42f, 0.44f, 0.75f, 1f);

        Button button = go.AddComponent<Button>();
        button.targetGraphic = background;

        MakeLabel(go.transform, "Label", "Praticar meus erros", 44, FontStyles.Bold, stretch: true);

        // Wired as a persistent listener so it survives domain reloads and shows in the Inspector,
        // rather than a runtime-only AddListener that looks unwired to anyone reading the scene.
        UnityEditor.Events.UnityEventTools.AddVoidPersistentListener(
            button.onClick, controller.StartPersonalisedPractice);
    }

    private static void BuildQuizPanel(Transform parent, PracticeQuizController controller)
    {
        GameObject panel = MakeChild(parent, "QuizPanel");
        Stretch(panel.GetComponent<RectTransform>());

        Image background = panel.AddComponent<Image>();
        background.color = new Color(0.10f, 0.11f, 0.20f, 0.97f);

        controller.quizRoot = panel;

        controller.progressText = MakeAnchored(panel.transform, "ProgressText", "1 / 10",
            36, FontStyles.Normal, new Vector2(0f, -160f), new Vector2(600f, 70f));

        controller.questionText = MakeAnchored(panel.transform, "QuestionText", "",
            60, FontStyles.Bold, new Vector2(0f, -420f), new Vector2(940f, 320f));

        controller.answerButtons = new Button[4];
        controller.answerLabels = new TMP_Text[4];

        // Two columns, two rows — the same shape as the duel screen's answer grid, so practice
        // feels like the game rather than a separate app.
        for (int i = 0; i < 4; i++)
        {
            float x = (i % 2 == 0) ? -240f : 240f;
            float y = -900f - (i / 2) * 230f;

            GameObject go = MakeChild(panel.transform, "Answer" + (i + 1));
            RectTransform rect = go.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 1f);
            rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = new Vector2(x, y);
            rect.sizeDelta = new Vector2(440f, 180f);

            Image buttonBackground = go.AddComponent<Image>();
            buttonBackground.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
            buttonBackground.type = Image.Type.Sliced;
            buttonBackground.color = Color.white;

            Button button = go.AddComponent<Button>();
            button.targetGraphic = buttonBackground;

            controller.answerButtons[i] = button;
            controller.answerLabels[i] = MakeLabel(go.transform, "Label", "", 46,
                FontStyles.Bold, stretch: true, color: Color.black);
        }

        panel.SetActive(false);
    }

    private static void BuildResultsPanel(Transform parent, PracticeQuizController controller)
    {
        GameObject panel = MakeChild(parent, "ResultsPanel");
        Stretch(panel.GetComponent<RectTransform>());

        Image background = panel.AddComponent<Image>();
        background.color = new Color(0.10f, 0.11f, 0.20f, 0.97f);

        controller.resultsRoot = panel;

        MakeAnchored(panel.transform, "ResultsTitle", "Resultado", 56, FontStyles.Bold,
            new Vector2(0f, -220f), new Vector2(800f, 90f));

        controller.scoreText = MakeAnchored(panel.transform, "ScoreText", "0 / 10",
            110, FontStyles.Bold, new Vector2(0f, -400f), new Vector2(800f, 180f));

        controller.weakestTopicText = MakeAnchored(panel.transform, "WeakestTopicText", "",
            42, FontStyles.Bold, new Vector2(0f, -620f), new Vector2(940f, 110f));

        controller.breakdownText = MakeAnchored(panel.transform, "BreakdownText", "",
            36, FontStyles.Normal, new Vector2(0f, -980f), new Vector2(940f, 560f));

        GameObject doneGo = MakeChild(panel.transform, "DoneButton");
        RectTransform doneRect = doneGo.GetComponent<RectTransform>();
        doneRect.anchorMin = new Vector2(0.5f, 0f);
        doneRect.anchorMax = new Vector2(0.5f, 0f);
        doneRect.pivot = new Vector2(0.5f, 0f);
        doneRect.anchoredPosition = new Vector2(0f, 260f);
        doneRect.sizeDelta = new Vector2(560f, 130f);

        Image doneBackground = doneGo.AddComponent<Image>();
        doneBackground.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
        doneBackground.type = Image.Type.Sliced;
        doneBackground.color = new Color(0.42f, 0.44f, 0.75f, 1f);

        Button doneButton = doneGo.AddComponent<Button>();
        doneButton.targetGraphic = doneBackground;
        controller.doneButton = doneButton;

        MakeLabel(doneGo.transform, "Label", "Pronto", 44, FontStyles.Bold, stretch: true);

        panel.SetActive(false);
    }

    // --- helpers ---

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    private static GameObject MakeChild(Transform parent, string name)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return go;
    }

    private static TMP_Text MakeAnchored(Transform parent, string name, string text, float size,
        FontStyles style, Vector2 position, Vector2 sizeDelta)
    {
        GameObject go = MakeChild(parent, name);
        RectTransform rect = go.GetComponent<RectTransform>();

        // Anchored to the top edge, positioned downward. The UI is authored at 2556 tall and
        // stretched to fit, so top-anchored offsets stay put on every screen.
        rect.anchorMin = new Vector2(0.5f, 1f);
        rect.anchorMax = new Vector2(0.5f, 1f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = sizeDelta;

        return ConfigureLabel(go, text, size, style, Color.white);
    }

    private static TMP_Text MakeLabel(Transform parent, string name, string text, float size,
        FontStyles style, bool stretch = false, Color? color = null)
    {
        GameObject go = MakeChild(parent, name);

        if (stretch)
            Stretch(go.GetComponent<RectTransform>());

        return ConfigureLabel(go, text, size, style, color ?? Color.white);
    }

    private static TMP_Text ConfigureLabel(GameObject go, string text, float size,
        FontStyles style, Color color)
    {
        TextMeshProUGUI label = go.AddComponent<TextMeshProUGUI>();
        label.text = text;
        label.fontSize = size;
        label.fontStyle = style;
        label.alignment = TextAlignmentOptions.Center;
        label.color = color;

        // Shrink-to-fit only. A long question must stay on screen rather than clipping, but nothing
        // should grow past the size chosen here.
        label.enableAutoSizing = true;
        label.fontSizeMin = size * 0.45f;
        label.fontSizeMax = size;

        return label;
    }
}
