using UnityEditor;
using UnityEngine;

// Makes the game's sounds arrive when the finger does.
//
// Two separate delays, and both were at Unity's defaults, which are tuned for music rather than
// for interface feedback.
public static class AudioLatencySetup
{
    // Unity's default is 1024 -- "Best performance". The engine keeps several buffers in flight,
    // so at 44.1kHz that is somewhere between 45 and 90ms before a sound reaches the speaker, which
    // is comfortably enough to feel like lag. 256 is the "Best latency" setting and lands around
    // 6ms per buffer.
    //
    // The trade is CPU: a smaller buffer means the audio thread wakes four times as often. For a
    // handful of short mono clips on a modern phone that is nothing, and it is the standard choice
    // for anything where a sound answers a touch.
    // The Inspector's "DSP Buffer Size" dropdown, which is what actually decides this:
    //   0 Default   1 Best latency   2 Good latency   3 Best performance
    //
    // m_DSPBufferSize is the RESOLVED value and Unity recomputes it from this one, so setting the
    // resolved field directly does nothing at all -- it is overwritten on the next reload.
    private const int BestLatency = 1;

    [MenuItem("Trivia Duel/Setup/Fix Audio Latency")]
    public static void Apply()
    {
        // Nothing scene-shaped survives Play mode: Unity discards every change on exit, so a tool
        // run now reports success and leaves no trace. Worse, it reads the RUNNING values --
        // TextFitsItsBox has already shrunk labels to fit by then, so a font authored at 100pt
        // measures 41 and any box sized from it comes out wrong as well as unsaved.
        if (EditorApplication.isPlaying)
        {
            Debug.LogError("AudioLatencySetup: stop Play mode first. Changes made while playing are thrown " +
                           "away, and the values read are the running ones rather than the real ones.");
            return;
        }

        SetBufferSize();
        SetClipImport("Assets/Resources/Click");
        SetClipImport("Assets/Resources/Match");

        AssetDatabase.SaveAssets();
        Debug.Log("AudioLatencySetup: done.");
    }

    private static void SetBufferSize()
    {
        Object[] settings = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/AudioManager.asset");

        if (settings == null || settings.Length == 0)
        {
            Debug.LogError("AudioLatencySetup: could not open AudioManager.asset.");
            return;
        }

        SerializedObject audio = new SerializedObject(settings[0]);
        SerializedProperty requested = audio.FindProperty("m_RequestedDSPBufferSize");
        SerializedProperty resolved = audio.FindProperty("m_DSPBufferSize");

        if (requested == null)
        {
            Debug.LogError("AudioLatencySetup: no m_RequestedDSPBufferSize on the AudioManager.");
            return;
        }

        int before = resolved != null ? resolved.intValue : -1;

        requested.intValue = BestLatency;
        audio.ApplyModifiedPropertiesWithoutUndo();

        EditorUtility.SetDirty(settings[0]);

        // Project settings do not go out with AssetDatabase.SaveAssets, so this has to ask for the
        // save itself rather than leaving a note telling somebody else to.
        EditorApplication.ExecuteMenuItem("File/Save Project");

        Debug.Log($"AudioLatencySetup: DSP buffer was {before}; set to Best latency and saved.");
    }

    // Every UI clip here is a few kilobytes of mono. Compressing them buys nothing worth having
    // and costs a decode on the way to the speaker; not preloading them costs a load on top of
    // that, the first time each one is used.
    private static void SetClipImport(string folder)
    {
        string[] guids = AssetDatabase.FindAssets("t:AudioClip", new[] { folder });
        int changed = 0;

        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            AudioImporter importer = AssetImporter.GetAtPath(path) as AudioImporter;

            if (importer == null)
                continue;

            AudioImporterSampleSettings settings = importer.defaultSampleSettings;

            bool needsChange =
                settings.loadType != AudioClipLoadType.DecompressOnLoad ||
                settings.compressionFormat != AudioCompressionFormat.PCM ||
                !settings.preloadAudioData ||
                importer.loadInBackground;

            if (!needsChange)
                continue;

            settings.loadType = AudioClipLoadType.DecompressOnLoad;
            settings.compressionFormat = AudioCompressionFormat.PCM;
            settings.preloadAudioData = true;

            importer.defaultSampleSettings = settings;
            importer.loadInBackground = false;

            // Nothing here touches the importer's 3D flag: it is legacy and this Unity ignores it,
            // so setting it looks like it works and changes nothing. Whether a sound is positional
            // is decided by the AudioSource, and both of ours set spatialBlend to 0 outright.
            importer.SaveAndReimport();
            changed++;
        }

        Debug.Log($"AudioLatencySetup: {changed} of {guids.Length} clip(s) in {folder} set to " +
                  "uncompressed and preloaded.");
    }
}
