using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

// Builds the practice quiz and results screens into the Learning page and wires them to a
// PracticeQuizController.
//
// Non-destructive by design. Re-running only creates what is MISSING and re-attaches the
// references; it never touches the position, size, colour, font or sprite of anything that already
// exists. That means the generated page is a starting point you are free to restyle, move and
// re-parent, and running the menu item again afterwards will not undo your work.
//
// "Rebuild From Scratch" is the separate, deliberate way to throw that away.
public static class PracticePageBuilder
{
    private const string PanelName = "PracticeArea";

    [MenuItem("Trivia Duel/Setup/Build or Repair Practice Page")]
    public static void Build()
    {
        BuildInternal(fromScratch: false);
    }

    [MenuItem("Trivia Duel/Setup/Rebuild Practice Page From Scratch")]
    public static void Rebuild()
    {
        bool confirmed = EditorUtility.DisplayDialog(
            "Rebuild the practice page?",
            "This deletes the existing practice page and generates a fresh one. Any styling, " +
            "repositioning or extra objects you added inside it will be lost.\n\n" +
            "To keep your changes, use \"Build or Repair Practice Page\" instead — it only fills " +
            "in what is missing.",
            "Delete and rebuild", "Cancel");

        if (confirmed)
            BuildInternal(fromScratch: true);
    }

    private static void BuildInternal(bool fromScratch)
    {
        LobbyPageSwitcher switcher = Object.FindAnyObjectByType<LobbyPageSwitcher>(FindObjectsInactive.Include);

        if (switcher == null || switcher.learningPage == null)
        {
            Debug.LogError("Need a LobbyPageSwitcher with its Learning Page assigned. Open " +
                           "ProjectCapstone and check the Inspector.");
            return;
        }

        Transform parent = switcher.learningPage.transform;

        if (fromScratch)
        {
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
        }

        // Reuse the existing controller wherever it now lives — you may have moved the panel.
        PracticeQuizController controller = Object.FindAnyObjectByType<PracticeQuizController>(
            FindObjectsInactive.Include);

        GameObject area;

        if (controller != null)
        {
            area = controller.gameObject;
        }
        else
        {
            area = FindOrCreate(parent, PanelName, out bool created);

            if (created)
                Stretch(area.GetComponent<RectTransform>());

            controller = Undo.AddComponent<PracticeQuizController>(area);
        }

        Undo.RecordObject(controller, "Build Practice Page");

        int createdCount = EnsureMistakeLog();

        createdCount += BuildStartButton(area.transform, controller);
        createdCount += BuildQuizPanel(area.transform, controller);
        createdCount += BuildResultsPanel(area.transform, controller);

        EditorUtility.SetDirty(controller);
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(area.scene);
        Selection.activeGameObject = area;

        Debug.Log(createdCount == 0
            ? "Practice page already complete — nothing created, references re-attached. Your " +
              "styling is untouched."
            : $"Practice page: created {createdCount} missing object(s) and re-attached references. " +
              "Anything that already existed was left exactly as you had it. Save the scene (Cmd+S).",
            area);
    }

    // Without this in the scene nothing records anything: the practice button would hand back a
    // generic set and "seu ponto fraco" would never have data. It is a DontDestroyOnLoad singleton
    // like PlayerIQManager, so it wants its own root object rather than living under the Canvas.
    private static int EnsureMistakeLog()
    {
        if (Object.FindAnyObjectByType<MistakeLogManager>(FindObjectsInactive.Include) != null)
            return 0;

        GameObject go = new GameObject("MistakeLogManager");
        Undo.RegisterCreatedObjectUndo(go, "Create MistakeLogManager");
        Undo.AddComponent<MistakeLogManager>(go);

        Debug.Log("Created a MistakeLogManager object — mistakes are now recorded.", go);
        return 1;
    }

    private static int BuildStartButton(Transform parent, PracticeQuizController controller)
    {
        // If you already wired a button of your own to StartPersonalisedPractice, that IS the start
        // button — making a second one would leave a stray default sitting on the page.
        foreach (Button existing in Object.FindObjectsByType<Button>(
                     FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (HasPersistentCall(existing, controller))
                return 0;
        }

        GameObject go = FindOrCreate(parent, "PractiseMistakesButton", out bool created);

        if (created)
        {
            RectTransform rect = go.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0f);
            rect.anchorMax = new Vector2(0.5f, 0f);
            rect.pivot = new Vector2(0.5f, 0f);
            rect.anchoredPosition = new Vector2(0f, 220f);
            rect.sizeDelta = new Vector2(820f, 130f);

            Image background = go.AddComponent<Image>();
            background.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
            background.type = Image.Type.Sliced;
            background.color = new Color(0.42f, 0.44f, 0.75f, 1f);

            Button created_button = go.AddComponent<Button>();
            created_button.targetGraphic = background;

            MakeLabel(go.transform, "Label", "Praticar meus erros", 44, FontStyles.Bold, stretch: true);
        }

        Button button = go.GetComponent<Button>();

        // Re-attached every run, but only if it is not already there, so a second run cannot end up
        // starting two practice sets from one tap.
        if (button != null && !HasPersistentCall(button, controller))
        {
            UnityEditor.Events.UnityEventTools.AddVoidPersistentListener(
                button.onClick, controller.StartPersonalisedPractice);
        }

        return created ? 1 : 0;
    }

    private static int BuildQuizPanel(Transform parent, PracticeQuizController controller)
    {
        int created = 0;

        GameObject panel = controller.quizRoot;

        if (panel == null)
        {
            panel = FindOrCreate(parent, "QuizPanel", out bool panelCreated);
            created += panelCreated ? 1 : 0;

            if (panelCreated)
            {
                Stretch(panel.GetComponent<RectTransform>());
                Image background = panel.AddComponent<Image>();
                background.color = new Color(0.10f, 0.11f, 0.20f, 0.97f);
                panel.SetActive(false);
            }

            controller.quizRoot = panel;
        }

        if (controller.progressText == null)
            controller.progressText = FindOrCreateLabel(panel.transform, "ProgressText", "1 / 10",
                36, FontStyles.Normal, new Vector2(0f, -160f), new Vector2(600f, 70f), ref created);

        if (controller.questionText == null)
            controller.questionText = FindOrCreateLabel(panel.transform, "QuestionText", "",
                60, FontStyles.Bold, new Vector2(0f, -420f), new Vector2(940f, 320f), ref created);

        if (controller.answerButtons == null || controller.answerButtons.Length != 4)
            controller.answerButtons = new Button[4];

        if (controller.answerLabels == null || controller.answerLabels.Length != 4)
            controller.answerLabels = new TMP_Text[4];

        for (int i = 0; i < 4; i++)
        {
            // A slot you filled in yourself is left completely alone — no default button is made
            // for it, so your own art never ends up sitting behind one of mine.
            if (controller.answerButtons[i] != null)
            {
                if (controller.answerLabels[i] == null)
                    controller.answerLabels[i] =
                        controller.answerButtons[i].GetComponentInChildren<TMP_Text>(true);

                continue;
            }

            GameObject go = FindOrCreate(panel.transform, "Answer" + (i + 1), out bool answerCreated);
            created += answerCreated ? 1 : 0;

            if (answerCreated)
            {
                // Two columns, two rows — the same shape as the duel screen's answer grid, so
                // practice feels like the game rather than a separate app.
                float x = (i % 2 == 0) ? -240f : 240f;
                float y = -900f - (i / 2) * 230f;

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

                Button newButton = go.AddComponent<Button>();
                newButton.targetGraphic = buttonBackground;

                MakeLabel(go.transform, "Label", "", 46, FontStyles.Bold, stretch: true, color: Color.black);
            }

            controller.answerButtons[i] = go.GetComponent<Button>();

            Transform label = go.transform.Find("Label");
            controller.answerLabels[i] = label != null ? label.GetComponent<TMP_Text>() : null;
        }

        return created;
    }

    private static int BuildResultsPanel(Transform parent, PracticeQuizController controller)
    {
        int created = 0;

        GameObject panel = controller.resultsRoot;

        if (panel == null)
        {
            panel = FindOrCreate(parent, "ResultsPanel", out bool panelCreated);
            created += panelCreated ? 1 : 0;

            if (panelCreated)
            {
                Stretch(panel.GetComponent<RectTransform>());
                Image background = panel.AddComponent<Image>();
                background.color = new Color(0.10f, 0.11f, 0.20f, 0.97f);
                panel.SetActive(false);
            }

            controller.resultsRoot = panel;

            FindOrCreateLabel(panel.transform, "ResultsTitle", "Resultado", 56, FontStyles.Bold,
                new Vector2(0f, -220f), new Vector2(800f, 90f), ref created);
        }

        if (controller.scoreText == null)
            controller.scoreText = FindOrCreateLabel(panel.transform, "ScoreText", "0 / 10",
                110, FontStyles.Bold, new Vector2(0f, -400f), new Vector2(800f, 180f), ref created);

        if (controller.weakestTopicText == null)
            controller.weakestTopicText = FindOrCreateLabel(panel.transform, "WeakestTopicText", "",
                42, FontStyles.Bold, new Vector2(0f, -620f), new Vector2(940f, 110f), ref created);

        if (controller.breakdownText == null)
            controller.breakdownText = FindOrCreateLabel(panel.transform, "BreakdownText", "",
                36, FontStyles.Normal, new Vector2(0f, -980f), new Vector2(940f, 560f), ref created);

        if (controller.doneButton != null)
            return created;

        GameObject doneGo = FindOrCreate(panel.transform, "DoneButton", out bool doneCreated);
        created += doneCreated ? 1 : 0;

        if (doneCreated)
        {
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

            Button newDone = doneGo.AddComponent<Button>();
            newDone.targetGraphic = doneBackground;

            MakeLabel(doneGo.transform, "Label", "Pronto", 44, FontStyles.Bold, stretch: true);
        }

        controller.doneButton = doneGo.GetComponent<Button>();

        return created;
    }

    // --- helpers ---

    // The heart of the non-destructive behaviour: an object that already exists is handed back
    // untouched, so whatever you did to it in the Inspector survives.
    private static GameObject FindOrCreate(Transform parent, string name, out bool created)
    {
        Transform existing = parent.Find(name);

        if (existing != null)
        {
            created = false;
            return existing.gameObject;
        }

        GameObject go = new GameObject(name, typeof(RectTransform));
        Undo.RegisterCreatedObjectUndo(go, "Build Practice Page");
        go.transform.SetParent(parent, false);

        created = true;
        return go;
    }

    private static TMP_Text FindOrCreateLabel(Transform parent, string name, string text, float size,
        FontStyles style, Vector2 position, Vector2 sizeDelta, ref int created)
    {
        GameObject go = FindOrCreate(parent, name, out bool wasCreated);

        if (!wasCreated)
            return go.GetComponent<TMP_Text>();

        created++;

        // Anchored to the top edge, positioned downward. The UI is authored at 2556 tall and
        // stretched to fit, so top-anchored offsets stay put on every screen.
        RectTransform rect = go.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 1f);
        rect.anchorMax = new Vector2(0.5f, 1f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = sizeDelta;

        return ConfigureLabel(go, text, size, style, Color.white);
    }

    // The method name matters as well as the target: several buttons here point at the same
    // controller, and matching on the target alone would read the "Pronto" button (which calls Hide)
    // as an already-wired start button.
    private static bool HasPersistentCall(Button button, PracticeQuizController controller,
        string methodName = "StartPersonalisedPractice")
    {
        for (int i = 0; i < button.onClick.GetPersistentEventCount(); i++)
            if (button.onClick.GetPersistentTarget(i) == controller &&
                button.onClick.GetPersistentMethodName(i) == methodName)
                return true;

        return false;
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    private static TMP_Text MakeLabel(Transform parent, string name, string text, float size,
        FontStyles style, bool stretch = false, Color? color = null)
    {
        GameObject go = FindOrCreate(parent, name, out bool created);

        if (!created)
            return go.GetComponent<TMP_Text>();

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
