using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// The More page, as settings: the two sound switches, the tutorials, the privacy policy, the account,
// and the credits.
//
// The buttons are real objects in the scene, made by Trivia Duel > Setup > Build Settings Buttons, so
// they can be moved, resized and restyled in the editor. What they do is added here rather than saved
// in the scene. If nobody built them, the same objects are made when the game starts.
public class SettingsPage : MonoBehaviour
{
    [Header("Filled in by Trivia Duel > Setup > Build Settings Buttons")]
    public Button soundButton;
    public Button musicButton;
    public Button tutorialButton;
    public Button teamTutorialButton;
    public Button privacyButton;
    public Button accountButton;
    public Button creditsButton;

    [Tooltip("Switched off until Créditos is tapped.")]
    public GameObject creditsPanel;
    public Button creditsCloseButton;

    [Tooltip("Switched off until Política de privacidade is tapped.")]
    public GameObject privacyPanel;
    public Button privacyCloseButton;

    private const float ButtonGap = 45f;
    private const float ColumnTop = 640f;

    // Deleting an account cannot be undone, so the button on the account panel asks twice first. The
    // labels live here because the panel and its editor setup both use them.
    public static readonly string[] DeleteLabels = { "Deletar conta", "Certeza?", "Absoluta?" };

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

        if (page.MissingAnyButton())
            page.Build(null);
    }

    private void OnEnable()
    {
        Wire();
        Refresh();
        SetCreditsOpen(false);
        SetPrivacyOpen(false);

        PlayerStats.Changed += Refresh;
        PlayerStats.LoginFinished += OnLoginFinished;
        PlayerStats.Deleted += Refresh;
    }

    private void OnDisable()
    {
        PlayerStats.Changed -= Refresh;
        PlayerStats.LoginFinished -= OnLoginFinished;
        PlayerStats.Deleted -= Refresh;
    }

    private void Wire()
    {
        if (wired)
            return;

        wired = true;
        MakeLabelsWhite();

        if (soundButton != null) soundButton.onClick.AddListener(ToggleSound);
        if (musicButton != null) musicButton.onClick.AddListener(ToggleMusic);
        if (tutorialButton != null) tutorialButton.onClick.AddListener(() => FirstRunTutorial.Replay());
        if (teamTutorialButton != null) teamTutorialButton.onClick.AddListener(() => TeamFirstRunTutorial.Replay());
        if (privacyButton != null) privacyButton.onClick.AddListener(() => SetPrivacyOpen(true));
        if (privacyCloseButton != null) privacyCloseButton.onClick.AddListener(() => SetPrivacyOpen(false));
        if (accountButton != null) accountButton.onClick.AddListener(() => LoginPanel.OpenFor(LobbyPageSwitcher.LobbyPage.More));
        if (creditsButton != null) creditsButton.onClick.AddListener(() => SetCreditsOpen(true));
        if (creditsCloseButton != null) creditsCloseButton.onClick.AddListener(() => SetCreditsOpen(false));
    }

    // White labels on every settings button, whatever colour the Jogar style gave them.
    public void MakeLabelsWhite()
    {
        foreach (Button button in AllButtons())
        {
            TMP_Text label = button != null ? button.GetComponentInChildren<TMP_Text>(true) : null;

            if (label != null)
                label.color = Color.white;
        }
    }

    // In the order they stand on the page.
    private Button[] Column() =>
        new[] { soundButton, musicButton, tutorialButton, teamTutorialButton, privacyButton, accountButton, creditsButton };

    private IEnumerable<Button> AllButtons()
    {
        foreach (Button button in Column())
            yield return button;

        yield return creditsCloseButton;
        yield return privacyCloseButton;
    }

    private bool MissingAnyButton()
    {
        foreach (Button button in Column())
            if (button == null)
                return true;

        return privacyPanel == null;
    }

    private void ToggleSound()
    {
        GameSound.SetOn(!GameSound.IsOn);
        Refresh();
    }

    private void ToggleMusic()
    {
        GameMusic.SetOn(!GameMusic.IsOn);
        Refresh();
    }

    private void OnLoginFinished(bool accepted)
    {
        Refresh();
    }

    // Only the labels that say what state things are in; the others keep whatever they were given in
    // the scene.
    private void Refresh()
    {
        SetLabel(soundButton, GameSound.IsOn ? "Efeitos: ligados" : "Efeitos: desligados");
        SetLabel(musicButton, GameMusic.IsOn ? "Música: ligada" : "Música: desligada");
        SetLabel(accountButton, string.IsNullOrEmpty(PlayerStats.Username)
            ? "Entrar ou criar conta"
            : "Conta: " + PlayerStats.Username);
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

    private void SetPrivacyOpen(bool open)
    {
        if (privacyPanel == null)
            return;

        privacyPanel.SetActive(open);

        if (open)
            privacyPanel.transform.SetAsLastSibling();
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

        if (soundButton == null) soundButton = MakeButton(page, "SoundButton", "Efeitos: ligados", font, created);
        if (musicButton == null) musicButton = MakeButton(page, "MusicButton", "Música: ligada", font, created);
        if (tutorialButton == null) tutorialButton = MakeButton(page, "TutorialButton", "Rever tutorial", font, created);
        if (teamTutorialButton == null) teamTutorialButton = MakeButton(page, "TeamTutorialButton", "Rever tutorial 2x2", font, created);
        if (privacyButton == null) privacyButton = MakeButton(page, "PrivacyButton", "Política de privacidade", font, created);
        if (accountButton == null) accountButton = MakeButton(page, "AccountButton", "Entrar ou criar conta", font, created);
        if (creditsButton == null) creditsButton = MakeButton(page, "CreditsButton", "Créditos", font, created);

        if (creditsPanel == null)
            creditsPanel = MakeCredits(page, font, created, out creditsCloseButton);

        if (privacyPanel == null)
            privacyPanel = MakePrivacy(page, font, created, out privacyCloseButton);

        LayOutColumn();
        return created;
    }

    // Puts the buttons in one evenly spaced column, in the order of Column(), whatever order they
    // were made in. Used by the editor setup as well, so a page built before a button existed ends up
    // the same as one built today.
    public void LayOutColumn()
    {
        float top = ColumnTop;

        foreach (Button button in Column())
        {
            if (button == null)
                continue;

            RectTransform rect = (RectTransform)button.transform;
            float height = rect.sizeDelta.y > 1f ? rect.sizeDelta.y : JogarButtonStyle.Size.y;

            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = new Vector2(0f, top - height * 0.5f);
            top -= height + ButtonGap;
        }
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

    // Placed at the top of the column; LayOutColumn moves it to where it belongs once they all exist.
    private static Button MakeButton(RectTransform parent, string name, string text, TMP_FontAsset font,
        List<GameObject> created)
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

        rect.sizeDelta = JogarButtonStyle.Size;
        rect.anchoredPosition = new Vector2(0f, ColumnTop);

        if (created != null)
            created.Add(go);

        return button;
    }

    private static GameObject MakeCredits(RectTransform page, TMP_FontAsset font, List<GameObject> created,
        out Button close)
    {
        GameObject root = FullPagePanel(page, "CreditsPanel");
        RectTransform rect = (RectTransform)root.transform;

        MakeLabel(rect, "Title", "Créditos", 110f, 800f, 200f, font);
        MakeLabel(rect, "Body",
            "Estalo\n\nCriado por Antonio Leite\nProjeto Capstone, 2026\n\nFeito com Unity",
            60f, 260f, 800f, font);

        close = PanelCloseButton(rect, font);
        root.SetActive(false);
        created.Add(root);
        return root;
    }

    // The policy itself, in the app, because a player cannot be asked to go and find it elsewhere --
    // and Google Play asks for it to be reachable from inside the game. It scrolls: it is long.
    private static GameObject MakePrivacy(RectTransform page, TMP_FontAsset font, List<GameObject> created,
        out Button close)
    {
        GameObject root = FullPagePanel(page, "PrivacyPanel");
        RectTransform rect = (RectTransform)root.transform;

        MakeLabel(rect, "Title", "Privacidade", 100f, 980f, 160f, font);

        GameObject viewport = new GameObject("Scroll", typeof(RectTransform), typeof(Image), typeof(Mask), typeof(ScrollRect));
        RectTransform viewportRect = (RectTransform)viewport.transform;
        viewportRect.SetParent(rect, false);
        viewportRect.anchorMin = viewportRect.anchorMax = new Vector2(0.5f, 0.5f);
        viewportRect.sizeDelta = new Vector2(1000f, 1500f);
        viewportRect.anchoredPosition = new Vector2(0f, 130f);
        viewport.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.04f);
        viewport.GetComponent<Mask>().showMaskGraphic = true;

        GameObject content = new GameObject("Text", typeof(RectTransform));
        RectTransform contentRect = (RectTransform)content.transform;
        contentRect.SetParent(viewportRect, false);
        contentRect.anchorMin = new Vector2(0f, 1f);
        contentRect.anchorMax = new Vector2(1f, 1f);
        contentRect.pivot = new Vector2(0.5f, 1f);
        contentRect.offsetMin = new Vector2(24f, 0f);
        contentRect.offsetMax = new Vector2(-24f, 0f);

        TextMeshProUGUI body = content.AddComponent<TextMeshProUGUI>();

        if (font != null)
            body.font = font;

        body.text = PrivacyText;
        body.fontSize = 42f;
        body.alignment = TextAlignmentOptions.TopLeft;
        body.textWrappingMode = TextWrappingModes.Normal;
        body.overflowMode = TextOverflowModes.Overflow;
        body.color = Color.white;
        body.raycastTarget = false;

        ContentSizeFitter fitter = content.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        ScrollRect scroll = viewport.GetComponent<ScrollRect>();
        scroll.content = contentRect;
        scroll.viewport = viewportRect;
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Elastic;
        scroll.scrollSensitivity = 40f;

        close = PanelCloseButton(rect, font);
        root.SetActive(false);
        created.Add(root);
        return root;
    }

    // Short, in plain Portuguese, and the same as Tools/Store/PRIVACY.md says in full.
    private const string PrivacyText =
        "<b>Política de Privacidade do Estalo</b>\nAtualizada em 2 de novembro de 2026.\n\n" +
        "<b>O que o jogo guarda</b>\n" +
        "• Uma chave aleatória criada no primeiro acesso, para reconhecer seu progresso. O servidor " +
        "guarda apenas um resumo criptográfico dela.\n" +
        "• O apelido e o avatar que você escolher, para mostrar quem é quem na partida.\n" +
        "• Sua pontuação, partidas, vitórias e derrotas, para o ranking e a dificuldade.\n" +
        "• Seu nome de usuário, se você criar uma conta, para recuperar o progresso em outro celular.\n" +
        "• Seu endereço IP enquanto você está conectado, porque sem ele não há conexão.\n\n" +
        "<b>O que o jogo não guarda</b>\nLocalização, contatos, fotos, microfone, câmera, " +
        "identificadores de publicidade e histórico de navegação. Sem anúncios, sem venda de dados e " +
        "sem rastreamento de terceiros.\n\n" +
        "<b>Senhas</b>\nA senha é tratada pela Unity Authentication e nunca passa pelo servidor do " +
        "Estalo. O desenvolvedor não tem acesso a nenhuma senha.\n\n" +
        "<b>Idade</b>\nO Estalo é para jogadores de 13 anos ou mais. Nada de pessoal é obrigatório " +
        "para jogar: use um apelido, não seu nome completo.\n\n" +
        "<b>Apagar seus dados</b>\nEm Conta, toque em \"Deletar conta\". A conta e todo o histórico " +
        "saem do servidor na hora, e este celular esquece o que tinha guardado.\n\n" +
        "<b>Contato</b>\nantonio.leite29@avenues.org";

    private static GameObject FullPagePanel(RectTransform page, string name)
    {
        GameObject root = new GameObject(name, typeof(RectTransform), typeof(Image));
        RectTransform rect = (RectTransform)root.transform;
        rect.SetParent(page, false);
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        root.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.88f);
        return root;
    }

    private static Button PanelCloseButton(RectTransform panel, TMP_FontAsset font)
    {
        Button close = MakeButton(panel, "FecharButton", "Fechar", font, null);
        RectTransform rect = (RectTransform)close.transform;
        rect.anchoredPosition = new Vector2(0f, -900f);
        return close;
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
