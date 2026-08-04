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

    [MenuItem("Trivia Duel/Build Android APK")]
    public static void BuildAndroidApk()
    {
        if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Android, BuildTarget.Android))
        {
            Debug.LogError("Android Build Support is not installed. Unity Hub > Installs > " +
                           "6000.4.12f1 > gear > Add Modules > Android Build Support, with both " +
                           "'Android SDK & NDK Tools' and 'OpenJDK' ticked.");
            return;
        }

        // Always re-apply first: a build that silently went out with the wrong bundle id or an
        // unpinned SDK is worse than one that fails, because nobody notices until testers are
        // already holding it.
        ApplyAndroidBuildSettings();

        string[] scenes = System.Array.ConvertAll(
            System.Array.FindAll(EditorBuildSettings.scenes, s => s.enabled),
            s => s.path);

        if (scenes.Length == 0)
        {
            Debug.LogError("No scenes enabled in File > Build Settings. The APK would open to a black screen.");
            return;
        }

        // Version-stamped filename: testers end up holding several builds over several sessions,
        // and "which APK were you on?" is unanswerable if they are all called the same thing.
        string fileName = $"TriviaDuel-{PlayerSettings.bundleVersion}-" +
                          $"{System.DateTime.Now:yyyyMMdd-HHmm}.apk";
        string outputDir = System.IO.Path.Combine(
            System.IO.Directory.GetParent(Application.dataPath).FullName, "Builds");
        System.IO.Directory.CreateDirectory(outputDir);

        string outputPath = System.IO.Path.Combine(outputDir, fileName);

        EditorUserBuildSettings.SwitchActiveBuildTarget(NamedBuildTarget.Android, BuildTarget.Android);

        UnityEditor.Build.Reporting.BuildReport report = BuildPipeline.BuildPlayer(
            scenes, outputPath, BuildTarget.Android, BuildOptions.None);

        if (report.summary.result == UnityEditor.Build.Reporting.BuildResult.Succeeded)
        {
            // Measured off the file on disk, not report.summary.totalSize — that counts the
            // uncompressed build output and reads about 20x the real download.
            long apkBytes = new System.IO.FileInfo(outputPath).Length;

            Debug.Log($"APK built: {outputPath} " +
                      $"({apkBytes / (1024 * 1024)} MB, " +
                      $"{report.summary.totalTime.TotalMinutes:F1} min). " +
                      "Send this file to testers — they install it directly, no cable needed.");
            EditorUtility.RevealInFinder(outputPath);
        }
        else
        {
            Debug.LogError($"APK build {report.summary.result} with {report.summary.totalErrors} error(s). " +
                           "The specific cause is above this line in the Console.");
        }
    }

    [MenuItem("Trivia Duel/Setup/Fix Canvas Scalers For Phones")]
    public static void FixCanvasScalers()
    {
        CanvasScaler[] scalers = Object.FindObjectsByType<CanvasScaler>(
            FindObjectsInactive.Include);

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

    private const string UiRootName = "UIRoot";

    // The resolution this UI was actually laid out against, evidenced by BottomBar: full-stretch
    // anchors with anchoredPosition y = -1087 and sizeDelta y = -2174 put its bottom edge exactly
    // on the screen bottom only when the parent is 2556 tall.
    private static readonly Vector2 AuthoredResolution = new Vector2(1179f, 2556f);

    // Forces the design resolution to fill the screen exactly on any device: nothing cropped,
    // no bars, at the cost of distorting shapes wherever the aspect ratio differs from the one the
    // UI was authored at. Run this or "Fix Canvas Scalers For Phones", never both.
    //
    // Everything goes under ONE wrapper root. An earlier version put a fitter on each direct child
    // of the Canvas, which broke any element positioned against the screen rather than being a
    // full-screen page — BottomBar is anchored to the bottom edge, and re-anchoring it to a centred
    // rect dropped it into the middle of the screen.
    [MenuItem("Trivia Duel/Setup/Stretch UI To Fill Screen")]
    public static void StretchUiToFillScreen()
    {
        Canvas[] canvases = Object.FindObjectsByType<Canvas>(
            FindObjectsInactive.Include);

        if (canvases.Length == 0)
        {
            Debug.LogWarning("No Canvas found in the open scene. Open ProjectCapstone first.");
            return;
        }

        int rootsFitted = 0;

        foreach (Canvas canvas in canvases)
        {
            if (canvas.transform.parent != null)
                continue;

            Vector2 authoredSize = AuthoredResolution;

            // Deliberately a constant, not the Canvas's live size. Reading the live size looks
            // smarter but silently captures whatever the Game view happens to be showing — after a
            // platform switch that is an Android preset like 800x480, and the whole UI then scales
            // from a resolution nobody ever designed against.
            RectTransform canvasRect = canvas.GetComponent<RectTransform>();
            Vector2 liveSize = canvasRect.rect.size;

            if (liveSize.y > 1f && Mathf.Abs(liveSize.y - authoredSize.y) > 1f)
            {
                Debug.Log($"Game view is currently {liveSize.x} x {liveSize.y}, but the UI is " +
                          $"treated as authored at {authoredSize.x} x {authoredSize.y}. Change " +
                          "AuthoredResolution in BuildSetup.cs if that is wrong.", canvas);
            }

            CanvasScaler scaler = canvas.GetComponent<CanvasScaler>();

            if (scaler != null)
            {
                Undo.RecordObject(scaler, "Stretch UI");

                // Must be neutral: CanvasStretchFitter does all the scaling itself, and a scaler
                // still set to Scale With Screen Size would multiply on top of it.
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
                scaler.scaleFactor = 1f;
                EditorUtility.SetDirty(scaler);
            }

            // Undo any damage from the earlier per-child version before rebuilding.
            foreach (RectTransform child in canvas.transform)
            {
                CanvasStretchFitter stray = child.GetComponent<CanvasStretchFitter>();

                if (stray != null && child.name != UiRootName)
                {
                    Undo.DestroyObjectImmediate(stray);
                    Debug.LogWarning($"Removed a stretch fitter from '{child.name}' — it is a " +
                                     "screen-positioned element, not a full-screen root.", child);
                }
            }

            Transform existingRoot = canvas.transform.Find(UiRootName);
            RectTransform uiRoot;

            if (existingRoot != null)
            {
                uiRoot = existingRoot as RectTransform;
            }
            else
            {
                GameObject rootGo = new GameObject(UiRootName, typeof(RectTransform));
                Undo.RegisterCreatedObjectUndo(rootGo, "Stretch UI");
                uiRoot = rootGo.GetComponent<RectTransform>();
                uiRoot.SetParent(canvas.transform, false);
            }

            // Reparent in the order they already sit in, so draw order is unchanged. Iterating a
            // copy because moving a child mutates the list being walked.
            Transform[] existingChildren = new Transform[canvas.transform.childCount];

            for (int i = 0; i < canvas.transform.childCount; i++)
                existingChildren[i] = canvas.transform.GetChild(i);

            foreach (Transform child in existingChildren)
            {
                if (child == uiRoot)
                    continue;

                // worldPositionStays: false keeps the local coordinates the UI was authored with,
                // which is what we want — UIRoot is the same size as the Canvas at this moment.
                Undo.SetTransformParent(child, uiRoot, false, "Stretch UI");
            }

            CanvasStretchFitter fitter = uiRoot.GetComponent<CanvasStretchFitter>();

            if (fitter == null)
                fitter = Undo.AddComponent<CanvasStretchFitter>(uiRoot.gameObject);

            Undo.RecordObject(fitter, "Stretch UI");
            fitter.referenceResolution = authoredSize;
            fitter.uniformScale = false;
            EditorUtility.SetDirty(fitter);

            rootsFitted++;
            Debug.Log($"'{canvas.name}' now stretches from an authored {authoredSize.x} x " +
                      $"{authoredSize.y} to fill any screen.", uiRoot);
        }

        Debug.Log($"{rootsFitted} canvas(es) fitted. Everything now lives under '{UiRootName}' and " +
                  "stretches together, so elements keep the anchors they were authored with. If the " +
                  "distortion bothers you, tick 'Uniform Scale' on the UIRoot to trade it for margins.");

        EditorSceneManagerSaveHint();
    }

    private static void EditorSceneManagerSaveHint()
    {
        Scene scene = SceneManager.GetActiveScene();
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
        Debug.Log($"Scene '{scene.name}' marked dirty — save it (Cmd+S) to keep the change.");
    }
}
