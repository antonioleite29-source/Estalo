using System.Collections.Generic;

// Server-side queue. Players press Start, land here, and get grouped into as many concurrent
// matches as the queue can fill: 4 ready for 1v1 makes 2 matches, 6 makes 3, 8 makes 4; 8 ready
// for 2v2 makes 2. Anyone left over stays queued and is matched as soon as enough others arrive.
//
// Grouping is by difficulty level (derived from each player's IQ), so players meet opponents near
// their own level instead of whoever happened to press Start first.
public class Matchmaker
{
    public struct QueuedPlayer
    {
        public ulong clientId;
        public int difficultyLevel;

        // Each player queues for the mode they picked on their own device. There is no room-wide
        // mode: one person choosing 2v2 must not drag everyone else into it, and with concurrent
        // matches there is no reason it should — a 1v1 and a 2v2 can be forming at the same time.
        public MatchMode mode;
    }

    private readonly List<QueuedPlayer> queue = new List<QueuedPlayer>();
    private int nextMatchId = 1;

    public int QueuedCount => queue.Count;

    public int QueuedCountFor(MatchMode mode)
    {
        int count = 0;

        for (int i = 0; i < queue.Count; i++)
            if (queue[i].mode == mode)
                count++;

        return count;
    }

    public static int RequiredPlayersFor(MatchMode mode) => mode == MatchMode.TeamFour ? 4 : 2;

    public bool IsQueued(ulong clientId) => IndexOf(clientId) >= 0;

    public void Enqueue(ulong clientId, int difficultyLevel, MatchMode mode)
    {
        int existing = IndexOf(clientId);

        if (existing >= 0)
        {
            // Already waiting — refresh rather than queueing them twice. The mode is refreshed too,
            // so someone who changes their mind while waiting moves to the other queue.
            QueuedPlayer updated = queue[existing];
            updated.difficultyLevel = difficultyLevel;
            updated.mode = mode;
            queue[existing] = updated;
            return;
        }

        queue.Add(new QueuedPlayer { clientId = clientId, difficultyLevel = difficultyLevel, mode = mode });
    }

    public void Remove(ulong clientId)
    {
        int index = IndexOf(clientId);

        if (index >= 0)
            queue.RemoveAt(index);
    }

    public void Clear() => queue.Clear();

    // Pulls out every complete group the queue can currently form. Returns one list of client ids
    // per match, ordered so callers can hand out seats by position.
    public List<List<ulong>> FormMatches(MatchMode mode)
    {
        List<List<ulong>> formed = new List<List<ulong>>();
        int required = RequiredPlayersFor(mode);

        // Only the people who asked for THIS mode. Someone waiting for 2v2 must never be pulled
        // into a 1v1 just because they happened to be in the queue.
        List<QueuedPlayer> sorted = new List<QueuedPlayer>();

        for (int i = 0; i < queue.Count; i++)
            if (queue[i].mode == mode)
                sorted.Add(queue[i]);

        if (sorted.Count < required)
            return formed;

        // Sorting by level puts similar players adjacent, so taking consecutive runs groups the
        // closest available skill levels. Ties keep queue order, so longer waits are served first.
        sorted.Sort((a, b) => a.difficultyLevel.CompareTo(b.difficultyLevel));

        int groupCount = sorted.Count / required;

        for (int g = 0; g < groupCount; g++)
        {
            List<ulong> group = new List<ulong>(required);

            for (int i = 0; i < required; i++)
                group.Add(sorted[g * required + i].clientId);

            formed.Add(group);
        }

        // Whoever did not fill a complete group stays queued for the next round of matching.
        for (int g = 0; g < groupCount; g++)
            for (int i = 0; i < required; i++)
                Remove(sorted[g * required + i].clientId);

        return formed;
    }

    public int TakeNextMatchId() => nextMatchId++;

    // How many more players this queue needs before another match can form. Drives the
    // "waiting for N more players" line on the waiting screen.
    public int PlayersStillNeeded(MatchMode mode)
    {
        int required = RequiredPlayersFor(mode);
        int remainder = QueuedCountFor(mode) % required;
        return remainder == 0 ? 0 : required - remainder;
    }

    private int IndexOf(ulong clientId)
    {
        for (int i = 0; i < queue.Count; i++)
            if (queue[i].clientId == clientId)
                return i;

        return -1;
    }
}
