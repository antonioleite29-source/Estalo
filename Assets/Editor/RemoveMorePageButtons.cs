using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Takes the old LAN controls off the More page: Hospedar, the address box and Entrar, Desconectar.
// The game connects to the always-on server by itself, so they only confused players.
//
// Runs once on its own the first time Unity compiles it with the scene open -- save the scene
// afterwards -- and stays in the menu to run again. Undo brings the buttons back. ConnectPageController
// checks every one of these for null, so it keeps working without them.
[InitializeOnLoad]
public static class RemoveMorePageButtons
{
    private const string DoneKey = "EstaloRemovedMorePageButtons";

    static RemoveMorePageButtons()
    {
        if (!EditorPrefs.GetBool(DoneKey, false))
            EditorApplication.delayCall += RunOnce;
    }

    private static void RunOnce()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            return;

        if (Remove())
            EditorPrefs.SetBool(DoneKey, true);
    }

    [MenuItem("Trivia Duel/Setup/Remove More Page Buttons")]
    private static void RunFromMenu()
    {
        if (!Remove())
            Debug.Log("RemoveMorePageButtons: nothing left to remove on the More page.");
    }

    private static bool Remove()
    {
        List<GameObject> doomed = new List<GameObject>();

        foreach (ConnectPageController page in Object.FindObjectsByType<ConnectPageController>(FindObjectsInactive.Include))
        {
            if (page.hostButton != null) doomed.Add(page.hostButton.gameObject);
            if (page.joinButton != null) doomed.Add(page.joinButton.gameObject);
            if (page.disconnectButton != null) doomed.Add(page.disconnectButton.gameObject);
            if (page.addressInput != null) doomed.Add(page.addressInput.gameObject);

            // "Entrar pelo endereço" -- the caption over the address box, meaningless without it.
            foreach (Transform child in page.GetComponentsInChildren<Transform>(true))
                if (child.name == "JoinLabel")
                    doomed.Add(child.gameObject);
        }

        if (doomed.Count == 0)
            return false;

        foreach (GameObject go in doomed)
        {
            if (go == null)
                continue;

            EditorSceneManager.MarkSceneDirty(go.scene);
            Debug.Log($"RemoveMorePageButtons: removed {go.name} from the More page.");
            Undo.DestroyObjectImmediate(go);
        }

        return true;
    }
}
