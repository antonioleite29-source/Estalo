using UnityEngine;

// Vibration, and whether this phone wants to hear anything at all.
//
// The two belong together because they answer the same question -- how much of itself the game is
// allowed to push into a room. A phone on silent in a classroom is the case that matters here.
public static class DeviceFeedback
{
    public enum Strength
    {
        // A button. Barely perceptible, and short enough not to buzz.
        Tap,

        // Something happened: an answer landing, a point.
        Bump,

        // The end of a match. The only one loud enough to hear across a desk.
        Thud
    }

    public static bool VibrationEnabled = true;

    // --- silent mode ------------------------------------------------------

    // iOS needs nothing here. With Mute Other Audio Sources off, Unity runs the Ambient audio
    // session, which the ring/silent switch already silences -- doing it again in script would
    // just be a second thing to keep in step.
    //
    // Android is the opposite: game audio is on the music stream, and the ringer does not touch
    // it. A phone set to silent in a classroom still plays every click at full volume unless the
    // ringer mode is read and obeyed.
    public static bool SoundAllowed
    {
        get
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            RefreshRingerMode();
            return ringerMode == RingerNormal;
#else
            return true;
#endif
        }
    }

#if UNITY_ANDROID && !UNITY_EDITOR
    private const int RingerSilent = 0;
    private const int RingerVibrate = 1;
    private const int RingerNormal = 2;

    private static int ringerMode = RingerNormal;
    private static float nextRingerCheck;

    // Polled rather than watched. Reading it goes through JNI, which is far too slow to do on
    // every tap, and there is no callback for it that does not need a BroadcastReceiver and a
    // custom Android plugin. Half a second is imperceptible for something a player changes with a
    // physical switch.
    private static void RefreshRingerMode()
    {
        if (Time.unscaledTime < nextRingerCheck)
            return;

        nextRingerCheck = Time.unscaledTime + 0.5f;

        try
        {
            using (AndroidJavaClass player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            using (AndroidJavaObject activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
            using (AndroidJavaObject audio = activity.Call<AndroidJavaObject>("getSystemService", "audio"))
            {
                ringerMode = audio.Call<int>("getRingerMode");
            }
        }
        catch (System.Exception exception)
        {
            // Treated as "make noise", because a phone that is not on silent is the common case
            // and a game that goes mute because of a JNI failure looks broken.
            ringerMode = RingerNormal;
            Debug.LogWarning("DeviceFeedback: could not read the ringer mode. " + exception.Message);
        }
    }
#endif

    // --- vibration --------------------------------------------------------

    public static void Vibrate(Strength strength)
    {
        if (!VibrationEnabled)
            return;

#if UNITY_ANDROID && !UNITY_EDITOR
        // Silent still vibrates; vibrate mode obviously does. Only a phone whose owner has turned
        // haptics off entirely should be still, and that is the OS's business, not ours.
        AndroidVibrate(Milliseconds(strength));
#elif UNITY_IOS && !UNITY_EDITOR
        // Unity gives iOS exactly one vibration and no control over its length -- it is the system
        // buzz, which is far too heavy for a button. So only the end of a match gets one.
        //
        // Light taptics would need UIImpactFeedbackGenerator through a native plugin. Worth doing
        // one day; not worth a plugin for a click.
        if (strength == Strength.Thud)
            Handheld.Vibrate();
#endif
    }

#if UNITY_ANDROID && !UNITY_EDITOR
    private static long Milliseconds(Strength strength)
    {
        switch (strength)
        {
            case Strength.Tap: return 12;
            case Strength.Bump: return 28;
            default: return 60;
        }
    }

    private static AndroidJavaObject vibrator;

    private static void AndroidVibrate(long milliseconds)
    {
        try
        {
            if (vibrator == null)
            {
                using (AndroidJavaClass player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (AndroidJavaObject activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
                {
                    vibrator = activity.Call<AndroidJavaObject>("getSystemService", "vibrator");
                }
            }

            if (vibrator == null || !vibrator.Call<bool>("hasVibrator"))
                return;

            // VibrationEffect from Android 8 on. The old vibrate(long) still works but is
            // deprecated and ignores amplitude, so a tap and a thud would feel identical.
            if (AndroidVersion >= 26)
            {
                using (AndroidJavaClass effects = new AndroidJavaClass("android.os.VibrationEffect"))
                using (AndroidJavaObject effect = effects.CallStatic<AndroidJavaObject>(
                           "createOneShot", milliseconds, GetDefaultAmplitude(effects)))
                {
                    vibrator.Call("vibrate", effect);
                }
            }
            else
            {
                vibrator.Call("vibrate", milliseconds);
            }
        }
        catch (System.Exception exception)
        {
            Debug.LogWarning("DeviceFeedback: vibration failed. " + exception.Message);
            VibrationEnabled = false;   // do not retry a hundred times a match
        }
    }

    private static int GetDefaultAmplitude(AndroidJavaClass effects)
    {
        try
        {
            return effects.GetStatic<int>("DEFAULT_AMPLITUDE");
        }
        catch (System.Exception)
        {
            return -1;   // what DEFAULT_AMPLITUDE is anyway
        }
    }

    private static int AndroidVersion
    {
        get
        {
            if (androidVersion > 0)
                return androidVersion;

            using (AndroidJavaClass build = new AndroidJavaClass("android.os.Build$VERSION"))
                androidVersion = build.GetStatic<int>("SDK_INT");

            return androidVersion;
        }
    }

    private static int androidVersion;
#endif
}
