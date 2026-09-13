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

    [Tooltip("The player's stats. Leave empty and one is built under the name field when the page opens.")]
    public TMP_Text statsText;

    private void OnEnable()
    {
        RefreshFromProfile();
        SetDropdownOpen(false);

        EnsureStatsText();
        RefreshStats();
        PlayerStats.Changed += RefreshStats;

        // Asks the server for a fresh leaderboard position; the answer lands through Changed.
        PlayerSideIdentity.SubmitLocalToLeaderboard();
    }

    private void OnDisable()
    {
        PlayerStats.Changed -= RefreshStats;
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

    // --- stats ----------------------------------------------------------

    // Built in code when nothing is wired, so the stats appear without anyone having to edit the
    // scene. Placed in the empty middle of the page -- below the name field and the avatar row,
    // above the navigation bar -- in the page's authored 1179 x 2556 units.
    private void EnsureStatsText()
    {
        if (statsText != null)
            return;

        GameObject go = new GameObject("StatsText", typeof(RectTransform));
        RectTransform rect = (RectTransform)go.transform;
        rect.SetParent(transform, false);
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.anchoredPosition = new Vector2(0f, 420f);
        rect.sizeDelta = new Vector2(980f, 1000f);

        // Just after the page heading, so the avatar dropdown still opens over the top of it.
        rect.SetSiblingIndex(Mathf.Min(1, transform.childCount - 1));

        TextMeshProUGUI text = go.AddComponent<TextMeshProUGUI>();

        // The page heading's font, so the stats look like part of this page.
        TMP_Text heading = GetComponentInChildren<TMP_Text>(true);

        if (heading != null && heading != text && heading.font != null)
            text.font = heading.font;

        text.alignment = TextAlignmentOptions.TopLeft;
        text.color = Color.white;
        text.enableAutoSizing = true;
        text.fontSizeMin = 30f;
        text.fontSizeMax = 64f;
        text.lineSpacing = 18f;
        text.raycastTarget = false;

        statsText = text;
    }

    private void RefreshStats()
    {
        if (statsText == null)
            return;

        string rank = PlayerStats.LeaderboardRank > 0
            ? $"#{PlayerStats.LeaderboardRank} de {PlayerStats.LeaderboardSize}"
            : "sem conexão";

        statsText.text =
            $"<b>Partidas jogadas:</b> {PlayerStats.MatchesPlayed}\n" +
            $"<b>Vitórias:</b> {PlayerStats.MatchesWon}\n" +
            $"<b>Derrotas:</b> {PlayerStats.MatchesLost}\n" +
            // Two decimals, the same as the IQ bar in the lobby, so the two screens agree.
            $"<b>Pontos máximos:</b> {PlayerStats.BestIQ:F2}\n" +
            $"<b>Pontos atuais:</b> {PlayerStats.CurrentIQ:F2}\n" +
            $"<b>Jogando há:</b> {PlayerStats.TimePlayingText()}\n" +
            $"<b>Ranking global:</b> {rank}";
    }

    // --- avatars --------------------------------------------------------

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
