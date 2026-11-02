using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

// Server-side player accounts: the one place a player's IQ and match record are allowed to change.
//
// A phone used to keep its own IQ and report it -- to matchmaking, and then to the leaderboard -- so
// anyone who could edit the app's saved data could be whatever they liked. Now the server gives each
// phone a secret key the first time it connects, keeps that player's IQ, best IQ, wins and losses
// against it, and changes them itself when a match it refereed ends. The phone keeps a copy only to
// draw the lobby and the Profile page, and the server's copy wins every time the two meet.
//
// The key travels in the connection request, so the account is known before the player can queue.
// Only a hash of it is stored: a leaked accounts.json does not let anyone play as anyone else.
//
// Not bulletproof. The key crosses the network unencrypted, and a brand-new account accepts the
// phone's own starting record once (see Claim) so the first-run placement and anyone who played before
// this existed keep their IQ. It stops the easy cheat -- editing a saved number -- not a determined one.
public static class PlayerAccounts
{
    public const float StartingIQ = 100f;

    // The most a one-time claim may bring in. Anything past this is not a tester's history.
    private const int MaxClaimedMatches = 1000;

    [Serializable]
    public class Account
    {
        public string id;          // public and safe to show; never used to sign in
        public string keyHash;     // SHA-256 of the secret key the phone holds
        public string name;
        public int avatarIndex = -1;   // -1 until the player has picked one
        public float iq = StartingIQ;
        public float bestIQ = StartingIQ;
        public int played;
        public int won;
        public int lost;
        public long firstSeen;     // unix seconds
        public bool claimed;       // the one-time starting record has been used

        // Set once the player logs in. Each phone signed into this account holds a key of its own.
        public string unityPlayerId;
        public string username;

        // The questions this player has seen lately, newest last, as level * 10000 + index. Kept so
        // the next match does not open with the question the last one closed on.
        public List<int> recentQuestions = new List<int>();

        // A reminder the player wrote for themselves. Unity's username-and-password login has no
        // email, so there is no reset link anywhere: a forgotten password is a lost account, and this
        // is the only thing standing between the two.
        public string passwordHint;
        public List<string> otherKeyHashes = new List<string>();
    }

    [Serializable]
    private class Table
    {
        public List<Account> accounts = new List<Account>();
    }

    private static Table table;
    private static readonly Dictionary<string, Account> byKeyHash = new Dictionary<string, Account>();
    private static readonly Dictionary<ulong, Account> online = new Dictionary<ulong, Account>();

    // Key presented in each connection request, held from approval until the client is connected.
    private static readonly Dictionary<ulong, string> presentedKeys = new Dictionary<ulong, string>();

    // Keys made this session that have not reached their phone yet.
    private static readonly Dictionary<ulong, string> unsentKeys = new Dictionary<ulong, string>();

    // On the cloud box, systemd's StateDirectory (/var/lib/triviaduel): the service user has no home and
    // ProtectHome hides it anyway, so persistentDataPath there cannot be written. Anywhere else -- a
    // phone hosting on the LAN, the Editor -- persistentDataPath is fine.
    private static string FilePath
    {
        get
        {
            string stateDirectory = Environment.GetEnvironmentVariable("STATE_DIRECTORY");
            string folder = string.IsNullOrEmpty(stateDirectory) ? Application.persistentDataPath : stateDirectory;
            return Path.Combine(folder, "accounts.json");
        }
    }

    // --- connecting -----------------------------------------------------

    // Called from connection approval, which is the only place the request's payload can be read.
    public static void NoteConnectionPayload(ulong clientId, byte[] payload)
    {
        presentedKeys[clientId] = payload != null && payload.Length > 0 ? Encoding.UTF8.GetString(payload) : string.Empty;
    }

    // Finds the account for the key this client connected with, or opens a new one when it had no
    // key or one this server does not know -- a first launch, or a reinstall.
    public static Account Attach(ulong clientId)
    {
        Load();

        presentedKeys.TryGetValue(clientId, out string key);
        presentedKeys.Remove(clientId);

        Account account = null;

        if (IsWellFormedKey(key))
            byKeyHash.TryGetValue(Hash(key), out account);

        if (account == null)
        {
            key = NewSecret();

            account = new Account
            {
                id = NewSecret().Substring(0, 12),
                keyHash = Hash(key),
                firstSeen = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            };

            table.accounts.Add(account);
            byKeyHash[account.keyHash] = account;
            unsentKeys[clientId] = key;
            Save();
        }

        online[clientId] = account;
        return account;
    }

    public static void Detach(ulong clientId)
    {
        online.Remove(clientId);
        presentedKeys.Remove(clientId);
        unsentKeys.Remove(clientId);
    }

    public static Account For(ulong clientId) => online.TryGetValue(clientId, out Account account) ? account : null;

    // Whoever else is already playing on this account, if anybody is. One account, one device at a
    // time: two phones on the same record would both write to it, and a match between them would be
    // a player against themselves.
    public static bool TryFindOtherDevice(Account account, ulong exceptClient, out ulong otherClient)
    {
        otherClient = 0;

        if (account == null)
            return false;

        foreach (KeyValuePair<ulong, Account> entry in online)
        {
            if (entry.Key != exceptClient && entry.Value == account)
            {
                otherClient = entry.Key;
                return true;
            }
        }

        return false;
    }

    // Throws the account away, with every key that reached it. A player may delete their account --
    // Google Play requires it of any app you can sign into -- and that has to mean the record is gone
    // from the server, not hidden. The phone that asked is left holding a key that points at nothing,
    // so its next connection opens a brand-new account.
    public static bool Delete(ulong clientId)
    {
        Account account = For(clientId);

        if (account == null)
            return false;

        Load();

        // One account, one device, so this is normally just the caller. Anyone else on it loses it too.
        List<ulong> holders = new List<ulong>();

        foreach (KeyValuePair<ulong, Account> entry in online)
            if (entry.Value == account)
                holders.Add(entry.Key);

        foreach (ulong holder in holders)
            online.Remove(holder);

        if (!string.IsNullOrEmpty(account.keyHash))
            byKeyHash.Remove(account.keyHash);

        if (account.otherKeyHashes != null)
            foreach (string hash in account.otherKeyHashes)
                if (!string.IsNullOrEmpty(hash))
                    byKeyHash.Remove(hash);

        table.accounts.Remove(account);
        presentedKeys.Remove(clientId);
        unsentKeys.Remove(clientId);
        Save();
        return true;
    }

    // The new key for this client if it has not been sent yet, otherwise empty. Taken exactly once.
    public static string TakeUnsentKey(ulong clientId)
    {
        if (!unsentKeys.TryGetValue(clientId, out string key))
            return string.Empty;

        unsentKeys.Remove(clientId);
        return key;
    }

    // --- changing a record ----------------------------------------------

    // The same step PlayerIQManager used to take on the phone: 50 / IQ, so it shrinks as IQ climbs.
    // Not saved here; the caller saves once after recording every player in the match.
    public static void RecordMatch(ulong clientId, bool didWin, string playerName)
    {
        Account account = For(clientId);

        if (account == null)
            return;

        float delta = 50f / Mathf.Max(1f, account.iq);
        account.iq += didWin ? delta : -delta;
        account.bestIQ = Mathf.Max(account.bestIQ, account.iq);
        account.played++;

        if (didWin)
            account.won++;
        else
            account.lost++;

        if (!string.IsNullOrWhiteSpace(playerName))
            account.name = DisplayNames.Clean(playerName);
    }

    // The one time the server takes numbers from the phone: an account that has never played a match
    // and never claimed may bring in a starting IQ and record. That covers the first-run placement
    // round, which is played on the phone, and everyone who played before the server kept records.
    // Clamped, and only once, so it cannot be used to climb.
    public static bool Claim(ulong clientId, float iq, int played, int won, long firstPlayed)
    {
        Account account = For(clientId);

        if (account == null || account.claimed || account.played > 0)
            return false;

        if (float.IsNaN(iq) || float.IsInfinity(iq))
            iq = StartingIQ;

        account.iq = Mathf.Clamp(iq, PlayerIQManager.IQ_MIN, PlayerIQManager.IQ_MAX);
        account.bestIQ = account.iq;
        account.played = Mathf.Clamp(played, 0, MaxClaimedMatches);
        account.won = Mathf.Clamp(won, 0, account.played);
        account.lost = account.played - account.won;

        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        if (firstPlayed > 0 && firstPlayed < account.firstSeen && firstPlayed <= now)
            account.firstSeen = firstPlayed;

        account.claimed = true;
        Save();
        return true;
    }

    // Ties a verified Unity login to an account. If the login already belongs to an account -- the
    // player signing in on a new phone, or after a reinstall -- this phone is moved onto that account
    // and given a key of its own for it. The account the phone had until then is left as it was.
    public static Account LinkLogin(ulong clientId, string unityPlayerId, string username)
    {
        Account current = For(clientId);

        if (current == null || string.IsNullOrEmpty(unityPlayerId))
            return current;

        Load();

        Account owner = table.accounts.Find(a => a != null && a.unityPlayerId == unityPlayerId);

        if (owner == null)
        {
            current.unityPlayerId = unityPlayerId;
            owner = current;
        }
        else if (owner != current)
        {
            string key = NewSecret();
            string hash = Hash(key);

            owner.otherKeyHashes.Add(hash);
            byKeyHash[hash] = owner;
            online[clientId] = owner;
            unsentKeys[clientId] = key;
        }

        if (!string.IsNullOrWhiteSpace(username))
            owner.username = username;

        Save();
        return owner;
    }

    // --- what they have seen lately ---------------------------------------

    // How many questions back an account remembers. Long enough that a player does not meet the
    // same question twice in an evening, short enough that a small pool does not run dry.
    public const int QuestionsRemembered = 80;

    private static int QuestionKey(int difficultyLevel, int questionIndex) =>
        difficultyLevel * 10000 + questionIndex;

    public static void RememberQuestion(ulong clientId, int difficultyLevel, int questionIndex)
    {
        Account account = For(clientId);

        if (account == null)
            return;

        if (account.recentQuestions == null)
            account.recentQuestions = new List<int>();

        int key = QuestionKey(difficultyLevel, questionIndex);
        account.recentQuestions.Remove(key);
        account.recentQuestions.Add(key);

        while (account.recentQuestions.Count > QuestionsRemembered)
            account.recentQuestions.RemoveAt(0);
    }

    // True when this player has had this question recently enough to remember it.
    public static bool HasSeenQuestion(ulong clientId, int difficultyLevel, int questionIndex)
    {
        Account account = For(clientId);

        return account != null && account.recentQuestions != null &&
               account.recentQuestions.Contains(QuestionKey(difficultyLevel, questionIndex));
    }

    // --- the password hint ----------------------------------------------

    // The most a hint may be. Long enough for a sentence, short enough not to be a second password
    // field or somewhere to write at other players.
    public const int MaxHintLength = 60;

    public static void SetHint(ulong clientId, string hint)
    {
        Account account = For(clientId);

        if (account == null)
            return;

        hint = (hint ?? string.Empty).Trim();

        if (hint.Length > MaxHintLength)
            hint = hint.Substring(0, MaxHintLength);

        account.passwordHint = hint;
        Save();
    }

    // Looked up by username, because the player asking has not signed in -- that is the whole point
    // of a hint. Anyone who knows a username can read its hint, so the phone warns the player of
    // that before they write one.
    public static string HintForUsername(string username)
    {
        if (string.IsNullOrWhiteSpace(username))
            return string.Empty;

        Load();

        username = username.Trim();
        Account account = table.accounts.Find(a => a != null && !string.IsNullOrEmpty(a.username) &&
                                                   string.Equals(a.username, username, StringComparison.OrdinalIgnoreCase));

        return account != null ? account.passwordHint ?? string.Empty : string.Empty;
    }

    // 1 is the top. Players on equal points share a position.
    public static (int rank, int size) RankOf(Account account)
    {
        Load();

        int rank = 1;

        foreach (Account other in table.accounts)
            if (other.iq > account.iq)
                rank++;

        return (rank, table.accounts.Count);
    }

    // --- storage --------------------------------------------------------

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
            // A damaged file must not take the server down with it. Moved aside rather than
            // overwritten, so the records can still be recovered by hand.
            Debug.LogError($"PlayerAccounts: could not read {FilePath}, starting empty. {e.Message}");

            try { File.Move(FilePath, FilePath + ".damaged-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss")); }
            catch (Exception) { }

            table = new Table();
        }

        if (table.accounts == null)
            table.accounts = new List<Account>();

        byKeyHash.Clear();

        foreach (Account account in table.accounts)
        {
            if (account == null)
                continue;

            if (account.otherKeyHashes == null)
                account.otherKeyHashes = new List<string>();

            if (account.recentQuestions == null)
                account.recentQuestions = new List<int>();

            if (!string.IsNullOrEmpty(account.keyHash))
                byKeyHash[account.keyHash] = account;

            foreach (string hash in account.otherKeyHashes)
                if (!string.IsNullOrEmpty(hash))
                    byKeyHash[hash] = account;
        }
    }

    // Written to a temporary file and moved into place, so a crash mid-write leaves the previous
    // records intact rather than half a file.
    public static void Save()
    {
        if (table == null)
            return;

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
            Debug.LogError($"PlayerAccounts: could not save {FilePath}. {e.Message}");
        }
    }

    // --- keys -----------------------------------------------------------

    private static string NewSecret()
    {
        byte[] bytes = new byte[16];

        using (RandomNumberGenerator random = RandomNumberGenerator.Create())
            random.GetBytes(bytes);

        return ToHex(bytes);
    }

    private static string Hash(string key)
    {
        using (SHA256 sha = SHA256.Create())
            return ToHex(sha.ComputeHash(Encoding.UTF8.GetBytes(key)));
    }

    private static bool IsWellFormedKey(string key)
    {
        if (string.IsNullOrEmpty(key) || key.Length != 32)
            return false;

        foreach (char c in key)
            if (!Uri.IsHexDigit(c))
                return false;

        return true;
    }

    private static string ToHex(byte[] bytes)
    {
        StringBuilder builder = new StringBuilder(bytes.Length * 2);

        foreach (byte b in bytes)
            builder.Append(b.ToString("x2"));

        return builder.ToString();
    }
}
