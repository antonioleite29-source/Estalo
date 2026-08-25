using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Puts the Portuguese wording into the places where the text lives in the scene rather than in
// code, so translating the scripts alone would leave them behind.
//
// A serialized string beats its own default the moment somebody types into the Inspector, and
// these were typed in English long before the game had a language. Changing the default in code
// does nothing to a scene that already carries a value.
public static class ApplyPortugueseText
{
    [MenuItem("Trivia Duel/Setup/Apply Portuguese UI Text")]
    public static void Apply()
    {
        int changed = 0;

        WaitingScreenController waiting =
            Object.FindAnyObjectByType<WaitingScreenController>(FindObjectsInactive.Include);

        if (waiting != null)
        {
            Undo.RecordObject(waiting, "Portuguese waiting screen");
            changed += Set(ref waiting.playersPrefix, "Jogadores: ");
            changed += Set(ref waiting.liveGamesPrefix, "Partidas em andamento: ");
            EditorUtility.SetDirty(waiting);
        }

        // Leftover English on labels that were laid out by hand. Matched on the exact old string so
        // this cannot touch anything that has since been reworded.
        changed += RenameLabels(new[]
        {
            ("Start", "Jogar"),
            ("Cancel", "Cancelar"),
            ("Pronto", "Pronto"),
            ("Current Level", "Nível atual"),
            ("IQ Points", "Pontos de IQ"),
            ("Resultado", "Resultado"),
            ("Players:", "Jogadores:"),
            ("Live games:", "Partidas em andamento:"),
            ("Seu IP: —", "Seu IP: —")
        });

        if (changed > 0)
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());

        Debug.Log($"ApplyPortugueseText: {changed} string(s) changed. Save the scene to keep it.");
    }

    private static int Set(ref string field, string value)
    {
        if (field == value)
            return 0;

        field = value;
        return 1;
    }

    private static int RenameLabels((string from, string to)[] pairs)
    {
        int changed = 0;

        foreach (TMP_Text text in Object.FindObjectsByType<TMP_Text>(FindObjectsInactive.Include))
        {
            foreach ((string from, string to) in pairs)
            {
                if (text.text.Trim() != from || from == to)
                    continue;

                Undo.RecordObject(text, "Portuguese label");
                text.text = to;
                EditorUtility.SetDirty(text);
                changed++;
                break;
            }
        }

        return changed;
    }
}
