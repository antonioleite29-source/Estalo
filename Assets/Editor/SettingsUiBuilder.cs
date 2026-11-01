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
    private const string WhiteKey = "EstaloSettingsLabelsWhite";
    private const string TeamTutorialKey = "EstaloSettingsTeamTutorialButton";
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

    // The buttons already in the scene were made with the Jogar label colour. Turned white once,
    // automatically -- save the scene after.
    [InitializeOnLoadMethod]
    private static void WhiteOnce()
    {
        if (EditorPrefs.GetBool(WhiteKey, false))
            return;

        EditorApplication.delayCall += () =>
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                return;

            SettingsPage page = Object.FindAnyObjectByType<SettingsPage>(FindObjectsInactive.Include);

            if (page == null)
                return;

            foreach (TMPro.TMP_Text label in page.GetComponentsInChildren<TMPro.TMP_Text>(true))
                Undo.RecordObject(label, "White settings labels");

            page.MakeLabelsWhite();

            foreach (TMPro.TMP_Text label in page.GetComponentsInChildren<TMPro.TMP_Text>(true))
                EditorUtility.SetDirty(label);

            EditorSceneManager.MarkSceneDirty(page.gameObject.scene);
            EditorPrefs.SetBool(WhiteKey, true);
            Debug.Log("SettingsUiBuilder: settings button labels are white. Save the scene.", page);
        };
    }

    // The Tutorial 2x2 button arrived after the first four, so a scene built earlier is missing it.
    [InitializeOnLoadMethod]
    private static void AddTeamTutorialOnce()
    {
        if (EditorPrefs.GetBool(TeamTutorialKey, false))
            return;

        EditorApplication.delayCall += () =>
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                return;

            SettingsPage page = Object.FindAnyObjectByType<SettingsPage>(FindObjectsInactive.Include);

            if (page == null || page.teamTutorialButton != null)
                return;

            Undo.RecordObject(page, UndoName);

            foreach (GameObject go in page.Build(null))
                Undo.RegisterCreatedObjectUndo(go, UndoName);

            page.MakeLabelsWhite();
            EditorUtility.SetDirty(page);
            EditorSceneManager.MarkSceneDirty(page.gameObject.scene);
            EditorPrefs.SetBool(TeamTutorialKey, true);
            Debug.Log("SettingsUiBuilder: Tutorial 2x2 is on the More page. Save the scene.", page);
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
