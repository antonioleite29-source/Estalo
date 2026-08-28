using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Stops the learning path looking like a stack of blurry squares, and puts the 2v2 board away.
public static class LearningArtSetup
{
    private const string PathArt = "Assets/Resources";
    private const string NewPathArt = "Assets/Art/UI/LearningPath";

    [MenuItem("Trivia Duel/Setup/Fix Learning Page Art")]
    public static void Apply()
    {
        if (EditorApplication.isPlaying)
        {
            Debug.LogError("LearningArtSetup: stop Play mode first. Changes made while playing are " +
                           "thrown away.");
            return;
        }

        WireNewPathArt();
        DockMistakesBar();
        HideTeamBoard();

        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        Debug.Log("LearningArtSetup: done. Save the scene to keep it.");
    }

    // The scrolling artwork, imported and handed to the scroller in filename order.
    //
    // Unlike the old 158x316 set, these are 4913x10650 at the phone's own aspect. Unity clamps the
    // long side to maxTextureSize, so they land around 945x2048 and are drawn at roughly 1180 wide
    // -- about a 1.25x upscale rather than seven times up.
    //
    // That is why they stay COMPRESSED. Block compression only became visible before because every
    // 4x4 block was being magnified into a smear; at this scale the blocks are around a pixel and
    // the alternative is roughly 31MB of texture for four decorations on the learning page.
    private static void WireNewPathArt()
    {
        List<Sprite> frames = new List<Sprite>();
        List<string> paths = new List<string>();

        foreach (string guid in AssetDatabase.FindAssets("t:Texture2D", new[] { NewPathArt }))
            paths.Add(AssetDatabase.GUIDToAssetPath(guid));

        paths.Sort(string.CompareOrdinal);

        foreach (string path in paths)
        {
            TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;

            if (importer == null)
                continue;

            if (importer.textureType != TextureImporterType.Sprite ||
                importer.spriteImportMode != SpriteImportMode.Single ||
                importer.maxTextureSize != 2048)
            {
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.maxTextureSize = 2048;
                importer.mipmapEnabled = false;
                importer.filterMode = FilterMode.Bilinear;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.textureCompression = TextureImporterCompression.CompressedHQ;
                importer.SaveAndReimport();
            }

            Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);

            if (sprite != null)
                frames.Add(sprite);
        }

        if (frames.Count == 0)
        {
            Debug.LogError($"LearningArtSetup: no sprites in {NewPathArt}.");
            return;
        }

        LobbyLearningScroller scroller =
            Object.FindAnyObjectByType<LobbyLearningScroller>(FindObjectsInactive.Include);

        if (scroller == null)
        {
            Debug.LogError("LearningArtSetup: no LobbyLearningScroller in the scene.");
            return;
        }

        Undo.RecordObject(scroller, "Wire the learning path art");
        scroller.sourceSprites = frames.ToArray();
        EditorUtility.SetDirty(scroller);

        Debug.Log($"LearningArtSetup: {frames.Count} path images wired, {frames[0].name} to " +
                  $"{frames[frames.Count - 1].name}.", scroller);
    }

    // The "Praticar meus erros" bar was floating a third of the way up the page, which was fine
    // when the page was scrolling wallpaper and is not fine now that lesson nodes scroll past
    // underneath it -- it would swallow the taps meant for whichever node was behind it.
    //
    // Docked to the bottom instead, and handed to the scroller so the column leaves room for it.
    private static void DockMistakesBar()
    {
        LobbyLearningScroller scroller =
            Object.FindAnyObjectByType<LobbyLearningScroller>(FindObjectsInactive.Include);

        if (scroller == null)
            return;

        RectTransform bar = null;

        foreach (RectTransform candidate in scroller.GetComponentsInChildren<RectTransform>(true))
        {
            if (candidate.name == "PractiseMistakesButton")
            {
                bar = candidate;
                break;
            }
        }

        if (bar == null)
        {
            Debug.LogWarning("LearningArtSetup: no PractiseMistakesButton on the learning page.");
            return;
        }

        Undo.RecordObject(bar, "Dock the practise-mistakes bar");
        bar.anchorMin = new Vector2(0.5f, 0f);
        bar.anchorMax = new Vector2(0.5f, 0f);
        bar.pivot = new Vector2(0.5f, 0f);
        bar.anchoredPosition = new Vector2(0f, 40f);
        bar.SetAsLastSibling();
        EditorUtility.SetDirty(bar);

        Undo.RecordObject(scroller, "Reserve room for the practise-mistakes bar");
        scroller.pinnedFooter = bar;
        EditorUtility.SetDirty(scroller);

        Debug.Log("LearningArtSetup: the practise-mistakes bar is docked at the bottom of the " +
                  "learning page and the lesson column now leaves room for it.", bar);
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
