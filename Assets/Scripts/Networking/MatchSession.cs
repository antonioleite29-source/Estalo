using System.Collections.Generic;
using UnityEngine;

// One running match, server-side only. Holds every piece of state that used to live as fields on
// TriviaDuelManager/TeamDuelManager — which is exactly why only one match could exist at a time:
// those are singletons that own both the rules and the UI.
//
// A MatchSession owns no UI at all. It decides what happens and hands the result to a router
// (TriviaNetworkSync), which sends it to this match's participants only. That is what lets several
// matches run side by side without their scores and questions overwriting each other on every client.
public class MatchSession
{
    public enum RoundState
    {
        OpenBuzz = 0,
        SoloLeft = 1,
        SoloRight = 2,
        Resolving = 3,
        MatchEnded = 4
    }

    public int MatchId { get; }
    public MatchMode Mode { get; }
    public int DifficultyLevel { get; }
    public bool IsFinished { get; private set; }

    // clientId -> side (1/2 in 1v1) or slot (1-4 in 2v2). Slots 1,2 are team A; 3,4 are team B.
    private readonly Dictionary<ulong, int> seatByClient = new Dictionary<ulong, int>();
    private readonly List<ulong> participants = new List<ulong>();

    private readonly IMatchRouter router;
    private readonly IQuestionSource questions;
    private readonly MatchRules rules;

    private RoundState roundState = RoundState.MatchEnded;
    private int currentQuestionIndex = -1;
    private int correctAnswerIndex = -1;
    private int teamAScore;
    private int teamBScore;
    private bool inputEnabled;
    private bool isTransitioning;
    private float soloTimer;
    private float inactivityTimer;
    private float resolveTimer;
    private int pendingSoloSide;
    private ResolveThen resolveThen = ResolveThen.Nothing;

    // 2v2 only: which slot on each team is currently allowed to answer.
    private int activeSlotA = 1;
    private int activeSlotB = 3;

    private enum ResolveThen
    {
        Nothing,
        NextRound,
        BeginSolo,
        ReturnToOpen
    }

    public IReadOnlyList<ulong> Participants => participants;

    public MatchSession(int matchId, MatchMode mode, IReadOnlyList<ulong> clientIds, int difficultyLevel,
                        IMatchRouter router, IQuestionSource questions, MatchRules rules)
    {
        MatchId = matchId;
        Mode = mode;
        DifficultyLevel = difficultyLevel;
        this.router = router;
        this.questions = questions;
        this.rules = rules;

        for (int i = 0; i < clientIds.Count; i++)
        {
            ulong clientId = clientIds[i];
            participants.Add(clientId);

            // 1v1 seats are 1,2. Team seats interleave so joining order alternates teams:
            // slots 1,3 lead each team, then 2,4 - matching PlayerSideIdentity.SlotForJoinIndex.
            seatByClient[clientId] = mode == MatchMode.TeamFour
                ? PlayerSideIdentity.SlotForJoinIndex(i)
                : i + 1;
        }
    }

    public int GetSeat(ulong clientId) => seatByClient.TryGetValue(clientId, out int seat) ? seat : 0;

    public bool Contains(ulong clientId) => seatByClient.ContainsKey(clientId);

    public void Begin()
    {
        IsFinished = false;
        teamAScore = 0;
        teamBScore = 0;
        activeSlotA = 1;
        activeSlotB = 3;
        router.MatchStarted(this);
        StartNextRound();
    }

    private void StartNextRound()
    {
        if (teamAScore >= rules.pointsToWin)
        {
            EndMatch(Mode == MatchMode.TeamFour ? "Time A venceu!" : "Jogador 1 venceu!", true, 1);
            return;
        }

        if (teamBScore >= rules.pointsToWin)
        {
            EndMatch(Mode == MatchMode.TeamFour ? "Time B venceu!" : "Jogador 2 venceu!", true, 2);
            return;
        }

        int poolSize = questions.GetPoolSize(DifficultyLevel);

        if (poolSize <= 0)
        {
            EndMatch("Sem perguntas disponíveis", false, 0);
            return;
        }

        // Avoid repeating the question we just asked, when there is anything else to pick.
        int next = Random.Range(0, poolSize);

        if (poolSize > 1)
        {
            int guard = 0;
            while (next == currentQuestionIndex && guard++ < 8)
                next = Random.Range(0, poolSize);
        }

        currentQuestionIndex = next;
        correctAnswerIndex = questions.GetCorrectAnswerIndex(DifficultyLevel, currentQuestionIndex);

        if (Mode == MatchMode.TeamFour)
        {
            // Rotate which team-mate is on the spot each round.
            activeSlotA = activeSlotA == 1 ? 2 : 1;
            activeSlotB = activeSlotB == 3 ? 4 : 3;
        }

        inactivityTimer = rules.inactivityEndSeconds;
        inputEnabled = true;
        isTransitioning = false;
        roundState = RoundState.OpenBuzz;

        router.PublishState(this);
        router.ButtonsAvailable(this);
    }

    public void SubmitAnswer(ulong clientId, int answerIndex)
    {
        if (IsFinished || answerIndex < 0 || answerIndex > 3)
            return;

        int seat = GetSeat(clientId);

        if (seat == 0 || !IsSeatAllowedToAnswer(seat))
            return;

        inactivityTimer = rules.inactivityEndSeconds;

        router.RecordOwnAnswer(this, clientId, currentQuestionIndex, answerIndex == correctAnswerIndex);

        bool wasInSolo = roundState == RoundState.SoloLeft || roundState == RoundState.SoloRight;

        inputEnabled = false;
        isTransitioning = true;
        roundState = RoundState.Resolving;
        router.LockAllButtons(this);

        if (answerIndex == correctAnswerIndex)
        {
            if (TeamForSeat(seat) == 1)
                teamAScore++;
            else
                teamBScore++;

            router.AnswerMarked(this, answerIndex, true, seat);
            router.PublishState(this);

            resolveTimer = rules.correctResolveSeconds + rules.nextRoundDelaySeconds;
            resolveThen = ResolveThen.NextRound;
            return;
        }

        router.AnswerMarked(this, answerIndex, false, seat);
        resolveTimer = rules.wrongFlashSeconds;

        // Missing during your own solo hands the round back to open buzz rather than starting
        // another solo — otherwise two players trading misses loop forever.
        if (wasInSolo)
        {
            resolveThen = ResolveThen.ReturnToOpen;
        }
        else
        {
            resolveThen = ResolveThen.BeginSolo;
            pendingSoloSide = OpposingSideOf(seat);
        }
    }

    private bool IsSeatAllowedToAnswer(int seat)
    {
        if (IsFinished || !inputEnabled || isTransitioning)
            return false;

        if (Mode == MatchMode.TeamFour && seat != activeSlotA && seat != activeSlotB)
            return false;

        switch (roundState)
        {
            case RoundState.OpenBuzz:
                return true;
            case RoundState.SoloLeft:
                return TeamForSeat(seat) == 1;
            case RoundState.SoloRight:
                return TeamForSeat(seat) == 2;
            default:
                return false;
        }
    }

    private int TeamForSeat(int seat) =>
        Mode == MatchMode.TeamFour ? PlayerSideIdentity.TeamForSlot(seat) : seat;

    private int OpposingSideOf(int seat) => TeamForSeat(seat) == 1 ? 2 : 1;

    // Driven once per frame by TriviaNetworkSync so every live match advances together.
    public void Tick(float deltaTime)
    {
        if (IsFinished)
            return;

        if (isTransitioning)
        {
            resolveTimer -= deltaTime;

            if (resolveTimer > 0f)
                return;

            switch (resolveThen)
            {
                case ResolveThen.NextRound:
                    resolveThen = ResolveThen.Nothing;
                    StartNextRound();
                    break;
                case ResolveThen.BeginSolo:
                    resolveThen = ResolveThen.Nothing;
                    BeginSolo(pendingSoloSide);
                    break;
                case ResolveThen.ReturnToOpen:
                    resolveThen = ResolveThen.Nothing;
                    ReturnToOpenBuzz();
                    break;
            }

            return;
        }

        bool roundLive = roundState == RoundState.OpenBuzz
                      || roundState == RoundState.SoloLeft
                      || roundState == RoundState.SoloRight;

        if (roundLive)
        {
            inactivityTimer -= deltaTime;

            if (inactivityTimer <= 0f)
            {
                EndMatch("Partida encerrada: ninguém respondeu por 1 minuto", false, 0);
                return;
            }
        }

        if (roundState == RoundState.SoloLeft || roundState == RoundState.SoloRight)
        {
            soloTimer -= deltaTime;
            router.PublishSoloTimer(this, Mathf.Max(0f, soloTimer));

            if (soloTimer <= 0f)
            {
                soloTimer = 0f;
                ReturnToOpenBuzz();
            }
        }
    }

    private void BeginSolo(int soloSide)
    {
        inputEnabled = true;
        isTransitioning = false;
        soloTimer = rules.soloTimeSeconds;
        inactivityTimer = rules.inactivityEndSeconds;
        roundState = soloSide == 1 ? RoundState.SoloLeft : RoundState.SoloRight;

        router.PublishState(this);
        router.ButtonsAvailable(this);
    }

    private void ReturnToOpenBuzz()
    {
        inputEnabled = true;
        isTransitioning = false;
        roundState = RoundState.OpenBuzz;
        inactivityTimer = rules.inactivityEndSeconds;

        router.PublishState(this);
        router.ButtonsAvailable(this);
    }

    public void EndMatch(string message, bool hasWinner, int winningTeam)
    {
        if (IsFinished)
            return;

        IsFinished = true;
        inputEnabled = false;
        isTransitioning = false;
        roundState = RoundState.MatchEnded;

        router.LockAllButtons(this);
        router.MatchEnded(this, message, hasWinner, winningTeam);
    }

    // Called when someone drops: the match cannot continue, since the missing seat can never
    // answer its solo turn and the round would wait forever.
    public void AbortForDisconnect(ulong departedClientId)
    {
        seatByClient.Remove(departedClientId);
        participants.Remove(departedClientId);

        EndMatch(Mode == MatchMode.TeamFour ? "Um jogador saiu da partida." : "Seu adversário saiu da partida.",
                 false, 0);
    }

    // --- accessors the router needs to build its messages ---
    public int RoundStateValue => (int)roundState;
    public int QuestionIndex => currentQuestionIndex;
    public int TeamAScore => teamAScore;
    public int TeamBScore => teamBScore;
    public int ActiveSlotA => activeSlotA;
    public int ActiveSlotB => activeSlotB;
}

// Everything a match needs to send to its own participants. Implemented by TriviaNetworkSync,
// which turns each call into an RPC targeted at this match's clients only.
public interface IMatchRouter
{
    void MatchStarted(MatchSession match);
    void PublishState(MatchSession match);
    void PublishSoloTimer(MatchSession match, float remaining);
    void ButtonsAvailable(MatchSession match);
    void LockAllButtons(MatchSession match);
    void AnswerMarked(MatchSession match, int answerIndex, bool wasCorrect, int answeringSeat);

    // Sent to the ONE client who answered, so that device can log its own mistakes. Scoring happens
    // here on the server, so without this a player's own errors are never visible on their phone —
    // and a practice set built from someone else's mistakes is not personal at all.
    void RecordOwnAnswer(MatchSession match, ulong clientId, int questionIndex, bool wasCorrect);
    void MatchEnded(MatchSession match, string message, bool hasWinner, int winningTeam);
}

// The question bank, kept behind an interface so MatchSession never touches a MonoBehaviour.
public interface IQuestionSource
{
    int GetPoolSize(int difficultyLevel);
    int GetCorrectAnswerIndex(int difficultyLevel, int questionIndex);
}

// Tunables copied off the manager once at match creation, so a match keeps consistent rules even
// if someone edits the Inspector mid-session.
[System.Serializable]
public struct MatchRules
{
    public int pointsToWin;
    public float soloTimeSeconds;
    public float wrongFlashSeconds;
    public float correctResolveSeconds;
    public float nextRoundDelaySeconds;
    public float inactivityEndSeconds;

    public static MatchRules Default => new MatchRules
    {
        pointsToWin = 7,
        soloTimeSeconds = 3f,
        wrongFlashSeconds = 0.5f,
        correctResolveSeconds = 1f,
        nextRoundDelaySeconds = 0.35f,
        inactivityEndSeconds = 60f
    };
}
