using UnityEngine;
using UnityEngine.UI;

// Stops a button looking stuck on after it has been tapped.
//
// Unity's EventSystem keeps the last thing you clicked *selected*, and a Selectable set to Color
// Tint draws its Selected Color for as long as that lasts. Every button in this scene carries a
// white Normal and a 96% grey Selected, so tapping one leaves it a shade darker than its
// neighbours until something else is tapped.
//
// On the answer buttons that never showed, because AnswerButtonVisual sets transition to None and
// paints its own states. On the bottom navigation bar it showed on every single press, and read as
// "you are on this page" — a highlight nobody wrote, that happens to be almost right, and is wrong
// the moment you leave the page by any other route.
//
// Selected is matched to Normal rather than clearing the selection through the EventSystem: a tap
// still needs to select the button for the click to be delivered at all, and fighting that causes
// stranger problems than it solves. Highlighted and Pressed are untouched, so a press still gives
// feedback while the finger is down.
//
// Runs at startup rather than living in the scene, for the same reason CanvasInputRepair does:
// it then covers a button added tomorrow, one built at runtime, and one on a page nobody thought
// to check.
public static class ButtonSelectionTint
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void MatchSelectedToNormal()
    {
        // Include inactive: most pages in this game start switched off, and their buttons are
        // exactly the ones that cannot be checked by looking at the running game.
        Selectable[] selectables = Object.FindObjectsByType<Selectable>(FindObjectsInactive.Include);

        int adjusted = 0;

        foreach (Selectable selectable in selectables)
        {
            if (selectable == null || selectable.transition != Selectable.Transition.ColorTint)
                continue;

            ColorBlock colors = selectable.colors;

            if (colors.selectedColor == colors.normalColor)
                continue;

            colors.selectedColor = colors.normalColor;

            // Assigning the whole block back, because ColorBlock is a struct — editing the copy
            // above changes nothing until it is put back.
            selectable.colors = colors;
            adjusted++;
        }

        if (adjusted > 0)
            Debug.Log($"ButtonSelectionTint: {adjusted} button(s) will no longer stay tinted after a tap.");
    }
}
