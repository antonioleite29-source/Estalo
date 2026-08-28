using System.Collections.Generic;
using UnityEngine;

// The list of lessons on the learning path, and how far along it the player has got.
//
// Thirty-two lessons: every topic twice, once early and once late, with the difficulty ramping
// from level 1 to level 7 across the whole ladder. So a topic is met once while it is gentle and
// again when it is hard, which is how the same material is supposed to be revisited.
public static class LessonLadder
{
    // Roughly the order these are taught in. Arithmetic first because everything else leans on it,
    // reasoning last because it leans on everything.
    private static readonly string[] TopicOrder =
    {
        "adicao", "subtracao", "multiplicacao", "divisao",
        "fracoes", "porcentagem", "potencias", "geometria",
        "sequencias", "algebra", "logica", "analogias",
        "classificacao", "probabilidade", "raciocinio", "geral"
    };

    public const int Count = 32;

    public struct Lesson
    {
        public int Number;        // 1-based, what the player sees on the button
        public string Topic;
        public int Level;         // 1-7, the same scale PlayerIQManager uses
    }

    public static Lesson At(int index)
    {
        index = Mathf.Clamp(index, 0, Count - 1);

        return new Lesson
        {
            Number = index + 1,
            Topic = TopicOrder[index % TopicOrder.Length],

            // Linear from 1 to 7 across the ladder, so the second pass through the topics is
            // always harder than the first.
            Level = Mathf.Clamp(1 + Mathf.FloorToInt(index * 6f / (Count - 1)), 1, 7)
        };
    }

    // --- progress -------------------------------------------------------

    // Per virtual player, same reasoning as everywhere else: Multiplayer Play Mode clones share
    // this machine's PlayerPrefs with the main Editor.
    private static string DoneKey => "EstaloLessonsDone" + NetworkBootstrap.GetLocalProfileSuffix();

    // How many lessons have been finished. Lesson n is unlocked when n - 1 are done, so this one
    // number is the whole progression.
    public static int Completed
    {
        get => Mathf.Clamp(PlayerPrefs.GetInt(DoneKey, 0), 0, Count);
        private set
        {
            PlayerPrefs.SetInt(DoneKey, Mathf.Clamp(value, 0, Count));
            PlayerPrefs.Save();
        }
    }

    public static bool IsUnlocked(int index) => index <= Completed;

    public static bool IsDone(int index) => index < Completed;

    // Called when a lesson is finished. Only ever moves forward: replaying lesson 3 after reaching
    // 10 must not send anybody back to 4.
    public static void MarkDone(int index)
    {
        if (index == Completed)
            Completed = index + 1;
    }

    public static void ResetProgress()
    {
        Completed = 0;
    }

    // The questions for one lesson: that topic at that level, falling back to the level on its own
    // when a topic has too few. A lesson that cannot fill itself is worse than a slightly
    // off-topic one.
    public static List<TriviaQuestion> BuildSet(Lesson lesson, int wanted)
    {
        List<TriviaQuestion> pool = new List<TriviaQuestion>();
        List<TriviaQuestion> offTopic = new List<TriviaQuestion>();

        TriviaDuelManager duel = TriviaDuelManager.Instance;

        if (duel == null)
            return pool;

        int size = duel.GetPoolSize(lesson.Level);

        for (int i = 0; i < size; i++)
        {
            TriviaQuestion question = duel.GetQuestionAt(lesson.Level, i);

            if (question == null)
                continue;

            if (question.topic == lesson.Topic)
                pool.Add(question);
            else
                offTopic.Add(question);
        }

        Shuffle(pool);

        if (pool.Count < wanted)
        {
            Shuffle(offTopic);
            pool.AddRange(offTopic);
        }

        if (pool.Count > wanted)
            pool.RemoveRange(wanted, pool.Count - wanted);

        return pool;
    }

    private static void Shuffle(List<TriviaQuestion> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }
}
