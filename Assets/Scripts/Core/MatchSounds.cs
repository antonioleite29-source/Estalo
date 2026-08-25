using UnityEngine;

// The moments a match actually has: you answered right, you scored, they scored, you won, you lost.
//
// Every note in all four comes from C major pentatonic, the same scale the button click is tuned
// to, which is why they never clash with each other or with a tap that lands on top of them.
//
// Deliberately paired as opposites rather than as good and bad: conceding is the point sound
// backwards and the loss is the win backwards. These are children doing maths, and a sound that
// reads as being told off is the fastest way to make somebody put the game down.
public static class MatchSounds
{
    private const string ClipFolder = "Match";

    public static float Volume = 0.8f;

    private static AudioSource source;
    private static AudioClip correct, point, against, win, lose;
    private static bool ready;

    // Three notes up the chord. It climbs because the wrong answer resolves downward onto the
    // root, and the pair only reads as opposites if one of them goes the other way.
    public static void PlayCorrect()
    {
        DeviceFeedback.Vibrate(DeviceFeedback.Strength.Bump);
        Play(correct);
    }

    public static void PlayPoint() => Play(point);
    public static void PlayAgainst() => Play(against);
    public static void PlayWin() => Play(win);
    public static void PlayLose() => Play(lose);

    // Everything routes through here so that "which side am I" is answered once rather than at
    // each of the several places a score can change.
    public static void PlayScored(bool byLocalPlayer)
    {
        if (byLocalPlayer)
            PlayPoint();
        else
            PlayAgainst();
    }

    public static void PlayEnded(bool localPlayerWon)
    {
        // The only one heavy enough to feel across a desk, and the only one iOS gets at all.
        DeviceFeedback.Vibrate(DeviceFeedback.Strength.Thud);

        if (localPlayerWon)
            PlayWin();
        else
            PlayLose();
    }

    private static void Play(AudioClip clip)
    {
        if (!DeviceFeedback.SoundAllowed || !Prepare() || clip == null)
            return;

        // PlayOneShot rather than Play: a point landing while the previous one is still ringing
        // should overlap, not cut it off.
        source.PlayOneShot(clip, Volume);
    }

    private static bool Prepare()
    {
        if (ready)
            return source != null;

        ready = true;

        if (NetworkBootstrap.IsDedicatedServerBuild)
            return false;

        correct = Resources.Load<AudioClip>(ClipFolder + "/Correct");
        point = Resources.Load<AudioClip>(ClipFolder + "/Point");
        against = Resources.Load<AudioClip>(ClipFolder + "/Against");
        win = Resources.Load<AudioClip>(ClipFolder + "/Win");
        lose = Resources.Load<AudioClip>(ClipFolder + "/Lose");

        if (correct == null && point == null && against == null && win == null && lose == null)
        {
            Debug.LogWarning("MatchSounds: nothing in Resources/" + ClipFolder + ", so the match is silent.");
            return false;
        }

        GameObject host = new GameObject("MatchSounds");
        Object.DontDestroyOnLoad(host);

        source = host.AddComponent<AudioSource>();
        source.playOnAwake = false;

        // 2D. A match sound has no position, and leaving it 3D makes it quieter depending on where
        // the camera happens to sit.
        source.spatialBlend = 0f;

        return true;
    }
}
