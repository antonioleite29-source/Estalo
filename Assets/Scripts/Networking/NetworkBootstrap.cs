using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

public class NetworkBootstrap : MonoBehaviour
{
    [SerializeField] private string connectAddress = "127.0.0.1";
    [SerializeField] private ushort connectPort = 7777;

    private void Start()
    {
        ConfigureTransport();
        BeginConnection();
    }

    private void ConfigureTransport()
    {
        UnityTransport transport = NetworkManager.Singleton.GetComponent<UnityTransport>();

        if (transport != null)
            transport.SetConnectionData(connectAddress, connectPort);
    }

    private void BeginConnection()
    {
        if (IsClonedVirtualPlayer())
            NetworkManager.Singleton.StartClient();
        else
            NetworkManager.Singleton.StartHost();
    }

    // Unity Multiplayer Play Mode launches each virtual player as a separate process with this
    // command-line flag — used here instead of a managed API so this doesn't depend on a specific
    // package version's runtime surface. Exposed publicly so other systems (e.g. PlayerProfileManager)
    // can tell local-testing virtual players apart, since they otherwise share this machine's
    // PlayerPrefs storage with the main Editor process.
    public static bool IsClonedVirtualPlayer()
    {
        return !string.IsNullOrEmpty(GetVirtualPlayerId());
    }

    // Each virtual player process gets its own unique -vpId=<id> launch argument from Multiplayer
    // Play Mode. Used to give each one its own PlayerPrefs storage slot (PlayerProfileManager,
    // PlayerIQManager) instead of every virtual player colliding on one shared "_VP" suffix.
    public static string GetLocalProfileSuffix()
    {
        string vpId = GetVirtualPlayerId();
        return string.IsNullOrEmpty(vpId) ? string.Empty : "_" + vpId;
    }

    private static string GetVirtualPlayerId()
    {
        const string prefix = "-vpId=";
        string[] args = System.Environment.GetCommandLineArgs();

        for (int i = 0; i < args.Length; i++)
        {
            if (args[i].StartsWith(prefix))
                return args[i].Substring(prefix.Length);
        }

        return string.Empty;
    }
}
