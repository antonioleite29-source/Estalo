using System.Collections.Generic;
using System.Text;
using Unity.Profiling;
using Unity.Profiling.LowLevel.Unsafe;
using UnityEngine;

// Prints where a frame's time and garbage actually go, every two seconds, to the Console.
//
// Read the history before changing this, because the same wrong turn has been taken five times and
// each time it looked like a measurement.
//
//   fps / frame ms   Once targetFrameRate is set the engine sleeps the rest of the frame, so
//                    "60 fps, 16.7 ms" is the cap being reported back, not a cost.
//
//   heap size        Profiler.GetMonoUsedSizeLong() is the whole process, Editor included, and the
//                    Update->LateUpdate window used to isolate "the game's share" spans only script
//                    updates — FixedUpdate, the canvas rebuild, netcode's NetworkUpdateLoop, input
//                    and all rendering fall outside it. "game 0 KB/s" meant "nothing allocated in
//                    the one slice being watched", which is not the same claim at all.
//
//   "Main Thread"    Includes the deliberate idle. Reports ~16.66 of 16.7 forever. Same trap again.
//
//   guessed names    Counters were looked up by (category, name) with the category guessed. A name
//                    that resolves nowhere returns Valid == false and printed "n/a" — which reads
//                    exactly like a cost that is not there. WaitForTargetFPS is in [VSync], not
//                    [Internal]; Canvas.BuildBatch is in [UI Render], not [Gui]. Both were being
//                    reported as absent while being the things most worth seeing.
//
// So counters are now resolved by asking the runtime for every handle it has and matching on name
// alone. If a name is in the catalogue it is measured; if it is not, that is stated plainly and is
// never silently a zero.
//
// The headline is PlayerLoop: the engine's actual per-frame work, which unlike "Main Thread" does
// not include the frame-rate sleep. Everything after it is the breakdown that explains it.
//
// Turn on with PERF_PROBE in Project Settings > Player > Scripting Define Symbols.
[DefaultExecutionOrder(-32000)]
public class PerfProbe : MonoBehaviour
{
    private const float ReportEverySeconds = 2f;

    private float elapsed;
    private int frames;
    private float worstFrameMs;

#if PERF_PROBE
    private static bool spawned;

    // Every counter this build exposes, by name, paired with the category the runtime says it
    // really lives in. Built once; the whole point is that the category is read, never guessed.
    private static Dictionary<string, ProfilerCategory> catalogue;

    private sealed class Counter
    {
        public readonly string Label;
        private readonly string statName;
        private ProfilerRecorder recorder;

        public Counter(string label, string statName)
        {
            Label = label;
            this.statName = statName;
        }

        public void Start()
        {
            // Capacity 120 so a two-second window at 60 fps averages over whole frames rather
            // than reporting whichever single frame happened to land on the report.
            if (catalogue.TryGetValue(statName, out ProfilerCategory category))
                recorder = ProfilerRecorder.StartNew(category, statName, 120);
        }

        public void Stop()
        {
            if (recorder.Valid)
                recorder.Dispose();
        }

        public bool Valid => recorder.Valid;

        public double Average()
        {
            if (!recorder.Valid || recorder.Count == 0)
                return 0d;

            double total = 0d;
            int count = recorder.Count;

            for (int i = 0; i < count; i++)
                total += recorder.GetSample(i).Value;

            return total / count;
        }
    }

    // Time, in nanoseconds. Names taken verbatim from the runtime's own catalogue.
    private readonly Counter playerLoop = new Counter("LOOP", "PlayerLoop");
    private readonly Counter mainThread = new Counter("frame", "Main Thread");
    private readonly Counter targetFpsWait = new Counter("capwait", "WaitForTargetFPS");
    private readonly Counter drawableWait = new Counter("gpuwait", "Wait for nextDrawable (display link)");
    private readonly Counter renderWait = new Counter("rtwait", "Gfx.WaitForRenderThread");

    private readonly Counter behaviourUpdate = new Counter("update", "BehaviourUpdate");
    private readonly Counter lateUpdate = new Counter("late", "LateBehaviourUpdate");
    private readonly Counter fixedUpdate = new Counter("fixed", "FixedBehaviourUpdate");
    private readonly Counter canvasBuild = new Counter("canvas", "Canvas.BuildBatch");
    private readonly Counter uguiBatches = new Counter("ugui", "UGUI.Rendering.UpdateBatches");
    private readonly Counter cameraRender = new Counter("draw", "Camera.Render");

    // Bytes / count.
    private readonly Counter gcAlloc = new Counter("gc", "GC.Alloc");

    private Counter[] timers;
#endif

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Spawn()
    {
#if !PERF_PROBE
        // No object, so no Update: the probe costs nothing when off rather than running quietly.
        return;
#else
        if (spawned)
            return;

        spawned = true;

        GameObject probe = new GameObject("PerfProbe");
        probe.AddComponent<PerfProbe>();
        DontDestroyOnLoad(probe);
#endif
    }

    // MPPM launches each clone with "-name Player 2" and the main Editor with no -name at all, so
    // this is what separates otherwise identical log lines from each other.
    private static string ProcessLabel()
    {
        try
        {
            string[] args = System.Environment.GetCommandLineArgs();

            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == "-name")
                    return args[i + 1];
            }
        }
        catch (System.Exception)
        {
            // Fall through to the default below.
        }

        return "main";
    }

#if PERF_PROBE
    private void OnEnable()
    {
        catalogue = new Dictionary<string, ProfilerCategory>();

        var handles = new List<ProfilerRecorderHandle>();
        ProfilerRecorderHandle.GetAvailable(handles);

        foreach (ProfilerRecorderHandle handle in handles)
        {
            ProfilerRecorderDescription d = ProfilerRecorderHandle.GetDescription(handle);
            catalogue[d.Name] = d.Category;
        }

        timers = new[]
        {
            playerLoop, mainThread, targetFpsWait, drawableWait, renderWait,
            behaviourUpdate, lateUpdate, fixedUpdate, canvasBuild, uguiBatches, cameraRender
        };

        for (int i = 0; i < timers.Length; i++)
            timers[i].Start();

        gcAlloc.Start();

        StringBuilder missing = new StringBuilder();

        for (int i = 0; i < timers.Length; i++)
        {
            if (!timers[i].Valid)
                missing.Append(missing.Length == 0 ? "" : ", ").Append(timers[i].Label);
        }

        if (!gcAlloc.Valid)
            missing.Append(missing.Length == 0 ? "" : ", ").Append(gcAlloc.Label);

        Debug.Log(missing.Length == 0
            ? $"PerfProbe [{ProcessLabel()}] all {timers.Length + 1} counters resolved from the runtime catalogue."
            : $"PerfProbe [{ProcessLabel()}] still missing and shown as n/a: {missing}");
    }

    private void OnDisable()
    {
        for (int i = 0; i < timers.Length; i++)
            timers[i].Stop();

        gcAlloc.Stop();
    }
#endif

    private void Update()
    {
        float ms = Time.unscaledDeltaTime * 1000f;

        elapsed += Time.unscaledDeltaTime;
        frames++;

        if (ms > worstFrameMs)
            worstFrameMs = ms;

        if (elapsed < ReportEverySeconds)
            return;

        Report();

        elapsed = 0f;
        frames = 0;
        worstFrameMs = 0f;
    }

    private void Report()
    {
#if PERF_PROBE
        float fps = frames / elapsed;

        int cap = Application.targetFrameRate > 0 ? Application.targetFrameRate : 60;
        double budgetMs = 1000d / cap;

        StringBuilder line = new StringBuilder($"PerfProbe [{ProcessLabel()}]  ");

        line.Append($"{fps:0} fps  worst {worstFrameMs:0.0} ms");

        // The headline. PlayerLoop excludes the frame-rate sleep, so unlike every number this file
        // has printed before, a low value here really does mean an idle machine.
        line.Append(playerLoop.Valid
            ? $"  |  LOOP {playerLoop.Average() / 1_000_000d:0.00} ms of {budgetMs:0.0} " +
              $"({playerLoop.Average() / 1_000_000d / budgetMs * 100d:0}%)"
            : "  |  LOOP n/a");

        line.Append(gcAlloc.Valid ? $"  |  gc {gcAlloc.Average() * fps / 1024d:0} KB/s" : "  |  gc n/a");

        line.Append("  |  waits:");
        line.Append(Ms(targetFpsWait));
        line.Append(Ms(drawableWait));
        line.Append(Ms(renderWait));

        line.Append("  |  work:");
        line.Append(Ms(behaviourUpdate));
        line.Append(Ms(lateUpdate));
        line.Append(Ms(fixedUpdate));
        line.Append(Ms(canvasBuild));
        line.Append(Ms(uguiBatches));
        line.Append(Ms(cameraRender));

        line.Append($"  |  frame {mainThread.Average() / 1_000_000d:0.00}  cap {cap}");

        Debug.Log(line.ToString());
#endif
    }

#if PERF_PROBE
    private static string Ms(Counter counter)
    {
        return counter.Valid
            ? $"  {counter.Label} {counter.Average() / 1_000_000d:0.00}"
            : $"  {counter.Label} n/a";
    }
#endif
}
