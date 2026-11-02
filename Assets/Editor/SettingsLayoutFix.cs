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
