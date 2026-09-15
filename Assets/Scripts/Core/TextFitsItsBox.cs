using TMPro;
using UnityEngine;

// Keeps every label inside the box it was given.
//
// The question text has done this since the beginning — TriviaDuelManager turns on auto-sizing in
// Awake so a long question shrinks rather than spilling over the board. Nothing else did, so a
// long name, a translated label or a three-digit score simply ran past its edge, and on a phone
// that is where it meets the edge of the screen.
//
// Auto-sizing here can only ever make text SMALLER: the maximum is pinned to whatever size the
// label was authored at, so anything that already fits is left exactly as it was laid out. Only
// the labels that would have overflowed change at all.
public static class TextFitsItsBox
{
    // Below this it stops being readable on a phone, and a label that needs 8pt to fit is a layout
    // problem worth seeing rather than hiding.
    private const float SmallestSize = 8f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Apply()
    {
        // Include inactive: most pages start switched off, and a label that overflows is usually
        // discovered by opening a page for the first time.
        TMP_Text[] texts = Object.FindObjectsByType<TMP_Text>(FindObjectsInactive.Include);

        int fitted = 0;

        foreach (TMP_Text text in texts)
        {
            if (text == null || text.enableAutoSizing)
                continue;

            // An input field manages its own text: TMP_InputField drives the size and the caret
            // position together, and auto-sizing underneath it makes the caret land in the wrong
            // place as soon as the text is long enough to shrink.
            if (text.GetComponentInParent<TMP_InputField>(true) != null)
                continue;

            // Read the authored size BEFORE switching auto-sizing on — once it is on, fontSize is
            // the computed value, and pinning the maximum to that would lock in whatever this
            // frame happened to produce.
            float authored = text.fontSize;

            text.enableAutoSizing = true;
            text.fontSizeMax = authored;
            text.fontSizeMin = SmallestSize;

            // Truncate, not overflow: if a label somehow still does not fit at the minimum, it is
            // cut at the edge of its box rather than drawn across whatever is beside it.
            text.overflowMode = TextOverflowModes.Truncate;

            fitted++;
        }

        if (fitted > 0)
            Debug.Log($"TextFitsItsBox: {fitted} label(s) will now shrink to stay inside their box.");
    }
}
