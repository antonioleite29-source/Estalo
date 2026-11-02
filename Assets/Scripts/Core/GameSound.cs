using UnityEngine;

// The sound effects -- button clicks and match sounds -- on or off, remembered between launches.
//
// Effects and music are two separate switches now, so a player can keep the clicks and drop the
// music, or the other way round (GameMusic holds the other one). That rules out AudioListener.volume,
// which silences everything at once: the two places that play an effect multiply their own volume by
// EffectsVolume instead.
public static class GameSound
{
    private const string Key = "EstaloSoundOn";

    public static bool IsOn => PlayerPrefs.GetInt(Key, 1) == 1;

    // What an effect should be multiplied by before it is played: 1 when effects are on, 0 when off.
    public static float EffectsVolume => IsOn ? 1f : 0f;

    public static void SetOn(bool on)
    {
        PlayerPrefs.SetInt(Key, on ? 1 : 0);
        PlayerPrefs.Save();
    }

    // An older build silenced everything through the listener. A phone that was switched off then
    // and has just updated would otherwise stay silent for good, because nothing turns it back up.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void UndoTheOldGlobalMute()
    {
        AudioListener.volume = 1f;
    }
}
