using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Puts one font on every piece of text in the game.
//
// The scene had drifted to three: Oswald on 34 labels, LiberationSans on 25 and Sora on 2 — not
// a design, just whatever each object happened to be created with. Changing that by hand means
// finding 61 components, several of them on objects that are switched off and so invisible in the
// Hierarchy until something activates them.
public static class ApplyGameFont
{
    // Inter, cut to a static Black at weight 900. Vendored rather than read from the Mac's font
    // folder for two reasons: a machine without the font installed would build a game with no text
    // in it, and the previous font here was Arial Black, which belongs to Monotype and is not
    // licensed to ship inside an app. Inter is OFL — see Assets/Fonts/Inter/OFL.txt.
    //
    // To go lighter, point this at Inter-SemiBold.ttf and delete the generated .asset so it is
    // rebuilt. Worth doing if the answer buttons read as too solid on a phone: 900 is drawn for
    // headlines, and this font ends up on a 17px button as well as a 56px logo.
    private const string SourceFontPath = "Assets/Fonts/Inter/Inter-Black.ttf";
    private const string FontAssetPath = "Assets/Fonts/Inter/Inter Black SDF.asset";

    // Named after the file so the two never drift apart in the Project window.
    private const string FontAssetName = "Inter Black SDF";

    [MenuItem("Trivia Duel/Setup/Apply Game Font To All Text")]
    public static void ApplyToEverything()
    {
        TMP_FontAsset fontAsset = LoadOrCreateFontAsset();

        if (fontAsset == null)
            return;

        int changed = 0;
        int alreadyRight = 0;

        // Include inactive: most of this game's text lives on pages that are switched off — the
        // profile page, the practice screen, the 2v2 board — and those are exactly the ones that
        // get missed when this is done by hand.
        TMP_Text[] texts = Object.FindObjectsByType<TMP_Text>(FindObjectsInactive.Include);

        foreach (TMP_Text text in texts)
        {
            if (text.font == fontAsset)
            {
                alreadyRight++;
                continue;
            }

            Undo.RecordObject(text, "Apply game font");

            // Only the font. Size, alignment, colour, wrapping and auto-sizing are per-label
            // decisions that were made deliberately, and reassigning the font does not disturb them.
            text.font = fontAsset;

            EditorUtility.SetDirty(text);
            changed++;
        }

        // So anything created from here on starts with the right font instead of reintroducing the
        // drift this method exists to undo.
        SetAsProjectDefault(fontAsset);

        if (changed > 0)
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());

        Debug.Log($"ApplyGameFont: {changed} text object(s) switched to {fontAsset.name}, " +
                  $"{alreadyRight} already correct. Save the scene to keep it.");
    }

    private static TMP_FontAsset LoadOrCreateFontAsset()
    {
        TMP_FontAsset existing = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontAssetPath);

        if (existing != null)
            return existing;

        Font source = AssetDatabase.LoadAssetAtPath<Font>(SourceFontPath);

        if (source == null)
        {
            Debug.LogError($"ApplyGameFont: no font at {SourceFontPath}. Nothing was changed.");
            return null;
        }

        // Dynamic, not a pre-baked atlas. The game is in Portuguese, so it needs ã, ç, é, õ and the
        // rest on demand; a static atlas only contains the characters someone remembered to list
        // when they made it, and a missing one shows up as a blank box mid-sentence.
        TMP_FontAsset created = TMP_FontAsset.CreateFontAsset(
            source,
            samplingPointSize: 90,
            atlasPadding: 9,
            renderMode: UnityEngine.TextCore.LowLevel.GlyphRenderMode.SDFAA,
            atlasWidth: 1024,
            atlasHeight: 1024,
            atlasPopulationMode: AtlasPopulationMode.Dynamic,
            enableMultiAtlasSupport: true);

        if (created == null)
        {
            Debug.LogError("ApplyGameFont: TextMeshPro could not build a font asset from " + SourceFontPath);
            return null;
        }

        created.name = FontAssetName;

        AssetDatabase.CreateAsset(created, FontAssetPath);

        // The atlas texture and material are sub-assets: without this they are lost the moment the
        // Editor reloads, and every label goes blank.
        if (created.atlasTextures != null && created.atlasTextures.Length > 0)
        {
            created.atlasTextures[0].name = FontAssetName + " Atlas";
            AssetDatabase.AddObjectToAsset(created.atlasTextures[0], created);
        }

        if (created.material != null)
        {
            created.material.name = FontAssetName + " Material";
            AssetDatabase.AddObjectToAsset(created.material, created);
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.ImportAsset(FontAssetPath);

        Debug.Log("ApplyGameFont: created " + FontAssetPath);

        return AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontAssetPath);
    }

    private static void SetAsProjectDefault(TMP_FontAsset fontAsset)
    {
        TMP_Settings settings = TMP_Settings.instance;

        if (settings == null)
            return;

        SerializedObject serialized = new SerializedObject(settings);
        SerializedProperty defaultFont = serialized.FindProperty("m_defaultFontAsset");

        if (defaultFont == null)
            return;

        defaultFont.objectReferenceValue = fontAsset;
        serialized.ApplyModifiedProperties();
        EditorUtility.SetDirty(settings);
        AssetDatabase.SaveAssets();
    }
}
