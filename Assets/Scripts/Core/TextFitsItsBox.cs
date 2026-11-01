using System.Collections.Generic;
using TMPro;
using UnityEngine;

// Keeps every label inside the box it was given: a line that is too long wraps onto the next line,
// and if the lines no longer fit the box the text shrinks -- never below 36.
//
// Fifteen labels in the scene, the question among them, were saved as No Wrap. Auto-sizing on its
// own could only shrink one long line until it hit the minimum, and then the end of the question
// was simply cut off.
//
// Auto-sizing here can only ever make text SMALLER: the maximum is pinned to whatever size the
// label was authored at, so anything that already fits is left exactly as it was laid out.
//
// Covers labels built in code after the scene loads too (lessons, login, stats), by looking for new
// ones a few times a second. Each label is set up once, so it can still be changed afterwards.
public class TextFitsItsBox : MonoBehaviour
{
    // Apple's 11pt floor on a phone, in this game's 1179-wide layout units (1pt = 3 units). Below
    // this it stops being readable, and a label that needs less to fit is a layout problem worth
    // seeing rather than hiding. Used by every auto-sizing label in the game.
    public const float SmallestSize = 36f;

    private const float ScanInterval = 0.25f;

    private readonly HashSet<int> done = new HashSet<int>();
    private float nextScan;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Install()
    {
        GameObject go = new GameObject(nameof(TextFitsItsBox));
        go.hideFlags = HideFlags.HideInHierarchy;
        DontDestroyOnLoad(go);

        int fitted = go.AddComponent<TextFitsItsBox>().Scan();
        Debug.Log($"TextFitsItsBox: {fitted} label(s) now wrap and shrink to stay inside their box.");
    }

    private void Update()
    {
        if (Time.unscaledTime < nextScan)
            return;

        nextScan = Time.unscaledTime + ScanInterval;
        Scan();
    }

    private int Scan()
    {
        int fitted = 0;

        // Include inactive: most pages start switched off.
        foreach (TMP_Text text in FindObjectsByType<TMP_Text>(FindObjectsInactive.Include))
        {
            if (text == null || !done.Add(text.GetInstanceID()))
                continue;

            Fit(text);
            fitted++;
        }

        return fitted;
    }

    // The floor in this label's own units. SmallestSize is measured in the layout, but a label can be
    // scaled inside it: the question box is drawn at 4x, so its 36 already looks like 144 on screen.
    // Taking 36 as its minimum made the minimum equal the maximum, and the question could not shrink
    // at all. A label authored smaller than the floor keeps its own size as the floor.
    public static float MinFor(TMP_Text text)
    {
        float scale = 1f;

        // Every scale between the label and the layout root -- the object CanvasStretchFitter sizes,
        // or the Canvas -- whose own scale is the screen fit rather than the design.
        for (Transform t = text.transform; t != null; t = t.parent)
        {
            if (t.GetComponent<CanvasStretchFitter>() != null || t.GetComponent<Canvas>() != null)
                break;

            scale *= Mathf.Abs(t.localScale.y);
        }

        if (scale <= 0.0001f)
            scale = 1f;

        return Mathf.Min(SmallestSize / scale, text.fontSizeMax);
    }

    public static void Fit(TMP_Text text)
    {
        // An input field manages its own text: TMP_InputField drives the size, scrolling and caret
        // together, and wrapping or auto-sizing underneath it puts the caret in the wrong place.
        if (text.GetComponentInParent<TMP_InputField>(true) != null)
            return;

        text.textWrappingMode = TextWrappingModes.Normal;

        if (!text.enableAutoSizing)
        {
            // Read the authored size BEFORE switching auto-sizing on -- once it is on, fontSize is
            // the computed value.
            float authored = text.fontSize;

            text.enableAutoSizing = true;
            text.fontSizeMax = authored;
        }

        // An answer may be one long word -- "Chegaram", "paralelepípedo" -- in a button barely wider
        // than it. Held at the 36 floor, TMP breaks the word in half rather than shrinking it, which
        // reads worse than small text, so answer labels alone may go under the floor.
        float floor = MinFor(text);

        if (text.GetComponentInParent<AnswerButtonVisual>(true) != null)
            floor *= 0.6f;

        text.fontSizeMin = floor;

        // Never cut off: if even 36 on several lines does not fit, the words stay visible past the
        // edge of the box, which shows where the box needs to be bigger.
        if (text.overflowMode == TextOverflowModes.Truncate || text.overflowMode == TextOverflowModes.Ellipsis)
            text.overflowMode = TextOverflowModes.Overflow;
    }
}
