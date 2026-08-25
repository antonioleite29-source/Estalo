using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// A click under every button in the game.
//
// Ten variations rather than one clip, because a single sample repeated on every tap stops being
// a sound and becomes a machine noise within about five presses -- the same reason a game
// footstep is never one file. They differ in pitch, level and length, so the ear reads them as
// one thing being done repeatedly instead of one recording being replayed.
//
// Attached to every Selectable at startup rather than wired in the scene, for the reason
// CanvasInputRepair spells out: it then covers a button added tomorrow, one built at runtime, and
// one on a page nobody thought to check.
public class ButtonClickSound : MonoBehaviour, IPointerDownHandler
{
    // Loaded by folder, so adding an eleventh variation is a matter of dropping a file in.
    private const string ClipFolder = "Click";

    // On press, not on release. A sound that waits for the finger to lift feels like a delay even
    // when it is only a hundred milliseconds, and every real game plays it on the way down.
    public void OnPointerDown(PointerEventData eventData) => Play();

    // Public so anything that acts like a button without being one -- the avatar on the lobby,
    // an answer tile -- can make the same noise.
    public static void Play()
    {
        if (bag == null && !Prepare())
            return;

        source.pitch = 1f;
        source.PlayOneShot(NextClip(), Volume);
    }

    [Range(0f, 1f)]
    public static float Volume = 0.6f;

    private static AudioSource source;
    private static AudioClip[] clips;
    private static List<int> bag;

    private static bool Prepare()
    {
        if (NetworkBootstrap.IsDedicatedServerBuild)
            return false;

        clips = Resources.LoadAll<AudioClip>(ClipFolder);

        if (clips == null || clips.Length == 0)
        {
            Debug.LogWarning("ButtonClickSound: no clips in Resources/" + ClipFolder + ", so buttons stay silent.");
            return false;
        }

        GameObject host = new GameObject("ButtonClickSound");
        DontDestroyOnLoad(host);

        source = host.AddComponent<AudioSource>();
        source.playOnAwake = false;

        // 2D. A UI sound has no position, and leaving it 3D makes it quieter the further the
        // camera happens to be from the origin -- which is a bug that only shows up on one scene.
        source.spatialBlend = 0f;

        bag = new List<int>();
        return true;
    }

    // A shuffle bag, not a random pick. Random repeats itself roughly one press in ten and
    // sometimes three times running, which is exactly the machine-noise effect the ten variations
    // exist to avoid. Drawing without replacement guarantees all ten before any repeats.
    private static AudioClip NextClip()
    {
        if (bag.Count == 0)
        {
            for (int i = 0; i < clips.Length; i++)
                bag.Add(i);

            // Fisher-Yates, then one guard: if the refilled bag would hand back the clip that just
            // played, swap it away from the front. Otherwise a repeat can still land across the
            // seam between two bags.
            for (int i = bag.Count - 1; i > 0; i--)
            {
                int j = Random.Range(0, i + 1);
                (bag[i], bag[j]) = (bag[j], bag[i]);
            }

            if (bag.Count > 1 && bag[bag.Count - 1] == lastPlayed)
                (bag[bag.Count - 1], bag[0]) = (bag[0], bag[bag.Count - 1]);
        }

        lastPlayed = bag[bag.Count - 1];
        bag.RemoveAt(bag.Count - 1);
        return clips[lastPlayed];
    }

    private static int lastPlayed = -1;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AttachToEveryButton()
    {
        if (NetworkBootstrap.IsDedicatedServerBuild)
            return;

        // Include inactive: most pages in this game start switched off, and their buttons are
        // exactly the ones that would otherwise be silent.
        Selectable[] selectables = Object.FindObjectsByType<Selectable>(FindObjectsInactive.Include);

        int attached = 0;

        foreach (Selectable selectable in selectables)
        {
            // A slider or an input field is dragged and typed into, not pressed, and clicking one
            // is not the moment a click belongs to.
            if (selectable is Slider || selectable is Scrollbar || selectable is TMPro.TMP_InputField)
                continue;

            if (selectable.GetComponent<ButtonClickSound>() != null)
                continue;

            selectable.gameObject.AddComponent<ButtonClickSound>();
            attached++;
        }

        if (attached > 0)
            Debug.Log($"ButtonClickSound: {attached} button(s) will click when pressed.");
    }
}
