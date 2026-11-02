using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// The More page, as settings: sound on or off, the account, the tutorial again, and the credits.
//
// The buttons are real objects in the scene, made by Trivia Duel > Setup > Build Settings Buttons, so
// they can be moved, resized and restyled in the editor. What they do is added here rather than saved
// in the scene. If nobody built them, the same objects are made when the game starts.
public class SettingsPage : MonoBehaviour
{
    [Header("Filled in by Trivia Duel > Setup > Build Settings Buttons")]
    public Button soundButton;
    public Button accountButton;
    public Button tutorialButton;
    public Button teamTutorialButton;
    public Button creditsButton;
    public Button deleteAccountButton;

    [Tooltip("Switched off until Créditos is tapped.")]
    public GameObject creditsPanel;
    public Button creditsCloseButton;

    private const float ButtonGap = 45f;

    // Deleting an account cannot be undone, so the button asks twice first (StagedConfirm).
    public static readonly string[] DeleteLabels = { "Deletar conta", "Certeza?", "Absoluta?" };

    private StagedConfirm deleteConfirm;

    private bool wired;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void EnsureOnMorePage()
    {
        if (NetworkBootstrap.IsDedicatedServerBuild)
            return;

        LobbyPageSwitcher switcher = FindAnyObjectByType<LobbyPageSwitcher>(FindObjectsInactive.Include);

        if (switcher == null || switcher.morePage == null)
            return;

        SettingsPage page = switcher.morePage.GetComponent<SettingsPage>();

        if (page == null)
            page = switcher.morePage.AddComponent<SettingsPage>();

        if (page.soundButton == null || page.deleteAccountButton == null)
            page.Build(null);
    }

    private void OnEnable()
    {
        Wire();
        Refresh();
        SetCreditsOpen(false);

        deleteConfirm?.Reset();

        PlayerStats.Changed += Refresh;
        PlayerStats.LoginFinished += OnLoginFinished;
        PlayerStats.Deleted += OnAccountDeleted;
    }

    private void OnDisable()
    {
        PlayerStats.Changed -= Refresh;
        PlayerStats.LoginFinished -= OnLoginFinished;
        PlayerStats.Deleted -= OnAccountDeleted;
    }

    private void Update()
    {
        deleteConfirm?.Tick();
    }

    private void Wire()
    {
        if (wired)
            return;

        wired = true;
        MakeLabelsWhite();

        if (soundButton != null) soundButton.onClick.AddListener(ToggleSound);
        if (accountButton != null) accountButton.onClick.AddListener(() => LoginPanel.OpenFor(LobbyPageSwitcher.LobbyPage.More));
        if (tutorialButton != null) tutorialButton.onClick.AddListener(() => FirstRunTutorial.Replay());
        if (teamTutorialButton != null) teamTutorialButton.onClick.AddListener(() => TeamFirstRunTutorial.Replay());
        if (creditsButton != null) creditsButton.onClick.AddListener(() => SetCreditsOpen(true));
        if (deleteAccountButton != null)
            deleteConfirm = new StagedConfirm(deleteAccountButton, DeleteLabels, DeleteAccount);
        if (creditsCloseButton != null) creditsCloseButton.onClick.AddListener(() => SetCreditsOpen(false));
    }

    // White labels on every settings button, whatever colour the Jogar style gave them.
    public void MakeLabelsWhite()
    {
        foreach (Button button in new[] { soundButton, accountButton, tutorialButton, teamTutorialButton, creditsButton,
                                        deleteAccountButton, creditsCloseButton })
        {
            TMP_Text label = button != null ? button.GetComponentInChildren<TMP_Text>(true) : null;

            if (label != null)
                label.color = Color.white;
        }
    }

    private void ToggleSound()
    {
        GameSound.SetOn(!GameSound.IsOn);
        Refresh();
    }

    private void OnLoginFinished(bool accepted)
    {
        Refresh();
    }

    // Only the two labels that say what state things are in; the others keep whatever they were
    // given in the scene.
    private void Refresh()
    {
        SetLabel(soundButton, GameSound.IsOn ? "Som: ligado" : "Som: desligado");
        SetLabel(accountButton, string.IsNullOrEmpty(PlayerStats.Username)
            ? "Entrar ou criar conta"
            : "Conta: " + PlayerStats.Username);
    }

    // The third tap. Offline the server never hears the ask, and deleting only this phone's copy would
    // leave the record standing, so the player is told rather than left thinking it is gone.
    private void DeleteAccount()
    {
        deleteConfirm.ShowNotice(PlayerStats.RequestDelete() ? "Deletando..." : "Sem conexão");
    }

    private void OnAccountDeleted()
    {
        deleteConfirm?.Reset();
        Refresh();
    }

    private static void SetLabel(Button button, string text)
    {
        TMP_Text label = button != null ? button.GetComponentInChildren<TMP_Text>(true) : null;

        if (label != null)
            label.text = text;
    }

    private void SetCreditsOpen(bool open)
    {
        if (creditsPanel == null)
            return;

        creditsPanel.SetActive(open);

        if (open)
            creditsPanel.transform.SetAsLastSibling();
    }

    // --- building -------------------------------------------------------

    // Makes whatever is missing, under this page, and returns the new top-level objects so the editor
    // can register them for Undo. Laid out in the page's 1179 x 2556 units, under the "Mais" heading.
    public List<GameObject> Build(TMP_FontAsset font)
    {
        List<GameObject> created = new List<GameObject>();

        if (font == null)
        {
            TMP_Text heading = GetComponentInChildren<TMP_Text>(true);

            if (heading != null)
                font = heading.font;
        }

        HideOldConnectControls();

        RectTransform page = (RectTransform)transform;
        float top = 640f;

        soundButton = EnsureButton(page, soundButton, "SoundButton", "Som: ligado", ref top, font, created);
        accountButton = EnsureButton(page, accountButton, "AccountButton", "Entrar ou criar conta", ref top, font, created);
        tutorialButton = EnsureButton(page, tutorialButton, "TutorialButton", "Rever tutorial", ref top, font, created);
        teamTutorialButton = EnsureButton(page, teamTutorialButton, "TeamTutorialButton", "Tutorial 2x2", ref top, font, created);
        creditsButton = EnsureButton(page, creditsButton, "CreditsButton", "Créditos", ref top, font, created);
        deleteAccountButton = EnsureButton(page, deleteAccountButton, "DeleteAccountButton", DeleteLabels[0], ref top, font, created);

        if (creditsPanel == null)
            creditsPanel = MakeCredits(page, font, created, out creditsCloseButton);

        return created;
    }

    // What is left of the old "Jogar com amigos" screen -- its title, the Wi-Fi hint, the IP line --
    // would sit behind the settings. Switched off rather than deleted.
    private void HideOldConnectControls()
    {
        ConnectPageController connect = GetComponentInChildren<ConnectPageController>(true);
        Transform content = connect != null ? connect.transform.Find("Content") : null;

        if (content != null && content.gameObject.activeSelf)
            content.gameObject.SetActive(false);
    }

    // A button that is already in the scene is left exactly where it is; the column carries on below
    // it, so a button added later -- Deletar conta -- lands under the others rather than on top of one.
    private static Button EnsureButton(RectTransform parent, Button existing, string name, string text,
        ref float top, TMP_FontAsset font, List<GameObject> created)
    {
        if (existing == null)
            return MakeButton(parent, name, text, ref top, font, created);

        RectTransform rect = (RectTransform)existing.transform;
        float height = rect.sizeDelta.y > 1f ? rect.sizeDelta.y : JogarButtonStyle.Size.y;
        top = rect.anchoredPosition.y - height * 0.5f - ButtonGap;
        return existing;
    }

    private static Button MakeButton(RectTransform parent, string name, string text, ref float top,
        TMP_FontAsset font, List<GameObject> created)
    {
        GameObject go = TMP_DefaultControls.CreateButton(new TMP_DefaultControls.Resources());
        go.name = name;

        RectTransform rect = (RectTransform)go.transform;
        rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);

        TMP_Text label = go.GetComponentInChildren<TMP_Text>();
        label.text = text;
        label.fontSize = 60f;

        if (font != null)
            label.font = font;

        Button button = go.GetComponent<Button>();
        JogarButtonStyle.ApplyTo(button);
        label.color = Color.white;

        Vector2 size = JogarButtonStyle.Size;
        rect.sizeDelta = size;
        rect.anchoredPosition = new Vector2(0f, top - size.y * 0.5f);
        top -= size.y + ButtonGap;

        if (created != null)
            created.Add(go);

        return button;
    }

    private static GameObject MakeCredits(RectTransform page, TMP_FontAsset font, List<GameObject> created,
        out Button close)
    {
        GameObject root = new GameObject("CreditsPanel", typeof(RectTransform), typeof(Image));
        RectTransform rect = (RectTransform)root.transform;
        rect.SetParent(page, false);
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        root.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.88f);

        MakeLabel(rect, "Title", "Créditos", 110f, 800f, 200f, font);
        MakeLabel(rect, "Body",
            "Estalo\n\nCriado por Antonio Leite\nProjeto Capstone, 2026\n\nFeito com Unity",
            60f, 260f, 800f, font);

        float top = -200f;
        close = MakeButton(rect, "FecharButton", "Fechar", ref top, font, null);

        root.SetActive(false);
        created.Add(root);
        return root;
    }

    private static void MakeLabel(RectTransform parent, string name, string text, float size, float centreY,
        float height, TMP_FontAsset font)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        RectTransform rect = (RectTransform)go.transform;
        rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(980f, height);
        rect.anchoredPosition = new Vector2(0f, centreY);

        TextMeshProUGUI label = go.AddComponent<TextMeshProUGUI>();

        if (font != null)
            label.font = font;

        label.text = text;
        label.fontSize = size;
        label.alignment = TextAlignmentOptions.Center;
        label.textWrappingMode = TextWrappingModes.Normal;
        label.color = Color.white;
        label.raycastTarget = false;
    }
}
