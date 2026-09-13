using System;
using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

// The lessons on the learning path, and how far along it the player has got.
//
// What each lesson teaches lives in Resources/Lessons.json rather than here: the teaching cards are
// words, and words get rewritten far more often than code. A lesson names one topic and one level
// from the question bank, and the order of the file is the order of the path, easiest first.
//
// Every topic a lesson names must actually exist in TriviaQuestions.txt at that level. The ladder
// this replaced asked for "fracoes", "porcentagem" and "geral", which the bank has never had, so a
// fifth of its lessons quietly filled up with whatever else was at that level.
public static class LessonLadder
{
    private const string ResourceName = "Lessons";

    // A lesson is finished by getting this many right, however many tries that takes.
    public const int CorrectToPass = 10;

    [Serializable]
    public class Lesson
    {
        public string title;
        public string topic;
        public int level;         // 1-7, the same scale PlayerIQManager uses
        public string[] cards;    // the teaching, shown one card at a time before the questions

        [NonSerialized] public int Number;   // 1-based, what the player sees on the button
    }

    [Serializable]
    private class LessonFile
    {
        public Lesson[] lessons;
    }

    private static Lesson[] lessons;

    private static Lesson[] All
    {
        get
        {
            if (lessons == null)
                lessons = Load();

            return lessons;
        }
    }

    public static int Count => All.Length;

    public static Lesson At(int index)
    {
        if (Count == 0)
            return null;

        return All[Mathf.Clamp(index, 0, Count - 1)];
    }

    private static Lesson[] Load()
    {
        TextAsset file = Resources.Load<TextAsset>(ResourceName);

        if (file == null)
        {
            Debug.LogError($"LessonLadder: no Resources/{ResourceName}.json, so the learning path is empty.");
            return new Lesson[0];
        }

        LessonFile parsed = JsonUtility.FromJson<LessonFile>(file.text);
        Lesson[] loaded = parsed != null && parsed.lessons != null ? parsed.lessons : new Lesson[0];

        for (int i = 0; i < loaded.Length; i++)
            loaded[i].Number = i + 1;

        return loaded;
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

    // Everything up to and including the next lesson is open, so any finished lesson can be
    // played again.
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

    // Every question for one lesson, shuffled: its topic at its level. Falls back to the whole level
    // when the topic has none there, since a lesson that cannot start at all is worse than an
    // off-topic one.
    public static List<TriviaQuestion> BuildPool(Lesson lesson)
    {
        List<TriviaQuestion> onTopic = new List<TriviaQuestion>();
        List<TriviaQuestion> wholeLevel = new List<TriviaQuestion>();

        TriviaDuelManager duel = TriviaDuelManager.Instance;

        if (duel == null || lesson == null)
            return onTopic;

        int size = duel.GetPoolSize(lesson.level);

        for (int i = 0; i < size; i++)
        {
            TriviaQuestion question = duel.GetQuestionAt(lesson.level, i);

            if (question == null)
                continue;

            wholeLevel.Add(question);

            if (question.topic == lesson.topic)
                onTopic.Add(question);
        }

        if (onTopic.Count == 0 && wholeLevel.Count > 0)
            Debug.LogWarning($"LessonLadder: lesson {lesson.Number} asks for \"{lesson.topic}\" at level " +
                             $"{lesson.level}, which the question bank does not have. Using the whole level.");

        List<TriviaQuestion> pool = onTopic.Count > 0 ? onTopic : wholeLevel;
        Shuffle(pool);
        return pool;
    }

    public static void Shuffle<T>(List<T> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }
}
