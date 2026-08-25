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

    // Named rather than passed as an AudioClip, because the clips do not exist until Prepare has
    // run -- and an argument is evaluated before the method it is being passed to. Play(against)
    // therefore handed over a null on the very first call of a session and dropped the sound
    // without a word. Asking for a clip by name means it is looked up AFTER loading.
    private enum Cue { Correct, Point, Against, Win, Lose }

    public static void PlayCorrect()
    {
        DeviceFeedback.Vibrate(DeviceFeedback.Strength.Bump);
        Play(Cue.Correct);
    }

    public static void PlayPoint() => Play(Cue.Point);
    public static void PlayAgainst() => Play(Cue.Against);
    public static void PlayWin() => Play(Cue.Win);
    public static void PlayLose() => Play(Cue.Lose);

    // Everything routes through here so that "which side am I" is answered once rather than at
    // each of the several places a score can change.
    public static void PlayScored(bool byLocalPlayer)
    {
        Play(byLocalPlayer ? Cue.Point : Cue.Against);
    }

    public static void PlayEnded(bool localPlayerWon)
    {
        // The only one heavy enough to feel across a desk, and the only one iOS gets at all.
        DeviceFeedback.Vibrate(DeviceFeedback.Strength.Thud);
        Play(localPlayerWon ? Cue.Win : Cue.Lose);
    }

    private static AudioSource source;
    private static readonly System.Collections.Generic.Dictionary<Cue, AudioClip> clips =
        new System.Collections.Generic.Dictionary<Cue, AudioClip>();
    private static bool ready;

    private static void Play(Cue cue)
    {
        if (!DeviceFeedback.SoundAllowed || !Prepare())
            return;

        if (!clips.TryGetValue(cue, out AudioClip clip) || clip == null)
        {
            Debug.LogWarning($"MatchSounds: no clip for {cue} in Resources/{ClipFolder}, so that " +
                             "moment is silent.");
            return;
        }

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

        foreach (Cue cue in System.Enum.GetValues(typeof(Cue)))
            clips[cue] = Resources.Load<AudioClip>(ClipFolder + "/" + cue);

        bool anyFound = false;
        foreach (AudioClip clip in clips.Values)
            anyFound |= clip != null;

        if (!anyFound)
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