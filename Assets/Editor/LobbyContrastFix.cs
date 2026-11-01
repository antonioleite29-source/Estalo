using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

// "Jogar" was pale blue on a blue button, which reads as a disabled control -- the one button the
// whole lobby exists for.
//
// This used to repaint the 1x1 and 2x2 buttons as well, white against grey. It looked wrong on the
// real artwork, so those two keep the colours they were authored with; ModeColourRestore puts them
// back on a scene this already touched.
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
