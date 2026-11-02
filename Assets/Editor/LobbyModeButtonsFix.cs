using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

// Takes out the ModeButton and SoloButton copies an earlier version of this file put under Jogar.
// The lobby already has its own buttons; those copies were clutter sitting in the middle of the
// page saying "Clássico" and "Solo".
//
// Runs once on its own; save the scene after. Undo brings them back.
[InitializeOnLoad]
public static class RemoveBuiltLobbyButtons
{
    private const string DoneKey = "EstaloRemovedBuiltLobbyButtons";
    private const string UndoName = "Remove the built mode and solo buttons";

    static RemoveBuiltLobbyButtons()
    {
        if (EditorPrefs.GetBool(DoneKey, false))
            return;

        EditorApplication.delayCall += () =>
        {
            if (!EditorApplication.isPlayingOrWillChangePlaymode && Remove())
                EditorPrefs.SetBool(DoneKey, true);
        };
    }

    [MenuItem("Trivia Duel/Setup/Remove The Built Mode And Solo Buttons")]
    private static bool Remove()
    {
        LobbyPageSwitcher switcher = Object.FindAnyObjectByType<LobbyPageSwitcher>(FindObjectsInactive.Include);
        Button jogar = JogarButtonStyle.Template;

        if (switcher == null || jogar == null)
            return false;

        Transform parent = jogar.transform.parent;

        if (parent == null)
            return false;

        bool removed = false;

        for (int i = parent.childCount - 1; i >= 0; i--)
        {
            GameObject child = parent.GetChild(i).gameObject;

            if (child.name != "ModeButton" && child.name != "SoloButton")
                continue;

            Button button = child.GetComponent<Button>();

            // The switcher must not be left pointing at something that is about to be destroyed.
            if (button != null)
            {
                Undo.RecordObject(switcher, UndoName);

                if (switcher.ruleSetButton == button)
                    switcher.ruleSetButton = null;

                if (switcher.soloButton == button)
                    switcher.soloButton = null;

                EditorUtility.SetDirty(switcher);
            }

            Undo.DestroyObjectImmediate(child);
            removed = true;
        }

        if (!removed)
            return false;

        EditorSceneManager.MarkSceneDirty(switcher.gameObject.scene);
        Debug.Log("RemoveBuiltLobbyButtons: the copies under Jogar are gone. Save the scene.", switcher);
        return true;
    }
}

// Two menu items for pointing the lobby at buttons that are already in the scene: select the button
// in the Hierarchy, then pick the one it is. Quicker than finding the slot on the switcher, and it
// leaves the name alone.
public static class UseSelectedLobbyButton
{
    [MenuItem("Trivia Duel/Setup/Use Selected Button As The Mode Button")]
    private static void AsMode() => Assign(mode: true);

    [MenuItem("Trivia Duel/Setup/Use Selected Button As The Solo Button")]
    private static void AsSolo() => Assign(mode: false);

    private static void Assign(bool mode)
    {
        LobbyPageSwitcher switcher = Object.FindAnyObjectByType<LobbyPageSwitcher>(FindObjectsInactive.Include);
        Button button = Selection.activeGameObject != null
            ? Selection.activeGameObject.GetComponent<Button>()
            : null;

        if (switcher == null || button == null)
        {
            Debug.LogWarning("UseSelectedLobbyButton: select a GameObject with a Button on it first.");
            return;
        }

        Undo.RecordObject(switcher, "Point the lobby at a button");

        if (mode)
            switcher.ruleSetButton = button;
        else
            switcher.soloButton = button;

        EditorUtility.SetDirty(switcher);
        EditorSceneManager.MarkSceneDirty(switcher.gameObject.scene);

        Debug.Log($"UseSelectedLobbyButton: \"{button.name}\" is the {(mode ? "mode" : "solo")} button now. " +
                  "Save the scene.", button);
    }
}
