using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Imports the 2v2 opening frames and hands them to TeamDuelManager in order.
//
// Order is the whole job. A frame sequence dragged in by hand arrives in whatever order the
// Project window happened to be sorted in, and a single frame out of place is the kind of fault
// that is obvious in motion and invisible in the Inspector.
public static class TeamIntroSetup
{
    private const string FrameFolder = "Assets/Art/SoloTransition/IntroAnimation2v2";

    [MenuItem("Trivia Duel/Setup/Apply 2v2 Opening Animation")]
    public static void Apply()
    {
        List<Sprite> frames = ImportFrames();

        if (frames.Count == 0)
        {
            Debug.LogError($"TeamIntroSetup: no sprites in {FrameFolder}.");
            return;
        }

        TeamDuelManager team = Object.FindAnyObjectByType<TeamDuelManager>(FindObjectsInactive.Include);

        if (team == null)
        {
            Debug.LogError("TeamIntroSetup: no TeamDuelManager in the scene.");
            return;
        }

        Undo.RecordObject(team, "Apply 2v2 opening animation");
        team.matchStartFrames = frames.ToArray();
        EditorUtility.SetDirty(team);

        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());

        Debug.Log($"TeamIntroSetup: {frames.Count} frames wired, {frames[0].name} to " +
                  $"{frames[frames.Count - 1].name}. Save the scene to keep it.", team);
    }

    private static List<Sprite> ImportFrames()
    {
        List<Sprite> frames = new List<Sprite>();
        string[] guids = AssetDatabase.FindAssets("t:Texture2D", new[] { FrameFolder });
        List<string> paths = new List<string>();

        foreach (string guid in guids)
            paths.Add(AssetDatabase.GUIDToAssetPath(guid));

        // Sorted by filename, which is what the trailing numbers on an exported sequence are for.
        paths.Sort(string.CompareOrdinal);

        foreach (string path in paths)
        {
            TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;

            if (importer == null)
                continue;

            // Single, not Multiple. A texture imported as Multiple produces sub-sprites and no
            // single sprite to put in a list, and the assignment quietly does nothing.
            if (importer.textureType != TextureImporterType.Sprite ||
                importer.spriteImportMode != SpriteImportMode.Single)
            {
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.alphaIsTransparency = true;
                importer.SaveAndReimport();
            }

            Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);

            if (sprite != null)
                frames.Add(sprite);
        }

        return frames;
    }
}
