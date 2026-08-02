using UnityEngine;

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

    // Maps this device's IQ to difficulty level 1-7. Below 70 = Level 1, above 150 = Level 7.
    public int GetLocalDifficultyLevel()
    {
        float iq = Mathf.Clamp(GetLocalIQ(), IQ_MIN, IQ_MAX);
        int level = Mathf.FloorToInt((iq - IQ_MIN) / (IQ_RANGE / LEVELS)) + 1;
        return Mathf.Clamp(level, 1, LEVELS);
    }

    // delta = 50 / currentIQ, applied against this device's own IQ. Call once per match end.
    public void AdjustLocalIQAfterMatch(bool didWin)
    {
        float currentIQ = GetLocalIQ();
        float delta = 50f / currentIQ;
        SetLocalIQ(didWin ? currentIQ + delta : currentIQ - delta);
    }
}
