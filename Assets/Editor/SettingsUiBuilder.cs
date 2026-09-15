using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Puts the settings buttons on the More page as real scene objects: Som, Conta, Rever tutorial and
// Créditos, plus the credits panel (switched off). Runs once on its own the first time Unity compiles
// it with the scene open -- save the scene afterwards -- and stays in the menu. Undo removes it all.
// Safe to run again: only what is missing is made.
[InitializeOnLoad]
public static class SettingsUiBuilder
{
    private const string BuiltKey = "EstaloSettingsButtonsBuilt";
    private const string UndoName = "Build settings buttons";

    static SettingsUiBuilder()
    {
        if (EditorPrefs.GetBool(BuiltKey, false))
            return;

        EditorApplication.delayCall += () =>
        {
            if (!EditorApplication.isPlayingOrWillChangePlaymode && Build())
                EditorPrefs.SetBool(BuiltKey, true);
        };
    }

    [MenuItem("Trivia Duel/Setup/Build Settings Buttons")]
    private static void BuildFromMenu()
    {
        Build();
    }

    private static bool Build()
    {
        LobbyPageSwitcher switcher = Object.FindAnyObjectByType<LobbyPageSwitcher>(FindObjectsInactive.Include);

        if (switcher == null || switcher.morePage == null)
        {
            Debug.LogWarning("SettingsUiBuilder: no LobbyPageSwitcher with a More page in the open scene.");
            return false;
        }

        GameObject more = switcher.morePage;

        // Recorded before Build switches it off, so Undo brings the old screen back too.
        ConnectPageController connect = more.GetComponentInChildren<ConnectPageController>(true);
        Transform content = connect != null ? connect.transform.Find("Content") : null;

        if (content != null)
            Undo.RecordObject(content.gameObject, UndoName);

        SettingsPage page = more.GetComponent<SettingsPage>();

        if (page == null)
            page = Undo.AddComponent<SettingsPage>(more);

        Undo.RecordObject(page, UndoName);

        foreach (GameObject go in page.Build(null))
            Undo.RegisterCreatedObjectUndo(go, UndoName);

        EditorUtility.SetDirty(page);
        EditorSceneManager.MarkSceneDirty(more.scene);

        Debug.Log("SettingsUiBuilder: Som, Conta, Rever tutorial and Créditos are on the More page. " +
                  "CreditsPanel starts switched off -- tick it to edit it. Save the scene.", page);
        return true;
    }
}
