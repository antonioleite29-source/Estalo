using UnityEngine;

// Stretches a UI root so the design resolution always fills the whole screen exactly, on any
// device, with no bars and nothing cropped.
//
// The trade this makes deliberately: filling a screen whose aspect differs from the reference
// requires scaling X and Y by different amounts, so shapes distort. Going from the 1080x1920
// reference to a 19.5:9 phone stretches roughly 22% more vertically than horizontally — circles
// become ovals and text gets taller. Tick `uniformScale` to give that up in exchange for letterbox
// margins instead.
//
// Put this on each full-screen root under the Canvas (LobbyRoot, the gameplay root, ...), and set
// the Canvas Scaler to Constant Pixel Size with scale factor 1 — otherwise the scaler and this
// component both scale, and the result is the two multiplied together.
[ExecuteAlways]
[RequireComponent(typeof(RectTransform))]
public class CanvasStretchFitter : MonoBehaviour
{
    [Tooltip("The resolution the UI was laid out for. Everything is positioned as if the screen " +
             "were exactly this size, then scaled to the real one.")]
    public Vector2 referenceResolution = new Vector2(1080f, 1920f);

    [Tooltip("Keep shapes undistorted by scaling both axes equally. Fills the screen in one " +
             "direction only, leaving margins in the other.")]
    public bool uniformScale;

    private RectTransform rect;
    private Vector2 lastScreenSize;
    private Vector2 lastReference;
    private bool lastUniform;

    private void OnEnable()
    {
        rect = GetComponent<RectTransform>();
        Apply();
    }

    private void Update()
    {
        Vector2 screenSize = new Vector2(Screen.width, Screen.height);

        // Only touch the transform when something actually changed. This runs every frame on every
        // root, and phones rotate or resize rarely enough that recomputing each frame is waste.
        if (screenSize == lastScreenSize && referenceResolution == lastReference && uniformScale == lastUniform)
            return;

        Apply();
    }

    private void Apply()
    {
        if (rect == null)
            rect = GetComponent<RectTransform>();

        if (referenceResolution.x <= 0f || referenceResolution.y <= 0f)
        {
            Debug.LogError("CanvasStretchFitter: reference resolution must be positive on both axes.", this);
            return;
        }

        // Centre-anchored at exactly the design size, so children keep the positions they were
        // authored at. All the adaptation happens in localScale below.
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = referenceResolution;

        float scaleX = Screen.width / referenceResolution.x;
        float scaleY = Screen.height / referenceResolution.y;

        if (uniformScale)
        {
            float fit = Mathf.Min(scaleX, scaleY);
            scaleX = fit;
            scaleY = fit;
        }

        rect.localScale = new Vector3(scaleX, scaleY, 1f);

        lastScreenSize = new Vector2(Screen.width, Screen.height);
        lastReference = referenceResolution;
        lastUniform = uniformScale;
    }
}
