using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

// The learning path: a finite column of lesson nodes running down the winding artwork.
//
// This used to be an endless scroller that recycled three slots forever, which is the right shape
// for decoration and the wrong one for lessons — lesson seven has to be in the same place every
// time you open the page. It is a fixed-height column now, which is also simpler: the images tile
// down it, the nodes sit at known heights, and scrolling is just moving the column and clamping.
//
// Nodes sit in the margin BESIDE the path rather than on it. The ribbon is a quarter of the width,
// so the two margins together are three quarters and the wider of them is never less than a third
// of the screen — there is always room on one side, even where the other side is down to four
// percent.
public class LobbyLearningScroller : MonoBehaviour
{
    [Header("Path artwork")]
    [Tooltip("The path images, tiled down the column in this order.")]
    public Sprite[] sourceSprites;

    [Tooltip("How far each image runs under the next one, in pixels. Only there to stop float " +
             "drift opening a hairline at the join.")]
    [Range(0f, 40f)]
    public float slotOverlap = 8f;

    [Header("Lessons")]
    [Tooltip("Vertical distance between lesson nodes, as a fraction of the visible height. " +
             "0.28 puts about three and a half on screen at once.")]
    [Range(0.15f, 0.6f)]
    public float nodeSpacing = 0.28f;

    [Tooltip("Node diameter, as a fraction of the visible WIDTH.")]
    [Range(0.08f, 0.3f)]
    public float nodeSize = 0.16f;

    [Header("Scroll")]
    [Tooltip("How fast momentum fades after releasing a drag (higher = stops faster).")]
    [Range(0.5f, 10f)]
    public float dragFriction = 4f;

    // The centre of the path as a fraction of the width, sixteen samples down each image, measured
    // from the artwork itself. Baked rather than computed at runtime: reading pixels means the
    // textures must stay CPU-readable, which doubles their memory for a number that never changes.
    private static readonly float[] PathCentre =
    {
        // NewPath1
        0.336f, 0.456f, 0.581f, 0.632f, 0.468f, 0.383f, 0.568f, 0.642f,
        0.664f, 0.620f, 0.502f, 0.351f, 0.498f, 0.700f, 0.583f, 0.468f,
        // NewPath2
        0.341f, 0.285f, 0.319f, 0.542f, 0.400f, 0.329f, 0.353f, 0.593f,
        0.664f, 0.620f, 0.502f, 0.351f, 0.498f, 0.656f, 0.583f, 0.474f,
        // NewPath3
        0.368f, 0.253f, 0.180f, 0.339f, 0.485f, 0.334f, 0.216f, 0.172f,
        0.243f, 0.483f, 0.507f, 0.436f, 0.295f, 0.517f, 0.551f, 0.495f,
        // NewPath4
        0.336f, 0.456f, 0.588f, 0.444f, 0.370f, 0.463f, 0.571f, 0.642f,
        0.664f, 0.620f, 0.502f, 0.466f, 0.654f, 0.774f, 0.656f, 0.517f
    };

    private const int SamplesPerImage = 16;
    private const float PathHalfWidth = 0.126f;   // the ribbon is about a quarter of the width

    private RectTransform panel;
    private RectTransform content;
    private readonly List<GameObject> built = new List<GameObject>();
    private readonly List<LessonNode> nodes = new List<LessonNode>();

    private float viewH;
    private float viewW;
    private float contentH;
    private float scroll;          // 0 at the top, contentH - viewH at the bottom

    private bool isDragging;
    private bool draggedFar;       // a drag this far is a scroll, not a tap on a node
    private float lastPointerY;
    private float dragVelocity;
    private readonly List<(float time, float y)> velHistory = new List<(float, float)>();
    private const float VelWindowSec = 0.1f;
    private const float TapSlop = 12f;

    private sealed class LessonNode
    {
        public int Index;
        public Image Background;
        public TMP_Text Label;
        public Button Button;
        public RectTransform Rect;
    }

    private void Awake()
    {
        panel = GetComponent<RectTransform>();
        EnsureMask();
    }

    private IEnumerator Start()
    {
        // Two frames so the Canvas has finished laying out before rect.height is read.
        yield return null;
        yield return null;
        Build();
    }

    // Start only ever runs once, and a script recompile clears the built objects without re-running
    // it. Rebuilding whenever the page comes back with nothing on it covers that, and refreshes
    // the lock states after a lesson has been finished.
    private void OnEnable()
    {
        if (content == null || built.Count == 0)
            StartCoroutine(RebuildNextFrame());
        else
            RefreshNodes();
    }

    private IEnumerator RebuildNextFrame()
    {
        yield return null;
        Build();
    }

    private void Build()
    {
        foreach (GameObject old in built)
            if (old != null)
                Destroy(old);

        built.Clear();
        nodes.Clear();

        if (sourceSprites == null || sourceSprites.Length == 0)
            return;

        Canvas.ForceUpdateCanvases();
        viewH = panel.rect.height > 0f ? panel.rect.height : Screen.height;
        viewW = panel.rect.width > 0f ? panel.rect.width : Screen.width;

        float spacing = viewH * nodeSpacing;

        // Half a spacing of air at each end, so the first and last nodes are not jammed against
        // the edge of the scroll.
        contentH = Mathf.Max(viewH, (LessonLadder.Count + 1) * spacing);

        content = NewChild("PathContent", transform);
        content.anchorMin = new Vector2(0f, 1f);
        content.anchorMax = new Vector2(1f, 1f);
        content.pivot = new Vector2(0.5f, 1f);
        content.sizeDelta = new Vector2(0f, contentH);
        content.anchoredPosition = Vector2.zero;

        BuildTiles();
        BuildNodes(spacing);

        scroll = 0f;
        ApplyScroll();
    }

    private void BuildTiles()
    {
        int tiles = Mathf.CeilToInt(contentH / viewH) + 1;

        for (int i = 0; i < tiles; i++)
        {
            RectTransform rect = NewChild("Path" + i, content);
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.sizeDelta = new Vector2(0f, viewH + slotOverlap);
            rect.anchoredPosition = new Vector2(0f, -i * viewH);

            Image image = rect.gameObject.AddComponent<Image>();
            image.sprite = sourceSprites[i % sourceSprites.Length];
            image.preserveAspect = false;
            image.color = Color.white;
            image.raycastTarget = false;
        }
    }

    private void BuildNodes(float spacing)
    {
        ButtonTheme theme = TriviaDuelManager.Instance != null ? TriviaDuelManager.Instance.buttonTheme : null;
        float diameter = viewW * nodeSize;

        for (int i = 0; i < LessonLadder.Count; i++)
        {
            // Lesson one at the top, descending — the path flows downward and so does reading.
            float y = -(i + 1f) * spacing;

            RectTransform rect = NewChild("Lesson" + (i + 1), content);
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(diameter, diameter);
            rect.anchoredPosition = new Vector2(MarginXAt(y) * viewW, y);

            Image background = rect.gameObject.AddComponent<Image>();
            background.preserveAspect = true;

            if (theme != null && theme.normalSprite != null)
                background.sprite = theme.normalSprite;

            Button button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = background;
            button.transition = Selectable.Transition.None;

            TMP_Text label = NewChild("Number", rect).gameObject.AddComponent<TextMeshProUGUI>();
            RectTransform labelRect = label.rectTransform;
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;
            label.alignment = TextAlignmentOptions.Center;
            label.enableAutoSizing = true;
            label.fontSizeMin = 10f;
            label.fontSizeMax = diameter * 0.5f;
            label.raycastTarget = false;
            label.text = (i + 1).ToString();

            int index = i;
            button.onClick.AddListener(() => OnNodeTapped(index));

            nodes.Add(new LessonNode
            {
                Index = i, Background = background, Label = label, Button = button, Rect = rect
            });
        }

        RefreshNodes();
    }

    // Which side has room, and where in it. The ribbon is a quarter of the width, so the wider
    // margin is never below about a third — placing the node in the middle of it always clears the
    // path, even where the other side has almost nothing.
    private float MarginXAt(float contentY)
    {
        float centre = PathCentreAt(contentY);

        float leftRoom = centre - PathHalfWidth;
        float rightRoom = 1f - (centre + PathHalfWidth);

        return leftRoom >= rightRoom
            ? leftRoom * 0.5f
            : centre + PathHalfWidth + rightRoom * 0.5f;
    }

    private float PathCentreAt(float contentY)
    {
        if (viewH <= 0f)
            return 0.5f;

        // contentY runs negative downward from the top of the column.
        float rows = -contentY / viewH;
        int image = Mathf.FloorToInt(rows) % sourceSprites.Length;

        if (image < 0)
            image += sourceSprites.Length;

        float within = rows - Mathf.Floor(rows);
        float sample = within * SamplesPerImage - 0.5f;

        int a = Mathf.Clamp(Mathf.FloorToInt(sample), 0, SamplesPerImage - 1);
        int b = Mathf.Clamp(a + 1, 0, SamplesPerImage - 1);
        float t = Mathf.Clamp01(sample - a);

        // The baked curve only covers as many images as were measured; beyond that it repeats.
        int block = (image % (PathCentre.Length / SamplesPerImage)) * SamplesPerImage;

        return Mathf.Lerp(PathCentre[block + a], PathCentre[block + b], t);
    }

    public void RefreshNodes()
    {
        ButtonTheme theme = TriviaDuelManager.Instance != null ? TriviaDuelManager.Instance.buttonTheme : null;

        foreach (LessonNode node in nodes)
        {
            bool unlocked = LessonLadder.IsUnlocked(node.Index);
            bool done = LessonLadder.IsDone(node.Index);

            if (theme != null)
            {
                Sprite sprite = done ? theme.pressedRightSprite
                             : unlocked ? theme.normalSprite
                             : theme.pressedSprite;

                if (sprite != null)
                    node.Background.sprite = sprite;
            }

            // Locked nodes are dimmed rather than hidden: seeing what is coming is most of why a
            // path like this works at all.
            node.Background.color = unlocked ? Color.white : new Color(1f, 1f, 1f, 0.45f);
            node.Label.color = unlocked ? Color.white : new Color(1f, 1f, 1f, 0.5f);
            node.Button.interactable = unlocked;
        }
    }

    private void OnNodeTapped(int index)
    {
        // A drag that happened to end on a node is a scroll, not a tap. Without this every attempt
        // to scroll by grabbing the middle of the screen would open a lesson.
        if (draggedFar || !LessonLadder.IsUnlocked(index))
            return;

        PracticeQuizController practice = FindAnyObjectByType<PracticeQuizController>(FindObjectsInactive.Include);

        if (practice == null)
        {
            Debug.LogError("LobbyLearningScroller: no PracticeQuizController, so a lesson cannot start.");
            return;
        }

        practice.StartLesson(index);
    }

    // --- scrolling ------------------------------------------------------

    private void Update()
    {
        if (content == null)
            return;

        // Pointer, not Mouse: a phone has no mouse, so Mouse.current is null there and this used
        // to return on its first line every frame.
        Pointer pointer = Pointer.current;

        if (pointer == null)
            return;

        float pointerY = pointer.position.ReadValue().y;

        if (pointer.press.wasPressedThisFrame)
        {
            isDragging = true;
            draggedFar = false;
            lastPointerY = pointerY;
            dragVelocity = 0f;
            velHistory.Clear();
            velHistory.Add((Time.time, pointerY));
        }
        else if (pointer.press.wasReleasedThisFrame && isDragging)
        {
            isDragging = false;
            dragVelocity = ReleaseVelocity(pointerY);
        }

        if (!isDragging && Mathf.Approximately(dragVelocity, 0f))
            return;

        float delta;

        if (isDragging)
        {
            velHistory.Add((Time.time, pointerY));

            while (velHistory.Count > 1 && Time.time - velHistory[0].time > VelWindowSec)
                velHistory.RemoveAt(0);

            delta = pointerY - lastPointerY;
            lastPointerY = pointerY;

            if (Mathf.Abs(pointerY - velHistory[0].y) > TapSlop)
                draggedFar = true;
        }
        else
        {
            dragVelocity *= Mathf.Exp(-dragFriction * Time.deltaTime);

            if (Mathf.Abs(dragVelocity) < 2f)
                dragVelocity = 0f;

            delta = dragVelocity * Time.deltaTime;
        }

        // Dragging up moves the content up, which means scrolling further down the list.
        scroll = Mathf.Clamp(scroll + delta, 0f, Mathf.Max(0f, contentH - viewH));
        ApplyScroll();
    }

    private void ApplyScroll()
    {
        if (content != null)
            content.anchoredPosition = new Vector2(0f, Mathf.Round(scroll));
    }

    private float ReleaseVelocity(float currentY)
    {
        if (velHistory.Count == 0)
            return 0f;

        float dt = Time.time - velHistory[0].time;
        return dt < 0.001f ? 0f : (currentY - velHistory[0].y) / dt;
    }

    // --- plumbing -------------------------------------------------------

    private RectTransform NewChild(string name, Transform parent)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        built.Add(go);
        return go.GetComponent<RectTransform>();
    }

    private void EnsureMask()
    {
        // Whether the Image was already here decides whether the mask may hide it. A Mask draws
        // nothing when showMaskGraphic is false, and on this object the masking graphic is the
        // page's own background art — hiding it switched the page's artwork off.
        bool hadOwnGraphic = GetComponent<Image>() != null;

        if (!hadOwnGraphic)
            gameObject.AddComponent<Image>().color = Color.black;

        if (GetComponent<Mask>() == null)
            gameObject.AddComponent<Mask>().showMaskGraphic = hadOwnGraphic;
    }

    private void OnRectTransformDimensionsChange()
    {
        if (content == null)
            return;

        float h = panel.rect.height;

        if (h > 0f && !Mathf.Approximately(h, viewH))
            StartCoroutine(RebuildNextFrame());
    }
}
