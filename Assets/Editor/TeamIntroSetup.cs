using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Imports the 2v2 exported sequences and hands them to TeamDuelManager in order.
//
// Order is the whole job. A frame sequence dragged in by hand arrives in whatever order the
// Project window happened to be sorted in, and a single frame out of place is the kind of fault
// that is obvious in motion and invisible in the Inspector.
public static class TeamIntroSetup
{
    private const string OpeningFolder = "Assets/Art/SoloTransition/IntroAnimation2v2";
    private const string BlueSoloFolder = "Assets/Art/SoloTransition/BlueSolo2v2";
    private const string RedSoloFolder = "Assets/Art/SoloTransition/RedSolo2v2";

    [MenuItem("Trivia Duel/Setup/Apply 2v2 Animations")]
    public static void Apply()
    {
        // Nothing scene-shaped survives Play mode: Unity discards every change on exit, so a tool
        // run now reports success and leaves no trace. Worse, it reads the RUNNING values --
        // TextFitsItsBox has already shrunk labels to fit by then, so a font authored at 100pt
        // measures 41 and any box sized from it comes out wrong as well as unsaved.
        if (EditorApplication.isPlaying)
        {
            Debug.LogError("TeamIntroSetup: stop Play mode first. Changes made while playing are thrown " +
                           "away, and the values read are the running ones rather than the real ones.");
            return;
        }

        TeamDuelManager team = Object.FindAnyObjectByType<TeamDuelManager>(FindObjectsInactive.Include);

        if (team == null)
        {
            Debug.LogError("TeamIntroSetup: no TeamDuelManager in the scene.");
            return;
        }

        Undo.RecordObject(team, "Apply 2v2 animations");

        team.matchStartFrames = Wire(OpeningFolder, "opening");
        team.yourSoloFrames = Wire(BlueSoloFolder, "blue solo");
        team.otherSoloFrames = Wire(RedSoloFolder, "red solo");

        EditorUtility.SetDirty(team);
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());

        Debug.Log("TeamIntroSetup: done. Save the scene to keep it.", team);
    }

    private static Sprite[] Wire(string folder, string label)
    {
        List<Sprite> frames = ImportFrames(folder);

        if (frames.Count == 0)
        {
            Debug.LogWarning($"TeamIntroSetup: no sprites in {folder}, so the {label} is left empty.");
            return new Sprite[0];
        }

        Debug.Log($"TeamIntroSetup: {label} — {frames.Count} frames, " +
                  $"{frames[0].name} to {frames[frames.Count - 1].name}.");

        return frames.ToArray();
    }

    private static List<Sprite> ImportFrames(string folder)
    {
        List<Sprite> frames = new List<Sprite>();
        string[] guids = AssetDatabase.FindAssets("t:Texture2D", new[] { folder });
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
