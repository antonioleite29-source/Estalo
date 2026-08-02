using UnityEngine;

public class LobbyScreenController : MonoBehaviour
{
    public static LobbyScreenController Instance { get; private set; }

    private GameObject root;

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
