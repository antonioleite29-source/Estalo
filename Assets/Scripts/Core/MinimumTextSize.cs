using TMPro;
using UnityEngine;

// No text in the game is ever drawn smaller than 70 (in the 1179 x 2556 layout units).
//
// Applied at runtime to every TextMeshPro label, including the ones built in code after the scene
// loads -- lessons, the login panel, the Profile stats, the learning path -- so nothing has to be
// edited in the scene for the rule to hold. A fixed size under 70 is raised to 70; an auto-sized
// label may still shrink to fit, but never past 70.
//
// A label that no longer fits at 70 spills past its box rather than being cut off, so a question or
// an answer is never silently missing a word; that is the place to widen the box in the scene.
public class MinimumTextSize : MonoBehaviour
{
    public const float Smallest = 70f;

    // Labels made in code get picked up within this long of appearing.
    private const float ScanInterval = 0.1f;

    private float nextScan;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Install()
    {
        GameObject go = new GameObject(nameof(MinimumTextSize));
        go.hideFlags = HideFlags.HideInHierarchy;
        DontDestroyOnLoad(go);
        go.AddComponent<MinimumTextSize>().RaiseAll();
    }

    private void OnEnable()
    {
        // Just before the canvases draw, so a label made this frame is fixed before anyone sees it.
        Canvas.willRenderCanvases += OnWillRenderCanvases;
    }

    private void OnDisable()
    {
        Canvas.willRenderCanvases -= OnWillRenderCanvases;
    }

    private void OnWillRenderCanvases()
    {
        if (Time.unscaledTime < nextScan)
            return;

        nextScan = Time.unscaledTime + ScanInterval;
        RaiseAll();
    }

    private void RaiseAll()
    {
        foreach (TMP_Text text in FindObjectsByType<TMP_Text>(FindObjectsInactive.Include))
            Raise(text);
    }

    public static void Raise(TMP_Text text)
    {
        if (text == null)
            return;

        bool changed = false;

        if (text.enableAutoSizing)
        {
            if (text.fontSizeMin < Smallest) { text.fontSizeMin = Smallest; changed = true; }
            if (text.fontSizeMax < Smallest) { text.fontSizeMax = Smallest; changed = true; }
        }
        else if (text.fontSize < Smallest)
        {
            text.fontSize = Smallest;
            changed = true;
        }

        if (changed && (text.overflowMode == TextOverflowModes.Truncate || text.overflowMode == TextOverflowModes.Ellipsis))
            text.overflowMode = TextOverflowModes.Overflow;
    }
}
