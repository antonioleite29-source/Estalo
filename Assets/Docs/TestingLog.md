# Testing Log

Every issue found while testing the APK on real phones. Fill a row in as soon as it
happens — not at the end of the session, when the details have gone.

Rule for the session: **log everything first, fix nothing until the session is over.**
Fixing mid-session changes the build under the other testers and makes their reports
impossible to compare.

## Devices tested

| # | Tester | Phone | Android version | Screen size | APK version |
|---|--------|-------|-----------------|-------------|-------------|
| 1 |        |       |                 |             | 0.1         |
| 2 |        |       |                 |             | 0.1         |
| 3 |        |       |                 |             | 0.1         |
| 4 |        |       |                 |             | 0.1         |

Aim for at least four, spanning one small/older phone and one large modern one — the
UI scaling problems show up differently at each pixel density.

## Issues

Severity: **S1** blocks play · **S2** breaks a match · **S3** annoying but playable ·
**S4** cosmetic.

| # | Device | Mode | What they did | What happened | What should happen | Severity | Status |
|---|--------|------|---------------|---------------|--------------------|----------|--------|
|   |        |      |               |               |                    |          | Open   |

## Session script

Run the same steps on every phone, so a device-specific problem is obvious:

1. Install the APK. Note anything Android says while installing (Play Protect warning,
   permission prompts).
2. Open the app. Set a name and pick an avatar on the Profile page.
3. One phone hosts; every other phone joins by typing the host's IP.
4. Play a full 1v1 to the end. Note whether the questions were readable at normal
   holding distance and whether any tap missed.
5. Play a full 2v2 to the end.
6. With four phones queued for 1v1, confirm two separate matches form and their scores
   move independently.
7. Force-quit one phone mid-match. The others should report the disconnect and return to
   the lobby, not hang.
8. Hand them the Google Form while the game is still open in front of them.

## Known before testing starts

Things already understood — don't spend a row on them.

- Difficulty is the **average** of the players in the match, so a strong player matched
  with a weak one gets questions between the two levels. Open design question for Arthur.
- The Learning tab is not built yet; the button leads to a placeholder.
- Questions are Portuguese-language maths only.
