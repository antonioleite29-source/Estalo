using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

// Adds "Deletar conta" to a More page that was built before it existed, under Créditos.
//
// Google Play asks any app you can sign into for a way to delete the account from inside the app.
// The button is made by copying Créditos, so it keeps whatever style the column already had.
//
// Runs once on its own; save the scene after. Undo takes the button back out.
[InitializeOnLoad]
public static class DeleteAccountButtonFix
{
    private const string DoneKey = "EstaloDeleteAccountButton";
    private const string UndoName = "Add the delete account button";

    static DeleteAccountButtonFix()
    {
        if (EditorPrefs.GetBool(DoneKey, false))
            return;

        EditorApplication.delayCall += () =>
        {
            if (!EditorApplication.isPlayingOrWillChangePlaymode && Add())
                EditorPrefs.SetBool(DoneKey, true);
        };
    }

    [MenuItem("Trivia Duel/Setup/Add Delete Account Button")]
    private static bool Add()
    {
        SettingsPage page = Object.FindAnyObjectByType<SettingsPage>(FindObjectsInactive.Include);

        if (page == null || page.deleteAccountButton != null || page.creditsButton == null)
            return false;

        GameObject copy = Object.Instantiate(page.creditsButton.gameObject, page.creditsButton.transform.parent);
        copy.name = "DeleteAccountButton";
        Undo.RegisterCreatedObjectUndo(copy, UndoName);

        RectTransform rect = (RectTransform)copy.transform;
        RectTransform credits = (RectTransform)page.creditsButton.transform;
        float height = credits.sizeDelta.y > 1f ? credits.sizeDelta.y : 188f;

        rect.sizeDelta = credits.sizeDelta;
        rect.anchoredPosition = credits.anchoredPosition + new Vector2(0f, -(height + 45f));
        copy.transform.SetSiblingIndex(page.creditsButton.transform.GetSiblingIndex() + 1);

        TMPro.TMP_Text label = copy.GetComponentInChildren<TMPro.TMP_Text>(true);

        if (label != null)
        {
            label.text = "Deletar conta";
            label.color = Color.white;
        }

        Undo.RecordObject(page, UndoName);
        page.deleteAccountButton = copy.GetComponent<Button>();
        EditorUtility.SetDirty(page);
        EditorSceneManager.MarkSceneDirty(page.gameObject.scene);

        Debug.Log("DeleteAccountButtonFix: Deletar conta is under Créditos on the More page. Save the scene.", page);
        return true;
    }
}
