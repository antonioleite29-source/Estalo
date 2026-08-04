using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Drives the Connect page: the player either hosts a game and reads their address out loud, or
// types the host's address and joins. Nothing else in the lobby is reachable until one of those
// succeeds, since every mode needs a session first.
public class ConnectPageController : MonoBehaviour
{
    [Header("--- BUTTONS ---")]
    [Tooltip("Starts hosting on this device. Other players then type this device's address to join.")]
    public Button hostButton;

    [Tooltip("Joins the address typed into the Address Input field.")]
    public Button joinButton;

    [Tooltip("Optional: leaves the current session and comes back to this page.")]
    public Button disconnectButton;

    [Header("--- TEXT ---")]
    [Tooltip("Where the player types the host's address, e.g. 192.168.1.42.")]
    public TMP_InputField addressInput;

    [Tooltip("Shows connection progress and errors. Keep this large — it's the only feedback a tester gets.")]
    public TMP_Text statusText;

    [Tooltip("Shows this device's own address in large text, for other players to type in.")]
    public TMP_Text myAddressText;

    [Header("--- NAVIGATION ---")]
    [Tooltip("Drag the LobbyPageSwitcher here so the lobby opens automatically once connected.")]
    public LobbyPageSwitcher lobbyPageSwitcher;

    [Tooltip("Switch to the lobby page as soon as this device joins a session.")]
    public bool openLobbyOnConnect = true;

    // Testers connect to the same host repeatedly across a session — remembering the last address
    // saves them retyping a full IP on a phone keyboard every single time.
    private const string PREFS_KEY_LAST_ADDRESS = "LastConnectAddress";

    private NetworkBootstrap bootstrap;

    private void Awake()
    {
        MobileInputField.MakeSafeForPhones(addressInput);
        BindButtons();

        // Subscribe here rather than in OnEnable: this page is deactivated the moment we connect
        // and switch to the lobby, so an OnEnable/OnDisable pair would unsubscribe exactly when
        // the SessionEnded we most need to hear about — a mid-game disconnect — arrives.
        Subscribe();
    }

    private void OnEnable()
    {
        RefreshMyAddress();
        RestoreLastAddress();
        SetInteractable(true);
        BeginSearchingForGames();
    }

    private void OnDisable()
    {
        StopSearchingForGames();
    }

    private void OnDestroy()
    {
        Unsubscribe();
        StopSearchingForGames();
    }

    // ---------------------------------------------------------------
    // Finding games on the Wi-Fi
    // ---------------------------------------------------------------

    [Header("--- WI-FI DISCOVERY ---")]
    [Tooltip("Join automatically as soon as a game is found on this Wi-Fi, without the player " +
             "having to type or tap anything. Untick to make them press Join.")]
    public bool autoJoinFirstHostFound = true;

    private bool searching;

    // Searching starts the moment this page appears, so by the time a tester has read the screen
    // the game has usually already been found. Nobody has to know what an IP address is.
    private void BeginSearchingForGames()
    {
        if (searching || Bootstrap == null)
            return;

        // A host has nothing to look for and would only find itself.
        if (Bootstrap.IsInSession)
            return;

        searching = true;
        Bootstrap.Discovery.HostFound += HandleHostFound;
        Bootstrap.Discovery.StartSearching();

        SetStatus("Procurando jogos no Wi-Fi...");
    }

    private void StopSearchingForGames()
    {
        if (!searching || Bootstrap == null)
            return;

        searching = false;
        Bootstrap.Discovery.HostFound -= HandleHostFound;
        Bootstrap.Discovery.StopAll();
    }

    private void HandleHostFound(LanDiscovery.FoundHost host)
    {
        // Ignore ourselves. This page starts searching before NetworkBootstrap has had its Start
        // call, so on the machine that ends up hosting, the search is already running when the
        // host's own broadcasts begin — and without this check it would try to join itself.
        if (Bootstrap == null || Bootstrap.IsInSession || host.Address == NetworkBootstrap.GetLocalIPv4())
        {
            StopSearchingForGames();
            return;
        }

        if (addressInput != null)
            addressInput.text = host.Address;

        SetStatus($"Encontrado: {host.Name}");

        if (!autoJoinFirstHostFound)
            return;

        StopSearchingForGames();
        Bootstrap.StartClientAt(host.Address);
    }

    private void BindButtons()
    {
        if (hostButton != null)
            hostButton.onClick.AddListener(OnHostClicked);

        if (joinButton != null)
            joinButton.onClick.AddListener(OnJoinClicked);

        if (disconnectButton != null)
            disconnectButton.onClick.AddListener(OnDisconnectClicked);
    }

    // NetworkBootstrap lives on a different object whose Awake may not have run when this page is
    // the one shown first, so resolve it lazily rather than caching it in our own Awake.
    private NetworkBootstrap Bootstrap
    {
        get
        {
            if (bootstrap == null)
                bootstrap = NetworkBootstrap.Instance != null
                    ? NetworkBootstrap.Instance
                    : FindAnyObjectByType<NetworkBootstrap>();

            return bootstrap;
        }
    }

    private void Subscribe()
    {
        if (Bootstrap == null)
        {
            SetStatus("No NetworkBootstrap in the scene — drag one onto the NetworkManager object.");
            return;
        }

        Bootstrap.StatusChanged += SetStatus;
        Bootstrap.SessionStarted += HandleSessionStarted;
        Bootstrap.SessionEnded += HandleSessionEnded;
    }

    private void Unsubscribe()
    {
        if (bootstrap == null)
            return;

        bootstrap.StatusChanged -= SetStatus;
        bootstrap.SessionStarted -= HandleSessionStarted;
        bootstrap.SessionEnded -= HandleSessionEnded;
    }

    // ---------------------------------------------------------------
    // Button handlers
    // ---------------------------------------------------------------

    public void OnHostClicked()
    {
        if (Bootstrap == null)
            return;

        SetInteractable(false);
        Bootstrap.StartHost();

        // Hosting either succeeds immediately or not at all (unlike joining, which is asynchronous
        // and reports back through SessionEnded). If the port was busy there's no event coming, so
        // re-enable the buttons here or the player is left staring at a dead screen.
        if (!Bootstrap.IsInSession)
            SetInteractable(true);
    }

    public void OnJoinClicked()
    {
        if (Bootstrap == null)
            return;

        string address = addressInput != null ? addressInput.text.Trim() : string.Empty;

        if (!NetworkBootstrap.IsValidIPv4(address))
        {
            SetStatus("Type the host's address, like 192.168.1.42.");
            return;
        }

        PlayerPrefs.SetString(PREFS_KEY_LAST_ADDRESS, address);
        PlayerPrefs.Save();

        SetInteractable(false);
        Bootstrap.StartClientAt(address);
    }

    public void OnDisconnectClicked()
    {
        Bootstrap?.Disconnect();
    }

    // ---------------------------------------------------------------
    // NetworkBootstrap events
    // ---------------------------------------------------------------

    private void HandleSessionStarted()
    {
        RefreshMyAddress();

        if (openLobbyOnConnect && lobbyPageSwitcher != null)
            lobbyPageSwitcher.ShowLobbyPage();
    }

    private void HandleSessionEnded(string reason)
    {
        SetInteractable(true);

        // A dropped session has to bring the player back here, or they sit on a lobby whose
        // Start button can never do anything.
        if (lobbyPageSwitcher != null)
            lobbyPageSwitcher.ShowConnectPage();
    }

    // ---------------------------------------------------------------
    // Display
    // ---------------------------------------------------------------

    private void SetStatus(string message)
    {
        if (statusText != null)
            statusText.text = message;

        Debug.Log("Connect: " + message);
    }

    private void SetInteractable(bool isInteractable)
    {
        if (hostButton != null)
            hostButton.interactable = isInteractable;

        if (joinButton != null)
            joinButton.interactable = isInteractable;

        if (addressInput != null)
            addressInput.interactable = isInteractable;
    }

    private void RefreshMyAddress()
    {
        if (myAddressText != null)
            myAddressText.text = NetworkBootstrap.GetLocalIPv4();
    }

    private void RestoreLastAddress()
    {
        if (addressInput == null || !string.IsNullOrEmpty(addressInput.text))
            return;

        string saved = PlayerPrefs.GetString(PREFS_KEY_LAST_ADDRESS, string.Empty);

        addressInput.text = !string.IsNullOrEmpty(saved)
            ? saved
            : (Bootstrap != null ? Bootstrap.DefaultConnectAddress : string.Empty);
    }
}
