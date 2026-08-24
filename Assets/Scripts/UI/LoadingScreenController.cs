using System.Collections;
using UnityEngine;
using UnityEngine.UI;

// The Estalo screen: held on launch, and again behind the waiting room.
//
// Builds itself at runtime rather than living in the scene. A launch screen has to be up before
// anything else has drawn, and anything sitting in the scene hierarchy is subject to whatever the
// lobby, the page switcher and the canvas fitter do to it on their own Awake. Nothing can reorder
// or deactivate a canvas that did not exist when they ran.
public class LoadingScreenController : MonoBehaviour
{
    // The art, by name, from Resources. No inspector slot to forget to fill.
    private const string SpritePath = "LoadingScreen";

    // Above every other canvas in the project by a wide margin, so it covers the lobby, the
    // gameplay page and the match-start overlay without depending on sibling order.
    private const int SortingOrder = 32000;

    public const float HoldSeconds = 1.7f;
    public const float FadeSeconds = 0.35f;

    private static Sprite cached;
    private static bool cacheChecked;

    public static Sprite Artwork
    {
        get
        {
            if (!cacheChecked)
            {
                cacheChecked = true;
                cached = Resources.Load<Sprite>(SpritePath);

                if (cached == null)
                {
                    Debug.LogWarning("LoadingScreenController: no sprite at Resources/" + SpritePath +
                                     ". The launch screen and the waiting-room background will be blank.");
                }
            }

            return cached;
        }
    }

    // AfterSceneLoad, not BeforeSceneLoad: the canvas needs the scene's EventSystem and camera
    // setup to exist, and this still runs before the first frame is presented.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void ShowOnLaunch()
    {
        // A dedicated server has no display and no player looking at one.
        if (NetworkBootstrap.IsDedicatedServerBuild)
            return;

        if (Artwork == null)
            return;

        GameObject host = new GameObject("LaunchScreen");
        DontDestroyOnLoad(host);
        host.AddComponent<LoadingScreenController>().Build();
    }

    private CanvasGroup group;

    private void Build()
    {
        Canvas canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = SortingOrder;

        // Constant Pixel Size, so the image is placed by the RectTransform below rather than by a
        // reference resolution this object knows nothing about.
        CanvasScaler scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;

        group = gameObject.AddComponent<CanvasGroup>();
        group.alpha = 1f;
        group.blocksRaycasts = true;

        GameObject imageObject = new GameObject("Art", typeof(RectTransform));
        imageObject.transform.SetParent(transform, false);

        Image image = imageObject.AddComponent<Image>();
        image.sprite = Artwork;
        image.raycastTarget = false;
        image.preserveAspect = false;
        image.type = Image.Type.Simple;

        FillScreen(image.rectTransform);

        StartCoroutine(HoldThenFade());
    }

    // Anchored to all four corners with zero insets: the art is drawn at 1179x2556, the same shape
    // as the phone, so filling outright is right and letterboxing it would show bars.
    public static void FillScreen(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = Vector2.zero;
        rect.localScale = Vector3.one;
    }

    private IEnumerator HoldThenFade()
    {
        // Unscaled throughout. Nothing should be able to make the launch screen outstay its
        // welcome by touching Time.timeScale.
        yield return new WaitForSecondsRealtime(HoldSeconds);

        // Raycasts released as the fade begins, so the half-faded screen cannot swallow the first
        // tap on the lobby behind it.
        group.blocksRaycasts = false;

        float elapsed = 0f;

        while (elapsed < FadeSeconds)
        {
            elapsed += Time.unscaledDeltaTime;
            group.alpha = 1f - Mathf.Clamp01(elapsed / FadeSeconds);
            yield return null;
        }

        Destroy(gameObject);
    }
}
