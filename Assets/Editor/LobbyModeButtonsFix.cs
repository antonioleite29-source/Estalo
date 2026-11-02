using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

// Puts the game-mode and Solo buttons on the lobby as real scene objects, under Jogar, and names
// them so they are found again: ModeButton and SoloButton.
//
// The game builds them at runtime when the scene has none, but a runtime object cannot be moved,
// resized or restyled in the editor and is made again on every launch. This makes them permanent.
//
// Runs once on its own; save the scene after. Undo takes them back out. Safe to run again: it does
// nothing when the switcher already has both.
[InitializeOnLoad]
public static class LobbyModeButtonsFix
{
    private const string DoneKey = "EstaloLobbyModeAndSoloButtons";
    private const string UndoName = "Add the mode and solo buttons";

    static LobbyModeButtonsFix()
    {
        if (EditorPrefs.GetBool(DoneKey, false))
            return;

        EditorApplication.delayCall += () =>
        {
            if (!EditorApplication.isPlayingOrWillChangePlaymode && Add())
                EditorPrefs.SetBool(DoneKey, true);
        };
    }

    [MenuItem("Trivia Duel/Setup/Add Mode And Solo Buttons")]
    private static bool Add()
    {
        LobbyPageSwitcher switcher = Object.FindAnyObjectByType<LobbyPageSwitcher>(FindObjectsInactive.Include);

        if (switcher == null)
            return false;

        Button jogar = JogarButtonStyle.Template;

        if (jogar == null)
        {
            Debug.LogWarning("LobbyModeButtonsFix: no button labelled \"Jogar\" to sit under.", switcher);
            return false;
        }

        if (switcher.ruleSetButton != null && switcher.soloButton != null)
            return false;

        RectTransform anchor = (RectTransform)jogar.transform;
        float height = anchor.sizeDelta.y > 1f ? anchor.sizeDelta.y : 150f;
        float step = height * 0.78f + 26f;
        Vector2 top = anchor.anchoredPosition - new Vector2(0f, height * 0.5f + 26f);

        Undo.RecordObject(switcher, UndoName);

        if (switcher.ruleSetButton == null)
            switcher.ruleSetButton = Copy(jogar, "ModeButton", GameRuleSets.NameOf(GameRuleSet.Classic),
                top - new Vector2(0f, step * 0.5f));

        if (switcher.soloButton == null)
            switcher.soloButton = Copy(jogar, "SoloButton", "Solo", top - new Vector2(0f, step * 1.5f));

        EditorUtility.SetDirty(switcher);
        EditorSceneManager.MarkSceneDirty(switcher.gameObject.scene);

        Debug.Log("LobbyModeButtonsFix: ModeButton and SoloButton are under Jogar and wired to the " +
                  "switcher. Move them wherever you like, then save the scene.", switcher);
        return true;
    }

    private static Button Copy(Button jogar, string name, string text, Vector2 position)
    {
        GameObject copy = Object.Instantiate(jogar.gameObject, jogar.transform.parent);
        copy.name = name;
        Undo.RegisterCreatedObjectUndo(copy, UndoName);

        RectTransform rect = (RectTransform)copy.transform;
        RectTransform from = (RectTransform)jogar.transform;

        rect.sizeDelta = new Vector2(from.sizeDelta.x * 0.72f, from.sizeDelta.y * 0.78f);
        rect.anchoredPosition = position;

        TMPro.TMP_Text label = copy.GetComponentInChildren<TMPro.TMP_Text>(true);

        if (label != null)
        {
            label.text = text;
            label.fontSize = label.fontSize * 0.8f;
            label.color = Color.white;
        }

        // Whatever Jogar's own click does, this one must not do.
        Button button = copy.GetComponent<Button>();
        button.onClick = new Button.ButtonClickedEvent();
        return button;
    }
}

// Two menu items for pointing the lobby at buttons that are already in the scene: select the button
// in the Hierarchy, then pick the one it is. Quicker than finding the slot on the switcher, and it
// leaves the name alone.
public static class UseSelectedLobbyButton
{
    [MenuItem("Trivia Duel/Setup/Use Selected Button As The Mode Button")]
    private static void AsMode() => Assign(mode: true);

    [MenuItem("Trivia Duel/Setup/Use Selected Button As The Solo Button")]
    private static void AsSolo() => Assign(mode: false);

    private static void Assign(bool mode)
    {
        LobbyPageSwitcher switcher = Object.FindAnyObjectByType<LobbyPageSwitcher>(FindObjectsInactive.Include);
        Button button = Selection.activeGameObject != null
            ? Selection.activeGameObject.GetComponent<Button>()
            : null;

        if (switcher == null || button == null)
        {
            Debug.LogWarning("UseSelectedLobbyButton: select a GameObject with a Button on it first.");
            return;
        }

        Undo.RecordObject(switcher, "Point the lobby at a button");

        if (mode)
            switcher.ruleSetButton = button;
        else
            switcher.soloButton = button;

        EditorUtility.SetDirty(switcher);
        EditorSceneManager.MarkSceneDirty(switcher.gameObject.scene);

        Debug.Log($"UseSelectedLobbyButton: \"{button.name}\" is the {(mode ? "mode" : "solo")} button now. " +
                  "Save the scene.", button);
    }
}
