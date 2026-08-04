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

    public void SetSourceSprites(Sprite[] sprites)
    {
        sourceSprites = sprites;
        StartCoroutine(RebuildNextFrame());
    }

    private IEnumerator RebuildNextFrame()
    {
        yield return null;
        Build();
    }

    private void Build()
    {
        for (int i = transform.childCount - 1; i >= 0; i--)
            Destroy(transform.GetChild(i).gameObject);

        if (!HasValidSprites()) return;

        Canvas.ForceUpdateCanvases();
        viewH = panel.rect.height > 0f ? panel.rect.height : Screen.height;
        // Slots are 6px taller than the step so seams always overlap regardless of float drift
        slotH = viewH + 6f;

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
        }
    }

    private void Update()
    {
        if (slotRTs == null || slotRTs.Length == 0) return;

        var mouse = Mouse.current;
        if (mouse == null) return;

        float currentMouseY = mouse.position.ReadValue().y;

        if (mouse.leftButton.wasPressedThisFrame)
        {
            isDragging   = true;
            lastMouseY   = currentMouseY;
            dragVelocity = 0f;
            velHistory.Clear();
            velHistory.Add((Time.time, currentMouseY));
        }
        else if (mouse.leftButton.wasReleasedThisFrame)
        {
            isDragging = false;
            // Compute release velocity from the last VelWindowSec of movement
            dragVelocity = ComputeReleaseVelocity(currentMouseY);
        }

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
