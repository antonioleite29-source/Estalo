using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Stops the learning path looking like a stack of blurry squares, and puts the 2v2 board away.
public static class LearningArtSetup
{
    private const string PathArt = "Assets/Resources";

    [MenuItem("Trivia Duel/Setup/Fix Learning Page Art")]
    public static void Apply()
    {
        if (EditorApplication.isPlaying)
        {
            Debug.LogError("LearningArtSetup: stop Play mode first. Changes made while playing are " +
                           "thrown away.");
            return;
        }

        UncompressPathArt();
        HideTeamBoard();

        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        Debug.Log("LearningArtSetup: done. Save the scene to keep it.");
    }

    // These are 158x316 and get stretched across the whole height of a phone -- around seven times
    // up. Block compression works in 4x4 blocks, so at that magnification every block becomes a
    // visible smear, which is the "square blur" where two images meet. They are a few kilobytes
    // each; there is nothing to save by compressing them.
    private static void UncompressPathArt()
    {
        int changed = 0;

        foreach (string guid in AssetDatabase.FindAssets("t:Texture2D", new[] { PathArt }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);

            if (!System.IO.Path.GetFileName(path).StartsWith("Path"))
                continue;

            TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;

            if (importer == null || importer.textureCompression == TextureImporterCompression.Uncompressed)
                continue;

            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.mipmapEnabled = false;
            importer.filterMode = FilterMode.Bilinear;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.SaveAndReimport();
            changed++;

            Debug.Log($"LearningArtSetup: {System.IO.Path.GetFileName(path)} uncompressed.");
        }

        if (changed == 0)
            Debug.Log("LearningArtSetup: the path art was already uncompressed.");
    }

    // It was saved switched on, and nothing used to switch it off, so every other screen drew
    // underneath a live 2v2 board. The managers put each other away now; this clears the state it
    // was saved in.
    private static void HideTeamBoard()
    {
        TeamDuelManager team = Object.FindAnyObjectByType<TeamDuelManager>(FindObjectsInactive.Include);

        if (team == null || team.teamGameplayRoot == null || !team.teamGameplayRoot.activeSelf)
            return;

        Undo.RecordObject(team.teamGameplayRoot, "Hide the 2v2 board");
        team.teamGameplayRoot.SetActive(false);
        EditorUtility.SetDirty(team.teamGameplayRoot);

        Debug.Log("LearningArtSetup: the 2v2 board was left active in the scene — switched off.",
                  team.teamGameplayRoot);
    }
}
