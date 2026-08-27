using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Puts the 2v2 board artwork on the 2v2 board.
//
// Done through the Editor rather than by editing the scene file, so Unity owns the write and
// cannot silently save over it.
public static class TeamBackgroundSetup
{
    private const string ArtworkPath = "Assets/Art/Backgrounds/Background2x2final.png";

    [MenuItem("Trivia Duel/Setup/Apply 2v2 Background")]
    public static void Apply()
    {
        Sprite artwork = LoadAsSingleSprite(ArtworkPath);

        if (artwork == null)
            return;

        TeamDuelManager team = Object.FindAnyObjectByType<TeamDuelManager>(FindObjectsInactive.Include);

        if (team == null)
        {
            Debug.LogError("TeamBackgroundSetup: no TeamDuelManager in the scene.");
            return;
        }

        Image board = team.teamBackground != null ? team.teamBackground : FindBoard(team);

        if (board == null)
        {
            Debug.LogError("TeamBackgroundSetup: no Image directly under Team Gameplay Root to put " +
                           "the artwork on.", team);
            return;
        }

        Undo.RecordObject(board, "Apply 2v2 background");
        board.sprite = artwork;

        // White, so the artwork shows at its own colours. A tint left over from an earlier
        // placeholder is the classic reason new art looks wrong the moment it goes in.
        board.color = Color.white;
        board.type = Image.Type.Simple;
        EditorUtility.SetDirty(board);

        // Wired explicitly rather than left to the runtime fallback, which walks the children and
        // takes the first Image it finds. That happens to be right today and stops being right the
        // moment anything is reordered.
        if (team.teamBackground != board)
        {
            Undo.RecordObject(team, "Wire the 2v2 background");
            team.teamBackground = board;
            EditorUtility.SetDirty(team);
        }

        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        Debug.Log($"TeamBackgroundSetup: '{board.name}' now shows {artwork.name}. Save the scene to keep it.", board);
    }

    // A texture imported as Multiple has no single sprite to hand an Image -- it produces
    // sub-sprites instead, and the assignment quietly does nothing. Switched to Single first.
    private static Sprite LoadAsSingleSprite(string path)
    {
        TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;

        if (importer == null)
        {
            Debug.LogError($"TeamBackgroundSetup: nothing importable at {path}.");
            return null;
        }

        if (importer.textureType != TextureImporterType.Sprite ||
            importer.spriteImportMode != SpriteImportMode.Single)
        {
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.alphaIsTransparency = true;
            importer.SaveAndReimport();

            Debug.Log("TeamBackgroundSetup: reimported the artwork as a single sprite.");
        }

        Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);

        if (sprite == null)
            Debug.LogError($"TeamBackgroundSetup: {path} still has no sprite after reimporting.");

        return sprite;
    }

    private static Image FindBoard(TeamDuelManager team)
    {
        if (team.teamGameplayRoot == null)
            return null;

        // The board sits first under the root, ahead of the question text and the answer buttons.
        foreach (Transform child in team.teamGameplayRoot.transform)
        {
            Image candidate = child.GetComponent<Image>();

            if (candidate != null && child.GetComponent<Selectable>() == null)
                return candidate;
        }

        return null;
    }
}
