using System.Collections.Generic;
using UnityEngine;

// Remembers what this player gets wrong, so the Learning tab can hand back the things they
// actually struggle with instead of a random selection.
//
// Deliberately per device and per profile, like PlayerIQManager: what you personally miss is the
// whole point, and Editor virtual players share one machine's PlayerPrefs, so keys are suffixed to
// keep four test players from pooling their mistakes into one log.
public class MistakeLogManager : MonoBehaviour
{
    private static MistakeLogManager instance;

    // Resolves itself if the static has been lost — a script recompile mid-session clears statics
    // without calling Awake again, which left practice unable to find the mistake log at all.
    public static MistakeLogManager Instance
    {
        get
        {
            if (instance == null)
            {
                instance = FindAnyObjectByType<MistakeLogManager>(FindObjectsInactive.Include);

                // Found rather than woken, so its log has not been read off disk yet.
                instance?.EnsureLoaded();
            }

            return instance;
        }
        private set => instance = value;
    }

    private const string PREFS_KEY_LOG = "MistakeLog_Local";

    // A topic needs a few attempts before its error rate means anything. Below this, one unlucky
    // question would brand a topic as the player's weakest.
    private const int MinimumAttemptsForWeakness = 3;

    // Where an answer came from. Weakness is judged on MATCH answers only: practice sets are built
    // FROM the weak topics, so counting practice would pile attempts onto exactly those topics and
    // drown out the real signal — answer a few right in practice and a topic stops looking weak
    // even though the player still loses on it in real games.
    public enum AnswerSource
    {
        Match,
        Practice
    }

    [System.Serializable]
    private class TopicRecord
    {
        public string topic;
        public int attempts;
        public int misses;
    }

    [System.Serializable]
    private class QuestionRecord
    {
        public string question;
        public string topic;
        public int difficulty;
        public int misses;
    }

    [System.Serializable]
    private class LogData
    {
        public List<TopicRecord> topics = new List<TopicRecord>();
        public List<QuestionRecord> questions = new List<QuestionRecord>();
    }

    private LogData data = new LogData();

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        Load();
    }

    private static string Key => PREFS_KEY_LOG + NetworkBootstrap.GetLocalProfileSuffix();

    private bool hasLoaded;

    internal void EnsureLoaded()
    {
        if (hasLoaded)
            return;

        Load();
    }

    private void Load()
    {
        hasLoaded = true;

        string json = PlayerPrefs.GetString(Key, string.Empty);

        if (string.IsNullOrEmpty(json))
        {
            data = new LogData();
            return;
        }

        // A corrupt or half-written log must not stop the game starting; an empty history is a
        // perfectly survivable state.
        data = JsonUtility.FromJson<LogData>(json) ?? new LogData();
    }

    private void Save()
    {
        PlayerPrefs.SetString(Key, JsonUtility.ToJson(data));
        PlayerPrefs.Save();
    }

    // Call once per answer THIS player gave. Both right and wrong: an error rate needs the
    // denominator, and a topic answered correctly ten times should stop being called weak.
    public void RecordAnswer(TriviaQuestion question, int difficultyLevel, bool wasCorrect,
        AnswerSource source = AnswerSource.Match)
    {
        if (question == null)
            return;

        string topic = string.IsNullOrEmpty(question.topic) ? "geral" : question.topic;

        // Only real matches move the topic statistics that decide "seu ponto fraco".
        if (source == AnswerSource.Match)
        {
            TopicRecord topicRecord = FindOrAddTopic(topic);
            topicRecord.attempts++;

            if (!wasCorrect)
                topicRecord.misses++;
        }

        QuestionRecord questionRecord = FindQuestion(question.question);

        if (wasCorrect)
        {
            // Getting it right retires it from the practice pool rather than only stopping the
            // count going up — otherwise a question missed once follows the player forever.
            if (questionRecord != null)
            {
                questionRecord.misses--;

                if (questionRecord.misses <= 0)
                    data.questions.Remove(questionRecord);
            }
        }
        else
        {
            if (questionRecord == null)
            {
                questionRecord = new QuestionRecord
                {
                    question = question.question,
                    topic = topic,
                    difficulty = difficultyLevel
                };

                data.questions.Add(questionRecord);
            }

            questionRecord.misses++;
        }

        Save();
    }

    // Topics sorted worst-first by error rate. Only those with enough attempts to be meaningful.
    public List<string> GetWeakTopics()
    {
        List<TopicRecord> eligible = new List<TopicRecord>();

        for (int i = 0; i < data.topics.Count; i++)
            if (data.topics[i].attempts >= MinimumAttemptsForWeakness && data.topics[i].misses > 0)
                eligible.Add(data.topics[i]);

        eligible.Sort((a, b) => ErrorRate(b).CompareTo(ErrorRate(a)));

        List<string> topics = new List<string>(eligible.Count);

        for (int i = 0; i < eligible.Count; i++)
            topics.Add(eligible[i].topic);

        return topics;
    }

    public string GetWeakestTopic()
    {
        List<string> weak = GetWeakTopics();
        return weak.Count > 0 ? weak[0] : string.Empty;
    }

    public float GetAccuracyFor(string topic)
    {
        TopicRecord record = FindTopic(topic);

        if (record == null || record.attempts == 0)
            return 1f;

        return (record.attempts - record.misses) / (float)record.attempts;
    }

    public List<string> GetPractisedTopics()
    {
        List<string> topics = new List<string>(data.topics.Count);

        for (int i = 0; i < data.topics.Count; i++)
            topics.Add(data.topics[i].topic);

        return topics;
    }

    public int MissedQuestionCount => data.questions.Count;

    // True once real matches have produced enough data to judge a weakness at all. Lets the results
    // screen say "play some games first" instead of implying the player has no weak topics.
    public bool HasMatchHistory
    {
        get
        {
            for (int i = 0; i < data.topics.Count; i++)
                if (data.topics[i].attempts >= MinimumAttemptsForWeakness)
                    return true;

            return false;
        }
    }

    // The questions this player actually got wrong, worst first, then padded from their weak topics
    // if there are not enough. Padding matters early on: with two logged mistakes a "practice"
    // button that hands back two questions feels broken.
    public List<TriviaQuestion> BuildPracticeSet(int count, IReadOnlyList<TriviaQuestion> allQuestions)
    {
        List<TriviaQuestion> practice = new List<TriviaQuestion>();

        if (allQuestions == null || allQuestions.Count == 0 || count <= 0)
            return practice;

        List<QuestionRecord> missed = new List<QuestionRecord>(data.questions);
        missed.Sort((a, b) => b.misses.CompareTo(a.misses));

        for (int i = 0; i < missed.Count && practice.Count < count; i++)
        {
            TriviaQuestion match = FindQuestionText(allQuestions, missed[i].question);

            if (match != null)
                practice.Add(match);
        }

        if (practice.Count >= count)
            return practice;

        List<string> weakTopics = GetWeakTopics();

        for (int t = 0; t < weakTopics.Count && practice.Count < count; t++)
            AddUnseenFromTopic(practice, allQuestions, weakTopics[t], count);

        // Still short: top up with anything, so the set is always the promised length.
        for (int i = 0; i < allQuestions.Count && practice.Count < count; i++)
            if (!practice.Contains(allQuestions[i]))
                practice.Add(allQuestions[i]);

        return practice;
    }

    public void ClearLog()
    {
        data = new LogData();
        Save();
    }

    private void AddUnseenFromTopic(List<TriviaQuestion> practice,
        IReadOnlyList<TriviaQuestion> allQuestions, string topic, int count)
    {
        for (int i = 0; i < allQuestions.Count && practice.Count < count; i++)
        {
            TriviaQuestion candidate = allQuestions[i];

            if (candidate != null && candidate.topic == topic && !practice.Contains(candidate))
                practice.Add(candidate);
        }
    }

    private static TriviaQuestion FindQuestionText(IReadOnlyList<TriviaQuestion> allQuestions, string text)
    {
        for (int i = 0; i < allQuestions.Count; i++)
            if (allQuestions[i] != null && allQuestions[i].question == text)
                return allQuestions[i];

        return null;
    }

    private static float ErrorRate(TopicRecord record) =>
        record.attempts == 0 ? 0f : record.misses / (float)record.attempts;

    private TopicRecord FindTopic(string topic)
    {
        for (int i = 0; i < data.topics.Count; i++)
            if (data.topics[i].topic == topic)
                return data.topics[i];

        return null;
    }

    private TopicRecord FindOrAddTopic(string topic)
    {
        TopicRecord existing = FindTopic(topic);

        if (existing != null)
            return existing;

        TopicRecord created = new TopicRecord { topic = topic };
        data.topics.Add(created);
        return created;
    }

    private QuestionRecord FindQuestion(string question)
    {
        for (int i = 0; i < data.questions.Count; i++)
            if (data.questions[i].question == question)
                return data.questions[i];

        return null;
    }
}
