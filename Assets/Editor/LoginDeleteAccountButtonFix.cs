using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

// The same button on the account panel, under Fechar: the player who taps "Conta" to look at their
// account is the one who wants to delete it, so it is there as well as on the More page.
[InitializeOnLoad]
public static class LoginDeleteAccountButtonFix
{
    private const string DoneKey = "EstaloLoginDeleteAccountButton";
    private const string UndoName = "Add the delete account button to the login panel";

    static LoginDeleteAccountButtonFix()
    {
        if (EditorPrefs.GetBool(DoneKey, false))
            return;

        EditorApplication.delayCall += () =>
        {
            if (!EditorApplication.isPlayingOrWillChangePlaymode && Add())
                EditorPrefs.SetBool(DoneKey, true);
        };
    }

    [MenuItem("Trivia Duel/Setup/Add Delete Account Button To Login")]
    private static bool Add()
    {
        LoginPanel panel = Object.FindAnyObjectByType<LoginPanel>(FindObjectsInactive.Include);

        if (panel == null || panel.deleteAccountButton != null || panel.closeButton == null)
            return false;

        GameObject copy = Object.Instantiate(panel.closeButton.gameObject, panel.closeButton.transform.parent);
        copy.name = "DeletarContaButton";
        Undo.RegisterCreatedObjectUndo(copy, UndoName);

        RectTransform rect = (RectTransform)copy.transform;
        RectTransform close = (RectTransform)panel.closeButton.transform;
        float height = close.sizeDelta.y > 1f ? close.sizeDelta.y : 150f;

        // Shorter than the others, below Fechar, so it is the quietest thing on the panel.
        rect.sizeDelta = new Vector2(close.sizeDelta.x, height * 0.72f);
        rect.anchoredPosition = close.anchoredPosition + new Vector2(0f, -(height * 0.86f + 20f));
        copy.transform.SetSiblingIndex(panel.closeButton.transform.GetSiblingIndex() + 1);

        TMPro.TMP_Text label = copy.GetComponentInChildren<TMPro.TMP_Text>(true);

        if (label != null)
        {
            label.text = "Deletar conta";
            label.color = Color.white;
        }

        Undo.RecordObject(panel, UndoName);
        panel.deleteAccountButton = copy.GetComponent<Button>();
        EditorUtility.SetDirty(panel);
        EditorSceneManager.MarkSceneDirty(panel.gameObject.scene);

        Debug.Log("LoginDeleteAccountButtonFix: Deletar conta is on the account panel, under Fechar. " +
                  "Save the scene.", panel);
        return true;
    }
}
