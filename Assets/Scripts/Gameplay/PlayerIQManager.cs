using UnityEngine;

// This phone's copy of its IQ. The server's copy (PlayerAccounts) is the real one: it is what
// matchmaking and the leaderboard read, and it overwrites this one every time the phone connects or
// finishes a match. This copy exists so the lobby, the difficulty level and the lessons have a number
// to use while offline.
public class PlayerIQManager : MonoBehaviour
{
    public static PlayerIQManager Instance { get; private set; }

    public const float IQ_MIN = 70f;
    public const float IQ_MAX = 150f;
    private const float IQ_RANGE = 80f;
    private const int LEVELS = 7;
    private const float IQ_DEFAULT = 100f;

    private const string PREFS_KEY_IQ = "PlayerIQ_Local";

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    // Multiplayer Play Mode virtual players share this machine's PlayerPrefs with the main Editor —
    // same reasoning as PlayerProfileManager.KeySuffix. Never applies on two real separate devices.
    private static string KeySuffix => NetworkBootstrap.GetLocalProfileSuffix();

    public float GetLocalIQ()
    {
        return PlayerPrefs.GetFloat(PREFS_KEY_IQ + KeySuffix, IQ_DEFAULT);
    }

    public void SetLocalIQ(float iq)
    {
        PlayerPrefs.SetFloat(PREFS_KEY_IQ + KeySuffix, iq);
        PlayerPrefs.Save();
    }

    // Whether this phone has ever stored an IQ -- from a match, the placement round or the server.
    public bool HasSavedIQ()
    {
        return PlayerPrefs.HasKey(PREFS_KEY_IQ + KeySuffix);
    }

    // Maps an IQ to difficulty level 1-7. Below 70 = Level 1, above 150 = Level 7. Static so the
    // server can use it on its own copy of every player's IQ.
    public static int LevelForIQ(float iq)
    {
        iq = Mathf.Clamp(iq, IQ_MIN, IQ_MAX);
        int level = Mathf.FloorToInt((iq - IQ_MIN) / (IQ_RANGE / LEVELS)) + 1;
        return Mathf.Clamp(level, 1, LEVELS);
    }

    public int GetLocalDifficultyLevel()
    {
        return LevelForIQ(GetLocalIQ());
    }

    // delta = 50 / currentIQ, applied against this device's own IQ.
    //
    // Only for a match this device referees by itself, with no server. A match the server ran is
    // scored on the server (PlayerAccounts.RecordMatch) and the new IQ arrives through PlayerStats --
    // adjusting it here as well would count every match twice, and would be the phone deciding its
    // own score again.
    public void AdjustLocalIQAfterMatch(bool didWin)
    {
        float currentIQ = GetLocalIQ();
        float delta = 50f / currentIQ;
        SetLocalIQ(didWin ? currentIQ + delta : currentIQ - delta);
    }
}
