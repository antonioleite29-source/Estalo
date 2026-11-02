using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// The dropdown behind the mode button: the four rule sets, one tap each, the chosen one lit.
//
// Built in code under the lobby page, the way the avatar row is, and switched off until the mode
// button is tapped. Nothing here decides anything -- it writes the player's choice to
// GameRuleSetChoice, which is what travels with their next Start.
public class RuleSetPanel : MonoBehaviour
{
    private readonly List<Button> options = new List<Button>();
    private readonly List<TMP_Text> titles = new List<TMP_Text>();

    private static readonly Color Chosen = new Color32(34, 184, 207, 255);
    private static readonly Color NotChosen = Color.white;

    public static RuleSetPanel CreateUnder(Transform page, TMP_FontAsset font)
    {
        GameObject root = new GameObject("RuleSetPanel", typeof(RectTransform), typeof(Image));
        root.SetActive(false);

        RectTransform rect = (RectTransform)root.transform;
        rect.SetParent(page, false);
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        rect.SetAsLastSibling();
        root.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.88f);

        RuleSetPanel panel = root.AddComponent<RuleSetPanel>();
        panel.Build(rect, font);
        return panel;
    }

    private void Build(RectTransform rect, TMP_FontAsset font)
    {
        Label(rect, "Title", "Modo de jogo", 100f, 900f, 160f, font, TextAlignmentOptions.Center);

        float top = 640f;
        const float height = 230f;
        const float gap = 30f;

        foreach (GameRuleSet set in GameRuleSets.All)
        {
            GameRuleSet chosen = set;

            GameObject go = TMP_DefaultControls.CreateButton(new TMP_DefaultControls.Resources());
            go.name = set + "Option";

            RectTransform button = (RectTransform)go.transform;
            button.SetParent(rect, false);
            button.anchorMin = button.anchorMax = new Vector2(0.5f, 0.5f);
            button.sizeDelta = new Vector2(980f, height);
            button.anchoredPosition = new Vector2(0f, top - height * 0.5f);
            top -= height + gap;

            // The button's own label becomes the name; the one-liner goes under it.
            TMP_Text name = go.GetComponentInChildren<TMP_Text>();
            name.text = GameRuleSets.NameOf(set);
            name.fontSize = 62f;
            name.alignment = TextAlignmentOptions.Top;
            name.margin = new Vector4(24f, 22f, 24f, 0f);

            if (font != null)
                name.font = font;

            Label(button, "Description", GameRuleSets.DescriptionOf(set), 40f, -52f, 120f, font,
                TextAlignmentOptions.Top);

            Button clickable = go.GetComponent<Button>();
            JogarButtonStyle.ApplyTo(clickable);
            button.sizeDelta = new Vector2(980f, height);
            clickable.onClick.AddListener(() => Choose(chosen));

            options.Add(clickable);
            titles.Add(name);
        }

        Button close = TMP_DefaultControls.CreateButton(new TMP_DefaultControls.Resources()).GetComponent<Button>();
        close.gameObject.name = "FecharButton";
        RectTransform closeRect = (RectTransform)close.transform;
        closeRect.SetParent(rect, false);
        closeRect.anchorMin = closeRect.anchorMax = new Vector2(0.5f, 0.5f);
        JogarButtonStyle.ApplyTo(close);
        closeRect.sizeDelta = new Vector2(500f, 140f);
        closeRect.anchoredPosition = new Vector2(0f, top - 110f);

        TMP_Text closeLabel = close.GetComponentInChildren<TMP_Text>();
        closeLabel.text = "Fechar";
        closeLabel.fontSize = 56f;
        closeLabel.color = Color.white;

        if (font != null)
            closeLabel.font = font;

        close.onClick.AddListener(Close);
    }

    public void Open()
    {
        gameObject.SetActive(true);
        transform.SetAsLastSibling();
        RefreshHighlight();
    }

    public void Close() => gameObject.SetActive(false);

    private void OnEnable() => RefreshHighlight();

    private void Choose(GameRuleSet set)
    {
        GameRuleSetChoice.Chosen = set;
        RefreshHighlight();
        Close();
    }

    private void RefreshHighlight()
    {
        for (int i = 0; i < options.Count && i < GameRuleSets.All.Length; i++)
        {
            bool isChosen = GameRuleSets.All[i] == GameRuleSetChoice.Chosen;
            Color tint = isChosen ? Chosen : NotChosen;

            Image art = options[i] != null ? options[i].GetComponent<Image>() : null;

            if (art != null)
                art.color = tint;

            if (titles[i] != null)
                titles[i].color = tint;
        }
    }

    private static TMP_Text Label(RectTransform parent, string name, string text, float size, float centreY,
        float height, TMP_FontAsset font, TextAlignmentOptions alignment)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        RectTransform rect = (RectTransform)go.transform;
        rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(900f, height);
        rect.anchoredPosition = new Vector2(0f, centreY);

        TextMeshProUGUI label = go.AddComponent<TextMeshProUGUI>();

        if (font != null)
            label.font = font;

        label.text = text;
        label.fontSize = size;
        label.alignment = alignment;
        label.textWrappingMode = TextWrappingModes.Normal;
        label.overflowMode = TextOverflowModes.Overflow;
        label.color = Color.white;
        label.raycastTarget = false;
        return label;
    }
}
