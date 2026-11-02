using UnityEngine;

// The numbers for the queue this phone is actually in, sent by the server to that phone alone.
//
// The broadcast counts (NetQueuedOneVsOne, NetQueuedTeamFour) are one per mode and go to everyone,
// so they cannot tell a player waiting for a 2v2 Sequência how many others are waiting for the same
// thing -- they add up every rule set at once. That is how four people could be queued while no
// match formed and the screen gave no hint why.
public static class QueueStatus
{
    // Anything older than this is treated as nothing: the server speaks twice a second while
    // somebody is queued, so a gap this long means these numbers are no longer about now.
    private const float FreshSeconds = 3f;

    public static int Queued { get; private set; }
    public static int Required { get; private set; }
    public static GameRuleSet RuleSet { get; private set; }

    private static float heardAt;

    public static bool IsFresh => heardAt > 0f && Time.unscaledTime - heardAt <= FreshSeconds;

    public static void Note(int queued, int required, GameRuleSet ruleSet)
    {
        Queued = queued;
        Required = required;
        RuleSet = ruleSet;
        heardAt = Time.unscaledTime;
    }

    // On leaving the queue, so a stale pair of numbers cannot be drawn on the next wait.
    public static void Forget()
    {
        heardAt = 0f;
    }
}
