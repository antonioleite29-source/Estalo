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
    private const string DuelArtworkPath = "Assets/Art/Backgrounds/Backgounrd1x1final.png";

    [MenuItem("Trivia Duel/Setup/Apply 2v2 Board")]
    public static void Apply()
    {
        // Nothing scene-shaped survives Play mode: Unity discards every change on exit, so a tool
        // run now reports success and leaves no trace. Worse, it reads the RUNNING values --
        // TextFitsItsBox has already shrunk labels to fit by then, so a font authored at 100pt
        // measures 41 and any box sized from it comes out wrong as well as unsaved.
        if (EditorApplication.isPlaying)
        {
            Debug.LogError("TeamBackgroundSetup: stop Play mode first. Changes made while playing are thrown " +
                           "away, and the values read are the running ones rather than the real ones.");
            return;
        }

        SizeScoreBoxes();
        CoverTheScreen();
        ApplyDuelBoard();

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
    // The 1v1 board, which is a different mechanism entirely.
    //
    // Nothing shows the sprite sitting on that Image: entering Open Buzz applies
    // openBuzzBackground.backgroundSprite over it every round. So the artwork has to go on the
    // STATE, not on the Image — putting it on the Image alone would look right in the Scene view
    // and be replaced the moment a match started.
    private static void ApplyDuelBoard()
    {
        Sprite artwork = LoadAsSingleSprite(DuelArtworkPath);

        if (artwork == null)
            return;

        TriviaDuelManager duel = Object.FindAnyObjectByType<TriviaDuelManager>(FindObjectsInactive.Include);

        if (duel == null)
        {
            Debug.LogError("TeamBackgroundSetup: no TriviaDuelManager in the scene.");
            return;
        }

        Undo.RecordObject(duel, "Apply 1v1 background");
        duel.openBuzzBackground.backgroundSprite = artwork;
        EditorUtility.SetDirty(duel);

        // And on the Image too, so the Scene view shows what the game will rather than whichever
        // placeholder was last dragged onto it.
        if (duel.gameBackground != null)
        {
            Undo.RecordObject(duel.gameBackground, "Apply 1v1 background");
            duel.gameBackground.sprite = artwork;
            duel.gameBackground.color = Color.white;
            EditorUtility.SetDirty(duel.gameBackground);
        }

        Debug.Log($"TeamBackgroundSetup: 1v1 Open Buzz now shows {artwork.name}.", duel);
    }

    // The board has to reach the edges of the screen, or it does not.
    //
    // It was 1173 x 2506 against a 1179 x 2556 design, and nudged four pixels right and seven up on
    // top of that -- so roughly twenty-five pixels of nothing at the top and bottom, showing
    // whatever sits behind. A background is one of the few things that should deliberately
    // overshoot: there is no cost to covering more than the screen and a visible seam the moment
    // it covers less.
    private const float Bleed = 24f;

    private static void CoverTheScreen()
    {
        TeamDuelManager team = Object.FindAnyObjectByType<TeamDuelManager>(FindObjectsInactive.Include);

        if (team == null || team.teamBackground == null)
            return;

        CanvasStretchFitter fitter = Object.FindAnyObjectByType<CanvasStretchFitter>(FindObjectsInactive.Include);

        // The design resolution, not the current window: the whole point of the fitter is that
        // everything is authored at one size and scaled to the real screen afterwards.
        Vector2 design = fitter != null ? fitter.referenceResolution : new Vector2(1179f, 2556f);
        Vector2 wanted = design + new Vector2(Bleed, Bleed);

        RectTransform rect = team.teamBackground.rectTransform;

        if (rect.sizeDelta == wanted && rect.anchoredPosition == Vector2.zero)
            return;

        Undo.RecordObject(rect, "Cover the screen with the 2v2 board");

        Debug.Log($"TeamBackgroundSetup: board {rect.sizeDelta.x:0}x{rect.sizeDelta.y:0} at " +
                  $"({rect.anchoredPosition.x:0.#}, {rect.anchoredPosition.y:0.#}) -> " +
                  $"{wanted.x:0}x{wanted.y:0} centred.", team.teamBackground);

        rect.sizeDelta = wanted;
        rect.anchoredPosition = Vector2.zero;

        // localScale is left alone on purpose: its x sign is what mirrors the board so each team
        // sees it from their own end, and normalising it here would flip half the players.
        EditorUtility.SetDirty(rect);
    }

    // A label auto-sizes down to fit its box, so a box smaller than the font is a font that
    // shrinks. These were 200x50 holding text authored at 100pt: before text was made to stay
    // inside its box it simply overflowed and looked right, and the moment it started obeying, the
    // score got small. The box is the thing that was wrong.
    private static void SizeScoreBoxes()
    {
        TeamDuelManager team = Object.FindAnyObjectByType<TeamDuelManager>(FindObjectsInactive.Include);

        if (team == null)
            return;

        Fit(team.teamAScoreText);
        Fit(team.teamBScoreText);
    }

    private static void Fit(TMPro.TMP_Text score)
    {
        if (score == null)
            return;

        RectTransform rect = score.rectTransform;

        // Room for the glyph plus its ascender and descender. 1.3x the point size is the usual
        // rule of thumb and leaves a two-digit score comfortable.
        float needed = Mathf.Ceil(score.fontSize * 1.3f);

        if (rect.sizeDelta.y >= needed)
            return;

        Undo.RecordObject(rect, "Fit the 2v2 score box");
        Vector2 size = rect.sizeDelta;
        Debug.Log($"TeamBackgroundSetup: '{score.name}' box {size.x}x{size.y} -> {size.x}x{needed} " +
                  $"for {score.fontSize:0}pt text.", score);

        size.y = needed;
        rect.sizeDelta = size;
        EditorUtility.SetDirty(rect);
    }

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
