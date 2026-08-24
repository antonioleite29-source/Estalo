# Estalo

A Portuguese-language maths trivia duel for iPhone and Android. Two or four players race
to answer the same question; buzzing in with a wrong answer hands the other side a solo
turn on a timer. Built in Unity 6 with Netcode for GameObjects, played against an
always-on server — nobody hosts, and nobody types an address.

## The rules

Every round shows one question to everyone in the match at once.

1. **Open buzz** — anyone may answer.
2. A **correct** answer scores a point and the round ends.
3. A **wrong** answer locks that side out and starts a **solo turn**: only the other side
   may answer, on a `soloTimeSeconds` countdown shown as a ring.
4. If the solo turn expires with no answer, nobody scores.

First to **7 points** wins a 1v1; first to **9** wins a 2v2. Both are set in the
Inspector on `TriviaDuelManager` and `TeamDuelManager`.

A match that goes 60 seconds with no attempt at all ends itself, so an abandoned game
cannot strand the other player on the board.

## Adaptive difficulty

Each device keeps its own IQ in `PlayerPrefs`, starting at **100** and clamped to
**70–150**. That maps to one of **7 difficulty levels**, and questions are drawn from that
level's pool.

After every match the IQ moves by `50 / currentIQ` — up on a win, down on a loss. The
divisor makes the step shrink as IQ climbs, so ratings settle instead of drifting forever.

Matchmaking groups players by level, so opponents are the closest available in skill
rather than whoever pressed Start first. A match's difficulty is the **average** of its
players' levels.

## Learning from mistakes

Every answer a player gives is logged on their own device by topic — `adicao`,
`fracoes`, `logica` and so on. The Learning page turns that into a practice set built
from the questions they actually got wrong, padded from their weakest topics when there
are not yet enough. See `MistakeLogManager` and `PracticeQuizController`.

## Questions

`Assets/Resources/TriviaQuestions.txt` — **1,819 rows**, tab-separated:

```
difficulty  topic   question         answerA  answerB  answerC  answerD  correctIndex
1           adicao  Quanto é 2 + 1?  2        4        3        5        2
```

`correctIndex` is 0-based. To add questions, append rows — no code change needed. Every
topic carries at least 100 questions, weighted towards what actually comes up at school.

## How a match runs

The server owns the rules. A `MatchSession` holds one running match and decides
everything — who may answer, who scores, when a solo starts, when it ends — then hands
the result to `TriviaNetworkSync`, which sends it as an RPC addressed to that match's own
players. Several matches run side by side without ever seeing each other's state.

The two managers on the client are **views**. They draw what arrives and nothing else.

## Running it

**Requires Unity 6000.4.12f1** (see `ProjectSettings/ProjectVersion.txt`). For phone
builds you also need Android Build Support with the SDK & NDK Tools and OpenJDK modules,
or Xcode for iOS.

Open `Assets/ProjectCapstone.unity` and press Play. The Editor connects to the server
address set on `NetworkBootstrap` — it is always a client, never the server, even while
the active build target is Dedicated Server.

### Two players in the Editor

`Window > Multiplayer > Multiplayer Play Mode`, then tick Player 2. Virtual players share
this machine's `PlayerPrefs`, so profile and IQ keys are suffixed per virtual player — see
`NetworkBootstrap.GetLocalProfileSuffix()`.

### Phones

`Trivia Duel > Build Android APK` builds a version-stamped APK into `Builds/`. iOS goes
through Unity's Build Profiles to an Xcode project, then Run on a device. The `Trivia
Duel > Setup` menu has one-click fixes for player settings, icons and UI scaling.

`./Tools/phones.sh` boots Android emulators, installs the newest APK and launches it.

## The server

A headless Linux build runs as a systemd service on a small cloud box, listening on UDP
**7777**. Players connect to it the moment the app opens and stay connected; there is no
Connect screen in the normal flow.

```
Trivia Duel > Build Linux Server        # in Unity
./Tools/Server/deploy.sh root@YOUR_IP   # upload and restart
```

`Tools/Server/setup-server.sh` provisions a fresh box — user, firewall, service, log
rotation. `triviaduel.service` restarts the game on crash and on reboot.

Clearing **Server Address** on `NetworkBootstrap` falls back to the older arrangement,
where one phone hosts and the others find it over UDP broadcast on the same Wi-Fi. That
path is still in the code as a fallback for networks the server cannot be reached from.

## Layout

```
Assets/Scripts/
  Core/         FrameRateCap, CanvasInputRepair, PerfProbe
  Gameplay/     TriviaDuelManager (1v1 view), TeamDuelManager (2v2 view),
                PlayerIQManager, MistakeLogManager, PracticeQuizController
  Networking/   NetworkBootstrap, TriviaNetworkSync, MatchSession, Matchmaker,
                LanDiscovery, PlayerSideIdentity
  UI/           LobbyPageSwitcher, ConnectPageController, WaitingScreenController,
                LoadingScreenController, LobbyLearningScroller
Assets/Editor/  Build and scene-setup tooling (the "Trivia Duel" menu)
Assets/Docs/    Architecture notes and the phone testing log
Tools/          phones.sh, and Server/ — provisioning and deploy scripts
```

`Assets/Docs/Architecture.md` explains why the netcode is shaped the way it is. Read it
before changing anything under `Networking/`.
