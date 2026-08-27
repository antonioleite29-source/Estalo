using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;

public class LobbyLearningScroller : MonoBehaviour
{
    [Header("Images")]
    [Tooltip("Drag your sprites here. They scroll in the order shown.")]
    public Sprite[] sourceSprites;

    [Header("Scroll")]
    [Tooltip("How fast momentum fades after releasing a drag (higher = stops faster).")]
    [Range(0.5f, 10f)]
    public float dragFriction = 4f;

    private RectTransform panel;
    private RectTransform[] slotRTs;
    private Image[] slotImgs;
    private float[] slotY;
    private float viewH;
    private float slotH;
    private const int SlotCount = 3;
    private int nextSpriteIndex;
    private readonly List<GameObject> builtSlots = new List<GameObject>();

    private bool isDragging;
    private float lastMouseY;
    private float dragVelocity;

    // Stores recent (time, y) samples to compute release velocity over a window
    private readonly List<(float time, float y)> velHistory = new List<(float, float)>();
    private const float VelWindowSec = 0.1f;

    private void Awake()
    {
        panel = GetComponent<RectTransform>();
        EnsureMask();
    }

    private IEnumerator Start()
    {
        // Wait two frames so Canvas finishes layout before reading rect.height
        yield return null;
        yield return null;
        Build();
    }

    // Start only ever runs once. Opening the Learning tab again after the slots have been lost — a
    // script recompile clears them without re-running Start — used to leave the page permanently
    // blank, so the path is rebuilt whenever the page comes back with nothing on it.
    private void OnEnable()
    {
        if (slotRTs == null || slotRTs.Length == 0)
            StartCoroutine(RebuildNextFrame());
    }

    private IEnumerator RebuildNextFrame()
    {
        yield return null;
        Build();
    }

    private void Build()
    {
        // Only the slots this component made are cleared. It used to destroy every child, which
        // quietly deleted anything else placed on the Learning page — the practice button among
        // them — the moment the path first built itself.
        List<GameObject> doomed = new List<GameObject>(builtSlots);

        for (int i = 0; i < doomed.Count; i++)
            if (doomed[i] != null)
                Destroy(doomed[i]);

        builtSlots.Clear();

        // Children that are not ours are kept, and put back on top once the slots exist: slots are
        // appended, and a later sibling draws over an earlier one, so without this the path art
        // would cover whatever is sitting on the page.
        List<Transform> keep = new List<Transform>();

        for (int i = 0; i < transform.childCount; i++)
        {
            Transform child = transform.GetChild(i);

            // Destroy is deferred to the end of the frame, so slots torn down a moment ago are
            // still children right now. Without this check a rebuild would adopt them as page
            // furniture and raise them back over the new path.
            if (!doomed.Contains(child.gameObject))
                keep.Add(child);
        }

        if (!HasValidSprites())
        {
            RaiseAbovePath(keep);
            return;
        }

        Canvas.ForceUpdateCanvases();
        viewH = panel.rect.height > 0f ? panel.rect.height : Screen.height;
        // Slots overlap the step rather than meeting it exactly, so float drift and per-slot
        // rounding can never open a hairline between two of them. Six pixels was fine at the
        // authored size and is nothing once a 316px-tall image is stretched across a phone, so it
        // scales with the view instead of being a constant.
        slotH = viewH + Mathf.Max(12f, viewH * 0.02f);

        slotRTs = new RectTransform[SlotCount];
        slotImgs = new Image[SlotCount];
        slotY = new float[SlotCount];
        nextSpriteIndex = 0;

        for (int i = 0; i < SlotCount; i++)
        {
            var go = new GameObject("Slot" + i, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(transform, false);

            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(1f, 0f);
            rt.pivot     = new Vector2(0.5f, 0f);
            rt.sizeDelta = new Vector2(0f, slotH);

            slotY[i] = -i * viewH;
            rt.anchoredPosition = new Vector2(0f, Mathf.Floor(slotY[i]));

            var img = go.GetComponent<Image>();
            img.sprite = PickNext();
            img.preserveAspect = false;
            img.color = Color.white;

            slotRTs[i] = rt;
            slotImgs[i] = img;
            builtSlots.Add(go);
        }

        RaiseAbovePath(keep);
    }

    private static void RaiseAbovePath(List<Transform> keep)
    {
        for (int i = 0; i < keep.Count; i++)
            if (keep[i] != null)
                keep[i].SetAsLastSibling();
    }

    private void Update()
    {
        if (slotRTs == null || slotRTs.Length == 0) return;

        // Pointer, not Mouse. A phone has no mouse at all, so Mouse.current is null there and this
        // returned on the first line every frame -- the page scrolled perfectly in the Editor and
        // not at all on the device, which is the most expensive kind of bug to notice.
        //
        // Pointer.current is whichever device last reported: the mouse in the Editor, the
        // touchscreen on a phone. `press` is the left button on one and a finger on the other, so
        // the drag logic below needs no idea which it is.
        Pointer pointer = Pointer.current;
        if (pointer == null) return;

        float currentMouseY = pointer.position.ReadValue().y;

        if (pointer.press.wasPressedThisFrame)
        {
            isDragging   = true;
            lastMouseY   = currentMouseY;
            dragVelocity = 0f;
            velHistory.Clear();
            velHistory.Add((Time.time, currentMouseY));
        }
        else if (pointer.press.wasReleasedThisFrame)
        {
            isDragging = false;
            // Compute release velocity from the last VelWindowSec of movement
            dragVelocity = ComputeReleaseVelocity(currentMouseY);
        }

        // Nothing moving means nothing to redraw. Writing anchoredPosition dirties the canvas even
        // when the value is unchanged, and this page sits on the same canvas as the whole game — so
        // a still path was forcing a full UI rebuild every frame for no visible reason.
        if (!isDragging && dragVelocity == 0f)
            return;

        float delta;
        if (isDragging)
        {
            // Record position sample, drop samples older than the window
            velHistory.Add((Time.time, currentMouseY));
            while (velHistory.Count > 1 && Time.time - velHistory[0].time > VelWindowSec)
                velHistory.RemoveAt(0);

            delta = currentMouseY - lastMouseY;
            lastMouseY = currentMouseY;
        }
        else
        {
            // Coast: exponential friction until nearly stopped
            dragVelocity *= Mathf.Exp(-dragFriction * Time.deltaTime);
            if (Mathf.Abs(dragVelocity) < 2f) dragVelocity = 0f;
            delta = dragVelocity * Time.deltaTime;
        }

        // Move all slots
        float minY = float.MaxValue;
        float maxY = float.MinValue;
        for (int i = 0; i < SlotCount; i++)
        {
            slotY[i] += delta;
            if (slotY[i] < minY) minY = slotY[i];
            if (slotY[i] > maxY) maxY = slotY[i];
        }

        for (int i = 0; i < SlotCount; i++)
        {
            if (slotY[i] >= viewH)
            {
                slotY[i] = minY - viewH;
                slotImgs[i].sprite = PickNext();
            }
            else if (slotY[i] < -(viewH * (SlotCount - 1)))
            {
                slotY[i] = maxY + viewH;
                slotImgs[i].sprite = PickNext();
            }
            slotRTs[i].anchoredPosition = new Vector2(0f, Mathf.Floor(slotY[i]));
        }
    }

    private float ComputeReleaseVelocity(float currentY)
    {
        // Find the oldest sample still within the window
        int oldest = 0;
        for (int i = 0; i < velHistory.Count; i++)
        {
            if (Time.time - velHistory[i].time <= VelWindowSec) { oldest = i; break; }
        }
        float dt = Time.time - velHistory[oldest].time;
        if (dt < 0.001f) return 0f;
        return (currentY - velHistory[oldest].y) / dt;
    }

    private Sprite PickNext()
    {
        if (sourceSprites == null || sourceSprites.Length == 0) return null;
        int tries = sourceSprites.Length;
        while (tries-- > 0)
        {
            Sprite s = sourceSprites[nextSpriteIndex % sourceSprites.Length];
            nextSpriteIndex++;
            if (s != null) return s;
        }
        return null;
    }

    private bool HasValidSprites()
    {
        if (sourceSprites == null) return false;
        foreach (var s in sourceSprites)
            if (s != null) return true;
        return false;
    }

    private void EnsureMask()
    {
        // Whether the Image was already here decides whether the mask may hide it. A Mask draws
        // nothing when showMaskGraphic is false — and on this object the masking graphic is the
        // Learning page's own background art, so hiding it switched the page's artwork off the
        // moment the player opened the tab.
        bool hadOwnGraphic = GetComponent<Image>() != null;

        if (!hadOwnGraphic)
            gameObject.AddComponent<Image>().color = Color.black;

        if (GetComponent<Mask>() == null)
        {
            // Keep showing a background that belongs to the page; hide one we invented purely to
            // give the mask a shape to clip against.
            gameObject.AddComponent<Mask>().showMaskGraphic = hadOwnGraphic;
        }
    }

    private void OnRectTransformDimensionsChange()
    {
        if (slotRTs == null) return;
        float h = panel.rect.height;
        if (h > 0f && !Mathf.Approximately(h, viewH))
            StartCoroutine(RebuildNextFrame());
    }
}
