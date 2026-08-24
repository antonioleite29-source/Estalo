using System;
using UnityEngine;

// Caps how fast the game is allowed to redraw.
//
// Without this the project renders as fast as the hardware can manage. The active quality level is
// "Very Low", whose vSyncCount is 0, and nothing set a target frame rate — so a 2D quiz screen that
// changes a few times a second was being redrawn hundreds or thousands of times a second. On a
// laptop that is heat and fan noise for nothing; on a phone it is battery.
//
// The cap alone was not enough, and PerfProbe is what showed why. With the game pinned at 60 fps it
// still allocated 0 KB/s of its own while the process around it allocated ~10 MB/s: the cost was
// never the game, it was the Unity Editor hosting it. A Multiplayer Play Mode virtual player is not
// a lightweight build — it is a second complete Editor, ~2.4 GB of it, with its own asset database
// and its own repaint loop. A 2v2 test is four of those plus the main Editor, five processes doing
// full Editor work to draw a screen with 20 draw calls in it. That is the heat, and it scales with
// how fast each of those processes is asked to run.
//
// So virtual players get a lower cap than the real thing. See VirtualPlayerFrameRate for why 30.
//
// No scene wiring: RuntimeInitializeOnLoadMethod runs in every build and every Play session,
// including virtual players, so it cannot be forgotten on a new scene or lost in a merge.
public static class FrameRateCap
{
    // 60 is twice the rate anything in this game actually changes, and matches the refresh of the
    // phones it targets. Raising it costs heat and buys nothing a player can see.
    private const int TargetFrameRate = 60;

    // 30, not lower, because NetworkManager's TickRate in ProjectCapstone.unity is 30. Matching the
    // two means a clone renders exactly one frame per network tick: half the drawing, while every
    // tick is still handled on its own frame the way it is on a real device. Drop to 15 and ticks
    // start arriving two-per-frame, which quietly changes the timing of the thing being tested —
    // a clone that lies about netcode is worse than a warm laptop.
    private const int VirtualPlayerFrameRate = 30;

    // Which way the frame is paced. This is an A/B, not a preference — see the comment below.
    //
    // Profiling one Editor process burning ~215% of a core found its largest non-blocking Unity
    // frame to be ThreadedStreamBuffer::HandleOutOfBufferToReadFrom, which is the render thread
    // BUSY-WAITING for commands the main thread has not produced yet. A spin burns a whole core
    // while achieving nothing, and it explains how a screen with 16 draw calls keeps two cores hot:
    // one thread working, one thread spinning beside it.
    //
    // targetFrameRate paces by having Unity decide when to release the next frame, which is what
    // leaves the render thread spinning in the gap. vSync instead blocks on the display's own
    // signal, in the driver, where a waiting thread is descheduled rather than spinning.
    //
    // On a 60Hz panel vSyncCount = 1 gives the same 60 fps as the cap did, so this trades nothing
    // away if it works. If the fans do not change, the spin is not the cost and this reverts.
    // static readonly rather than const so the compiler keeps both branches live: with a const
    // the unused arm is stripped and warns as unreachable, and the whole point is that this
    // flips back in one edit if the measurement says vSync was not it.
    private static readonly bool PaceWithVSync = false;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Apply()
    {
        bool clone = IsVirtualPlayer();

        if (PaceWithVSync)
        {
            // 1 = every display refresh (60 fps on this panel), 2 = every second refresh (30), which
            // is the clone's half-rate equivalent without needing targetFrameRate at all.
            QualitySettings.vSyncCount = clone ? 2 : 1;

            // -1 hands pacing entirely to vSync. Leaving a target set as well makes Unity apply
            // whichever is slower, which would quietly reintroduce the gap this is trying to close.
            Application.targetFrameRate = -1;
        }
        else
        {
            // vSync has to be off for targetFrameRate to be honoured at all — while it is on, Unity
            // ignores the target and follows the display instead.
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = clone ? VirtualPlayerFrameRate : TargetFrameRate;
        }

        Debug.Log($"FrameRateCap: pacing with {(PaceWithVSync ? "vSync" : "targetFrameRate")}, " +
                  $"vSyncCount={QualitySettings.vSyncCount}, target={Application.targetFrameRate}, " +
                  $"clone={clone}");
    }

    // NetworkBootstrap already reads MPPM's "-vpId=<id>" launch argument to give each clone its own
    // profile storage, so the question "am I a clone?" is answered in one place rather than two
    // that can disagree. Wrapped because a throw here would take the frame cap down with it, and no
    // cap is the failure mode this whole file exists to prevent.
    private static bool IsVirtualPlayer()
    {
        try
        {
            return NetworkBootstrap.IsClonedVirtualPlayer();
        }
        catch (Exception)
        {
            // Treated as "not a clone", so the real game keeps its real frame rate.
            return false;
        }
    }
}
