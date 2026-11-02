using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Makes a button look like the lobby's "Jogar" button: its artwork, its tint and press behaviour, and
// its label's font and colour.
//
// Copied from the real button at runtime rather than pointing at the image file, so restyling "Jogar"
// in the scene restyles every button that follows it, with nothing else to update.
public static class JogarButtonStyle
{
    private const string TemplateLabel = "Jogar";

    private static Button template;

    // The Jogar button's size in canvas units, for laying out rows before a button is made. Falls back
    // to a sensible button size when there is no Jogar to measure.
    public static Vector2 Size
    {
        get
        {
            Button source = FindTemplate();
            RectTransform rect = source != null ? (RectTransform)source.transform : null;

            if (rect == null || rect.rect.width <= 0f || rect.rect.height <= 0f)
                return new Vector2(760f, 150f);

            return Vector2.Scale(rect.rect.size, (Vector2)rect.localScale);
        }
    }

    public static void ApplyTo(Button button)
    {
        Button source = FindTemplate();

        if (source == null || button == null || source == button)
            return;

        // Same dimensions too, centred on wherever the button was placed.
        RectTransform target = (RectTransform)button.transform;
        Vector2 centre = target.anchorMin + (target.anchorMax - target.anchorMin) * 0.5f;
        target.anchorMin = target.anchorMax = centre;
        target.sizeDelta = Size;
        target.localScale = Vector3.one;

        Image from = source.GetComponent<Image>();
        Image to = button.GetComponent<Image>();

        if (from != null && to != null)
        {
            to.sprite = from.sprite;
            to.type = from.type;
            to.color = from.color;
            to.preserveAspect = from.preserveAspect;
        }

        button.transition = source.transition;
        button.colors = source.colors;
        button.spriteState = source.spriteState;

        TMP_Text fromLabel = source.GetComponentInChildren<TMP_Text>(true);
        TMP_Text toLabel = button.GetComponentInChildren<TMP_Text>(true);

        if (fromLabel == null || toLabel == null)
            return;

        toLabel.font = fromLabel.font;
        toLabel.fontSharedMaterial = fromLabel.fontSharedMaterial;
        toLabel.color = fromLabel.color;
        toLabel.fontStyle = fromLabel.fontStyle;

        // "Jogar" is one short word at a large size; "Praticar meus erros" is not. Same size as a
        // ceiling, shrinking to fit whatever the label says.
        toLabel.enableAutoSizing = true;
        toLabel.fontSizeMax = fromLabel.fontSize;
        toLabel.fontSizeMin = fromLabel.fontSize * 0.35f;
    }

    // Found by its label, including while the lobby page is switched off.
    // The real "Jogar" button, for anything that needs to sit next to it rather than just look
    // like it. Null when the scene has none.
    public static Button Template => FindTemplate();

    private static Button FindTemplate()
    {
        if (template != null)
            return template;

        foreach (Button candidate in Object.FindObjectsByType<Button>(FindObjectsInactive.Include))
        {
            TMP_Text label = candidate.GetComponentInChildren<TMP_Text>(true);

            if (label != null && label.text.Trim().Equals(TemplateLabel, System.StringComparison.OrdinalIgnoreCase))
            {
                template = candidate;
                break;
            }
        }

        if (template == null)
            Debug.LogWarning("JogarButtonStyle: no button labelled \"Jogar\" in the scene to copy the look from.");

        return template;
    }
}
