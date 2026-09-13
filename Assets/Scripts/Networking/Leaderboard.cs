using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

// The global leaderboard, kept by the server: one row per player, ranked by IQ points.
//
// Server-only. Each client reports its own IQ -- the same self-reported number matchmaking already
// trusts, with the same weakness (see "Open questions" in Architecture.md) -- and the server answers
// with where that puts them. It lives in a file under the server's persistent data folder rather
// than beside the build, so a redeploy, which replaces the build, keeps every row.
//
// On the older LAN fallback, where one phone hosts, that phone keeps a leaderboard of its own. It is
// only global when the dedicated server is the one answering.
public static class Leaderboard
{
    [Serializable]
    private class Entry
    {
        public string id;
        public string name;
        public float iq;
        public long updated;   // unix seconds, for pruning players who have long gone
    }

    [Serializable]
    private class Table
    {
        public List<Entry> entries = new List<Entry>();
    }

    private static Table table;
    private static readonly Dictionary<string, Entry> byId = new Dictionary<string, Entry>();

    // On the cloud box, systemd's StateDirectory (/var/lib/triviaduel) -- the service user has no home
    // and ProtectHome hides it anyway, so persistentDataPath there cannot be written. Anywhere else
    // (the LAN host, a local test) persistentDataPath is fine.
    private static string FilePath
    {
        get
        {
            string stateDirectory = Environment.GetEnvironmentVariable("STATE_DIRECTORY");
            string folder = string.IsNullOrEmpty(stateDirectory) ? Application.persistentDataPath : stateDirectory;
            return Path.Combine(folder, "leaderboard.json");
        }
    }

    // Updates this player's row and returns their position (1 is the top) and how many players the
    // board holds. Players on equal points share a position.
    public static (int rank, int size) Submit(string id, string name, float iq)
    {
        if (string.IsNullOrEmpty(id) || float.IsNaN(iq) || float.IsInfinity(iq))
            return (0, 0);

        Load();

        if (!byId.TryGetValue(id, out Entry entry))
        {
            entry = new Entry { id = id };
            table.entries.Add(entry);
            byId[id] = entry;
        }

        entry.name = name;
        entry.iq = iq;
        entry.updated = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        Save();

        int rank = 1;

        foreach (Entry other in table.entries)
            if (other.iq > entry.iq)
                rank++;

        return (rank, table.entries.Count);
    }

    private static void Load()
    {
        if (table != null)
            return;

        table = new Table();

        try
        {
            if (File.Exists(FilePath))
                table = JsonUtility.FromJson<Table>(File.ReadAllText(FilePath)) ?? new Table();
        }
        catch (Exception e)
        {
            // A damaged file must not take the server down with it. Starting an empty board loses
            // the rankings; refusing to run loses every match.
            Debug.LogWarning($"Leaderboard: could not read {FilePath}, starting empty. {e.Message}");
            table = new Table();
        }

        if (table.entries == null)
            table.entries = new List<Entry>();

        byId.Clear();

        foreach (Entry entry in table.entries)
            if (entry != null && !string.IsNullOrEmpty(entry.id))
                byId[entry.id] = entry;
    }

    // Written to a temporary file and moved into place, so a crash mid-write leaves the previous
    // board intact rather than half a file.
    private static void Save()
    {
        try
        {
            string temporary = FilePath + ".tmp";
            File.WriteAllText(temporary, JsonUtility.ToJson(table));

            if (File.Exists(FilePath))
                File.Delete(FilePath);

            File.Move(temporary, FilePath);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"Leaderboard: could not save {FilePath}. {e.Message}");
        }
    }
}
