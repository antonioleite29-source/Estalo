using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

// Builds the Connect page (host / join by IP) into the open scene and wires it to the
// LobbyPageSwitcher. Written as a generator rather than assembled by hand in the Inspector so the
// page can be rebuilt from scratch if the scene is ever damaged, and so the wiring is reviewable.
//
// Safe to re-run: it deletes any page it built previously first.
public static class ConnectPageBuilder
{
    private const string PageName = "ConnectPage";

    [MenuItem("Trivia Duel/Setup/Build Connect Page")]
    public static void Build()
    {
        LobbyPageSwitcher switcher = Object.FindAnyObjectByType<LobbyPageSwitcher>(FindObjectsInactive.Include);

        if (switcher == null)
        {
            Debug.LogError("No LobbyPageSwitcher in the open scene. Open ProjectCapstone first.");
            return;
        }

        if (switcher.lobbyPage == null)
        {
            Debug.LogError("LobbyPageSwitcher.lobbyPage is empty — it is used as the template for " +
                           "where the Connect page goes and how big it is. Assign it first.");
            return;
        }

        Transform parent = switcher.lobbyPage.transform.parent;

        // Re-runnable: drop the previously generated page instead of stacking a second one.
        Transform existing = parent.Find(PageName);

        if (existing != null)
            Undo.DestroyObjectImmediate(existing.gameObject);

        // Match the other pages exactly rather than guessing a size — whatever PageArea does to
        // them (stretch, offsets, scale) it will do to this one too.
        GameObject page = new GameObject(PageName, typeof(RectTransform));
        Undo.RegisterCreatedObjectUndo(page, "Build Connect Page");
        page.transform.SetParent(parent, false);
        CopyRect(switcher.lobbyPage.GetComponent<RectTransform>(), page.GetComponent<RectTransform>());

        GameObject content = MakeChild(page.transform, "Content");
        RectTransform contentRect = content.GetComponent<RectTransform>();
        Stretch(contentRect);

        // A layout group rather than fixed positions: the reference resolution is about to change
        // from 800x600 to 1080x1920, and hand-placed elements would all need redoing.
        VerticalLayoutGroup layout = content.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(60, 60, 80, 80);
        layout.spacing = 28f;
        layout.childAlignment = TextAnchor.UpperCenter;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        layout.childControlWidth = true;
        layout.childControlHeight = true;

        MakeLabel(content.transform, "Title", "Jogar com amigos", 64, FontStyles.Bold, 90);
        MakeLabel(content.transform, "Hint",
            "Todos precisam estar no mesmo Wi-Fi.", 34, FontStyles.Normal, 60);

        TMP_Text myAddress = MakeLabel(content.transform, "MyAddressText", "Seu IP: —", 44, FontStyles.Bold, 80);
        Button hostButton = MakeButton(content.transform, "HostButton", "Criar sala", 120);

        MakeLabel(content.transform, "JoinLabel", "ou entre no IP de um amigo:", 34, FontStyles.Normal, 55);

        TMP_InputField addressInput = MakeInputField(content.transform, "AddressInput", "192.168.0.10", 110);
        Button joinButton = MakeButton(content.transform, "JoinButton", "Entrar", 120);
        Button disconnectButton = MakeButton(content.transform, "DisconnectButton", "Desconectar", 100);

        TMP_Text status = MakeLabel(content.transform, "StatusText", "", 34, FontStyles.Normal, 120);

        ConnectPageController controller = page.AddComponent<ConnectPageController>();
        controller.hostButton = hostButton;
        controller.joinButton = joinButton;
        controller.disconnectButton = disconnectButton;
        controller.addressInput = addressInput;
        controller.statusText = status;
        controller.myAddressText = myAddress;
        controller.lobbyPageSwitcher = switcher;

        Undo.RecordObject(switcher, "Wire Connect Page");
        switcher.connectPage = page;
        EditorUtility.SetDirty(switcher);

        page.SetActive(false);

        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(page.scene);
        Selection.activeGameObject = page;

        Debug.Log("Connect page built and wired to LobbyPageSwitcher.connectPage. " +
                  "Save the scene (Cmd+S) to keep it.", page);
    }

    private static void CopyRect(RectTransform from, RectTransform to)
    {
        to.anchorMin = from.anchorMin;
        to.anchorMax = from.anchorMax;
        to.pivot = from.pivot;
        to.anchoredPosition = from.anchoredPosition;
        to.sizeDelta = from.sizeDelta;
        to.localScale = from.localScale;
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    private static GameObject MakeChild(Transform parent, string name)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return go;
    }

    private static TMP_Text MakeLabel(Transform parent, string name, string text, float size,
        FontStyles style, float height)
    {
        GameObject go = MakeChild(parent, name);
        TextMeshProUGUI label = go.AddComponent<TextMeshProUGUI>();
        label.text = text;
        label.fontSize = size;
        label.fontStyle = style;
        label.alignment = TextAlignmentOptions.Center;
        label.color = Color.white;

        // Auto-sizing down only: a long status message ("Falha ao conectar: ...") must stay on
        // screen rather than clipping, but nothing should grow past the size chosen here.
        label.enableAutoSizing = true;
        label.fontSizeMin = size * 0.5f;
        label.fontSizeMax = size;

        go.AddComponent<LayoutElement>().preferredHeight = height;
        return label;
    }

    private static Button MakeButton(Transform parent, string name, string text, float height)
    {
        GameObject go = MakeChild(parent, name);

        Image background = go.AddComponent<Image>();
        background.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
        background.type = Image.Type.Sliced;
        background.color = new Color(0.42f, 0.44f, 0.75f, 1f);

        Button button = go.AddComponent<Button>();
        button.targetGraphic = background;

        GameObject labelGo = MakeChild(go.transform, "Label");
        Stretch(labelGo.GetComponent<RectTransform>());

        TextMeshProUGUI label = labelGo.AddComponent<TextMeshProUGUI>();
        label.text = text;
        label.fontSize = 42;
        label.fontStyle = FontStyles.Bold;
        label.alignment = TextAlignmentOptions.Center;
        label.color = Color.white;

        go.AddComponent<LayoutElement>().preferredHeight = height;
        return button;
    }

    private static TMP_InputField MakeInputField(Transform parent, string name, string placeholder, float height)
    {
        GameObject go = MakeChild(parent, name);

        Image background = go.AddComponent<Image>();
        background.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/InputFieldBackground.psd");
        background.type = Image.Type.Sliced;
        background.color = Color.white;

        GameObject textArea = MakeChild(go.transform, "Text Area");
        RectTransform textAreaRect = textArea.GetComponent<RectTransform>();
        Stretch(textAreaRect);
        textAreaRect.offsetMin = new Vector2(20f, 12f);
        textAreaRect.offsetMax = new Vector2(-20f, -12f);
        textArea.AddComponent<RectMask2D>();

        GameObject placeholderGo = MakeChild(textArea.transform, "Placeholder");
        Stretch(placeholderGo.GetComponent<RectTransform>());
        TextMeshProUGUI placeholderText = placeholderGo.AddComponent<TextMeshProUGUI>();
        placeholderText.text = placeholder;
        placeholderText.fontSize = 40;
        placeholderText.fontStyle = FontStyles.Italic;
        placeholderText.alignment = TextAlignmentOptions.Left;
        placeholderText.color = new Color(0.2f, 0.2f, 0.2f, 0.5f);

        GameObject textGo = MakeChild(textArea.transform, "Text");
        Stretch(textGo.GetComponent<RectTransform>());
        TextMeshProUGUI text = textGo.AddComponent<TextMeshProUGUI>();
        text.text = string.Empty;
        text.fontSize = 40;
        text.alignment = TextAlignmentOptions.Left;
        text.color = Color.black;
        text.richText = false;

        TMP_InputField input = go.AddComponent<TMP_InputField>();
        input.textViewport = textAreaRect;
        input.textComponent = text;
        input.placeholder = placeholderText;
        input.targetGraphic = background;

        // A phone keyboard that offers letters for an IP address is a guaranteed typo. Decimal
        // gives the numeric pad, and the content type still permits the dots.
        input.contentType = TMP_InputField.ContentType.DecimalNumber;
        input.keyboardType = TouchScreenKeyboardType.NumbersAndPunctuation;
        input.characterLimit = 15;
        input.lineType = TMP_InputField.LineType.SingleLine;

        go.AddComponent<LayoutElement>().preferredHeight = height;
        return input;
    }
}
