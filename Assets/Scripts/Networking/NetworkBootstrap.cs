using System;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

public class NetworkBootstrap : MonoBehaviour
{
    public static NetworkBootstrap Instance { get; private set; }

    [Tooltip("Port both the host listens on and clients connect to. Every device must agree on this.")]
    [SerializeField] private ushort connectPort = 7777;

    [Tooltip("Pre-filled into the Join address box. Only a convenience — the player can type any address.")]
    [SerializeField] private string defaultConnectAddress = "192.168.1.1";

    [Tooltip("Editor only: automatically host (main Editor) or join 127.0.0.1 (virtual players) on Play, " +
             "so Multiplayer Play Mode testing needs no clicking. Never runs in a build.")]
    [SerializeField] private bool autoConnectInEditor = true;

    // Raised whenever the connection state changes, with a message meant for the player
    // (e.g. "Waiting for players…", "Could not reach 192.168.1.42"). The Connect page listens
    // to this rather than polling NetworkManager every frame.
    public event Action<string> StatusChanged;

    // Raised once this device is actually in a session, host or client. Used by the Connect page
    // to hand control over to the lobby.
    public event Action SessionStarted;

    // Raised when this device leaves or loses the session, with the reason if the server gave one.
    public event Action<string> SessionEnded;

    public bool IsConnecting { get; private set; }

    public bool IsInSession => NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;

    // The largest room any mode needs. Capacity deliberately does NOT follow the selected mode:
    // players connect as soon as the app opens, long before anyone picks 1v1 or 2v2, so a
    // mode-based cap would reject players 3 and 4 before 2v2 could ever be chosen. The exact
    // per-mode player count is enforced later, at TriviaNetworkSync's ready gate.
    public const int MaxRoomPlayers = 4;

    public int RoomCapacity => MaxRoomPlayers;

    public ushort Port => connectPort;

    public string DefaultConnectAddress => defaultConnectAddress;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    private void Start()
    {
        SubscribeToNetworkManager();
        EnableConnectionApproval();

        // A build always waits for the player to pick Host or Join on the Connect page. Only the
        // Editor auto-connects, so the existing Multiplayer Play Mode workflow keeps working
        // exactly as it did before this screen existed.
        if (Application.isEditor && autoConnectInEditor)
        {
            if (IsClonedVirtualPlayer())
                StartClientAt("127.0.0.1");
            else
                StartHost();
        }
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;

        UnsubscribeFromNetworkManager();
    }

    // ---------------------------------------------------------------
    // Public entry points — wire these to the Connect page buttons
    // ---------------------------------------------------------------

    public void StartHost()
    {
        if (NetworkManager.Singleton == null)
        {
            ReportStatus("No NetworkManager in the scene.");
            return;
        }

        if (NetworkManager.Singleton.IsListening)
            return;

        // Listen on 0.0.0.0 rather than the machine's own LAN address: the host doesn't
        // necessarily know which interface a phone will arrive on (Wi-Fi vs. hotspot), and
        // binding to one specific address silently refuses connections from the other.
        ConfigureTransport("127.0.0.1", listenAddress: "0.0.0.0");

        if (NetworkManager.Singleton.StartHost())
        {
            IsConnecting = false;
            ReportStatus("Hosting on " + GetLocalIPv4() + ":" + connectPort);
            SessionStarted?.Invoke();
        }
        else
        {
            ReportStatus("Could not start hosting. Is the port already in use?");
        }
    }

    public void StartClientAt(string address)
    {
        if (NetworkManager.Singleton == null)
        {
            ReportStatus("No NetworkManager in the scene.");
            return;
        }

        if (NetworkManager.Singleton.IsListening)
            return;

        address = (address ?? string.Empty).Trim();

        if (!IsValidIPv4(address))
        {
            ReportStatus("\"" + address + "\" is not a valid address. It should look like 192.168.1.42.");
            return;
        }

        ConfigureTransport(address, listenAddress: "0.0.0.0");

        if (NetworkManager.Singleton.StartClient())
        {
            IsConnecting = true;
            ReportStatus("Connecting to " + address + "…");
        }
        else
        {
            ReportStatus("Could not start connecting to " + address + ".");
        }
    }

    // Leaves the current session. Safe to call when not connected.
    public void Disconnect()
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening)
            return;

        NetworkManager.Singleton.Shutdown();
        IsConnecting = false;
        ReportStatus("Disconnected.");
        SessionEnded?.Invoke(string.Empty);
    }

    // ---------------------------------------------------------------
    // Connection approval — capacity gate
    // ---------------------------------------------------------------

    // Without this, nothing caps the room. A third phone joining a 1v1 is accepted and
    // PlayerSideIdentity hands it AssignedSide = 2 (side 1 goes to the server, side 2 to everyone
    // else), so two players end up sharing one side and either can answer for it.
    private void EnableConnectionApproval()
    {
        if (NetworkManager.Singleton == null)
            return;

        // Both ends must agree on this flag: NetworkConfig is hashed and compared during the
        // handshake, so setting it only on the host would make every client fail to connect.
        NetworkManager.Singleton.NetworkConfig.ConnectionApproval = true;
        NetworkManager.Singleton.ConnectionApprovalCallback = HandleConnectionApproval;
    }

    private void HandleConnectionApproval(
        NetworkManager.ConnectionApprovalRequest request,
        NetworkManager.ConnectionApprovalResponse response)
    {
        response.Pending = false;
        response.CreatePlayerObject = true;

        // The host itself always gets in — rejecting it would tear down the session it just opened.
        if (request.ClientNetworkId == NetworkManager.ServerClientId ||
            request.ClientNetworkId == NetworkManager.Singleton.LocalClientId)
        {
            response.Approved = true;
            return;
        }

        bool matchRunning =
            (TriviaDuelManager.Instance != null && TriviaDuelManager.Instance.IsMatchRunning) ||
            (TeamDuelManager.Instance != null && TeamDuelManager.Instance.IsMatchRunning);

        if (matchRunning)
        {
            response.Approved = false;
            response.Reason = "A match is already in progress. Try again when it ends.";
            return;
        }

        if (NetworkManager.Singleton.ConnectedClientsIds.Count >= MaxRoomPlayers)
        {
            response.Approved = false;
            response.Reason = "This game is full (" + MaxRoomPlayers + " players).";
            return;
        }

        response.Approved = true;
    }

    // ---------------------------------------------------------------
    // Address helpers
    // ---------------------------------------------------------------

    // This device's LAN address, shown on the host's screen for other players to type in.
    // Returns "unavailable" rather than throwing when there's no usable interface (airplane
    // mode, no Wi-Fi), so the Connect page can display something meaningful either way.
    public static string GetLocalIPv4()
    {
        try
        {
            foreach (NetworkInterface adapter in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (adapter.OperationalStatus != OperationalStatus.Up)
                    continue;

                if (adapter.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                    continue;

                foreach (UnicastIPAddressInformation info in adapter.GetIPProperties().UnicastAddresses)
                {
                    if (info.Address.AddressFamily != AddressFamily.InterNetwork)
                        continue;

                    if (IPAddress.IsLoopback(info.Address))
                        continue;

                    // 169.254.x.x is a self-assigned address handed out when DHCP failed —
                    // nothing else on the network can reach it, so it's never the one to show.
                    if (info.Address.ToString().StartsWith("169.254."))
                        continue;

                    return info.Address.ToString();
                }
            }
        }
        catch (Exception exception)
        {
            Debug.LogWarning("NetworkBootstrap: could not read local IP address. " + exception.Message);
        }

        return "unavailable";
    }

    public static bool IsValidIPv4(string address)
    {
        if (string.IsNullOrWhiteSpace(address))
            return false;

        // IPAddress.TryParse accepts partial forms like "192.168" (padding with zeros), which would
        // send the player off connecting to an address they never typed. Require all four parts.
        if (address.Split('.').Length != 4)
            return false;

        return IPAddress.TryParse(address, out IPAddress parsed)
            && parsed.AddressFamily == AddressFamily.InterNetwork;
    }

    private void ConfigureTransport(string address, string listenAddress)
    {
        UnityTransport transport = NetworkManager.Singleton.GetComponent<UnityTransport>();

        if (transport != null)
            transport.SetConnectionData(address, connectPort, listenAddress);
        else
            Debug.LogWarning("NetworkBootstrap: NetworkManager has no UnityTransport component.");
    }

    // ---------------------------------------------------------------
    // NetworkManager callbacks
    // ---------------------------------------------------------------

    private void SubscribeToNetworkManager()
    {
        if (NetworkManager.Singleton == null)
            return;

        NetworkManager.Singleton.OnClientConnectedCallback += HandleClientConnected;
        NetworkManager.Singleton.OnClientDisconnectCallback += HandleClientDisconnected;
    }

    private void UnsubscribeFromNetworkManager()
    {
        if (NetworkManager.Singleton == null)
            return;

        NetworkManager.Singleton.OnClientConnectedCallback -= HandleClientConnected;
        NetworkManager.Singleton.OnClientDisconnectCallback -= HandleClientDisconnected;
    }

    private void HandleClientConnected(ulong clientId)
    {
        // Fires on the host for every arrival, and on a client only for itself.
        if (NetworkManager.Singleton.IsServer)
        {
            int count = NetworkManager.Singleton.ConnectedClientsIds.Count;
            ReportStatus(count + " player" + (count == 1 ? "" : "s") + " connected. Hosting on "
                         + GetLocalIPv4() + ":" + connectPort);
            return;
        }

        if (clientId != NetworkManager.Singleton.LocalClientId)
            return;

        IsConnecting = false;
        ReportStatus("Connected.");
        SessionStarted?.Invoke();
    }

    private void HandleClientDisconnected(ulong clientId)
    {
        if (NetworkManager.Singleton.IsServer && clientId != NetworkManager.Singleton.LocalClientId)
        {
            int count = NetworkManager.Singleton.ConnectedClientsIds.Count;
            ReportStatus("A player left. " + count + " connected.");
            return;
        }

        // Reaching here as a client means either the connection attempt was refused or an
        // established session dropped — IsConnecting tells the two apart, which matters because
        // "could not reach the host" and "the host closed the game" need different advice.
        string reason = NetworkManager.Singleton.DisconnectReason;

        if (string.IsNullOrEmpty(reason))
        {
            reason = IsConnecting
                ? "Could not reach the host. Check the address and that both devices are on the same Wi-Fi."
                : "Disconnected from the host.";
        }

        IsConnecting = false;
        ReportStatus(reason);
        SessionEnded?.Invoke(reason);
    }

    private void ReportStatus(string message)
    {
        StatusChanged?.Invoke(message);
    }

    // ---------------------------------------------------------------
    // Multiplayer Play Mode virtual players (Editor only)
    // ---------------------------------------------------------------

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
