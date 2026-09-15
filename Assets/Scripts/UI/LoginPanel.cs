using System;
using TMPro;
using Unity.Services.Authentication;
using Unity.Services.Core;
using UnityEngine;
using UnityEngine.UI;

// "Entrar ou criar conta": username and password through Unity Authentication, opened from the
// Profile page.
//
// Nobody has to log in to play -- the server already gives every phone an account. Logging in is what
// lets a player get that account back on a new phone or after a reinstall. Unity handles the password;
// the phone only hands the server Unity's access token, which the server checks (UnityLoginVerifier)
// before tying the login to the account.
//
// The panel is a real object in the scene, made by Trivia Duel > Setup > Build Login Buttons, so it can
// be moved and restyled in the editor. It starts switched off; Show() switches it on. If nobody ran the
// menu, the Profile page builds the same objects at runtime instead.
public class LoginPanel : MonoBehaviour
{
    [Header("Filled in by Trivia Duel > Setup > Build Login Buttons")]
    public TMP_InputField usernameField;
    public TMP_InputField passwordField;
    public TMP_Text statusText;
    public TMP_Text subtitleText;
    public Button signInButton;
    public Button createAccountButton;
    public Button closeButton;

    private string pendingUsername;
    private bool wired;

    // Where to go when the panel closes, when it was opened from somewhere other than the Profile
    // page -- after the tutorial, or from the More page -- so the player is not left on a page they
    // never chose to open.
    private LobbyPageSwitcher returnSwitcher;
    private LobbyPageSwitcher.LobbyPage returnPage;

    // Opened by the tutorial as it finishes. Skipped for anyone already logged in.
    public static void OfferAfterTutorial()
    {
        if (!string.IsNullOrEmpty(PlayerStats.Username))
            return;

        OpenFor(LobbyPageSwitcher.LobbyPage.Lobby);
    }

    // The panel lives on the Profile page, so that page is shown under it; closing it goes to returnTo.
    public static void OpenFor(LobbyPageSwitcher.LobbyPage returnTo)
    {
        LobbyPageSwitcher switcher = FindAnyObjectByType<LobbyPageSwitcher>(FindObjectsInactive.Include);
        ProfilePageController profile = FindAnyObjectByType<ProfilePageController>(FindObjectsInactive.Include);

        if (switcher == null || profile == null)
            return;

        switcher.ShowProfilePage();

        LoginPanel panel = profile.OpenLogin();

        if (panel != null)
        {
            panel.returnSwitcher = switcher;
            panel.returnPage = returnTo;
        }
    }

    public void Show()
    {
        gameObject.SetActive(true);
        transform.SetAsLastSibling();
        FitSubtitle();
        SetBusy(false, string.Empty);
    }

    // The subtitle wraps onto as many lines as it needs, at its own size, instead of shrinking or
    // being cut off. The box keeps its bottom edge -- the fields are laid out under it -- and grows
    // upward into the gap below the title.
    private void FitSubtitle()
    {
        if (subtitleText == null)
        {
            Transform found = transform.Find("Subtitle");
            subtitleText = found != null ? found.GetComponent<TMP_Text>() : null;
        }

        if (subtitleText == null)
            return;

        TMP_Text text = subtitleText;
        float size = text.enableAutoSizing ? text.fontSizeMax : text.fontSize;
        text.enableAutoSizing = false;
        text.fontSize = size;
        text.textWrappingMode = TextWrappingModes.Normal;
        text.overflowMode = TextOverflowModes.Overflow;

        // The status messages are long sentences too.
        if (statusText != null)
        {
            statusText.enableAutoSizing = false;
            statusText.textWrappingMode = TextWrappingModes.Normal;
            statusText.overflowMode = TextOverflowModes.Overflow;
        }

        RectTransform rect = text.rectTransform;
        float height = rect.sizeDelta.y;
        float bottom = rect.anchoredPosition.y - rect.pivot.y * height;
        float needed = text.GetPreferredValues(text.text, rect.rect.width, 0f).y;

        rect.pivot = new Vector2(rect.pivot.x, 0f);
        rect.sizeDelta = new Vector2(rect.sizeDelta.x, Mathf.Max(height, needed));
        rect.anchoredPosition = new Vector2(rect.anchoredPosition.x, bottom);
    }

    public void Hide()
    {
        gameObject.SetActive(false);

        if (returnSwitcher != null)
        {
            LobbyPageSwitcher switcher = returnSwitcher;
            returnSwitcher = null;
            switcher.ShowPage(returnPage);
        }
    }

    private void OnEnable()
    {
        Wire();
        FitSubtitle();
        PlayerStats.LoginFinished += OnLoginFinished;
    }

    private void OnDisable()
    {
        PlayerStats.LoginFinished -= OnLoginFinished;
        CancelInvoke(nameof(Hide));
    }

    // Listeners are added here rather than saved in the scene, so the buttons can be swapped or
    // restyled freely without losing what they do.
    private void Wire()
    {
        if (wired)
            return;

        wired = true;

        if (signInButton != null) signInButton.onClick.AddListener(() => Submit(false));
        if (createAccountButton != null) createAccountButton.onClick.AddListener(() => Submit(true));
        if (closeButton != null) closeButton.onClick.AddListener(Hide);

        if (passwordField != null)
            passwordField.contentType = TMP_InputField.ContentType.Password;

        if (usernameField != null) MobileInputField.MakeSafeForPhones(usernameField);
        if (passwordField != null) MobileInputField.MakeSafeForPhones(passwordField);
    }

    private async void Submit(bool create)
    {
        string username = usernameField != null ? usernameField.text.Trim() : string.Empty;
        string password = passwordField != null ? passwordField.text : string.Empty;

        if (create && !PasswordIsStrongEnough(password))
        {
            SetStatus("A senha precisa de 8 a 30 caracteres, com letra maiúscula, minúscula, número e símbolo.");
            return;
        }

        SetBusy(true, create ? "Criando conta..." : "Entrando...");

        try
        {
            if (UnityServices.State != ServicesInitializationState.Initialized)
                await UnityServices.InitializeAsync();

            if (AuthenticationService.Instance.IsSignedIn)
                AuthenticationService.Instance.SignOut();

            if (create)
                await AuthenticationService.Instance.SignUpWithUsernamePasswordAsync(username, password);
            else
                await AuthenticationService.Instance.SignInWithUsernamePasswordAsync(username, password);

            if (this == null)
                return;

            pendingUsername = username;

            if (PlayerStats.SendLogin(AuthenticationService.Instance.AccessToken, username))
            {
                SetStatus("Confirmando com o servidor...");
                return;   // stays busy until the server answers
            }

            SetBusy(false, "Sem conexão com o servidor. Tente de novo em instantes.");
        }
        catch (Exception e)
        {
            Debug.LogWarning("LoginPanel: " + e);

            if (this == null)
                return;

            SetBusy(false, Explain(e, create));
        }
    }

    private void OnLoginFinished(bool accepted)
    {
        SetBusy(false, accepted
            ? "Pronto! Você entrou como " + pendingUsername + "."
            : "O servidor não aceitou o login. Tente de novo.");

        if (accepted)
            Invoke(nameof(Hide), 1.5f);
    }

    private static string Explain(Exception e, bool create)
    {
        if (e is ServicesInitializationException || e.Message.Contains("project"))
            return "O login ainda não está configurado (projeto sem Unity Cloud).";

        return create
            ? "Não foi possível criar a conta. Esse nome pode já existir. Use 3 a 20 letras ou números."
            : "Usuário ou senha incorretos.";
    }

    // Unity's rule, checked here first so the player gets a clear reason instead of a failed request.
    private static bool PasswordIsStrongEnough(string password)
    {
        if (password.Length < 8 || password.Length > 30)
            return false;

        bool upper = false, lower = false, digit = false, symbol = false;

        foreach (char c in password)
        {
            if (char.IsUpper(c)) upper = true;
            else if (char.IsLower(c)) lower = true;
            else if (char.IsDigit(c)) digit = true;
            else symbol = true;
        }

        return upper && lower && digit && symbol;
    }

    private void SetStatus(string message)
    {
        if (statusText != null)
            statusText.text = message;
    }

    private void SetBusy(bool busy, string message)
    {
        SetStatus(message);

        foreach (Button button in new[] { signInButton, createAccountButton, closeButton })
            if (button != null)
                button.interactable = !busy;
    }

    // --- building -------------------------------------------------------

    // Makes the panel's objects under a page, switched off. The editor menu uses this so the pieces
    // exist in the scene to be edited by hand; the Profile page uses it at runtime only as a fallback.
    public static LoginPanel CreateUnder(Transform page, TMP_FontAsset font)
    {
        GameObject root = new GameObject("LoginPanel", typeof(RectTransform), typeof(Image));

        // Off before the component is added, so OnEnable cannot run before the fields are filled in.
        root.SetActive(false);

        RectTransform rect = (RectTransform)root.transform;
        rect.SetParent(page, false);
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        rect.SetAsLastSibling();
        root.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.88f);

        LoginPanel panel = root.AddComponent<LoginPanel>();

        // Centred in the space above the bottom bar (about 380 tall), not on the whole page.
        float y = 890f;

        Label(rect, "Title", "Sua conta", 110f, ref y, 200f, font);
        panel.subtitleText = Label(rect, "Subtitle", "Entre para recuperar seu progresso em outro celular.", 50f, ref y, 160f, font);

        panel.usernameField = Field(rect, "UsernameField", "Nome de usuário", false, ref y, font);
        panel.passwordField = Field(rect, "PasswordField", "Senha", true, ref y, font);

        panel.signInButton = MakeButton(rect, "EntrarButton", "Entrar", ref y, font);
        panel.createAccountButton = MakeButton(rect, "CriarContaButton", "Criar conta", ref y, font);
        panel.closeButton = MakeButton(rect, "FecharButton", "Fechar", ref y, font);

        panel.statusText = Label(rect, "Status", string.Empty, 44f, ref y, 260f, font);
        return panel;
    }

    private static RectTransform Row(RectTransform parent, string name, ref float y, float width, float height)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        RectTransform rect = (RectTransform)go.transform;
        rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(width, height);
        rect.anchoredPosition = new Vector2(0f, y - height * 0.5f);
        y -= height + 30f;
        return rect;
    }

    private static TMP_Text Label(RectTransform parent, string name, string text, float size, ref float y,
        float height, TMP_FontAsset font)
    {
        TextMeshProUGUI label = Row(parent, name, ref y, 900f, height).gameObject.AddComponent<TextMeshProUGUI>();

        if (font != null)
            label.font = font;

        label.text = text;
        label.fontSize = size;
        label.alignment = TextAlignmentOptions.Center;
        label.textWrappingMode = TextWrappingModes.Normal;
        label.overflowMode = TextOverflowModes.Overflow;
        label.color = Color.white;
        label.raycastTarget = false;
        return label;
    }

    private static TMP_InputField Field(RectTransform parent, string name, string placeholder, bool password,
        ref float y, TMP_FontAsset font)
    {
        float height = 150f;
        GameObject go = TMP_DefaultControls.CreateInputField(new TMP_DefaultControls.Resources());
        go.name = name;

        RectTransform rect = (RectTransform)go.transform;
        rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(900f, height);
        rect.anchoredPosition = new Vector2(0f, y - height * 0.5f);
        y -= height + 30f;

        TMP_InputField field = go.GetComponent<TMP_InputField>();
        field.contentType = password ? TMP_InputField.ContentType.Password : TMP_InputField.ContentType.Standard;
        field.characterLimit = 30;
        field.pointSize = 56f;

        if (field.placeholder is TMP_Text hint)
            hint.text = placeholder;

        if (font != null)
            field.fontAsset = font;

        return field;
    }

    private static Button MakeButton(RectTransform parent, string name, string text, ref float y, TMP_FontAsset font)
    {
        GameObject go = TMP_DefaultControls.CreateButton(new TMP_DefaultControls.Resources());
        go.name = name;

        Vector2 size = JogarButtonStyle.Size;
        RectTransform rect = (RectTransform)go.transform;
        rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = size;
        rect.anchoredPosition = new Vector2(0f, y - size.y * 0.5f);
        y -= size.y + 30f;

        TMP_Text label = go.GetComponentInChildren<TMP_Text>();
        label.text = text;
        label.fontSize = 60f;

        if (font != null)
            label.font = font;

        Button button = go.GetComponent<Button>();
        JogarButtonStyle.ApplyTo(button);
        return button;
    }
}
