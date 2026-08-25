using UnityEditor;
using UnityEngine;

// Forgets that the first-run tutorial has been seen, so it runs again on the next Play.
//
// Otherwise the only way to see it a second time is a fresh install, which makes the one piece of
// the game most in need of watching somebody else use it the hardest piece to show anybody.
public static class ReplayFirstRun
{
    [MenuItem("Trivia Duel/Setup/Replay First Run Tutorial")]
    public static void Replay()
    {
        // Both the plain key and the per-virtual-player ones: testing through Multiplayer Play Mode
        // writes a suffixed key, and clearing only the plain one leaves the clones thinking they
        // have already seen it.
        PlayerPrefs.DeleteKey("EstaloFirstRunDone");

        for (int i = 0; i < 8; i++)
            PlayerPrefs.DeleteKey("EstaloFirstRunDone_" + i);

        PlayerPrefs.Save();

        Debug.Log("ReplayFirstRun: cleared. Press Play and the tutorial runs from the top.");
    }
}
