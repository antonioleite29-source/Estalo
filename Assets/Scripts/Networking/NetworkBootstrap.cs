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

    [Header("--- ALWAYS-ON SERVER ---")]
    [Tooltip("Address of the always-on server: an IP like 203.0.113.10, or a name like " +
             "trivia.meudominio.com. Leave empty to keep the old behaviour, where one player hosts " +
             "and the others type an address on the Connect page.")]
    public string serverAddress = "";

    [Tooltip("Connect to Server Address by itself on launch, and keep reconnecting if it drops. " +
             "With this on, nobody hosts and nobody types an address — the game is simply online.")]
    public bool autoConnectToServer = true;

    [Tooltip("Seconds to wait before trying the server again after a failed or lost connection.")]
    public float reconnectDelaySeconds = 5f;

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

    // Capacity deliberately does NOT follow the selected mode: players connect as soon as the app
    // opens, long before anyone picks 1v1 or 2v2, so a mode-based cap would reject players 3 and 4
    // before 2v2 could ever be chosen. The exact per-mode player count is enforced later, at the
    // matchmaker, which forms as many matches as there are players for.
    //
    // 8 rather than 4 because matches now run concurrently: 8 players is two 2v2s or four 1v1s at
    // once. At 4 the room filled up before a second match could ever form, and a host plus four
    // test devices was already one over.
    public const int MaxRoomPlayers = 8;

    // What the always-on server holds, as opposed to one phone's room. Players are matched into
    // pairs and fours by the queue, so this is a count of people online at once, not of one game.
    public const int MaxServerPlayers = 64;

    public ushort Port => connectPort;

    public string DefaultConnectAddress => defaultConnectAddress;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        // Shutting down in OnDestroy/OnApplicationQuit is too late. Those run in the same teardown
        // phase as NetworkManager's own OnDestroy, and MonoBehaviour destruction order is undefined
        // — netcode was reaching its shutdown after pieces it needs had already gone, throwing
        // inside NetworkSceneManager.Dispose and never getting as far as closing the UDP socket.
        // That is what leaves port 7777 held by the Editor and forces a full restart.
        //
        // Application.quitting and the Editor's exiting-play-mode callback both fire BEFORE any of
        // that, so the transport is closed while netcode is still whole.
        Application.quitting += ShutdownNetworkingEarly;

        // The single most effective part of staying connected on a phone, and it is not a netcode
        // setting at all: stop the screen locking in the first place. Once Android suspends the
        // app, no timeout value keeps the connection alive, because the game is not running to
        // answer anything.
        if (keepScreenAwake)
            Screen.sleepTimeout = SleepTimeout.NeverSleep;

#if UNITY_EDITOR
        UnityEditor.EditorApplication.playModeStateChanged += HandlePlayModeChanged;
#endif
    }

#if UNITY_EDITOR
    private void HandlePlayModeChanged(UnityEditor.PlayModeStateChange change)
    {
        if (change == UnityEditor.PlayModeStateChange.ExitingPlayMode)
            ShutdownNetworkingEarly();
    }
#endif

    private void ShutdownNetworkingEarly()
    {
        Discovery.StopAll();
        ReleaseTransport();
    }

    private void Start()
    {
        SubscribeToNetworkManager();
        EnableConnectionApproval();

        // A headless build is the always-on server and nothing else. Checked first: it must never
        // fall through and try to be a client of itself.
        if (IsDedicatedServerBuild)
        {
            StartDedicatedServer();
            return;
        }

        // With a server address configured, every copy of the game is a client of it and nobody
        // hosts. The Editor is included on purpose — testing against the real server is the point
        // of having one.
        if (autoConnectToServer && !string.IsNullOrWhiteSpace(serverAddress))
        {
            BeginAutoConnect();
            return;
        }

        // No server configured, so this is the old arrangement: a build waits for the player to
        // pick Host or Join on the Connect page, and only the Editor connects by itself, which is
        // what keeps the Multiplayer Play Mode workflow working.
        if (Application.isEditor && autoConnectInEditor)
        {
            if (IsClonedVirtualPlayer())
                StartClientAt("127.0.0.1");
            else
                StartHost();
        }
    }

    // ---------------------------------------------------------------
    // Always-on server
    // ---------------------------------------------------------------

    // True in a build made with the Dedicated Server platform, and also when any build is launched
    // with -batchmode. The second half matters because it lets the server be tested from a normal
    // desktop build before the Linux server module is ever installed.
    public static bool IsDedicatedServerBuild
    {
        get
        {
            // Never the Editor, whatever the defines say. UNITY_SERVER is defined in the Editor
            // as soon as the active build target is Dedicated Server -- so simply building the
            // server once turned the Editor into one, and it stopped being able to play at all:
            // a dedicated server creates no player object for itself, so readying up failed with
            // "no local PlayerSideIdentity".
            //
            // The Editor is the machine you test FROM. It is always a client of whatever Server
            // Address points at.
            if (Application.isEditor)
                return false;

#if UNITY_SERVER
            return true;
#else
            return Application.isBatchMode;
#endif
        }
    }

    // Server, not host. StartHost would give this machine a player object and a seat in the room,
    // which is exactly what an always-on server must not have: it runs the queue and the matches
    // and never plays in one.
    public void StartDedicatedServer()
    {
        if (NetworkManager.Singleton == null)
        {
            ReportStatus("No NetworkManager in the scene.");
            return;
        }

        if (NetworkManager.Singleton.IsListening)
            return;

        // 0.0.0.0 so it answers on every interface the machine has. A cloud box usually has
        // several, and binding to whichever one was guessed is how a server ends up running
        // perfectly while refusing every connection.
        ConfigureTransport("0.0.0.0", listenAddress: "0.0.0.0", port: connectPort);

        if (NetworkManager.Singleton.StartServer())
        {
            ReportStatus("Dedicated server listening on port " + connectPort + ".");
            SessionStarted?.Invoke();
        }
        else
        {
            ReportStatus("Dedicated server could not bind port " + connectPort + ".");
        }
    }

    private Coroutine autoConnectRoutine;

    // Set while the reconnect loop owns the connection, so a deliberate Disconnect() can tell
    // itself apart from a drop and stop the loop instead of being fought by it.
    private bool wantsServerConnection;

    private void BeginAutoConnect()
    {
        wantsServerConnection = true;

        if (autoConnectRoutine == null)
            autoConnectRoutine = StartCoroutine(KeepConnectedToServer());
    }

    private void StopAutoConnect()
    {
        wantsServerConnection = false;

        if (autoConnectRoutine != null)
        {
            StopCoroutine(autoConnectRoutine);
            autoConnectRoutine = null;
        }
    }

    // Retries for as long as the app is running rather than giving up after a few attempts. The
    // server being briefly unreachable — a phone changing from Wi-Fi to mobile data, a reboot on
    // the server, a tunnel dropping — is an ordinary event, not a reason to strand the player on
    // an error screen with nothing to press.
    private System.Collections.IEnumerator KeepConnectedToServer()
    {
        while (wantsServerConnection)
        {
            if (NetworkManager.Singleton != null && !NetworkManager.Singleton.IsListening)
            {
                StartClientAt(serverAddress);

                // A failed attempt raises OnClientDisconnectCallback rather than returning false,
                // so give the handshake a moment before deciding whether anything came of it.
                float waited = 0f;
                while (waited < 5f && IsConnecting)
                {
                    waited += Time.unscaledDeltaTime;
                    yield return null;
                }
            }

            yield return new WaitForSecondsRealtime(Mathf.Max(1f, reconnectDelaySeconds));
        }

        autoConnectRoutine = null;
    }

    // Turns a name like trivia.meudominio.com into an address UnityTransport can use. UTP takes a
    // literal IP and does no lookup of its own, so without this a domain fails the same way a typo
    // would. Worth having: the address lives in a build on someone's phone, and a domain can be
    // repointed at a new server without shipping a new one.
    private static string ResolveToIPv4(string address)
    {
        if (string.IsNullOrWhiteSpace(address))
            return address;

        address = address.Trim();

        if (IsValidIPv4(address))
            return address;

        try
        {
            foreach (IPAddress candidate in Dns.GetHostAddresses(address))
            {
                if (candidate.AddressFamily == AddressFamily.InterNetwork)
                    return candidate.ToString();
            }
        }
        catch (Exception exception)
        {
            Debug.LogWarning("NetworkBootstrap: could not look up \"" + address + "\". " + exception.Message);
        }

        return address;
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;

        Application.quitting -= ShutdownNetworkingEarly;

#if UNITY_EDITOR
        UnityEditor.EditorApplication.playModeStateChanged -= HandlePlayModeChanged;
#endif

        UnsubscribeFromNetworkManager();

        // Still here as a backstop for a bootstrap destroyed on its own, mid-session. By the time a
        // whole quit reaches this point the early shutdown has already run and this does nothing.
        ReleaseTransport();
    }

    private void OnApplicationQuit()
    {
        ReleaseTransport();
        Discovery.StopAll();
    }

    // Android does not really quit an app when you swipe it away — the process is often kept
    // alive, so OnApplicationQuit never runs and the next launch inherits a NetworkManager that
    // still thinks it is connected and sockets that are still bound. That is why the app had to be
    // opened twice: the first launch failed on the leftovers, and only the second, after Android
    // had finally killed the process, started clean. Releasing on pause makes the first launch work.
    private void OnApplicationPause(bool isPaused)
    {
        // A client of the always-on server keeps its session across a short trip out of the app --
        // a notification, a glance at another app, a moment on the home screen. The OS suspends
        // the process, so nothing is sent while away, and that is exactly what the widened
        // disconnectTimeoutSeconds is for. Tearing the transport down here instead is what made
        // leaving the app forfeit the match.
        if (wantsServerConnection)
            return;

        if (!isPaused)
            return;

        // Still torn down for a player-hosted session. A suspended host is a game nobody can
        // reach, and one still advertising itself strands every phone that tries to join.
        ReleaseTransport();
        Discovery.StopAll();
    }

    // Leaving Play mode does not reliably close the UDP socket on its own: NetworkManager reports
    // IsListening == false while the transport still holds port 7777, so the next Play session
    // fails to bind with "address is already in use" and the only cure is restarting the Editor.
    // Shutting down explicitly on teardown closes it.
    private void ReleaseTransport()
    {
        if (NetworkManager.Singleton == null)
            return;

        // Unconditional. The guard here used to be (IsListening || IsClient), which is false in
        // precisely the case this method exists for — see the comment above: NetworkManager reports
        // IsListening == false while the transport is still holding the socket, so the guard
        // skipped the shutdown exactly when the shutdown was needed. Shutdown() is a no-op when
        // nothing was started, so the guard was protecting against nothing.
        NetworkManager.Singleton.Shutdown();

        // NOT NetworkTransport.Shutdown() as well, and this time the reason is measured rather than
        // argued. Calling it here disposes the transport's driver while NetworkManager still
        // intends to shut down through it, so the later ShutdownInternal walks into
        // UnityTransport.GetDisconnectEventMessage with the driver already gone and throws a
        // NullReferenceException on every exit from play mode. The unconditional Shutdown() above
        // is what actually releases the socket; this line only added noise on the way out.
    }

    // ---------------------------------------------------------------
    // LAN discovery
    // ---------------------------------------------------------------

    private LanDiscovery discovery;

    // Created on demand on this same GameObject rather than needing its own slot in the scene —
    // one less thing to wire, and it cannot end up on the wrong object.
    public LanDiscovery Discovery
    {
        get
        {
            if (discovery == null)
                discovery = gameObject.AddComponent<LanDiscovery>();

            return discovery;
        }
    }

    private string HostAdvertisedName()
    {
        string playerName = PlayerProfileManager.Instance != null
            ? PlayerProfileManager.Instance.GetLocalName()
            : null;

        return string.IsNullOrEmpty(playerName) ? "Estalo" : "Sala de " + playerName;
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
        // Try a few ports rather than only the configured one. A crashed session can leave the
        // previous socket bound to the Editor process itself, and nothing short of quitting Unity
        // gets it back — which made the game untestable until a restart. Stepping to the next port
        // costs nothing: phones learn the real port from LAN discovery, and Editor clients read it
        // back from where the host recorded it.
        for (ushort port = connectPort; port <= connectPort + PortSearchRange; port++)
        {
            ConfigureTransport("127.0.0.1", listenAddress: "0.0.0.0", port: port);

            if (!NetworkManager.Singleton.StartHost())
                continue;

            ActivePort = port;
            IsConnecting = false;

            if (port != connectPort)
            {
                Debug.LogWarning($"Network: port {connectPort} was still held by something, so this " +
                                 $"session is hosting on {port} instead.");
            }

            ReportStatus("Hospedando em " + GetLocalIPv4() + ":" + port);

            // Announce on the Wi-Fi so phones can find this game without anyone reading an IP
            // address aloud and typing it in. The advertised port is the one actually bound.
            Discovery.StartAdvertising(port, HostAdvertisedName());

            SessionStarted?.Invoke();
            return;
        }

        ReportStatus($"Não foi possível hospedar. As portas {connectPort}-{connectPort + PortSearchRange} " +
                     "estão todas em uso.");
    }

    // How many ports past the configured one the host may fall back to.
    private const int PortSearchRange = 8;

    // The port this session actually bound. Recorded so Editor virtual players, which cannot hear
    // LAN discovery over loopback, still know where to connect.
    //
    // This used to live in PlayerPrefs and it did not work, in a way that looked like it should.
    // PlayerPrefs is a per-process in-memory cache: each process loads it once at startup and
    // serves every read from that copy. PlayerPrefs.Save() flushes the HOST's copy to disk, but a
    // virtual player that was already running never re-reads the file, so it kept answering with
    // the value it booted with — and clones DO share this machine's prefs storage (see
    // IsClonedVirtualPlayer below), so the staleness was the whole of it. The host moved to 7778,
    // by which time the clone had long since cached 7777; it went on
    // dialling 7777, connected to the socket a previous session had left stranded there, and timed
    // out with ProtocolTimeout. It looked like a netcode fault and was really a stale cache.
    //
    // A file is read at the moment it is asked for, so it actually crosses the process boundary.
    private ushort ActivePort
    {
        get
        {
            try
            {
                if (System.IO.File.Exists(ActivePortFile) &&
                    ushort.TryParse(System.IO.File.ReadAllText(ActivePortFile).Trim(), out ushort stored))
                {
                    return stored;
                }
            }
            catch (System.Exception)
            {
                // Unreadable for any reason means "fall back to the configured port", which is
                // exactly what happened before this file existed.
            }

            return connectPort;
        }
        set
        {
            try
            {
                System.IO.File.WriteAllText(ActivePortFile, value.ToString());
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("Network: could not record the active port, so Editor virtual " +
                                 "players may dial the wrong one. " + e.Message);
            }
        }
    }

    // The temp directory rather than anywhere under the project: MPPM redirects a clone's Library
    // and can isolate its prefs and persistent data, but temp comes from the environment the clone
    // inherits from the Editor that launched it, so both processes genuinely see one file. Scoping
    // it to this machine is correct rather than sloppy — the only clients that read it are the ones
    // connecting over loopback, which are on this machine by definition.
    private static string ActivePortFile =>
        System.IO.Path.Combine(System.IO.Path.GetTempPath(), "TriviaDuel_ActiveHostPort");

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

        // Resolved before validating, so a domain name is as acceptable as an IP here.
        string resolved = ResolveToIPv4(address);

        if (!IsValidIPv4(resolved))
        {
            ReportStatus("\"" + address + "\" não é um endereço válido. Deve ser algo como " +
                         "192.168.1.42, ou um nome que aponte para um.");
            return;
        }

        // Loopback means an Editor virtual player, which cannot hear LAN discovery, so it takes the
        // port the host recorded. A real client keeps the configured port unless discovery told the
        // Connect page otherwise.
        ushort port = resolved == "127.0.0.1" ? ActivePort : connectPort;

        ConfigureTransport(resolved, listenAddress: "0.0.0.0", port: port);

        if (NetworkManager.Singleton.StartClient())
        {
            IsConnecting = true;
            ReportStatus("Conectando a " + address + "…");
        }
        else
        {
            ReportStatus("Não foi possível conectar a " + address + ".");
        }
    }

    // Leaves the current session. Safe to call when not connected.
    public void Disconnect()
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening)
            return;

        // Leaving on purpose. Without this the reconnect loop would notice the session gone and
        // put the player straight back into it, which reads as a Disconnect button that does not work.
        StopAutoConnect();

        NetworkManager.Singleton.Shutdown();
        IsConnecting = false;

        // Stop shouting: a host that has shut down but is still advertising leaves phones showing
        // a game they cannot join.
        Discovery.StopAll();

        ReportStatus("Desconectado.");
        SessionEnded?.Invoke(string.Empty);
    }

    // ---------------------------------------------------------------
    // Connection approval — capacity gate
    // ---------------------------------------------------------------

    // Without this, nothing caps the room and the host has no way to turn away a phone that
    // arrives once every seat is taken or a match is already under way.
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

        // Both gates below exist for a session hosted on somebody's phone: one room, one match at
        // a time, a handful of seats. An always-on server is the opposite of all three -- it holds
        // many matches at once and people arrive whenever they feel like it. Left in place, the
        // first pair to start a game would lock every later arrival out of the server for good.
        if (!IsDedicatedServerBuild)
        {
            bool matchRunning = TriviaDuelManager.Instance != null && TriviaDuelManager.Instance.IsMatchRunning;

            if (matchRunning)
            {
                response.Approved = false;
                response.Reason = "Já há uma partida em andamento. Tente de novo quando ela terminar.";
                return;
            }
        }

        int capacity = IsDedicatedServerBuild ? MaxServerPlayers : MaxRoomPlayers;

        if (NetworkManager.Singleton.ConnectedClientsIds.Count >= capacity)
        {
            response.Approved = false;
            response.Reason = "O servidor está cheio (" + capacity + " jogadores).";
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

    [Header("--- STAYING CONNECTED ---")]
    [Tooltip("How long a silent connection is tolerated before netcode drops it, in seconds. The " +
             "scene default is 30s; a phone that locks its screen stops sending anything at all, " +
             "so a short timeout ends the match while the player is still in it.")]
    public float disconnectTimeoutSeconds = 120f;

    [Tooltip("Keep the phone's screen awake while the app is running. A locked screen suspends the " +
             "app, which is the actual cause of 'my phone turned off and I got disconnected' — no " +
             "timeout setting can help once the OS has stopped running the game.")]
    public bool keepScreenAwake = true;

    private void ConfigureTransport(string address, string listenAddress, ushort port = 0)
    {
        UnityTransport transport = NetworkManager.Singleton.GetComponent<UnityTransport>();

        if (transport == null)
        {
            Debug.LogWarning("NetworkBootstrap: NetworkManager has no UnityTransport component.");
            return;
        }

        transport.SetConnectionData(address, port == 0 ? connectPort : port, listenAddress);

        // Widened from the scene's 30s. Backgrounding a phone for a few seconds — a notification
        // shade, a glance at another app — stops the game loop entirely, so nothing is sent and the
        // other end starts counting. A longer window lets the session survive that instead of
        // ending a match the player never left.
        if (disconnectTimeoutSeconds > 0f)
            transport.DisconnectTimeoutMS = Mathf.RoundToInt(disconnectTimeoutSeconds * 1000f);
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
            ReportStatus(count + (count == 1 ? " jogador conectado" : " jogadores conectados") + ". Hospedando em "
                         + GetLocalIPv4() + ":" + connectPort);
            return;
        }

        if (clientId != NetworkManager.Singleton.LocalClientId)
            return;

        IsConnecting = false;
        ReportStatus("Conectado.");
        SessionStarted?.Invoke();
    }

    private void HandleClientDisconnected(ulong clientId)
    {
        if (NetworkManager.Singleton.IsServer && clientId != NetworkManager.Singleton.LocalClientId)
        {
            int count = NetworkManager.Singleton.ConnectedClientsIds.Count;
            ReportStatus("Um jogador saiu. " + count + " conectados.");
            return;
        }

        // Reaching here as a client means either the connection attempt was refused or an
        // established session dropped — IsConnecting tells the two apart, which matters because
        // "could not reach the host" and "the host closed the game" need different advice.
        string reason = NetworkManager.Singleton.DisconnectReason;

        if (string.IsNullOrEmpty(reason))
        {
            reason = IsConnecting
                ? "Não foi possível encontrar o jogo. Confira o endereço e se os dois aparelhos estão no mesmo Wi-Fi."
                : "Você foi desconectado.";
        }

        IsConnecting = false;
        ReportStatus(reason);
        SessionEnded?.Invoke(reason);

        // The loop is still running and will try again on its own; this only makes sure the
        // session is fully shut down first, since netcode will not start a new client while the
        // old one is half up.
        if (wantsServerConnection && NetworkManager.Singleton.IsListening)
            NetworkManager.Singleton.Shutdown();
    }

    private void ReportStatus(string message)
    {
        StatusChanged?.Invoke(message);

        // Also to the console. The status label lives on the Connect page, which the Editor does
        // not open by default and a phone hides the moment it connects — so without this the one
        // message that says whether the session actually came up is invisible in both places.
        Debug.Log("Network: " + message);
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
