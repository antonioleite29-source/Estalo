using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

// Puts the nine-sliced button sprite on whatever is selected.
//
// A nine-sliced sprite keeps its corners and its edges at their own size however far the button is
// stretched, so one 160 x 160 file draws Jogar, 1x1, 2x2 and every settings button without the
// chamfer going oval on the wide ones -- which is what happens to Startbutton2.png and 1v1Button.png
// today, since they are two separate pictures of the same slab at two different shapes.
//
// Select the buttons in the Hierarchy, then Trivia Duel > UI > Nine Slice Selected Buttons. The white
// sprite keeps whatever colour the Image already had, so a cyan button stays cyan.
public static class NineSliceButtons
{
    private const string Folder = "Assets/Art/UI/NineSlice/";

    // Borders are 56 of the sprite's 160 pixels. Doubling the pixels-per-unit halves what they
    // measure on screen, so the corners stay crisp on a 100-unit-tall button as well as a 150.
    private const float PixelsPerUnitMultiplier = 2f;

    [MenuItem("Trivia Duel/UI/Nine Slice Selected Buttons")]
    private static void ApplyWhite() => Apply("ButtonNineSlice", keepColour: true);

    [MenuItem("Trivia Duel/UI/Nine Slice Selected Buttons (Slate)")]
    private static void ApplySlate() => Apply("ButtonSlateNineSlice", keepColour: false);

    [MenuItem("Trivia Duel/UI/Nine Slice Selected Buttons (Cyan)")]
    private static void ApplyCyan() => Apply("ButtonCyanNineSlice", keepColour: false);

    private static void Apply(string spriteName, bool keepColour)
    {
        Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(Folder + spriteName + ".png");

        if (sprite == null)
        {
            Debug.LogWarning($"NineSliceButtons: {Folder}{spriteName}.png is not in the project.");
            return;
        }

        if (sprite.border == Vector4.zero)
        {
            Debug.LogWarning($"NineSliceButtons: {spriteName} has no border, so it would stretch rather " +
                             "than slice. Set its Border in the Sprite Editor to 56 on all four sides.");
            return;
        }

        int changed = 0;

        foreach (GameObject go in Selection.gameObjects)
        {
            foreach (Image image in go.GetComponentsInChildren<Image>(true))
            {
                // The label's own background, if a button has one, is not the button.
                if (image.GetComponentInParent<Button>(true) == null)
                    continue;

                Undo.RecordObject(image, "Nine slice the button");

                image.sprite = sprite;
                image.type = Image.Type.Sliced;
                image.fillCenter = true;
                image.pixelsPerUnitMultiplier = PixelsPerUnitMultiplier;

                if (!keepColour)
                    image.color = Color.white;

                EditorUtility.SetDirty(image);
                changed++;
            }
        }

        if (changed == 0)
        {
            Debug.LogWarning("NineSliceButtons: nothing selected that has an Image under a Button.");
            return;
        }

        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        Debug.Log($"NineSliceButtons: {changed} button image(s) now use {spriteName}, sliced. Save the scene.");
    }
}
