// The four ways a match can be played. The mode (1v1 or 2v2) says how many people are in it; the
// rule set says what happens inside it.
//
// Both sides of the wire use this: the phone shows the names and the one-liners, the server turns
// the choice into MatchRules and plays by them. Players only meet others who chose the same set, so
// nobody is dropped into rules they did not pick.
public enum GameRuleSet
{
    Classic = 0,
    NoMercy = 1,
    Lightning = 2,
    Streak = 3,
}

public static class GameRuleSets
{
    public static readonly GameRuleSet[] All =
    {
        GameRuleSet.Classic, GameRuleSet.NoMercy, GameRuleSet.Lightning, GameRuleSet.Streak,
    };

    public static string NameOf(GameRuleSet set)
    {
        switch (set)
        {
            case GameRuleSet.NoMercy: return "Sem perdão";
            case GameRuleSet.Lightning: return "Relâmpago";
            case GameRuleSet.Streak: return "Sequência";
            default: return "Clássico";
        }
    }

    public static string DescriptionOf(GameRuleSet set)
    {
        switch (set)
        {
            case GameRuleSet.NoMercy:
                return "Errou, o ponto é do adversário. Sem tempo sozinho.";
            case GameRuleSet.Lightning:
                return "Partida curta: 5 pontos e metade do tempo sozinho.";
            case GameRuleSet.Streak:
                return "Acertos seguidos valem mais: 1, depois 2, depois 3.";
            default:
                return "Errou, o adversário responde sozinho. O de sempre.";
        }
    }

    public static GameRuleSet FromInt(int value) =>
        value >= 0 && value <= (int)GameRuleSet.Streak ? (GameRuleSet)value : GameRuleSet.Classic;

    // Takes the mode's own tunables and bends them to the chosen rules. Everything a rule set
    // changes is in MatchRules, so MatchSession plays by the numbers it is handed and knows nothing
    // about which set they came from.
    public static MatchRules Apply(GameRuleSet set, MatchRules rules)
    {
        switch (set)
        {
            case GameRuleSet.NoMercy:
                rules.missGivesThePoint = true;
                break;

            case GameRuleSet.Lightning:
                rules.pointsToWin = 5;
                rules.soloTimeSeconds *= 0.5f;
                break;

            case GameRuleSet.Streak:
                rules.streakScoring = true;
                break;
        }

        return rules;
    }
}
