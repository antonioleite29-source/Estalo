using UnityEngine;

public class PlayerProfileManager : MonoBehaviour
{
    public static PlayerProfileManager Instance { get; private set; }

    [Tooltip("Drag your avatar gallery sprites here. Players pick one of these on the Profile page.")]
    public Sprite[] availableAvatars;

    private const string PREFS_KEY_NAME = "PlayerProfileName";
    private const string PREFS_KEY_AVATAR = "PlayerProfileAvatarIndex";
    private const string DEFAULT_NAME = "Player";

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    // Multiplayer Play Mode virtual players run as separate processes but share this machine's
    // PlayerPrefs storage with the main Editor — without this, both "devices" would read/write the
    // exact same profile. On two real separate devices this suffix is never applied either way,
    // since IsClonedVirtualPlayer() only returns true for a local virtual player process.
    private static string KeySuffix => NetworkBootstrap.GetLocalProfileSuffix();

    public string GetLocalName()
    {
        return PlayerPrefs.GetString(PREFS_KEY_NAME + KeySuffix, DEFAULT_NAME);
    }

    public void SetLocalName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            name = DEFAULT_NAME;

        PlayerPrefs.SetString(PREFS_KEY_NAME + KeySuffix, name);
        PlayerPrefs.Save();
    }

    public int GetLocalAvatarIndex()
    {
        return PlayerPrefs.GetInt(PREFS_KEY_AVATAR + KeySuffix, 0);
    }

    public void SetLocalAvatarIndex(int index)
    {
        PlayerPrefs.SetInt(PREFS_KEY_AVATAR + KeySuffix, index);
        PlayerPrefs.Save();
    }

    public Sprite GetAvatarSprite(int index)
    {
        if (availableAvatars == null || index < 0 || index >= availableAvatars.Length)
            return null;

        return availableAvatars[index];
    }
}
