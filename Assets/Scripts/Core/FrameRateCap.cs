using UnityEngine;

// Caps how fast the game is allowed to redraw.
//
// Without this the project renders as fast as the hardware can manage. The active quality level is
// "Very Low", whose vSyncCount is 0, and nothing set a target frame rate — so a 2D quiz screen that
// changes a few times a second was being redrawn hundreds or thousands of times a second. On a
// laptop that is heat and fan noise for nothing; on a phone it is battery. With four Editor virtual
// players it is four processes doing it at once, which is why 2v2 testing was the worst of all.
//
// No scene wiring: RuntimeInitializeOnLoadMethod runs in every build and every Play session,
// including virtual players, so it cannot be forgotten on a new scene or lost in a merge.
public static class FrameRateCap
{
    // 60 is twice the rate anything in this game actually changes, and matches the refresh of the
    // phones it targets. Raising it costs heat and buys nothing a player can see.
    private const int TargetFrameRate = 60;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Apply()
    {
        // vSync has to be off for targetFrameRate to be honoured at all — while it is on, Unity
        // ignores the target and follows the display instead, which on a 120Hz screen means double
        // the work for a game that does not need it.
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = TargetFrameRate;
    }
}
