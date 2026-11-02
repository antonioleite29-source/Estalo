using System;
using UnityEngine;

// Which rules this phone is playing by, remembered between launches.
//
// The choice is the player's own and travels with their next Start: the server never decides it for
// them, and a player only ever meets others who picked the same one.
public static class GameRuleSetChoice
{
    private static string Key => "EstaloRuleSet" + NetworkBootstrap.GetLocalProfileSuffix();

    // Raised when the player picks a different set, so the lobby can relabel its button.
    public static event Action Changed;

    public static GameRuleSet Chosen
    {
        get => GameRuleSets.FromInt(PlayerPrefs.GetInt(Key, 0));
        set
        {
            if (value == Chosen)
                return;

            PlayerPrefs.SetInt(Key, (int)value);
            PlayerPrefs.Save();
            Changed?.Invoke();
        }
    }

    public static string ChosenName => GameRuleSets.NameOf(Chosen);
}
