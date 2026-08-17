using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

// Splits the one big Canvas into per-page sub-canvases.
//
// Unity rebuilds a canvas whole: change one number on it and every renderer under it is re-batched.
// This scene has ~133 renderers on a single Canvas — the lobby, all five pages, the gameplay
// screen, the practice panels and the waiting screen together — so the score ticking over, or the
// solo ring emptying, was rebuilding the entire game's UI every frame.
//
// Adding a Canvas component to a child makes it its own batching unit. Nothing else changes: a
// nested canvas inherits render mode and sorting from its parent, and the root's GraphicRaycaster
// still handles clicks for everything beneath it, so no raycaster is added here.
public static class CanvasSplitter
{
    // The roots worth isolating: each is a screen that redraws independently of the others.
    private static readonly string[] Targets =
    {
        "ProfilePage", "LearningPage", "LobbyPage", "MorePage", "ConnectPage",
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

        Debug.Log($"Canvas split: added {added.Count} sub-canvases ({string.Join(", ", added)}). " +
                  $"Already split: {already.Count}.", root);

        EditorUtility.SetDirty(root);
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

            Debug.Log($"{canvas.name}: {renderers} renderers in its own batch", canvas);
        }
    }
}
