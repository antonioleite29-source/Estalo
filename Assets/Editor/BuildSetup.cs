using UnityEditor;
using UnityEditor.Build;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// One-click project setup for the Android test build.
//
// These are all things that could be clicked in Project Settings by hand, but doing it in code
// means the settings are recorded, reviewable in git, and re-appliable if someone opens the project
// on another machine and Unity resets something. Run the menu items, then File > Save Project.
public static class BuildSetup
{
    private const string AndroidBundleId = "com.tomdeleite.triviaduel";

    [MenuItem("Trivia Duel/Setup/Apply Android Build Settings")]
    public static void ApplyAndroidBuildSettings()
    {
        PlayerSettings.companyName = "Tom de Leite";
        PlayerSettings.productName = "Trivia Duel";

        // The Android identifier was never set — only the Standalone one was, so an Android build
        // would ship under the leftover com.DefaultCompany id. Two apps sharing an id cannot be
        // installed side by side, which matters the moment testers have an older build on their phone.
        PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, AndroidBundleId);

        // Was 0, meaning "highest SDK installed" — the same source then builds differently on a
        // different machine. Pin it so a rebuild is reproducible.
        PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevel34;
        PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel25;

        // Was auto-rotation. The whole lobby and duel UI is laid out portrait; allowing landscape
        // means debugging a second layout nobody designed.
        PlayerSettings.defaultInterfaceOrientation = UIOrientation.Portrait;
        PlayerSettings.allowedAutorotateToPortrait = true;
        PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
        PlayerSettings.allowedAutorotateToLandscapeLeft = false;
        PlayerSettings.allowedAutorotateToLandscapeRight = false;
        PlayerSettings.useAnimatedAutorotation = false;

        // ARM64 only (already the case) — correct for every phone that can run this.
        PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;

        // Testers will be on several builds over several sessions; a visible version is the only
        // way a bug report can say which one it came from.
        PlayerSettings.bundleVersion = "0.1";
        PlayerSettings.Android.bundleVersionCode = 1;

        AssetDatabase.SaveAssets();

        Debug.Log($"Android build settings applied: id={AndroidBundleId}, " +
                  $"target SDK 34, min SDK 25, portrait-locked, ARM64, version 0.1. " +
                  "Now do File > Save Project.");
    }

    [MenuItem("Trivia Duel/Setup/Fix Canvas Scalers For Phones")]
    public static void FixCanvasScalers()
    {
        CanvasScaler[] scalers = Object.FindObjectsByType<CanvasScaler>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);

        if (scalers.Length == 0)
        {
            Debug.LogWarning("No CanvasScaler found in the open scene. Open ProjectCapstone first.");
            return;
        }

        foreach (CanvasScaler scaler in scalers)
        {
            Undo.RecordObject(scaler, "Fix Canvas Scaler");

            // Was Constant Pixel Size at an 800x600 reference: every element sized in raw pixels,
            // so on a 1080-wide phone the whole UI renders at roughly a third of its intended size.
            // This is the single root cause behind "the numbers and buttons are too small".
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080f, 1920f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            EditorUtility.SetDirty(scaler);
            Debug.Log($"Canvas scaler fixed on '{scaler.gameObject.name}'.", scaler);
        }

        EditorSceneManagerSaveHint();
    }

    private static void EditorSceneManagerSaveHint()
    {
        Scene scene = SceneManager.GetActiveScene();
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
        Debug.Log($"Scene '{scene.name}' marked dirty — save it (Cmd+S) to keep the change.");
    }
}
