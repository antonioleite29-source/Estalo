using UnityEngine;

public class LobbyScreenController : MonoBehaviour
{
    public static LobbyScreenController Instance { get; private set; }

    // Was a plain private field with nothing ever assigning it, which made ShowLobby/HideLobby
    // permanent no-ops — the lobby could never appear. Exposed so the scene can wire it.
    [Tooltip("The root GameObject of the lobby UI. Drag the object holding all four lobby pages here.")]
    [SerializeField] private GameObject root;

    // Leaving Root empty is a valid setup: LobbyPageSwitcher already shows/hides each lobby page
    // individually. Assign it only if there is a lobby container that is NOT an ancestor of the
    // gameplay roots — otherwise hiding the lobby also hides the match UI.
    private void Start()
    {
        if (root == null)
            Debug.Log("LobbyScreenController: 'Root' is empty — lobby visibility is handled by LobbyPageSwitcher instead.", this);
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    public void ShowLobby()
    {
        if (root != null)
            root.SetActive(true);
    }

    public void HideLobby()
    {
        if (root != null)
            root.SetActive(false);
    }
}
