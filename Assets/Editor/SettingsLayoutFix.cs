using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

// Lays the More page's five buttons out in one column again.
//
// Tutorial 2x2 arrived after the other four and was placed at the top of the column, exactly on top of
// Som -- two buttons in one spot, so the sound switch looked like it had gone. This puts the five back
// in order, evenly spaced, using the same step the page was built with.
//
// Runs once on its own; save the scene after. Undo puts the old positions back.
[InitializeOnLoad]
public static class SettingsLayoutFix
{
    private const string DoneKey = "EstaloSettingsRelaidOut";
    private const string UndoName = "Lay out the settings buttons";

    static SettingsLayoutFix()
    {
        if (EditorPrefs.GetBool(DoneKey, false))
            return;

        EditorApplication.delayCall += () =>
        {
            if (!EditorApplication.isPlayingOrWillChangePlaymode && Fix())
                EditorPrefs.SetBool(DoneKey, true);
        };
    }

    [MenuItem("Trivia Duel/Setup/Lay Out Settings Buttons")]
    private static bool Fix()
    {
        SettingsPage page = Object.FindAnyObjectByType<SettingsPage>(FindObjectsInactive.Include);

        if (page == null)
            return false;

        Button[] column = { page.soundButton, page.accountButton, page.tutorialButton,
                            page.teamTutorialButton, page.creditsButton };

        float top = 640f;
        bool moved = false;

        foreach (Button button in column)
        {
            if (button == null)
                continue;

            RectTransform rect = (RectTransform)button.transform;
            float height = rect.sizeDelta.y > 1f ? rect.sizeDelta.y : 188f;
            Vector2 wanted = new Vector2(0f, top - height * 0.5f);

            if ((rect.anchoredPosition - wanted).sqrMagnitude > 1f)
            {
                Undo.RecordObject(rect, UndoName);
                rect.anchoredPosition = wanted;
                EditorUtility.SetDirty(rect);
                moved = true;
            }

            top -= height + 45f;
        }

        if (moved)
        {
            EditorSceneManager.MarkSceneDirty(page.gameObject.scene);
            Debug.Log("SettingsLayoutFix: Som, Conta, Rever tutorial, Tutorial 2x2 and Créditos are in one " +
                      "column again. Save the scene.", page);
        }

        return moved;
    }
}

// Adds the "Mostrar senha" button to a login panel that was built before it existed, under the
// password field, and lays the panel out again so nothing lands on top of anything else.
[InitializeOnLoad]
public static class ShowPasswordButtonFix
{
    private const string DoneKey = "EstaloShowPasswordButton";

    static ShowPasswordButtonFix()
    {
        if (EditorPrefs.GetBool(DoneKey, false))
            return;

        EditorApplication.delayCall += () =>
        {
            if (!EditorApplication.isPlayingOrWillChangePlaymode && Add())
                EditorPrefs.SetBool(DoneKey, true);
        };
    }

    [MenuItem("Trivia Duel/Setup/Add Show Password Button")]
    private static bool Add()
    {
        LoginPanel panel = Object.FindAnyObjectByType<LoginPanel>(FindObjectsInactive.Include);

        if (panel == null || panel.showPasswordButton != null || panel.signInButton == null)
            return false;

        GameObject copy = Object.Instantiate(panel.signInButton.gameObject, panel.signInButton.transform.parent);
        copy.name = "MostrarSenhaButton";
        Undo.RegisterCreatedObjectUndo(copy, "Add show password button");

        RectTransform rect = (RectTransform)copy.transform;
        RectTransform sign = (RectTransform)panel.signInButton.transform;
        RectTransform password = panel.passwordField != null ? (RectTransform)panel.passwordField.transform : sign;

        // Between the password field and Entrar, taking half the gap each.
        rect.anchoredPosition = new Vector2(sign.anchoredPosition.x,
                                            (password.anchoredPosition.y + sign.anchoredPosition.y) * 0.5f);
        rect.sizeDelta = new Vector2(sign.sizeDelta.x, sign.sizeDelta.y * 0.72f);
        copy.transform.SetSiblingIndex(panel.signInButton.transform.GetSiblingIndex());

        TMPro.TMP_Text label = copy.GetComponentInChildren<TMPro.TMP_Text>(true);

        if (label != null)
            label.text = "Mostrar senha";

        Undo.RecordObject(panel, "Add show password button");
        panel.showPasswordButton = copy.GetComponent<Button>();
        EditorUtility.SetDirty(panel);
        EditorSceneManager.MarkSceneDirty(panel.gameObject.scene);

        Debug.Log("ShowPasswordButtonFix: Mostrar senha is on the login panel. Save the scene.", panel);
        return true;
    }
}
