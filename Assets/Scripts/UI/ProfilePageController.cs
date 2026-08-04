using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

public class ProfilePageController : MonoBehaviour
{
    [Tooltip("The name input field on the Profile page.")]
    public TMP_InputField nameInputField;

    [Tooltip("One Image per avatar slot, in the same order as PlayerProfileManager.availableAvatars.")]
    public Image[] avatarImages;

    [Tooltip("One highlight/selection-ring object per avatar slot, same order as avatarImages. Shown only for the selected avatar.")]
    public GameObject[] avatarHighlights;

    [Tooltip("The always-visible preview button showing your currently selected avatar. Clicking it opens/closes the avatar options.")]
    public Image selectedAvatarPreview;

    [Tooltip("The container holding the avatar option buttons (e.g. AvatarRow). Hidden by default, shown when the preview is clicked.")]
    public GameObject avatarOptionsPanel;

    private void OnEnable()
    {
        RefreshFromProfile();
        SetDropdownOpen(false);
    }

    private void Start()
    {
        MobileInputField.MakeSafeForPhones(nameInputField);
        BindAvatarButtons();
        BindPreviewButton();
    }

    private void RefreshFromProfile()
    {
        if (PlayerProfileManager.Instance == null)
            return;

        if (nameInputField != null)
            nameInputField.text = PlayerProfileManager.Instance.GetLocalName();

        RefreshAvatarImages();
        int selectedIndex = PlayerProfileManager.Instance.GetLocalAvatarIndex();
        RefreshHighlight(selectedIndex);
        RefreshSelectedPreview(selectedIndex);
    }

    private void RefreshAvatarImages()
    {
        if (avatarImages == null || PlayerProfileManager.Instance == null)
            return;

        for (int i = 0; i < avatarImages.Length; i++)
        {
            if (avatarImages[i] != null)
                avatarImages[i].sprite = PlayerProfileManager.Instance.GetAvatarSprite(i);
        }
    }

    private void BindAvatarButtons()
    {
        if (avatarImages == null)
            return;

        for (int i = 0; i < avatarImages.Length; i++)
        {
            int avatarIndex = i;
            Button button = avatarImages[i] != null ? avatarImages[i].GetComponent<Button>() : null;

            if (button == null)
                continue;

            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(() => OnAvatarButtonClicked(avatarIndex));
        }
    }

    public void OnNameEndEdit(string value)
    {
        if (PlayerProfileManager.Instance == null)
            return;

        PlayerProfileManager.Instance.SetLocalName(value);
        PushProfileToNetwork();
    }

    private void OnAvatarButtonClicked(int index)
    {
        if (PlayerProfileManager.Instance == null)
            return;

        PlayerProfileManager.Instance.SetLocalAvatarIndex(index);
        RefreshHighlight(index);
        RefreshSelectedPreview(index);
        SetDropdownOpen(false);
        PushProfileToNetwork();
    }

    private void BindPreviewButton()
    {
        Button previewButton = selectedAvatarPreview != null ? selectedAvatarPreview.GetComponent<Button>() : null;

        if (previewButton == null)
            return;

        previewButton.onClick.RemoveAllListeners();
        previewButton.onClick.AddListener(() => SetDropdownOpen(avatarOptionsPanel != null && !avatarOptionsPanel.activeSelf));
    }

    private void SetDropdownOpen(bool isOpen)
    {
        if (avatarOptionsPanel != null)
            avatarOptionsPanel.SetActive(isOpen);
    }

    private void RefreshSelectedPreview(int selectedIndex)
    {
        if (selectedAvatarPreview != null && PlayerProfileManager.Instance != null)
            selectedAvatarPreview.sprite = PlayerProfileManager.Instance.GetAvatarSprite(selectedIndex);
    }

    // Forwards your fresh profile choice to the network immediately — PlayerSideIdentity only reads
    // it once at connection time, which is normally before you've had a chance to pick anything.
    private void PushProfileToNetwork()
    {
        if (NetworkManager.Singleton == null || NetworkManager.Singleton.LocalClient == null)
            return;

        NetworkObject localPlayerObject = NetworkManager.Singleton.LocalClient.PlayerObject;
        PlayerSideIdentity localIdentity = localPlayerObject != null ? localPlayerObject.GetComponent<PlayerSideIdentity>() : null;
        localIdentity?.PushLocalProfile();
    }

    private void RefreshHighlight(int selectedIndex)
    {
        if (avatarHighlights == null)
            return;

        for (int i = 0; i < avatarHighlights.Length; i++)
        {
            if (avatarHighlights[i] != null)
                avatarHighlights[i].SetActive(i == selectedIndex);
        }
    }
}
