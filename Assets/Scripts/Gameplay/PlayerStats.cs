using System;
using UnityEngine;

// This player's record for the Profile page: matches played, won and lost, best and current IQ
// points, how long they have been playing, and where they sit on the global leaderboard.
//
// Kept in PlayerPrefs on the device, like IQ itself, so it shares IQ's weakness -- lost on
// reinstall, easy to edit -- until accounts exist. The leaderboard position is the one number that
// comes from the server; it is only cached here when the answer arrives.
public static class PlayerStats
{
    // Per virtual player: Multiplayer Play Mode clones share this machine's PlayerPrefs.
    private static string Suffix => NetworkBootstrap.GetLocalProfileSuffix();

    private static string PlayedKey => "EstaloMatchesPlayed" + Suffix;
    private static string WonKey => "EstaloMatchesWon" + Suffix;
    private static string BestIQKey => "EstaloBestIQ" + Suffix;
    private static string FirstPlayedKey => "EstaloFirstPlayed" + Suffix;
    private static string PlayerIdKey => "EstaloPlayerId" + Suffix;

    private const string DateFormat = "yyyy-MM-dd";

    // Fired when anything shown on the Profile page changes, including the leaderboard answer
    // arriving from the server a moment after the page opened.
    public static event Action Changed;

    // Only matches with a winner count. One that ends because a player dropped or nobody answered
    // for a minute has no result, and a loss for somebody's Wi-Fi going would be a lie.
    public static int MatchesPlayed => PlayerPrefs.GetInt(PlayedKey, 0);
    public static int MatchesWon => PlayerPrefs.GetInt(WonKey, 0);
    public static int MatchesLost => Mathf.Max(0, MatchesPlayed - MatchesWon);

    public static float CurrentIQ => PlayerIQManager.Instance != null ? PlayerIQManager.Instance.GetLocalIQ() : 100f;

    // Never below the current value, so a player from before this was tracked still reads sensibly.
    public static float BestIQ => Mathf.Max(PlayerPrefs.GetFloat(BestIQKey, 0f), CurrentIQ);

    // 0 until the server has answered this session.
    public static int LeaderboardRank { get; private set; }
    public static int LeaderboardSize { get; private set; }

    // A random id made once per device, so the leaderboard can tell players apart without accounts.
    // Names cannot do that job: two players can both be called "Jogador".
    public static string PlayerId
    {
        get
        {
            string id = PlayerPrefs.GetString(PlayerIdKey, string.Empty);

            if (string.IsNullOrEmpty(id))
            {
                id = Guid.NewGuid().ToString("N");
                PlayerPrefs.SetString(PlayerIdKey, id);
                PlayerPrefs.Save();
            }

            return id;
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void StampFirstPlayed()
    {
        if (NetworkBootstrap.IsDedicatedServerBuild)
            return;

        if (string.IsNullOrEmpty(PlayerPrefs.GetString(FirstPlayedKey, string.Empty)))
        {
            PlayerPrefs.SetString(FirstPlayedKey, DateTime.Today.ToString(DateFormat));
            PlayerPrefs.Save();
        }
    }

    public static void RecordMatch(bool didWin)
    {
        PlayerPrefs.SetInt(PlayedKey, MatchesPlayed + 1);

        if (didWin)
            PlayerPrefs.SetInt(WonKey, MatchesWon + 1);

        PlayerPrefs.Save();
        Changed?.Invoke();
    }

    // Called on every IQ write, which is how the best score keeps up without anything else having
    // to remember to.
    public static void NoteIQ(float iq)
    {
        if (iq <= PlayerPrefs.GetFloat(BestIQKey, 0f))
            return;

        PlayerPrefs.SetFloat(BestIQKey, iq);
        PlayerPrefs.Save();
    }

    public static void SetLeaderboardPosition(int rank, int size)
    {
        LeaderboardRank = rank;
        LeaderboardSize = size;
        Changed?.Invoke();
    }

    // "1 ano, 2 meses e 5 dias", counting from the day this device first opened Estalo. Calendar
    // months rather than 30-day blocks, so the day a month ticks over matches the calendar.
    public static string TimePlayingText()
    {
        DateTime first;

        if (!DateTime.TryParseExact(PlayerPrefs.GetString(FirstPlayedKey, string.Empty), DateFormat,
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out first))
            first = DateTime.Today;

        DateTime today = DateTime.Today;

        int years = today.Year - first.Year;
        int months = today.Month - first.Month;
        int days = today.Day - first.Day;

        if (days < 0)
        {
            DateTime previousMonth = today.AddMonths(-1);
            days += DateTime.DaysInMonth(previousMonth.Year, previousMonth.Month);
            months--;
        }

        if (months < 0)
        {
            months += 12;
            years--;
        }

        var parts = new System.Collections.Generic.List<string>();

        if (years > 0)
            parts.Add(years == 1 ? "1 ano" : years + " anos");

        if (months > 0)
            parts.Add(months == 1 ? "1 mês" : months + " meses");

        if (days > 0 || parts.Count == 0)
            parts.Add(days == 1 ? "1 dia" : days + " dias");

        return parts.Count == 1
            ? parts[0]
            : string.Join(", ", parts.GetRange(0, parts.Count - 1)) + " e " + parts[parts.Count - 1];
    }
}
