using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

// Creates the full-screen Image the match-start animation plays on.
//
// A menu item rather than instructions because every part of this is silent when it is wrong: the
// wrong parent and it never draws (TriviaGame is inactive while you are waiting), the wrong sibling
// index and it draws behind the waiting screen, a raycast target left on and it eats every button
// press in the lobby for the rest of the session.
public static class MatchStartOverlayBuilder
{
    private const string OverlayName = "MatchStartOverlay";

    [MenuItem("Trivia Duel/Setup/Build Match Start Overlay")]
    private static void Build()
    {
        // UIRoot rather than LobbyRoot: UIRoot is the CanvasStretchFitter root and is exactly the
        // screen (1179x2556), while LobbyRoot is 46px wider and would stretch the artwork by ~4%.
        // Being a SIBLING of LobbyRoot, drawn after it, is also what puts it above the waiting
        // screen without living inside the thing it needs to cover.
        RectTransform uiRoot = Object.FindObjectsByType<RectTransform>(FindObjectsInactive.Include)
            .FirstOrDefault(r => r.name == "UIRoot");

        if (uiRoot == null)
        {
            EditorUtility.DisplayDialog("Match Start Overlay",
                "Could not find a RectTransform named 'UIRoot' in the open scene.", "OK");
            return;
        }

        Transform existing = uiRoot.Find(OverlayName);
        GameObject overlay;

        if (existing != null)
        {
            overlay = existing.gameObject;
        }
        else
        {
            // Created WITH a RectTransform rather than adding one afterwards. GetComponent returns
            // a fake null for a missing component, which the ?? operator does not recognise, so the
            // usual "get it or add it" one-liner silently leaves you with a plain Transform.
            overlay = new GameObject(OverlayName, typeof(RectTransform));
            overlay.transform.SetParent(uiRoot, false);
            Undo.RegisterCreatedObjectUndo(overlay, "Build Match Start Overlay");
        }

        RectTransform rect = overlay.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.localScale = Vector3.one;

        Image image = overlay.GetComponent<Image>();

        if (image == null)
            image = overlay.AddComponent<Image>();

        image.color = Color.white;
        image.type = Image.Type.Simple;
        image.preserveAspect = false;

        // Nothing here is clickable, and a full-screen raycast target over the lobby would swallow
        // Start, Cancel and every navigation button underneath it.
        image.raycastTarget = false;

        overlay.transform.SetAsLastSibling();

        // Off by default: it is only ever switched on for the length of the animation.
        overlay.SetActive(false);

        int wired = 0;

        foreach (TriviaDuelManager manager in
                 Object.FindObjectsByType<TriviaDuelManager>(FindObjectsInactive.Include))
        {
            SerializedObject so = new SerializedObject(manager);
            SerializedProperty prop = so.FindProperty("matchStartOverlay");

            if (prop == null)
                continue;

            prop.objectReferenceValue = image;
            so.ApplyModifiedProperties();
            wired++;
        }

        EditorUtility.SetDirty(overlay);
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(overlay.scene);
        Selection.activeGameObject = overlay;

        Debug.Log($"Match Start Overlay ready under UIRoot, wired into {wired} manager(s). " +
                  "Drag your exported frames into Match Start Frames, then save the scene.", overlay);
    }
}
