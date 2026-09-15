using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Puts the Profile page's account button and the login panel into the scene as real objects, so they
// can be moved, resized and restyled in the editor before pressing Play. Both are styled like the
// "Jogar" button when made; after that, whatever you change on them is kept.
//
// Safe to run again: anything already wired on ProfilePageController is left alone.
public static class LoginUiBuilder
{
    [MenuItem("Trivia Duel/Setup/Build Login Buttons")]
    private static void Build()
    {
        ProfilePageController profile =
            Object.FindAnyObjectByType<ProfilePageController>(FindObjectsInactive.Include);

        if (profile == null)
        {
            Debug.LogError("LoginUiBuilder: no ProfilePageController in the open scene.");
            return;
        }

        TMP_Text heading = profile.GetComponentInChildren<TMP_Text>(true);
        TMP_FontAsset font = heading != null ? heading.font : null;

        Undo.RecordObject(profile, "Build login buttons");

        if (profile.accountButton == null)
        {
            profile.accountButton = ProfilePageController.CreateAccountButton(profile.transform, font);
            Undo.RegisterCreatedObjectUndo(profile.accountButton.gameObject, "Build login buttons");
        }

        if (profile.loginPanel == null)
        {
            profile.loginPanel = LoginPanel.CreateUnder(profile.transform, font);
            Undo.RegisterCreatedObjectUndo(profile.loginPanel.gameObject, "Build login buttons");
        }

        EditorUtility.SetDirty(profile);
        EditorSceneManager.MarkSceneDirty(profile.gameObject.scene);
        Selection.activeGameObject = profile.loginPanel.gameObject;

        Debug.Log("LoginUiBuilder: AccountButton and LoginPanel are on the Profile page. LoginPanel starts " +
                  "switched off -- tick it to edit it, untick it again, then save the scene.", profile);
    }
}
