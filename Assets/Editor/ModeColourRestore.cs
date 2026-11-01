using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Puts the 1x1 and 2x2 buttons back to the colours they had before LobbyContrastFix repainted them
// white and grey, which looked wrong against the button artwork.
//
// Cyan for the mode you picked, white for the other: the pair the scene was built with. Runs once,
// in the editor, on its own; save the scene after. Undo restores whatever was there.
[InitializeOnLoad]
public static class ModeColourRestore
{
    private const string DoneKey = "EstaloModeColoursRestored";

    static ModeColourRestore()
    {
        if (EditorPrefs.GetBool(DoneKey, false))
            return;

        EditorApplication.delayCall += () =>
        {
            if (!EditorApplication.isPlayingOrWillChangePlaymode && Restore())
                EditorPrefs.SetBool(DoneKey, true);
        };
    }

    [MenuItem("Trivia Duel/Setup/Restore Mode Button Colours")]
    private static bool Restore()
    {
        bool changed = false;

        foreach (LobbyPageSwitcher switcher in Object.FindObjectsByType<LobbyPageSwitcher>(FindObjectsInactive.Include))
        {
            Undo.RecordObject(switcher, "Restore mode button colours");
            switcher.selectedModeColor = new Color32(34, 184, 207, 255);
            switcher.unselectedModeColor = Color.white;
            EditorUtility.SetDirty(switcher);
            EditorSceneManager.MarkSceneDirty(switcher.gameObject.scene);

            Debug.Log("ModeColourRestore: 1x1 and 2x2 are back to cyan and white. Save the scene.", switcher);
            changed = true;
        }

        return changed;
    }
}
