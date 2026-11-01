using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

// Puts the twelve new avatars on the Profile page, as two rows of six.
//
// The page had six avatars in a single horizontal row, and the art was licensed stock portraits with
// a watermark on them. This swaps the row for a grid, makes as many buttons as there are avatars by
// copying the first one, and fills in the three arrays that drive them -- the sprites on
// PlayerProfileManager and the images and highlights on ProfilePageController.
//
// Runs once on its own the first time Unity compiles it with the scene open; save the scene after.
// Undo puts the old row back. Safe to run again from the menu: it re-reads the folder and rebuilds.
[InitializeOnLoad]
public static class AvatarGridBuilder
{
    private const string DoneKey = "EstaloAvatarGridBuilt";
    private const string UndoName = "Build avatar grid";
    private const string AvatarFolder = "Assets/Art/Avatars";

    // Six per row: the file names are Robo1-6 then Blob1-6, so a row of robots sits above a row of
    // blobs.
    private const int Columns = 6;

    static AvatarGridBuilder()
    {
        if (EditorPrefs.GetBool(DoneKey, false))
            return;

        EditorApplication.delayCall += () =>
        {
            if (!EditorApplication.isPlayingOrWillChangePlaymode && Build())
                EditorPrefs.SetBool(DoneKey, true);
        };
    }

    [MenuItem("Trivia Duel/Setup/Build Avatar Grid")]
    private static bool Build()
    {
        ProfilePageController profile = Object.FindAnyObjectByType<ProfilePageController>(FindObjectsInactive.Include);
        PlayerProfileManager manager = Object.FindAnyObjectByType<PlayerProfileManager>(FindObjectsInactive.Include);

        if (profile == null || manager == null)
        {
            Debug.LogWarning("AvatarGridBuilder: no ProfilePageController or PlayerProfileManager in the open scene.");
            return false;
        }

        Sprite[] sprites = LoadSprites();

        if (sprites.Length == 0)
        {
            Debug.LogWarning($"AvatarGridBuilder: no sprites in {AvatarFolder}, so there is nothing to show.");
            return false;
        }

        if (profile.avatarImages == null || profile.avatarImages.Length == 0 || profile.avatarImages[0] == null)
        {
            Debug.LogWarning("AvatarGridBuilder: the Profile page has no avatar buttons to copy.");
            return false;
        }

        Image first = profile.avatarImages[0];
        RectTransform row = first.transform.parent as RectTransform;

        if (row == null)
            return false;

        float cell = ((RectTransform)first.transform).rect.width;

        if (cell <= 1f)
            cell = 142f;

        // A grid instead of a row, so twelve avatars wrap onto a second line rather than shrinking
        // to a sixth of the width each.
        HorizontalLayoutGroup horizontal = row.GetComponent<HorizontalLayoutGroup>();

        if (horizontal != null)
            Undo.DestroyObjectImmediate(horizontal);

        GridLayoutGroup grid = row.GetComponent<GridLayoutGroup>();

        if (grid == null)
            grid = Undo.AddComponent<GridLayoutGroup>(row.gameObject);

        Undo.RecordObject(grid, UndoName);
        grid.cellSize = new Vector2(cell, cell);
        grid.spacing = new Vector2(cell * 0.12f, cell * 0.12f);
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = Columns;
        grid.childAlignment = TextAnchor.UpperCenter;

        // The row's own box has to be tall enough for two rows, or the second one is laid out under
        // the bottom edge of a box sized for one.
        int rows = Mathf.CeilToInt(sprites.Length / (float)Columns);
        Undo.RecordObject(row, UndoName);
        row.sizeDelta = new Vector2(Columns * cell + (Columns - 1) * grid.spacing.x,
                                    rows * cell + (rows - 1) * grid.spacing.y);

        List<Image> images = new List<Image>();
        List<GameObject> highlights = new List<GameObject>();

        for (int i = 0; i < sprites.Length; i++)
        {
            GameObject button = i < profile.avatarImages.Length && profile.avatarImages[i] != null
                ? profile.avatarImages[i].gameObject
                : CopyButton(first.gameObject, row, i);

            button.name = "AvatarButton" + (i + 1);

            Image image = button.GetComponent<Image>();
            Undo.RecordObject(image, UndoName);
            image.sprite = sprites[i];
            image.preserveAspect = true;
            EditorUtility.SetDirty(image);

            images.Add(image);

            Transform highlight = button.transform.Find("Highlight");

            if (highlight != null)
                highlights.Add(highlight.gameObject);
        }

        // Anything left over from the old six-button row, if the folder now holds fewer.
        for (int i = sprites.Length; i < profile.avatarImages.Length; i++)
        {
            if (profile.avatarImages[i] != null)
                Undo.DestroyObjectImmediate(profile.avatarImages[i].gameObject);
        }

        Undo.RecordObject(profile, UndoName);
        profile.avatarImages = images.ToArray();
        profile.avatarHighlights = highlights.ToArray();
        EditorUtility.SetDirty(profile);

        Undo.RecordObject(manager, UndoName);
        manager.availableAvatars = sprites;
        EditorUtility.SetDirty(manager);

        EditorSceneManager.MarkSceneDirty(profile.gameObject.scene);

        Debug.Log($"AvatarGridBuilder: {sprites.Length} avatars on the Profile page, {Columns} per row. " +
                  "Save the scene.", profile);
        return true;
    }

    // Robo1-6 first, then Blob1-6: the order here is the order the player sees.
    private static Sprite[] LoadSprites()
    {
        List<Sprite> sprites = new List<Sprite>();

        foreach (string prefix in new[] { "Robo", "Blob" })
        {
            for (int i = 1; i <= 24; i++)
            {
                Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>($"{AvatarFolder}/{prefix}{i}.png");

                if (sprite == null)
                    break;

                sprites.Add(sprite);
            }
        }

        return sprites.ToArray();
    }

    private static GameObject CopyButton(GameObject template, RectTransform parent, int index)
    {
        GameObject copy = Object.Instantiate(template, parent);
        copy.transform.SetSiblingIndex(index);
        Undo.RegisterCreatedObjectUndo(copy, UndoName);
        return copy;
    }
}
