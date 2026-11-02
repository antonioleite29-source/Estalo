using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

// Brings the More page up to date: adds the buttons that arrived after it was built -- Música,
// Política de privacidade -- takes Deletar conta off it (it lives on the account panel now), and puts
// the column in the order it should read in:
//
//   Efeitos, Música, Rever tutorial, Rever tutorial 2x2, Política de privacidade, Conta, Créditos
//
// Runs once on its own; save the scene after. Undo puts the old page back.
[InitializeOnLoad]
public static class MorePageOrderFix
{
    private const string DoneKey = "EstaloMorePageOrdered";
    private const string UndoName = "Bring the More page up to date";

    static MorePageOrderFix()
    {
        if (EditorPrefs.GetBool(DoneKey, false))
            return;

        EditorApplication.delayCall += () =>
        {
            if (!EditorApplication.isPlayingOrWillChangePlaymode && Fix())
                EditorPrefs.SetBool(DoneKey, true);
        };
    }

    [MenuItem("Trivia Duel/Setup/Bring The More Page Up To Date")]
    private static bool Fix()
    {
        SettingsPage page = Object.FindAnyObjectByType<SettingsPage>(FindObjectsInactive.Include);

        if (page == null)
            return false;

        Undo.RecordObject(page, UndoName);

        foreach (GameObject made in page.Build(null))
            Undo.RegisterCreatedObjectUndo(made, UndoName);

        // Deleting the account is asked for on the account panel, where the player went to look at
        // their account. One place is enough, and the More page is a worse one for it.
        Transform stale = page.transform.Find("DeleteAccountButton");

        if (stale != null)
            Undo.DestroyObjectImmediate(stale.gameObject);

        // "Tutorial 2x2" reads as a mode rather than a tutorial next to "Rever tutorial".
        TMPro.TMP_Text teamLabel = page.teamTutorialButton != null
            ? page.teamTutorialButton.GetComponentInChildren<TMPro.TMP_Text>(true)
            : null;

        if (teamLabel != null && teamLabel.text != "Rever tutorial 2x2")
        {
            Undo.RecordObject(teamLabel, UndoName);
            teamLabel.text = "Rever tutorial 2x2";
            EditorUtility.SetDirty(teamLabel);
        }

        foreach (Button button in new[] { page.soundButton, page.musicButton, page.tutorialButton,
                                          page.teamTutorialButton, page.privacyButton, page.accountButton,
                                          page.creditsButton })
            if (button != null)
                Undo.RecordObject(button.transform, UndoName);

        page.LayOutColumn();
        page.MakeLabelsWhite();

        EditorUtility.SetDirty(page);
        EditorSceneManager.MarkSceneDirty(page.gameObject.scene);

        Debug.Log("MorePageOrderFix: Efeitos, Música, Rever tutorial, Rever tutorial 2x2, Política de " +
                  "privacidade, Conta, Créditos, in that order. PrivacyPanel starts switched off -- tick " +
                  "it to edit it. Save the scene.", page);
        return true;
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
