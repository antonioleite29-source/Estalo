using System.Collections.Generic;
using UnityEngine;

// Server-side queue. Players press Start, land here, and get grouped into as many concurrent
// matches as the queue can fill: 4 ready for 1v1 makes 2 matches, 6 makes 3, 8 makes 4; 8 ready
// for 2v2 makes 2. Anyone left over stays queued and is matched as soon as enough others arrive.
//
// Grouping is by difficulty level (derived from each player's IQ), so players meet opponents near
// their own level instead of whoever happened to press Start first.
//
// For the first PatienceSeconds a player will only be put with others on exactly their level. After
// that they take the nearest level still waiting, because a wait with nobody to play is worse than
// an uneven match. Two people on different levels who are the only ones online therefore play --
// two seconds later.
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

        // And the rules they picked. Someone who chose Sem perdao never ends up in a Classico
        // match: a rule set the player did not ask for would be a different game.
        public GameRuleSet ruleSet;

        // When they pressed Start, so the queue can tell someone who just arrived from someone who
        // has been waiting and would rather play anybody.
        public float queuedAt;
    }

    // How long a player waits for their own level before the queue starts offering them the nearest
    // one instead.
    public const float PatienceSeconds = 2f;

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

    public int QueuedCountFor(MatchMode mode, GameRuleSet ruleSet)
    {
        int count = 0;

        for (int i = 0; i < queue.Count; i++)
            if (queue[i].mode == mode && queue[i].ruleSet == ruleSet)
                count++;

        return count;
    }

    public static int RequiredPlayersFor(MatchMode mode) => mode == MatchMode.TeamFour ? 4 : 2;

    public void Enqueue(ulong clientId, int difficultyLevel, MatchMode mode, GameRuleSet ruleSet)
    {
        int existing = IndexOf(clientId);

        if (existing >= 0)
        {
            // Already waiting — refresh rather than queueing them twice. The mode is refreshed too,
            // so someone who changes their mind while waiting moves to the other queue.
            QueuedPlayer updated = queue[existing];
            updated.difficultyLevel = difficultyLevel;
            updated.mode = mode;
            updated.ruleSet = ruleSet;
            queue[existing] = updated;
            return;
        }

        queue.Add(new QueuedPlayer
        {
            clientId = clientId, difficultyLevel = difficultyLevel, mode = mode, ruleSet = ruleSet,
            queuedAt = Time.unscaledTime
        });
    }

    public void Remove(ulong clientId)
    {
        int index = IndexOf(clientId);

        if (index >= 0)
            queue.RemoveAt(index);
    }

    // Pulls out every complete group the queue can currently form. Returns one list of client ids
    // per match, ordered so callers can hand out seats by position.
    //
    // Groups are taken closest-levels-first: every run of neighbouring players in the sorted queue
    // is considered, and the tightest one that everybody in it will accept is formed, then the next.
    // Nobody who has waited less than PatienceSeconds accepts a run that is not all one level.
    public List<List<ulong>> FormMatches(MatchMode mode, GameRuleSet ruleSet)
    {
        List<List<ulong>> formed = new List<List<ulong>>();
        int required = RequiredPlayersFor(mode);
        float now = Time.unscaledTime;

        while (true)
        {
            // Only the people who asked for THIS mode and THESE rules. Someone waiting for 2v2 must
            // never be pulled into a 1v1 just because they happened to be in the queue.
            List<QueuedPlayer> waiting = new List<QueuedPlayer>();

            for (int i = 0; i < queue.Count; i++)
                if (queue[i].mode == mode && queue[i].ruleSet == ruleSet)
                    waiting.Add(queue[i]);

            if (waiting.Count < required)
                break;

            // Sorting by level puts similar players next to each other, so a run of neighbours is
            // the closest grouping available. Ties keep queue order, so longer waits are served
            // first.
            waiting.Sort((a, b) => a.difficultyLevel.CompareTo(b.difficultyLevel));

            int bestStart = -1;
            int bestSpread = int.MaxValue;

            for (int start = 0; start + required <= waiting.Count; start++)
            {
                int spread = waiting[start + required - 1].difficultyLevel - waiting[start].difficultyLevel;

                if (spread >= bestSpread || !EveryoneAccepts(waiting, start, required, spread, now))
                    continue;

                bestStart = start;
                bestSpread = spread;

                if (spread == 0)
                    break;      // nothing beats everybody on the same level
            }

            if (bestStart < 0)
                break;

            List<ulong> group = new List<ulong>(required);

            for (int i = 0; i < required; i++)
                group.Add(waiting[bestStart + i].clientId);

            foreach (ulong clientId in group)
                Remove(clientId);

            formed.Add(group);
        }

        return formed;
    }

    // A run of players on the same level is always fine. A run that spans levels needs everybody in
    // it to have waited out their patience: a player who pressed Start a moment ago is still owed
    // the chance of an even match.
    private static bool EveryoneAccepts(List<QueuedPlayer> waiting, int start, int required, int spread, float now)
    {
        if (spread == 0)
            return true;

        for (int i = start; i < start + required; i++)
            if (now - waiting[i].queuedAt < PatienceSeconds)
                return false;

        return true;
    }

    public int TakeNextMatchId() => nextMatchId++;

    // How many more players this queue needs before another match can form. Drives the
    // "waiting for N more players" line on the waiting screen.
    public int PlayersStillNeeded(MatchMode mode, GameRuleSet ruleSet)
    {
        int required = RequiredPlayersFor(mode);
        int remainder = QueuedCountFor(mode, ruleSet) % required;
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
