using System.Collections.Generic;
using UnityEngine;

// What this phone has asked lately, so a solo match does not open with the question the last one
// closed on.
//
// The server keeps the same memory per account for networked matches (PlayerAccounts); this is its
// twin for the matches that never reach a server -- Solo, and the first-run placement round. Saved,
// so closing the game does not wipe it.
public static class RecentQuestions
{
    // Matches what an account remembers on the server. Long enough not to meet the same question
    // twice in an evening, short enough that a small pool does not run dry.
    public const int Remembered = 80;

    private static string Key => "EstaloRecentQuestions" + NetworkBootstrap.GetLocalProfileSuffix();

    private static List<int> loaded;

    private static List<int> Seen
    {
        get
        {
            if (loaded != null)
                return loaded;

            loaded = new List<int>();

            foreach (string piece in PlayerPrefs.GetString(Key, string.Empty).Split(','))
                if (int.TryParse(piece, out int value))
                    loaded.Add(value);

            return loaded;
        }
    }

    private static int KeyFor(int difficultyLevel, int questionIndex) =>
        difficultyLevel * 10000 + questionIndex;

    public static bool HasSeen(int difficultyLevel, int questionIndex) =>
        Seen.Contains(KeyFor(difficultyLevel, questionIndex));

    public static void Remember(int difficultyLevel, int questionIndex)
    {
        int key = KeyFor(difficultyLevel, questionIndex);
        Seen.Remove(key);
        Seen.Add(key);

        while (Seen.Count > Remembered)
            Seen.RemoveAt(0);

        PlayerPrefs.SetString(Key, string.Join(",", Seen));
        PlayerPrefs.Save();
    }

    // A question from this pool that has not come up lately, falling back to anything other than
    // the one just asked once the pool has been exhausted.
    public static int Pick(int difficultyLevel, int poolSize, int avoidIndex)
    {
        if (poolSize <= 1)
            return 0;

        for (int attempt = 0; attempt < 24; attempt++)
        {
            int candidate = Random.Range(0, poolSize);

            if (candidate != avoidIndex && !HasSeen(difficultyLevel, candidate))
            {
                Remember(difficultyLevel, candidate);
                return candidate;
            }
        }

        for (int candidate = 0; candidate < poolSize; candidate++)
        {
            if (candidate != avoidIndex && !HasSeen(difficultyLevel, candidate))
            {
                Remember(difficultyLevel, candidate);
                return candidate;
            }
        }

        int anything = Random.Range(0, poolSize);

        if (anything == avoidIndex)
            anything = (anything + 1) % poolSize;

        Remember(difficultyLevel, anything);
        return anything;
    }
}
