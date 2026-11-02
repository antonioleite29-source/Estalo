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

    // Sequencia only: who took the last point, and how many they have taken in a row.
    private int lastScoringTeam;
    private int streak;
    private ResolveThen resolveThen = ResolveThen.Nothing;

    // Seats kept empty for players whose phone dropped, by account id, and how long the match will
    // go on waiting for them.
    private readonly Dictionary<string, int> awaySeatByAccount = new Dictionary<string, int>();
    private float waitTimer;
    private int lastWaitSecondSent = -1;

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
        lastScoringTeam = 0;
        streak = 0;
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

        // Not the one just asked, and not one anybody here has had lately: the router knows what
        // each player has seen, across matches and across evenings, and picks accordingly.
        currentQuestionIndex = router.PickQuestion(this, DifficultyLevel, poolSize, currentQuestionIndex);
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

        int team = TeamForSeat(seat);

        if (answerIndex == correctAnswerIndex)
        {
            Score(team, PointsFor(team));

            router.AnswerMarked(this, answerIndex, true, seat);
            router.PublishState(this);

            resolveTimer = rules.correctResolveSeconds + rules.nextRoundDelaySeconds;
            resolveThen = ResolveThen.NextRound;
            return;
        }

        router.AnswerMarked(this, answerIndex, false, seat);
        resolveTimer = rules.wrongFlashSeconds;

        // Sem perdao: no turn on their own for the other side, the point is simply theirs. One
        // mistake costs a point outright, which is the whole idea of the rule set.
        if (rules.missGivesThePoint)
        {
            lastScoringTeam = 0;
            streak = 0;
            Score(OpposingSideOf(seat), 1);
            router.PublishState(this);
            resolveThen = ResolveThen.NextRound;
            resolveTimer = rules.wrongFlashSeconds + rules.nextRoundDelaySeconds;
            return;
        }

        // Their run is over, whoever takes the next point.
        if (rules.streakScoring && team == lastScoringTeam)
        {
            lastScoringTeam = 0;
            streak = 0;
        }

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

    // Sequencia: 1 for the first, 2 for the second in a row, 3 for the third and anything after.
    private int PointsFor(int team)
    {
        if (!rules.streakScoring)
            return 1;

        streak = team == lastScoringTeam ? streak + 1 : 1;
        lastScoringTeam = team;
        return Mathf.Clamp(streak, 1, 3);
    }

    private void Score(int team, int points)
    {
        if (team == 1)
            teamAScore += points;
        else
            teamBScore += points;
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
        if (IsWaitingForAPlayer)
        {
            waitTimer -= deltaTime;

            if (waitTimer <= 0f)
            {
                awaySeatByAccount.Clear();
                EndMatch(Mode == MatchMode.TeamFour ? "Um jogador saiu da partida." : "Seu adversário saiu da partida.",
                         false, 0);
                return;
            }

            // Once a second, so whoever is left can see it is still counting rather than stuck.
            if (WaitSecondsLeft != lastWaitSecondSent)
            {
                lastWaitSecondSent = WaitSecondsLeft;
                router.MatchHeld(this, true, lastWaitSecondSent);
            }

            return;
        }

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

    // Called when someone drops and is not coming back: the match cannot continue, since the
    // missing seat can never answer its solo turn and the round would wait forever.
    public void AbortForDisconnect(ulong departedClientId)
    {
        seatByClient.Remove(departedClientId);
        participants.Remove(departedClientId);
        awaySeatByAccount.Clear();

        EndMatch(Mode == MatchMode.TeamFour ? "Um jogador saiu da partida." : "Seu adversário saiu da partida.",
                 false, 0);
    }

    // --- somebody dropped, and may be back ------------------------------

    // How long the match waits for a phone that dropped. Long enough to walk through a tunnel or
    // for Wi-Fi to hand over to mobile data, short enough that whoever is left does not give up
    // first and close the game.
    public const float WaitForReturnSeconds = 45f;

    public bool IsWaitingForAPlayer => awaySeatByAccount.Count > 0;

    public int WaitSecondsLeft => Mathf.Max(0, Mathf.CeilToInt(waitTimer));

    // Keeps this player's seat, pauses the round, and tells everyone still here what is happening.
    // False when the seat cannot be kept -- an account the server never knew, or a match already
    // over -- and then the caller ends the match as before.
    public bool HoldForMissing(ulong clientId, string accountId)
    {
        if (IsFinished || string.IsNullOrEmpty(accountId) || !seatByClient.TryGetValue(clientId, out int seat))
            return false;

        seatByClient.Remove(clientId);
        participants.Remove(clientId);
        awaySeatByAccount[accountId] = seat;

        inputEnabled = false;
        isTransitioning = false;
        waitTimer = WaitForReturnSeconds;
        lastWaitSecondSent = -1;

        router.LockAllButtons(this);
        router.MatchHeld(this, true, WaitSecondsLeft);
        return true;
    }

    // The same player, on a new connection: the account is what identifies them, since a reconnect
    // brings a brand-new client id. The seat they left is the seat they get back.
    public bool TryReturn(string accountId, ulong clientId)
    {
        if (IsFinished || string.IsNullOrEmpty(accountId) || !awaySeatByAccount.TryGetValue(accountId, out int seat))
            return false;

        awaySeatByAccount.Remove(accountId);
        seatByClient[clientId] = seat;
        participants.Add(clientId);
        return true;
    }

    // Called once the router has given the returning player their seat back on the network. The
    // score stands and the match picks up at a fresh question rather than halfway through the one
    // that was on screen when the line died -- that question was half answered, and only one side
    // saw how far.
    public void ResumeAfterReturn()
    {
        if (IsFinished || IsWaitingForAPlayer)
            return;

        waitTimer = 0f;
        router.MatchHeld(this, false, 0);
        router.MatchStarted(this);
        StartNextRound();
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

    // The match is paused because somebody's phone dropped, and how many seconds it will go on
    // waiting for them. held false means they are back and play is about to carry on.
    void MatchHeld(MatchSession match, bool held, int secondsLeft);

    // Which question to ask next, out of a pool of poolSize, avoiding avoidIndex and whatever the
    // players in this match have seen recently. The router owns that memory; the match only asks.
    int PickQuestion(MatchSession match, int difficultyLevel, int poolSize, int avoidIndex);
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

    // Sem perdao: a wrong answer hands the point straight to the other side instead of giving them
    // a turn on their own to earn it.
    public bool missGivesThePoint;

    // Sequencia: correct answers in a row by the same side are worth 1, then 2, then 3. A miss by
    // that side starts the counting again.
    public bool streakScoring;

    public static MatchRules Default => new MatchRules
    {
        pointsToWin = 7,
        soloTimeSeconds = 3f,
        wrongFlashSeconds = 0.5f,
        correctResolveSeconds = 1f,
        nextRoundDelaySeconds = 0.35f,
        inactivityEndSeconds = 60f,
        missGivesThePoint = false,
        streakScoring = false
    };
}
