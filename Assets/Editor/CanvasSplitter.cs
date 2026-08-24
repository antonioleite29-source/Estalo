using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

// Splits the one big Canvas into per-page sub-canvases.
//
// Unity rebuilds a canvas whole: change one number on it and every renderer under it is re-batched.
// This scene has ~133 renderers on a single Canvas — the lobby, all five pages, the gameplay
// screen, the practice panels and the waiting screen together — so the score ticking over, or the
// solo ring emptying, was rebuilding the entire game's UI every frame.
//
// Adding a Canvas component to a child makes it its own batching unit. A nested canvas inherits
// render mode and sorting from its parent, so nothing about how the UI *looks* changes.
//
// Input is the part that does change, and getting this wrong is what broke the Start button:
//
//   Graphic.CacheCanvas()               binds each graphic to the NEAREST active canvas
//   GraphicRaycaster.canvas             is GetComponent<Canvas>() — its own canvas, not the root
//   GraphicRaycaster.Raycast()          only queries GetRaycastableGraphicsForCanvas(that canvas)
//
// So the moment a page becomes its own canvas, every button on it re-registers to the new canvas,
// and the root's raycaster — which only ever looks at graphics registered to the root — stops
// seeing them. They render perfectly and are completely dead to the touch. An earlier version of
// this file asserted the opposite in a comment and added no raycasters, which is exactly how the
// host ended up unable to press Start.
//
// Every sub-canvas that contains something clickable therefore gets its own GraphicRaycaster.
public static class CanvasSplitter
{
    // The roots worth isolating: each is a screen that redraws independently of the others.
    // These are the names as they exist in ProjectCapstone.unity, checked against the scene rather
    // than guessed: the first version of this list said "ProfilePage" and "LobbyPage", neither of
    // which is in the scene, so those two pages were silently never split.
    private static readonly string[] Targets =
    {
        "Profilepage", "LearningPage", "MainLobbypage", "MorePage", "ConnectPage",
        "PracticeArea", "WaitingScreen", "TriviaGame", "TeamGameplayRoot", "BottomBar"
    };

    [MenuItem("Trivia Duel/Setup/Split Canvas Into Pages")]
    public static void Split()
    {
        Canvas root = Object.FindAnyObjectByType<Canvas>();

        if (root == null)
        {
            EditorUtility.DisplayDialog("Split canvas", "No Canvas in the scene.", "OK");
            return;
        }

        List<string> added = new List<string>();
        List<string> already = new List<string>();

        foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
        {
            if (System.Array.IndexOf(Targets, child.name) < 0)
                continue;

            if (child.GetComponent<Canvas>() != null)
            {
                already.Add(child.name);
                continue;
            }

            Undo.AddComponent<Canvas>(child.gameObject);
            added.Add(child.name);
        }

        int repaired = RestoreInput();

        Debug.Log($"Canvas split: added {added.Count} sub-canvases ({string.Join(", ", added)}). " +
                  $"Already split: {already.Count}. Raycasters added: {repaired}.", root);

        EditorUtility.SetDirty(root);
    }

    // Safe to run on its own, and the fix for a scene that was already split by the version of this
    // file that added no raycasters.
    [MenuItem("Trivia Duel/Setup/Repair Sub-Canvas Input")]
    public static void Repair()
    {
        int repaired = RestoreInput();

        EditorUtility.DisplayDialog(
            "Repair sub-canvas input",
            repaired == 0
                ? "Every sub-canvas with buttons on it already has a GraphicRaycaster. Nothing to do."
                : $"Added {repaired} GraphicRaycaster(s). Buttons on those pages can be clicked again.",
            "OK");
    }

    private static int RestoreInput()
    {
        int added = 0;

        foreach (Canvas canvas in Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include))
        {
            if (canvas.GetComponent<GraphicRaycaster>() != null)
                continue;

            // A canvas holding only artwork — the scrolling background layer, for one — needs no
            // raycaster, and giving it one would put an invisible click-catcher into the scene.
            // What decides is whether anything under it is actually meant to be pressed.
            if (!HasOwnSelectable(canvas))
                continue;

            Undo.AddComponent<GraphicRaycaster>(canvas.gameObject);
            added++;
        }

        return added;
    }

    // Only selectables that belong to *this* canvas count. A button sitting under a deeper
    // sub-canvas is that canvas's problem, and will get its own raycaster on its own pass.
    private static bool HasOwnSelectable(Canvas canvas)
    {
        foreach (Selectable selectable in canvas.GetComponentsInChildren<Selectable>(true))
            if (selectable.GetComponentInParent<Canvas>(true) == canvas)
                return true;

        return false;
    }

    [MenuItem("Trivia Duel/Setup/Count UI Rebuild Cost")]
    public static void Count()
    {
        Canvas root = Object.FindAnyObjectByType<Canvas>();

        if (root == null)
            return;

        // What a rebuild actually costs is the number of renderers batched together, so counting
        // them per canvas says exactly how much work one changed value triggers.
        foreach (Canvas canvas in root.GetComponentsInChildren<Canvas>(true))
        {
            int renderers = 0;

            foreach (CanvasRenderer r in canvas.GetComponentsInChildren<CanvasRenderer>(true))
                if (r.GetComponentInParent<Canvas>() == canvas)
                    renderers++;

            bool clickable = canvas.GetComponent<GraphicRaycaster>() != null;

            Debug.Log($"{canvas.name}: {renderers} renderers in its own batch" +
                      (HasOwnSelectable(canvas) && !clickable ? "  — HAS BUTTONS BUT NO RAYCASTER" : ""), canvas);
        }
    }
}
