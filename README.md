# Trivia Duel

A Portuguese-language maths trivia duel for Android. Two or four players race to answer
the same question; buzzing in with a wrong answer hands the other side a solo turn on a
timer. Built in Unity 6 with Netcode for GameObjects, playing over local Wi-Fi.

## The rules

Every round shows one question to everyone in the match at once.

1. **Open buzz** — anyone may answer.
2. A **correct** answer scores a point and the round ends.
3. A **wrong** answer locks that side out and starts a **solo turn**: only the other side
   may answer, on a `soloTimeSeconds` countdown shown as a ring.
4. If the solo turn expires with no answer, nobody scores.

First to **7 points** wins a 1v1; first to **9** wins a 2v2. Both are set in the
Inspector on `TriviaDuelManager` and `TeamDuelManager`.

## Adaptive difficulty

Each device keeps its own IQ in `PlayerPrefs`, starting at **100** and clamped to
**70–150**. That maps to one of **7 difficulty levels**, and questions are drawn from that
level's pool.

After every match the IQ moves by `50 / currentIQ` — up on a win, down on a loss. The
divisor makes the step shrink as IQ climbs, so ratings settle instead of drifting forever.

Matchmaking groups players by level, so opponents are the closest available in skill
rather than whoever pressed Start first. A match's difficulty is the **average** of its
players' levels.

## Questions

`Assets/Resources/TriviaQuestions.txt` — 490 rows, tab-separated:

```
difficulty  question         answerA  answerB  answerC  answerD  correctIndex
1           Quanto é 2 + 1?  3        4        2        5        0
```

`correctIndex` is 0-based. To add questions, append rows — no code change needed.

## Running it

**Requires Unity 6000.4.12f1** (see `ProjectSettings/ProjectVersion.txt`). For Android
builds you also need Android Build Support with the SDK & NDK Tools and OpenJDK modules.

Open `Assets/ProjectCapstone.unity` and press Play. The Editor auto-hosts.

### Two players in the Editor

`Window > Multiplayer > Multiplayer Play Mode`, then tick Player 2. Virtual players
auto-connect to `127.0.0.1`. They share this machine's `PlayerPrefs`, so profile and IQ
keys are suffixed per virtual player — see `NetworkBootstrap.GetLocalProfileSuffix()`.

### Phones

`Trivia Duel > Build Android APK` builds a version-stamped APK into `Builds/`. The menu
also has one-click setup for the Android player settings and the UI scaling.

`./Tools/phones.sh` boots Android emulators, installs the newest APK and launches it.
Note that emulators sit behind NAT: your Mac is `10.0.2.2` from inside one, and Wi-Fi
discovery cannot reach them. Only a real phone can test that.

### Connecting

One device hosts; the rest find it automatically — the host announces itself over UDP
broadcast on the local network and joining devices pick it up without anyone typing an
address. The Connect page (inside the **Mais** page) still has a manual IP field as a
fallback for networks that block broadcast.

Everyone must be on the same Wi-Fi, and not a guest network — client isolation blocks
device-to-device traffic and looks exactly like the game being broken.

## Layout

```
Assets/Scripts/
  Gameplay/     TriviaDuelManager (1v1), TeamDuelManager (2v2), PlayerIQManager
  Networking/   NetworkBootstrap, TriviaNetworkSync, MatchSession, Matchmaker,
                LanDiscovery, PlayerSideIdentity
  UI/           LobbyPageSwitcher, ConnectPageController, WaitingScreenController
Assets/Editor/  Build and scene-setup tooling (the "Trivia Duel" menu)
Assets/Docs/    Architecture notes and the phone testing log
Tools/          phones.sh — Android emulator helper
```

`Assets/Docs/Architecture.md` explains why the netcode is shaped the way it is. Read it
before changing anything under `Networking/`.
