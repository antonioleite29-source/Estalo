using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// Makes sure every canvas that holds buttons can actually receive clicks.
//
// A GraphicRaycaster only raycasts graphics registered to its OWN canvas:
//
//   GraphicRaycaster.cs:115   m_Canvas = GetComponent<Canvas>();
//   GraphicRaycaster.cs:132   GraphicRegistry.GetRaycastableGraphicsForCanvas(canvas)
//   Graphic.cs:434            a graphic binds to the NEAREST active canvas, not the root
//
// So the moment a page is given its own Canvas component to cut down UI rebuild cost, every button
// on that page re-registers to the new canvas and the root's raycaster stops seeing them. They keep
// drawing perfectly and go completely dead to the touch — no error, no warning, nothing in the log.
// That is how this scene ended up with 23 of its 33 buttons unclickable: both sets of answer
// buttons, the whole bottom navigation bar, the connect page and the waiting screen's Cancel.
//
// This runs at startup rather than living in the scene file so it also covers virtual players and
// builds, and so splitting another page later can never silently kill its buttons again.
public static class CanvasInputRepair
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Repair()
    {
        Canvas[] canvases = Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include);

        int added = 0;

        foreach (Canvas canvas in canvases)
        {
            if (canvas.GetComponent<GraphicRaycaster>() != null)
                continue;

            // A canvas holding only artwork needs no raycaster, and giving it one would drop an
            // invisible click-catcher over the screen. What decides is whether anything under it is
            // meant to be pressed in the first place.
            if (!HasOwnSelectable(canvas))
                continue;

            canvas.gameObject.AddComponent<GraphicRaycaster>();
            added++;
        }

        // Without an EventSystem nothing is clickable no matter how many raycasters exist, and its
        // absence is just as silent, so it is worth saying out loud rather than assuming.
        if (Object.FindAnyObjectByType<EventSystem>() == null)
            Debug.LogError("CanvasInputRepair: there is no EventSystem in the scene, so no UI can be " +
                           "clicked at all. Add one via GameObject > UI > Event System.");

        if (added > 0)
            Debug.Log($"CanvasInputRepair: added {added} GraphicRaycaster(s) so buttons on split " +
                      $"canvases can be clicked. Run Trivia Duel > Setup > Repair Sub-Canvas Input to " +
                      $"make this permanent in the scene.");
    }

    // Only selectables belonging to *this* canvas count. One under a deeper sub-canvas is that
    // canvas's business and gets its own raycaster on its own pass.
    private static bool HasOwnSelectable(Canvas canvas)
    {
        foreach (Selectable selectable in canvas.GetComponentsInChildren<Selectable>(true))
            if (selectable.GetComponentInParent<Canvas>(true) == canvas)
                return true;

        return false;
    }
}
