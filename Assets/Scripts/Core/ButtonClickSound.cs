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
    // Which note this particular button plays. Every note is from C major pentatonic, so any
    // combination of buttons pressed in any order stays in key -- and in key with the match
    // sounds, which are drawn from the same five notes.
    public enum Note { C, D, E, G, A }

    [Tooltip("The note this button plays. C is the neutral tap; give a button a different note " +
             "when pressing it means something.")]
    public Note note = Note.C;

    // One folder per note, five variations inside each.
    private const string ClipFolder = "Click";

    // On press, not on release. A sound that waits for the finger to lift feels like a delay even
    // when it is only a hundred milliseconds, and every real game plays it on the way down.
    public void OnPointerDown(PointerEventData eventData) => Play(note);

    // Public so anything that acts like a button without being one -- the avatar on the lobby,
    // an answer tile -- can make the same noise.
    public static void Play(Note which = Note.C)
    {
        if (!Prepare())
            return;

        AudioClip clip = NextClip(which);

        if (clip != null)
            source.PlayOneShot(clip, Volume);
    }

    [Range(0f, 1f)]
    public static float Volume = 0.6f;

    private static AudioSource source;
    private static bool ready;

    // Per note, because each has its own five files and its own place in its own shuffle.
    private static readonly Dictionary<Note, AudioClip[]> clips = new Dictionary<Note, AudioClip[]>();
    private static readonly Dictionary<Note, List<int>> bags = new Dictionary<Note, List<int>>();
    private static readonly Dictionary<Note, int> lastPlayed = new Dictionary<Note, int>();

    private static bool Prepare()
    {
        if (ready)
            return source != null;

        ready = true;

        if (NetworkBootstrap.IsDedicatedServerBuild)
            return false;

        GameObject host = new GameObject("ButtonClickSound");
        DontDestroyOnLoad(host);

        source = host.AddComponent<AudioSource>();
        source.playOnAwake = false;

        // 2D. A UI sound has no position, and leaving it 3D makes it quieter the further the
        // camera happens to be from the origin -- a bug that only shows up on one scene.
        source.spatialBlend = 0f;

        return true;
    }

    private static AudioClip[] ClipsFor(Note which)
    {
        if (clips.TryGetValue(which, out AudioClip[] found))
            return found;

        found = Resources.LoadAll<AudioClip>(ClipFolder + "/" + which);

        if (found == null || found.Length == 0)
            Debug.LogWarning($"ButtonClickSound: nothing in Resources/{ClipFolder}/{which}, so those buttons are silent.");

        clips[which] = found;
        return found;
    }

    // A shuffle bag per note, not a random pick. Random repeats itself roughly one press in five
    // and sometimes twice running, which is exactly the machine-noise effect the variations exist
    // to avoid. Drawing without replacement guarantees all five before any repeats.
    private static AudioClip NextClip(Note which)
    {
        AudioClip[] pool = ClipsFor(which);

        if (pool == null || pool.Length == 0)
            return null;

        if (!bags.TryGetValue(which, out List<int> bag))
            bags[which] = bag = new List<int>();

        if (bag.Count == 0)
        {
            for (int i = 0; i < pool.Length; i++)
                bag.Add(i);

            for (int i = bag.Count - 1; i > 0; i--)
            {
                int j = Random.Range(0, i + 1);
                (bag[i], bag[j]) = (bag[j], bag[i]);
            }

            // One guard on the seam: a refilled bag whose first draw is the clip that just played
            // would let a repeat straddle two bags, which is the one case shuffling misses.
            if (bag.Count > 1 && lastPlayed.TryGetValue(which, out int previous) && bag[bag.Count - 1] == previous)
                (bag[bag.Count - 1], bag[0]) = (bag[0], bag[bag.Count - 1]);
        }

        int index = bag[bag.Count - 1];
        bag.RemoveAt(bag.Count - 1);
        lastPlayed[which] = index;

        return pool[index];
    }

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

            ButtonClickSound click = selectable.gameObject.AddComponent<ButtonClickSound>();

            // Everything is C except the one button that starts a game. Found by the word on it
            // rather than by which object it is: the Start button is called "Button" in the
            // hierarchy, and what it says is the one unambiguous fact about it.
            if (IsPlayButton(selectable))
                click.note = Note.E;

            attached++;
        }

        if (attached > 0)
            Debug.Log($"ButtonClickSound: {attached} button(s) will click when pressed.");
    }

    private static bool IsPlayButton(Selectable selectable)
    {
        TMPro.TMP_Text label = selectable.GetComponentInChildren<TMPro.TMP_Text>(true);

        if (label == null)
            return false;

        string written = label.text.Trim();
        return written == "Jogar" || written == "Start";
    }
}
