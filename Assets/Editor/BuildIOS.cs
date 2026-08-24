using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

// Builds the Xcode project for the iPhone, without the folder-picker dialog.
//
// The point is that the destination is fixed and the old one is cleared first. Unity offers to
// "append" to an existing Xcode project, which keeps whatever was left there last time — and
// leftovers from an older build are what produce signing failures that look like certificate
// problems and are really stale files.
public static class BuildIOS
{
    private const string OutputPath = "Builds/iOS";

    [MenuItem("Trivia Duel/Build iOS Xcode Project")]
    public static void Build()
    {
        // Same identity and orientation rules as Android, and the iOS identifier is set in there
        // too. Applied every time rather than trusted to still be set, because Project Settings is
        // one wrong click away from a build that ships under a different id.
        BuildSetup.ApplyAndroidBuildSettings();

        if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.iOS)
        {
            Debug.Log("BuildIOS: switching the active build target to iOS. This reimports assets " +
                      "and can take a while the first time.");

            if (!EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.iOS, BuildTarget.iOS))
            {
                Debug.LogError("BuildIOS: could not switch to iOS. Is the iOS Build Support module installed?");
                return;
            }
        }

        string[] scenes = ScenesInBuild();

        if (scenes.Length == 0)
        {
            Debug.LogError("BuildIOS: no enabled scenes in Build Settings, so there would be nothing to run.");
            return;
        }

        // Cleared rather than appended. See the note at the top: this is the cheap fix for a class
        // of Xcode errors that look like they are about certificates.
        if (Directory.Exists(OutputPath))
        {
            Debug.Log("BuildIOS: clearing the previous Xcode project at " + OutputPath);
            Directory.Delete(OutputPath, true);
        }

        Directory.CreateDirectory(OutputPath);

        BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = scenes,
            locationPathName = OutputPath,
            target = BuildTarget.iOS,
            options = BuildOptions.None
        });

        // Every failed step, not just the summary. A build that "failed" with no reason printed is
        // the thing that wastes an afternoon.
        foreach (BuildStep step in report.steps)
            foreach (BuildStepMessage message in step.messages)
                if (message.type == LogType.Error || message.type == LogType.Exception)
                    Debug.LogError($"BuildIOS [{step.name}] {message.content}");

        // The report can say Succeeded while nothing was written — checked directly instead.
        string project = Path.Combine(OutputPath, "Unity-iPhone.xcodeproj");

        if (report.summary.result != BuildResult.Succeeded || !Directory.Exists(project))
        {
            Debug.LogError($"BuildIOS: FAILED ({report.summary.result}). Nothing usable at {project}.");
            return;
        }

        Debug.Log($"BuildIOS: SUCCESS. Open {project} in Xcode, pick your iPhone, and press Run. " +
                  $"Took {report.summary.totalTime.TotalSeconds:0}s.");
    }

    private static string[] ScenesInBuild()
    {
        System.Collections.Generic.List<string> enabled = new System.Collections.Generic.List<string>();

        foreach (EditorBuildSettingsScene scene in EditorBuildSettings.scenes)
            if (scene.enabled)
                enabled.Add(scene.path);

        return enabled.ToArray();
    }
}
