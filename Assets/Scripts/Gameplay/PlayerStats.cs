using System;
using System.Text;
using Unity.Netcode;
using UnityEngine;

// This player's record for the Profile page: matches played, won and lost, best and current IQ
// points, how long they have been playing, and where they sit on the global leaderboard.
//
// The server owns all of it (PlayerAccounts). What is kept here is a copy of the last thing the server
// said, so the lobby and the Profile page have something to draw while offline. Editing these saved
// numbers changes what this phone shows and nothing else: the next connection overwrites them, and
// matchmaking and the leaderboard only ever read the server's copy.
public static class PlayerStats
{
    // Per virtual player: Multiplayer Play Mode clones share this machine's PlayerPrefs.
    private static string Suffix => NetworkBootstrap.GetLocalProfileSuffix();

    private static string AccountKeyKey => "EstaloAccountKey" + Suffix;
    private static string PlayedKey => "EstaloMatchesPlayed" + Suffix;
    private static string WonKey => "EstaloMatchesWon" + Suffix;
    private static string LostKey => "EstaloMatchesLost" + Suffix;
    private static string BestIQKey => "EstaloBestIQ" + Suffix;
    private static string FirstPlayedKey => "EstaloFirstPlayed" + Suffix;
    private static string ServerFirstSeenKey => "EstaloServerFirstSeen" + Suffix;
    private static string ClaimPendingKey => "EstaloStartingRecordPending" + Suffix;

    private const string DateFormat = "yyyy-MM-dd";

    // Fired when anything shown on the Profile page changes, including the server's answer arriving a
    // moment after the page opened.
    public static event Action Changed;

    // Only matches with a winner count. One that ends because a player dropped or nobody answered
    // for a minute has no result, and a loss for somebody's Wi-Fi going would be a lie.
    public static int MatchesPlayed => PlayerPrefs.GetInt(PlayedKey, 0);
    public static int MatchesWon => PlayerPrefs.GetInt(WonKey, 0);
    public static int MatchesLost => PlayerPrefs.GetInt(LostKey, Mathf.Max(0, MatchesPlayed - MatchesWon));

    public static float CurrentIQ => PlayerIQManager.Instance != null ? PlayerIQManager.Instance.GetLocalIQ() : 100f;

    public static float BestIQ => Mathf.Max(PlayerPrefs.GetFloat(BestIQKey, 0f), CurrentIQ);

    // 0 until the server has answered this session.
    // Remembered between launches: the Profile page used to say "sem conexão" for the whole second
    // between opening it and the server answering, on a phone that was plainly online.
    private static string RankKey => "EstaloRank" + Suffix;
    private static string RankSizeKey => "EstaloRankSize" + Suffix;

    public static int LeaderboardRank
    {
        get => PlayerPrefs.GetInt(RankKey, 0);
        private set => PlayerPrefs.SetInt(RankKey, value);
    }

    public static int LeaderboardSize
    {
        get => PlayerPrefs.GetInt(RankSizeKey, 0);
        private set => PlayerPrefs.SetInt(RankSizeKey, value);
    }

    // Whether the server can be reached right now, so a page can tell "not known yet" from "offline".
    public static bool ServerReachable => CanReachServer(out _);

    // --- the account key ------------------------------------------------

    // The secret the server gave this phone on its first connection. Empty until then.
    private static string AccountKey => PlayerPrefs.GetString(AccountKeyKey, string.Empty);

    // Sent inside every connection request, so the server knows whose account this is.
    public static byte[] ConnectionPayload() => Encoding.UTF8.GetBytes(AccountKey);

    // The Unity login tied to this account, empty if the player has not logged in.
    private static string UsernameKey => "EstaloUsername" + Suffix;
    public static string Username => PlayerPrefs.GetString(UsernameKey, string.Empty);

    // Raised when the server has checked a login: true if it was tied to the account.
    public static event Action<bool> LoginFinished;

    // Raised when the server sends this device away because the account was opened on another one.
    public static event Action AccountOpenedElsewhere;

    public static void NoteAccountOpenedElsewhere()
    {
        Debug.Log("PlayerStats: this account was opened on another device, so this one was signed out.");
        AccountOpenedElsewhere?.Invoke();
    }

    public static bool SendLogin(string accessToken, string username)
    {
        if (!CanReachServer(out TriviaNetworkSync sync))
            return false;

        // Before the account's record lands on this phone, put aside what the phone had of its own.
        RememberWhatThisPhoneWas();

        sync.LinkLoginServerRpc(accessToken, username);
        return true;
    }

    public static void ApplyLoginResult(bool accepted)
    {
        LoginFinished?.Invoke(accepted);
    }

    // --- logging out ------------------------------------------------------

    // What this phone was before somebody signed in on it. Signing in moves the phone onto the
    // account's record -- its IQ, its matches, its name and face -- and signing out has to give the
    // phone back what it had, not leave it wearing somebody else's history.
    //
    // Kept in PlayerPrefs rather than in memory: the player may sign in, close the game, open it a
    // week later and sign out, and the phone they get back should still be their own.
    private static string BeforeLoginKey(string key) => key + "BeforeLogin";

    private static readonly string[] PutAsideOnLogin =
    {
        "EstaloAccountKey", "EstaloMatchesPlayed", "EstaloMatchesWon", "EstaloMatchesLost",
        "EstaloBestIQ", "EstaloServerFirstSeen", "EstaloRank", "EstaloRankSize", "EstaloUsername",
        "PlayerProfileName", "PlayerProfileAvatarIndex", "PlayerIQ_Local",
    };

    public static bool IsLoggedIn => !string.IsNullOrEmpty(Username);

    private static void RememberWhatThisPhoneWas()
    {
        // Only the first sign-in puts anything aside. Signing into a second account without signing
        // out of the first must not overwrite the phone's own record with the first account's.
        if (PlayerPrefs.GetInt(BeforeLoginKey("EstaloSaved") + Suffix, 0) == 1)
            return;

        foreach (string key in PutAsideOnLogin)
            CopyPref(key + Suffix, BeforeLoginKey(key) + Suffix);

        PlayerPrefs.SetInt(BeforeLoginKey("EstaloSaved") + Suffix, 1);
        PlayerPrefs.Save();
    }

    // Signs out: the account stays on the server untouched, and this phone goes back to the record
    // it had before it ever signed in. The connection is made again straight away, because the
    // account key travels in the connection request and the server reads it only then.
    public static void LogOut()
    {
        if (PlayerPrefs.GetInt(BeforeLoginKey("EstaloSaved") + Suffix, 0) != 1)
        {
            // Signed in on a phone that had nothing of its own to go back to -- a fresh install, or
            // an account made here. It starts over as a new phone: the server opens an account for
            // it on the next connection.
            foreach (string key in PutAsideOnLogin)
                PlayerPrefs.DeleteKey(key + Suffix);
        }
        else
        {
            foreach (string key in PutAsideOnLogin)
            {
                CopyPref(BeforeLoginKey(key) + Suffix, key + Suffix);
                PlayerPrefs.DeleteKey(BeforeLoginKey(key) + Suffix);
            }

            PlayerPrefs.DeleteKey(BeforeLoginKey("EstaloSaved") + Suffix);
        }

        PlayerPrefs.Save();

        foreach (IQBarController bar in UnityEngine.Object.FindObjectsByType<IQBarController>())
            bar.RefreshFromManager();

        Changed?.Invoke();

        if (NetworkBootstrap.Instance != null)
            NetworkBootstrap.Instance.ReconnectNow();

        Debug.Log("PlayerStats: signed out, and this phone has its own record back.");
    }

    // PlayerPrefs has no "copy", and the type is only known by which getter returns something, so
    // each value is carried over under the type it was stored as.
    private static void CopyPref(string from, string to)
    {
        if (!PlayerPrefs.HasKey(from))
        {
            PlayerPrefs.DeleteKey(to);
            return;
        }

        string asString = PlayerPrefs.GetString(from, null);

        if (!string.IsNullOrEmpty(asString))
        {
            PlayerPrefs.SetString(to, asString);
            return;
        }

        float asFloat = PlayerPrefs.GetFloat(from, float.NaN);

        if (!float.IsNaN(asFloat))
        {
            PlayerPrefs.SetFloat(to, asFloat);
            return;
        }

        PlayerPrefs.SetInt(to, PlayerPrefs.GetInt(from, 0));
    }

    // --- the password hint ----------------------------------------------

    // Raised when the server answers with the hint for a username: the username asked about, and the
    // hint, which is empty when that account has none or does not exist.
    public static event Action<string, string> PasswordHintArrived;

    public static bool SendPasswordHint(string hint)
    {
        if (!CanReachServer(out TriviaNetworkSync sync))
            return false;

        sync.SetPasswordHintServerRpc(hint ?? string.Empty);
        return true;
    }

    public static bool RequestPasswordHint(string username)
    {
        if (!CanReachServer(out TriviaNetworkSync sync))
            return false;

        sync.RequestPasswordHintServerRpc(username ?? string.Empty);
        return true;
    }

    public static void ApplyPasswordHint(string username, string hint)
    {
        PasswordHintArrived?.Invoke(username ?? string.Empty, hint ?? string.Empty);
    }

    // --- deleting the account -------------------------------------------

    // Raised once the server has thrown the account away and this phone has forgotten it.
    public static event Action Deleted;

    // Asks the server to delete this account for good. False while offline: there is nothing to
    // delete on the phone alone, and pretending otherwise would leave the record on the server.
    public static bool RequestDelete()
    {
        if (!CanReachServer(out TriviaNetworkSync sync))
            return false;

        sync.DeleteAccountServerRpc();
        return true;
    }

    // Called by the server once the record is gone. Everything this phone saved goes with it: the
    // account key, the match record, the IQ, the name and face, and the flags that remember the
    // tutorials. What is left is a phone in the state it was in the day Estalo was installed.
    //
    // DeleteAll covers the lot, including keys added later that nobody remembered to list here. In
    // the Editor it also clears the virtual players' copies, which share this machine's PlayerPrefs.
    public static void ForgetEverything()
    {
        PlayerPrefs.DeleteAll();
        PlayerPrefs.Save();

        // The name and face are read back from their defaults now, so the match in progress and the
        // lobby stop showing whoever this used to be.
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.LocalClient != null)
            NetworkManager.Singleton.LocalClient.PlayerObject?.GetComponent<PlayerSideIdentity>()?.PushLocalProfile();

        foreach (IQBarController bar in UnityEngine.Object.FindObjectsByType<IQBarController>())
            bar.RefreshFromManager();

        Debug.Log("PlayerStats: the account was deleted, and this phone has forgotten everything it kept.");

        Deleted?.Invoke();
        Changed?.Invoke();
    }

    // --- talking to the server ------------------------------------------

    // Asks for a fresh copy, e.g. when the Profile page opens. Does nothing while offline.
    public static void RequestRefresh()
    {
        if (CanReachServer(out TriviaNetworkSync sync))
            sync.RequestAccountServerRpc();
    }

    // True if the ask went out. A page that opens before the connection is up uses this to try again
    // rather than showing the player an error that is really just a wait.
    public static bool TryRequestRefresh()
    {
        if (!CanReachServer(out TriviaNetworkSync sync))
            return false;

        sync.RequestAccountServerRpc();
        return true;
    }

    // Called by the server whenever this player's account changes: on connecting, after each match,
    // after a claim, and when asked.
    public static void ApplyServerAccount(string key, float iq, float bestIQ, int played, int won, int lost,
        long firstSeen, bool claimed, int rank, int size, string username,
        string accountName = null, int avatarIndex = -1)
    {
        PlayerPrefs.SetString(UsernameKey, username ?? string.Empty);

        bool firstAccount = string.IsNullOrEmpty(AccountKey);

        if (!string.IsNullOrEmpty(key))
            PlayerPrefs.SetString(AccountKeyKey, key);

        // A phone that already had a record before the server kept them hands it over, once, so
        // nobody who played before this change is sent back to 100.
        if (firstAccount && HasRecordOfItsOwn())
            PlayerPrefs.SetInt(ClaimPendingKey, 1);

        if (PlayerPrefs.GetInt(ClaimPendingKey, 0) == 1 && !claimed && played == 0)
        {
            PlayerPrefs.Save();

            // The server answers with another update carrying the claimed record. Until then the
            // phone's own numbers stay on screen rather than flicking to 100 and back.
            SendClaim();
            return;
        }

        PlayerPrefs.DeleteKey(ClaimPendingKey);

        PlayerPrefs.SetInt(PlayedKey, played);
        PlayerPrefs.SetInt(WonKey, won);
        PlayerPrefs.SetInt(LostKey, lost);
        PlayerPrefs.SetFloat(BestIQKey, bestIQ);
        PlayerPrefs.SetString(ServerFirstSeenKey, firstSeen.ToString());

        if (PlayerIQManager.Instance != null)
            PlayerIQManager.Instance.SetLocalIQ(iq);

        LeaderboardRank = rank;
        LeaderboardSize = size;

        // Sent only just after a login, so this never overwrites a name typed on this phone.
        ApplyProfileFromServer(accountName, avatarIndex);

        PlayerPrefs.Save();

        foreach (IQBarController bar in UnityEngine.Object.FindObjectsByType<IQBarController>())
            bar.RefreshFromManager();

        Changed?.Invoke();
    }

    // The account's own name and face, put on this phone after signing in, and pushed straight out to
    // the network so a match in progress shows them too.
    private static void ApplyProfileFromServer(string accountName, int avatarIndex)
    {
        PlayerProfileManager profile = PlayerProfileManager.Instance;

        if (profile == null)
            return;

        bool changed = false;

        if (!string.IsNullOrWhiteSpace(accountName) && accountName != profile.GetLocalName())
        {
            profile.SetLocalName(accountName);
            changed = true;
        }

        if (avatarIndex >= 0 && avatarIndex != profile.GetLocalAvatarIndex())
        {
            profile.SetLocalAvatarIndex(avatarIndex);
            changed = true;
        }

        if (!changed || NetworkManager.Singleton == null || NetworkManager.Singleton.LocalClient == null)
            return;

        NetworkObject localPlayer = NetworkManager.Singleton.LocalClient.PlayerObject;
        localPlayer?.GetComponent<PlayerSideIdentity>()?.PushLocalProfile();
    }

    // The first-run tutorial's placement round is played on the phone, so its result has to be handed
    // to the server rather than decided there. The server takes it only for an account that has not
    // played or claimed yet.
    public static void MarkStartingRecordPending()
    {
        PlayerPrefs.SetInt(ClaimPendingKey, 1);
        PlayerPrefs.Save();
        SendClaim();
    }

    private static void SendClaim()
    {
        if (CanReachServer(out TriviaNetworkSync sync))
            sync.ClaimStartingRecordServerRpc(CurrentIQ, MatchesPlayed, MatchesWon, FirstPlayedUnix());
    }

    private static bool HasRecordOfItsOwn() =>
        MatchesPlayed > 0 || (PlayerIQManager.Instance != null && PlayerIQManager.Instance.HasSavedIQ());

    private static bool CanReachServer(out TriviaNetworkSync sync)
    {
        sync = TriviaNetworkSync.Instance;

        return sync != null && sync.IsSpawned &&
               NetworkManager.Singleton != null && NetworkManager.Singleton.IsConnectedClient;
    }

    // --- how long they have been playing --------------------------------

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

    // The earlier of the day this phone first opened Estalo and the day the server first saw the
    // account -- a reinstalled phone forgets the first, a new server forgets the second.
    private static DateTime FirstPlayed()
    {
        DateTime first = DateTime.Today;

        if (DateTime.TryParseExact(PlayerPrefs.GetString(FirstPlayedKey, string.Empty), DateFormat,
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out DateTime local) && local < first)
            first = local;

        if (long.TryParse(PlayerPrefs.GetString(ServerFirstSeenKey, string.Empty), out long seconds) && seconds > 0)
        {
            DateTime server = DateTimeOffset.FromUnixTimeSeconds(seconds).LocalDateTime.Date;

            if (server < first)
                first = server;
        }

        return first;
    }

    private static long FirstPlayedUnix() => new DateTimeOffset(FirstPlayed()).ToUnixTimeSeconds();

    // "1 ano, 2 meses e 5 dias". Calendar months rather than 30-day blocks, so the day a month ticks
    // over matches the calendar.
    public static string TimePlayingText()
    {
        DateTime first = FirstPlayed();
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
