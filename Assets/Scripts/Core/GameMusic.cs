using System.Collections.Generic;
using UnityEngine;

// The music: a loop in the lobby, a different loop during a match, and a short piece when the match
// is won or lost.
//
// Clips are loaded by name from Resources/Music -- Lobby, Match, Win, Lose -- so new music is dropped
// in as files with nothing to wire up. A missing file is reported once and then ignored, which is why
// the game still runs with no music at all.
//
// Separate AudioSource from the sound effects, so the Som setting, the effects and the music never
// fight over one source. GameSound switching sound off silences this too, because it drops
// AudioListener.volume for everything.
public static class GameMusic
{
    private const string ClipFolder = "Music";

    // Under the effects on purpose: the music is the floor of the mix, not a layer of it.
    public static float Volume = 0.35f;

    private const float FadeSeconds = 0.6f;

    public enum Track { Lobby, Match, Win, Lose }

    private static AudioSource source;
    private static readonly Dictionary<Track, AudioClip> clips = new Dictionary<Track, AudioClip>();
    private static readonly HashSet<Track> missingReported = new HashSet<Track>();
    private static bool ready;
    private static Track? playing;

    // The lobby loop, and the one the game comes back to after a match. Does nothing if it is
    // already the music that is playing, so switching tabs does not restart it.
    public static void PlayLobby() => Play(Track.Lobby, loop: true);

    public static void PlayMatch() => Play(Track.Match, loop: true);

    // One-shot, and the lobby music does not come back by itself: whoever leaves the result screen
    // calls PlayLobby.
    public static void PlayResult(bool localPlayerWon) => Play(localPlayerWon ? Track.Win : Track.Lose, loop: false);

    public static void Stop()
    {
        playing = null;

        if (source != null)
            source.Stop();
    }

    private static void Play(Track track, bool loop)
    {
        if (playing == track && source != null && source.isPlaying)
            return;

        if (!Prepare())
            return;

        if (!clips.TryGetValue(track, out AudioClip clip) || clip == null)
        {
            if (missingReported.Add(track))
                Debug.Log($"GameMusic: no {track} clip in Resources/{ClipFolder}, so that music stays silent.");

            return;
        }

        playing = track;
        source.clip = clip;
        source.loop = loop;
        source.volume = Volume;
        source.Play();
    }

    private static bool Prepare()
    {
        if (ready)
            return source != null;

        ready = true;

        GameObject host = new GameObject("GameMusic");
        host.hideFlags = HideFlags.HideInHierarchy;
        Object.DontDestroyOnLoad(host);

        source = host.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.spatialBlend = 0f;
        source.ignoreListenerPause = true;

        foreach (Track track in System.Enum.GetValues(typeof(Track)))
            clips[track] = Resources.Load<AudioClip>($"{ClipFolder}/{track}");

        return true;
    }
}
