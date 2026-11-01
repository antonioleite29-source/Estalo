using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// The 2v2 explanation, shown once before a player's first team match, and again from the More page.
//
// Cards only, no test round: 2v2 needs three other people, so there is nothing to practise against.
// What it has to teach beyond the 1v1 tutorial is the part a tester could not work out on their own --
// only one player per team answers each round, and the turn swaps to the team-mate on the next one.
//
// Runs on its own GameObject, like FirstRunTutorial and LessonSession: the first thing it does is
// switch the lobby pages off, and a coroutine stops dead when its own object is deactivated.
public class TeamFirstRunTutorial : MonoBehaviour
{
    private static string SeenKey => "EstaloTeamTutorialDone" + NetworkBootstrap.GetLocalProfileSuffix();

    public static bool IsRunning { get; private set; }

    private const string PartnerName = "Parceiro";
    private const string OpponentOneName = "Oponente 1";
    private const string OpponentTwoName = "Oponente 2";

    // The outermost answer buttons carry the navigation, the same way a lesson's teaching cards do.
    private const int BackButton = 0;
    private int NextButton => team.answerButtonVisuals.Length - 1;

    private TeamDuelManager team;
    private bool awaitingTap;
    private int tapped = -1;
    private bool quit;

    // Called before the queue is joined, so nobody is kept waiting while the cards are read.
    public static bool ShowBeforeFirstTeamMatch(System.Action onFinished)
    {
        if (PlayerPrefs.GetInt(SeenKey, 0) == 1)
            return false;

        return Begin(onFinished);
    }

    // "Tutorial 2x2" on the More page.
    public static bool Replay(System.Action onFinished = null)
    {
        return Begin(onFinished);
    }

    private static bool Begin(System.Action onFinished)
    {
        if (IsRunning || FirstRunTutorial.IsRunning || LessonSession.IsRunning)
            return false;

        TeamDuelManager team = TeamDuelManager.Instance;

        if (team == null || team.answerButtonVisuals == null || team.answerButtonVisuals.Length < 2)
        {
            Debug.LogWarning("TeamFirstRunTutorial: no 2v2 board in the scene, so the tutorial cannot run.");
            return false;
        }

        GameObject host = new GameObject("TeamFirstRunTutorial");
        DontDestroyOnLoad(host);

        IsRunning = true;

        TeamFirstRunTutorial tutorial = host.AddComponent<TeamFirstRunTutorial>();
        tutorial.team = team;
        tutorial.StartCoroutine(tutorial.Run(onFinished));
        return true;
    }

    private IEnumerator Run(System.Action onFinished)
    {
        DressBoard();
        BindButtons();

        // Written before the cards rather than after them: somebody who closes the app halfway through
        // has already read most of it, and making them sit through it again is a punishment for
        // stopping. The More page button is there for a second look.
        PlayerPrefs.SetInt(SeenKey, 1);
        PlayerPrefs.Save();

        yield return PlayCards();

        Finish(onFinished);
    }

    // --- the cards ------------------------------------------------------

    private IEnumerator PlayCards()
    {
        int solo = Mathf.Max(1, Mathf.RoundToInt(team.soloTimeSeconds));

        string[] cards =
        {
            "No 2x2 você joga em dupla. Você e seu parceiro são o <b>Time A</b>, os outros dois são o " +
            "<b>Time B</b>. Os pontos são do time, não de cada um.",

            "A cada rodada, só <b>uma pessoa de cada time</b> responde. O quadradinho aceso embaixo da " +
            "foto mostra de quem é a vez — a sua foto tem contorno.",

            "Na rodada seguinte a vez passa para o seu parceiro, e depois volta para você. Quando não " +
            "for sua vez, os botões não respondem: é a vez dele.",

            "Quem está na vez erra? O time adversário ganha " + solo + " segundos para responder " +
            "sozinho, e o seu time só assiste.",

            "Quem chegar a <b>" + team.pointsToWin + " pontos</b> primeiro ganha a partida.",

            "Combine com seu parceiro: quem é mais rápido em conta, quem é melhor em lógica. " +
            "Boa sorte!"
        };

        int page = 0;

        while (page < cards.Length)
        {
            SetQuestionText(cards[page]);

            // The turn indicator moves with the card that explains it, so the rule is shown rather
            // than only described.
            if (page == 1)
                team.ApplyNetworkedActiveSlots(1, 3);
            else if (page == 2)
                team.ApplyNetworkedActiveSlots(2, 4);

            bool first = page == 0;
            bool last = page == cards.Length - 1;

            ShowNavigation(first ? "Sair" : "Voltar", last ? "Começar" : "Próximo");

            yield return WaitForTap();

            if (quit)
                yield break;

            if (tapped == NextButton)
                page++;
            else if (first)
                yield break;
            else
                page--;
        }
    }

    // --- the board ------------------------------------------------------

    // Four seats filled in with your own name and face plus three stand-ins, so the card about whose
    // turn it is has something real to point at.
    private void DressBoard()
    {
        if (team.lobbyPageSwitcher != null)
            team.lobbyPageSwitcher.HideAllPages();

        TriviaDuelManager duel = TriviaDuelManager.Instance;

        if (duel != null && duel.triviaGameplayRoot != null)
            duel.triviaGameplayRoot.SetActive(false);

        if (team.teamGameplayRoot != null)
            team.teamGameplayRoot.SetActive(true);

        if (team.bottomBar != null)
            team.bottomBar.SetActive(false);

        // Seat 1, the same seat the board gives a single player, so your own picture is on the left.
        team.RegisterLocalSlot(1);

        PlayerProfileManager profile = PlayerProfileManager.Instance;

        if (profile != null)
        {
            int mine = profile.GetLocalAvatarIndex();
            int avatars = profile.availableAvatars != null ? profile.availableAvatars.Length : 0;

            team.ApplyNetworkedTeamPlayerIdentity(1, profile.GetLocalName(), profile.GetAvatarSprite(mine));
            team.ApplyNetworkedTeamPlayerIdentity(2, PartnerName, profile.GetAvatarSprite(Other(mine, 1, avatars)));
            team.ApplyNetworkedTeamPlayerIdentity(3, OpponentOneName, profile.GetAvatarSprite(Other(mine, 2, avatars)));
            team.ApplyNetworkedTeamPlayerIdentity(4, OpponentTwoName, profile.GetAvatarSprite(Other(mine, 3, avatars)));
        }

        team.ApplyNetworkedScore(0, 0);
        team.ApplyNetworkedActiveSlots(0, 0);

        // The 2v2 board borrows the 1v1 theme, the same way a real team match does.
        ButtonTheme theme = TriviaDuelManager.Instance != null ? TriviaDuelManager.Instance.buttonTheme : null;

        if (theme != null)
        {
            foreach (AnswerButtonVisual visual in team.answerButtonVisuals)
                if (visual != null)
                    visual.ApplyTheme(theme);
        }
    }

    // A different face per seat where the gallery has enough of them; repeats beat an empty slot.
    private static int Other(int mine, int step, int avatars)
    {
        return avatars > 1 ? (mine + step) % avatars : mine;
    }

    private void SetQuestionText(string text)
    {
        if (team.questionText == null)
            return;

        team.questionText.text = text;

        if (team.questionText.gameObject.activeInHierarchy)
            team.questionText.ForceMeshUpdate();
    }

    private void ShowNavigation(string back, string next)
    {
        AnswerButtonVisual[] visuals = team.answerButtonVisuals;

        for (int i = 0; i < visuals.Length; i++)
        {
            if (visuals[i] == null)
                continue;

            if (i == BackButton || i == NextButton)
            {
                visuals[i].SetLabel(i == NextButton ? next : back);
                visuals[i].SetAvailableState();
            }
            else
            {
                visuals[i].SetLabel(string.Empty);
                visuals[i].SetDisabledState();
            }
        }
    }

    private IEnumerator WaitForTap()
    {
        tapped = -1;
        awaitingTap = true;

        while (awaitingTap && !quit)
            yield return null;

        awaitingTap = false;
    }

    private void BindButtons()
    {
        if (team.answerButtons == null)
            return;

        for (int i = 0; i < team.answerButtons.Length; i++)
        {
            int index = i;
            Button button = team.answerButtons[i];

            if (button == null)
                continue;

            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(() => OnTap(index));
        }
    }

    private void OnTap(int index)
    {
        if (!awaitingTap)
            return;

        tapped = index;
        awaitingTap = false;
    }

    // --- leaving --------------------------------------------------------

    private void Finish(System.Action onFinished)
    {
        IsRunning = false;

        // Left empty: TeamDuelManager claims the listeners back when a real match starts, and anything
        // left here would answer for a tutorial that has gone.
        if (team != null && team.answerButtons != null)
        {
            foreach (Button button in team.answerButtons)
                if (button != null)
                    button.onClick.RemoveAllListeners();
        }

        if (team != null)
        {
            team.ApplyNetworkedActiveSlots(0, 0);

            foreach (AnswerButtonVisual visual in team.answerButtonVisuals)
                if (visual != null)
                    visual.SetLabel(string.Empty);

            if (team.teamGameplayRoot != null)
                team.teamGameplayRoot.SetActive(false);

            if (team.bottomBar != null)
                team.bottomBar.SetActive(true);

            if (team.lobbyPageSwitcher != null)
                team.lobbyPageSwitcher.ShowLobbyPage();
        }

        onFinished?.Invoke();
        Destroy(gameObject);
    }

    private void OnDestroy()
    {
        IsRunning = false;
    }
}
