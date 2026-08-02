using UnityEngine;

[CreateAssetMenu(fileName = "ButtonTheme", menuName = "Trivia/Button Theme")]
public class ButtonTheme : ScriptableObject
{
    [Header("--- BUTTON SPRITES ---")]
    [Tooltip("The sprite shown when the button is in its normal (idle) state — not hovered, not pressed.")]
    public Sprite normalSprite;

    [Tooltip("The sprite shown when the mouse is hovering over the button.")]
    public Sprite highlightedSprite;

    [Tooltip("The sprite shown when the button is being clicked/pressed down.")]
    public Sprite pressedSprite;

    [Tooltip("The sprite shown when this answer was CORRECT. Shows after a player answers right.")]
    public Sprite pressedRightSprite;

    [Tooltip("The sprite shown when this answer was WRONG. Shows after a player answers wrong.")]
    public Sprite pressedWrongSprite;
}
