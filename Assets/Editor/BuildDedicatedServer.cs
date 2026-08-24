using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

// Builds the headless Linux server — the copy of the game with no graphics that runs on the rented
// box and does nothing but hold the queue and run matches.
//
// A separate menu item rather than a flag on the APK build, because the two produce completely
// different things from the same scene and shipping one where the other was meant is a mistake worth
// making impossible.
public static class BuildDedicatedServer
{
    [MenuItem("Trivia Duel/Build Linux Server")]
    public static void BuildLinuxServer()
    {
        if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Standalone, BuildTarget.StandaloneLinux64))
        {
            Debug.LogError("Linux Dedicated Server Build Support is not installed. Unity Hub > " +
                           "Installs > 6000.4.12f1 > gear > Add Modules > Linux Dedicated Server " +
                           "Build Support. (Linux Build Support on its own is not the same module.)");
            return;
        }

        string[] scenes = System.Array.ConvertAll(
            System.Array.FindAll(EditorBuildSettings.scenes, s => s.enabled),
            s => s.path);

        if (scenes.Length == 0)
        {
            Debug.LogError("No scenes enabled in File > Build Settings. The server would start with " +
                           "no NetworkManager and accept nobody.");
            return;
        }

        string outputDir = System.IO.Path.Combine(
            System.IO.Directory.GetParent(Application.dataPath).FullName, "Builds", "LinuxServer");
        System.IO.Directory.CreateDirectory(outputDir);

        string outputPath = System.IO.Path.Combine(outputDir, "TriviaDuelServer");

        // Switch the Editor to Linux/Server BEFORE building, rather than relying on BuildPlayer to
        // do it implicitly. Asking it to build a target that is not the active one — while the
        // project is sitting on Android or iOS — is how this ends up creating the output folder and
        // then quietly producing nothing in it.
        EditorUserBuildSettings.standaloneBuildSubtarget = StandaloneBuildSubtarget.Server;

        if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.StandaloneLinux64)
        {
            Debug.Log("Switching the active build target to Linux Dedicated Server — this takes a " +
                      "few minutes the first time, because every asset is reimported.");

            if (!EditorUserBuildSettings.SwitchActiveBuildTarget(NamedBuildTarget.Server,
                                                                 BuildTarget.StandaloneLinux64))
            {
                Debug.LogError("Could not switch to Linux Dedicated Server. Check that the module is " +
                               "installed in Unity Hub.");
                return;
            }
        }

        // Subtarget Server is what actually makes this headless and defines UNITY_SERVER, which is
        // what NetworkBootstrap reads to decide it is the dedicated server. Building plain
        // StandaloneLinux64 without it produces a normal game that expects a display and will sit
        // there failing to open a window on a machine that has none.
        BuildPlayerOptions options = new BuildPlayerOptions
        {
            scenes = scenes,
            locationPathName = outputPath,
            target = BuildTarget.StandaloneLinux64,
            subtarget = (int)StandaloneBuildSubtarget.Server,
            options = BuildOptions.None,
        };

        UnityEditor.Build.Reporting.BuildReport report = BuildPipeline.BuildPlayer(options);

        // Trust the file on disk, not the summary. A build whose postprocess step throws — the
        // case where the Editor has not registered the Linux module yet — still comes back as
        // Succeeded, and reporting that is worse than reporting nothing: it sends you off to
        // deploy a folder that is empty.
        bool wroteExecutable = System.IO.File.Exists(outputPath);

        if (report.summary.result == UnityEditor.Build.Reporting.BuildResult.Succeeded && !wroteExecutable)
        {
            Debug.LogError("Unity reported success but produced no executable at " + outputPath + ".\n" +
                           "Almost always this means the Editor was already running when the Linux " +
                           "Dedicated Server module was installed — it only registers modules at " +
                           "startup. Quit Unity, reopen the project, and run this again.");
            return;
        }

        if (report.summary.result == UnityEditor.Build.Reporting.BuildResult.Succeeded)
        {
            Debug.Log($"Linux server built: {outputDir} " +
                      $"({report.summary.totalTime.TotalMinutes:F1} min). " +
                      "Upload the whole LinuxServer folder — the executable alone will not run, it " +
                      "needs the _Data folder beside it. Tools/Server/deploy.sh does this for you.");
            EditorUtility.RevealInFinder(outputDir);
        }
        else
        {
            // The summary alone says "Failed" and nothing else. The steps carry the actual reason,
            // and without printing them the Console shows a failure with no cause anywhere.
            Debug.LogError($"Linux server build failed: {report.summary.result}, " +
                           $"{report.summary.totalErrors} error(s).");

            foreach (UnityEditor.Build.Reporting.BuildStep step in report.steps)
            {
                foreach (UnityEditor.Build.Reporting.BuildStepMessage message in step.messages)
                {
                    if (message.type == LogType.Error || message.type == LogType.Exception)
                        Debug.LogError($"  [{step.name}] {message.content}");
                }
            }
        }
    }
}
