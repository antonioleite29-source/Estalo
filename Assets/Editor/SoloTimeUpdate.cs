using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Raises the solo answer time from 3 to 4 seconds on the boards already saved in the scene.
//
// A tester in the September user test said the time to answer alone was "curto demois" -- too short.
// The field is serialized, so changing the default in code does nothing to the boards in the scene;
// this writes the new value once, in the editor. Save the scene afterwards. Undo puts 3 back.
[InitializeOnLoad]
public static class SoloTimeUpdate
{
    private const string DoneKey = "EstaloSoloTimeIsFour";
    private const float Seconds = 4f;

    static SoloTimeUpdate()
    {
        if (EditorPrefs.GetBool(DoneKey, false))
            return;

        EditorApplication.delayCall += () =>
        {
            if (!EditorApplication.isPlayingOrWillChangePlaymode && Apply())
                EditorPrefs.SetBool(DoneKey, true);
        };
    }

    [MenuItem("Trivia Duel/Setup/Set Solo Time To 4 Seconds")]
    private static bool Apply()
    {
        bool changed = false;

        foreach (TriviaDuelManager duel in Object.FindObjectsByType<TriviaDuelManager>(FindObjectsInactive.Include))
            changed |= Set(duel, ref duel.soloTimeSeconds, duel.gameObject);

        foreach (TeamDuelManager team in Object.FindObjectsByType<TeamDuelManager>(FindObjectsInactive.Include))
            changed |= Set(team, ref team.soloTimeSeconds, team.gameObject);

        if (!changed)
            Debug.Log("SoloTimeUpdate: the solo time is already 4 seconds on every board.");

        return changed;
    }

    private static bool Set(Object target, ref float field, GameObject owner)
    {
        if (Mathf.Approximately(field, Seconds))
            return false;

        Undo.RecordObject(target, "Set solo time");
        field = Seconds;
        EditorUtility.SetDirty(target);
        EditorSceneManager.MarkSceneDirty(owner.scene);

        Debug.Log($"SoloTimeUpdate: {owner.name} now gives {Seconds:0} seconds to answer alone. Save the scene.", owner);
        return true;
    }
}
