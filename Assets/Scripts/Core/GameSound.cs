using UnityEngine;

// Sound on or off for the whole game, remembered between launches.
//
// AudioListener.volume silences every AudioSource at once -- button clicks and match sounds alike --
// without either of them having to know the setting exists.
public static class GameSound
{
    private const string Key = "EstaloSoundOn";

    public static bool IsOn => PlayerPrefs.GetInt(Key, 1) == 1;

    public static void SetOn(bool on)
    {
        PlayerPrefs.SetInt(Key, on ? 1 : 0);
        PlayerPrefs.Save();
        Apply();
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Apply()
    {
        AudioListener.volume = IsOn ? 1f : 0f;
    }
}
