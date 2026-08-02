using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class AnswerButtonVisual : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler
{
    private enum VisualState
    {
        LockedPressed,
        Normal,
        Highlighted,
        PointerPressed,
        PressedRight,
        PressedWrong,
        Disabled
    }

    [Header("--- REQUIRED REFERENCES ---")]
    [Tooltip("The Button component on this answer button. Usually on the same GameObject — click the cog icon and hit Reset to auto-fill.")]
    public Button button;

    [Tooltip("The Image component used to display the button's sprite. Usually on the same GameObject.")]
    public Image targetImage;

    [Tooltip("The text label inside the button that shows the answer text.")]
    public TMP_Text labelText;

    [Tooltip("The RectTransform of this button (controls its size). Usually on the same GameObject.")]
    public RectTransform targetRectTransform;

    [Header("--- APPEARANCE ---")]
    [Tooltip("The theme that controls which sprite is shown for each button state (normal, hovered, correct, wrong). Drag a ButtonTheme asset here.")]
    public ButtonTheme currentTheme;

    [Space(5)]
    [Tooltip("If ON, the button image keeps its original proportions. If OFF, it stretches to fill the button.")]
    public bool preserveAspect = true;

    [Tooltip("How many extra pixels the button grows when it is in its normal (not pressed) state. Makes it look slightly raised.")]
    public float notPressedExtraSize = 16f;

    [Tooltip("How many extra pixels the button grows when it is pressed. Slightly smaller than the normal size to give a 'click' feel.")]
    public float pressedExtraSize = 8f;

    private Vector2 baseSize;
    private bool pointerInside;
    private VisualState currentState = VisualState.LockedPressed;

    private void Reset()
    {
        button = GetComponent<Button>();
        targetImage = GetComponent<Image>();
        targetRectTransform = GetComponent<RectTransform>();

        if (labelText == null)
            labelText = GetComponentInChildren<TMP_Text>(true);
    }

    private void Awake()
    {
        if (button == null) button = GetComponent<Button>();
        if (targetImage == null) targetImage = GetComponent<Image>();
        if (targetRectTransform == null) targetRectTransform = GetComponent<RectTransform>();
        if (labelText == null) labelText = GetComponentInChildren<TMP_Text>(true);

        if (button != null)
            button.transition = Selectable.Transition.None;

        if (targetImage != null)
            targetImage.preserveAspect = preserveAspect;

        if (targetRectTransform != null)
            baseSize = targetRectTransform.sizeDelta;

        ApplyCurrentVisual();
    }

    public void ApplyTheme(ButtonTheme theme)
    {
        currentTheme = theme;
        ApplyCurrentVisual();
    }

    public void SetLabel(string text)
    {
        if (labelText != null)
            labelText.text = text;
    }

    public void SetLockedPressedState() { SetState(VisualState.LockedPressed, false); }
    public void SetAvailableState()     { SetState(VisualState.Normal, true); }
    public void SetPressedRightState()  { SetState(VisualState.PressedRight, false); }
    public void SetPressedWrongState()  { SetState(VisualState.PressedWrong, false); }
    public void SetDisabledState()      { SetState(VisualState.Disabled, false); }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (!CanReactToPointer()) return;
        pointerInside = true;
        if (currentState == VisualState.Normal)
            SetState(VisualState.Highlighted, true);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (!CanReactToPointer()) return;
        pointerInside = false;
        if (currentState == VisualState.Highlighted || currentState == VisualState.PointerPressed)
            SetState(VisualState.Normal, true);
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        if (!CanReactToPointer()) return;
        SetState(VisualState.PointerPressed, true);
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        if (!CanReactToPointer()) return;
        SetState(pointerInside ? VisualState.Highlighted : VisualState.Normal, true);
    }

    private bool CanReactToPointer()
    {
        return button != null &&
               button.interactable &&
               currentState != VisualState.PressedRight &&
               currentState != VisualState.PressedWrong;
    }

    private void SetState(VisualState state, bool interactable)
    {
        currentState = state;

        if (button != null)
            button.interactable = interactable;

        ApplyCurrentVisual();
    }

    private void ApplyCurrentVisual()
    {
        if (targetImage == null)
            return;

        Sprite sprite = GetSpriteForState();
        bool showText = ShouldShowText();
        bool useNotPressedSize = currentState == VisualState.Normal || currentState == VisualState.Highlighted;

        if (sprite != null)
            targetImage.sprite = sprite;

        if (labelText != null)
            labelText.gameObject.SetActive(showText);

        if (targetRectTransform != null)
        {
            float extraSize = useNotPressedSize ? notPressedExtraSize : pressedExtraSize;
            targetRectTransform.sizeDelta = baseSize + new Vector2(extraSize, extraSize);
        }
    }

    private Sprite GetSpriteForState()
    {
        if (currentTheme == null)
            return targetImage != null ? targetImage.sprite : null;

        switch (currentState)
        {
            case VisualState.Highlighted:
                return currentTheme.highlightedSprite != null ? currentTheme.highlightedSprite : currentTheme.normalSprite;

            case VisualState.PointerPressed:
            case VisualState.LockedPressed:
            case VisualState.Disabled:
                return currentTheme.pressedSprite != null ? currentTheme.pressedSprite : currentTheme.normalSprite;

            case VisualState.PressedRight:
                return currentTheme.pressedRightSprite != null ? currentTheme.pressedRightSprite : currentTheme.pressedSprite;

            case VisualState.PressedWrong:
                return currentTheme.pressedWrongSprite != null ? currentTheme.pressedWrongSprite : currentTheme.pressedSprite;

            case VisualState.Normal:
            default:
                return currentTheme.normalSprite;
        }
    }

    private bool ShouldShowText()
    {
        switch (currentState)
        {
            case VisualState.Normal:
            case VisualState.Highlighted:
                return true;
            default:
                return false;
        }
    }
}
