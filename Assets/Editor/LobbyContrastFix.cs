using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

// Two contrast problems visible in the first phone screenshots, fixed once in the scene.
//
// "Jogar" was pale blue on a blue button, which reads as a disabled control -- the one button the
// whole lobby exists for. And the two mode buttons were tinted white and cyan, so whichever mode was
// NOT selected looked like the brighter of the two.
//
// Runs once on its own the first time Unity compiles it with the scene open; save the scene after.
// Undo puts the old colours back.
[InitializeOnLoad]
public static class LobbyContrastFix
{
    private const string DoneKey = "EstaloLobbyContrastFixed";
    private const string UndoName = "Fix lobby contrast";

    static LobbyContrastFix()
    {
        if (EditorPrefs.GetBool(DoneKey, false))
            return;

        EditorApplication.delayCall += () =>
        {
            if (!EditorApplication.isPlayingOrWillChangePlaymode && Fix())
                EditorPrefs.SetBool(DoneKey, true);
        };
    }

    [MenuItem("Trivia Duel/Setup/Fix Lobby Contrast")]
    private static bool Fix()
    {
        bool changed = false;

        foreach (LobbyPageSwitcher switcher in Object.FindObjectsByType<LobbyPageSwitcher>(FindObjectsInactive.Include))
        {
            Undo.RecordObject(switcher, UndoName);

            // White for the mode you picked, a dimmed grey for the other. Both were light before, so
            // the selection was carried only by a small difference in hue.
            switcher.selectedModeColor = Color.white;
            switcher.unselectedModeColor = new Color(0.62f, 0.62f, 0.70f, 1f);

            EditorUtility.SetDirty(switcher);
            EditorSceneManager.MarkSceneDirty(switcher.gameObject.scene);
            changed = true;
        }

        foreach (TMP_Text label in Object.FindObjectsByType<TMP_Text>(FindObjectsInactive.Include))
        {
            if (label.text.Trim() != "Jogar" || label.GetComponentInParent<Button>(true) == null)
                continue;

            Undo.RecordObject(label, UndoName);
            label.color = Color.white;
            EditorUtility.SetDirty(label);
            EditorSceneManager.MarkSceneDirty(label.gameObject.scene);

            Debug.Log("LobbyContrastFix: the Jogar label is white now. Save the scene.", label);
            changed = true;
        }

        return changed;
    }
}
