using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

// Brings the account panel up to date: adds whatever arrived after it was built -- Mostrar senha, the
// password hint and the button that reads one back, Deletar conta -- and lays the whole panel out
// again, two pairs sharing a row, so eleven things fit on a phone.
//
// New pieces are copied from the panel's own fields and buttons, so they keep its style. Runs once on
// its own; save the scene after. Undo puts the old panel back.
[InitializeOnLoad]
public static class LoginPanelSetupFix
{
    private const string DoneKey = "EstaloLoginPanelHintAndDelete";
    private const string UndoName = "Bring the account panel up to date";

    static LoginPanelSetupFix()
    {
        if (EditorPrefs.GetBool(DoneKey, false))
            return;

        EditorApplication.delayCall += () =>
        {
            if (!EditorApplication.isPlayingOrWillChangePlaymode && Fix())
                EditorPrefs.SetBool(DoneKey, true);
        };
    }

    [MenuItem("Trivia Duel/Setup/Bring The Account Panel Up To Date")]
    private static bool Fix()
    {
        LoginPanel panel = Object.FindAnyObjectByType<LoginPanel>(FindObjectsInactive.Include);

        if (panel == null || panel.signInButton == null || panel.passwordField == null)
            return false;

        Undo.RecordObject(panel, UndoName);

        if (panel.showPasswordButton == null)
            panel.showPasswordButton = CopyButton(panel, "MostrarSenhaButton", "Mostrar senha");

        if (panel.hintButton == null)
            panel.hintButton = CopyButton(panel, "VerDicaButton", "Ver dica da senha");

        if (panel.deleteAccountButton == null)
            panel.deleteAccountButton = CopyButton(panel, "DeletarContaButton", SettingsPage.DeleteLabels[0]);

        if (panel.hintField == null)
            panel.hintField = CopyHintField(panel);

        foreach (Transform child in panel.transform)
            Undo.RecordObject(child, UndoName);

        panel.LayOutPanel();

        EditorUtility.SetDirty(panel);
        EditorSceneManager.MarkSceneDirty(panel.gameObject.scene);

        Debug.Log("LoginPanelSetupFix: the account panel has Mostrar senha, a dica da senha with Ver dica, " +
                  "and Deletar conta, laid out in one column. Save the scene.", panel);
        return true;
    }

    private static Button CopyButton(LoginPanel panel, string name, string text)
    {
        GameObject copy = Object.Instantiate(panel.signInButton.gameObject, panel.signInButton.transform.parent);
        copy.name = name;
        Undo.RegisterCreatedObjectUndo(copy, UndoName);

        TMPro.TMP_Text label = copy.GetComponentInChildren<TMPro.TMP_Text>(true);

        if (label != null)
        {
            label.text = text;
            label.color = Color.white;
        }

        return copy.GetComponent<Button>();
    }

    // Copied from the password field, so it has the same font and size, then turned back into an
    // ordinary visible field: a hint nobody can read is no use to anybody.
    private static TMPro.TMP_InputField CopyHintField(LoginPanel panel)
    {
        GameObject copy = Object.Instantiate(panel.passwordField.gameObject, panel.passwordField.transform.parent);
        copy.name = "DicaField";
        Undo.RegisterCreatedObjectUndo(copy, UndoName);

        TMPro.TMP_InputField field = copy.GetComponent<TMPro.TMP_InputField>();
        field.contentType = TMPro.TMP_InputField.ContentType.Standard;
        field.characterLimit = PlayerAccounts.MaxHintLength;
        field.text = string.Empty;

        if (field.placeholder is TMPro.TMP_Text placeholder)
            placeholder.text = "Dica da senha (opcional)";

        return field;
    }
}
