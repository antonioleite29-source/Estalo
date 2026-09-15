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

    [Tooltip("\"Entrar ou criar conta\". Made by Trivia Duel > Setup > Build Login Buttons; built at runtime if empty.")]
    public Button accountButton;

    [Tooltip("The login panel, switched off until the account button is tapped. Made by the same menu.")]
    public LoginPanel loginPanel;

    // The login panel is saved switched on in the scene whenever it was left that way after editing
    // it. Awake runs the moment the page first opens, before anything can ask for the panel, so it
    // never greets whoever opens the Profile page.
    private void Awake()
    {
        if (loginPanel != null && loginPanel.gameObject.activeSelf)
            loginPanel.gameObject.SetActive(false);
    }

    private void OnEnable()
    {
        RefreshFromProfile();
        SetDropdownOpen(false);

        EnsureStatsText();
        RefreshStats();
        PlayerStats.Changed += RefreshStats;

        // Asks the server for a fresh copy of the record and leaderboard position; the answer lands
        // through Changed.
        PlayerStats.RequestRefresh();
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
        SetHighlightOpacity(HighlightOpacity);
    }

    // The selection ring over the chosen avatar, at half strength so the face under it still reads.
    private const float HighlightOpacity = 0.5f;

    private void SetHighlightOpacity(float alpha)
    {
        if (avatarHighlights == null)
            return;

        foreach (GameObject highlight in avatarHighlights)
        {
            if (highlight == null)
                continue;

            foreach (Graphic graphic in highlight.GetComponentsInChildren<Graphic>(true))
            {
                Color color = graphic.color;
                color.a = alpha;
                graphic.color = color;
            }
        }
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
        text.fontSizeMin = TextFitsItsBox.SmallestSize;
        text.fontSizeMax = 90f;
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
            // The heading is part of the same text, so it moves and scales with the box. Sized
            // relative to the lines, so auto-sizing shrinks both together.
            "<size=150%><b>Estatísticas</b></size>\n" +
            $"<b>Partidas jogadas:</b> {PlayerStats.MatchesPlayed}\n" +
            $"<b>Vitórias:</b> {PlayerStats.MatchesWon}\n" +
            $"<b>Derrotas:</b> {PlayerStats.MatchesLost}\n" +
            // Two decimals, the same as the IQ bar in the lobby, so the two screens agree.
            $"<b>Pontos máximos:</b> {PlayerStats.BestIQ:F2}\n" +
            $"<b>Pontos atuais:</b> {PlayerStats.CurrentIQ:F2}\n" +
            $"<b>Jogando há:</b> {PlayerStats.TimePlayingText()}\n" +
            $"<b>Ranking global:</b> {rank}";

        RefreshAccountButton();
    }

    // --- login ----------------------------------------------------------

    private bool accountButtonWired;

    // Under the stats: "Entrar ou criar conta", or who is logged in.
    private void RefreshAccountButton()
    {
        TMP_FontAsset font = statsText != null ? statsText.font : null;

        if (accountButton == null)
            accountButton = CreateAccountButton(transform, font);

        if (!accountButtonWired)
        {
            accountButton.onClick.AddListener(() => OpenLogin());
            accountButtonWired = true;
        }

        TMP_Text label = accountButton.GetComponentInChildren<TMP_Text>(true);

        if (label != null)
            label.text = string.IsNullOrEmpty(PlayerStats.Username)
                ? "Entrar ou criar conta"
                : "Conta: " + PlayerStats.Username;
    }

    public LoginPanel OpenLogin()
    {
        if (loginPanel == null)
            loginPanel = LoginPanel.CreateUnder(transform, statsText != null ? statsText.font : null);

        loginPanel.Show();
        return loginPanel;
    }

    // Used by the editor menu to put the button in the scene, and at runtime only if nobody did.
    public static Button CreateAccountButton(Transform page, TMP_FontAsset font)
    {
        GameObject go = TMP_DefaultControls.CreateButton(new TMP_DefaultControls.Resources());
        go.name = "AccountButton";

        RectTransform rect = (RectTransform)go.transform;
        rect.SetParent(page, false);
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);

        TMP_Text label = go.GetComponentInChildren<TMP_Text>();
        label.text = "Entrar ou criar conta";
        label.fontSize = 56f;

        if (font != null)
            label.font = font;

        Button button = go.GetComponent<Button>();
        JogarButtonStyle.ApplyTo(button);

        // Top edge just under the stats, whatever height the Jogar button gives it.
        rect.anchoredPosition = new Vector2(0f, -620f - JogarButtonStyle.Size.y * 0.5f);
        return button;
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
