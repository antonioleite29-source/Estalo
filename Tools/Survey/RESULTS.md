# Estalo user test — survey results

Nine people answered the Google Form between 14 and 18 September 2026. Raw answers are in
`responses-2026-09.tsv`, exported from the form's response sheet.

All nine testers knew the developer (family or friends), so the sample is friendly and the
scores below are likely a little generous. Everyone played 1v1; four also played 2v2 and one
tried the lesson path.

## Who tested

| Group | People |
|---|---|
| Age 14–17 | 2 |
| Age 18–25 | 1 |
| Age 26–40 | 2 |
| Over 40 | 4 |
| Plays mobile games daily | 2 |
| A few times a week | 2 |
| Rarely | 3 |
| Never | 2 |

Devices covered both platforms: iPhone 16 and another iPhone, Pixel 10 and two other Google
phones, and a Samsung S26 Ultra.

## Scores (1–5, mean of nine answers)

| Question | Mean | Range |
|---|---|---|
| Was the match fun? | **4.8** | 4–5 |
| Does the game help you practise maths? | **4.6** | 3–5 |
| Did the tutorial make it clear how to play? | **4.1** | 3–5 |
| How does the game look? | **3.7** | 2–5 |
| Was the text easy to read? | **3.7** | 2–5 |

Recommendation score (0–10): mean **9.3**, lowest 8. Seven promoters (9–10), two passives
(8), no detractors, so the Net Promoter Score is **+78**.

Eight of nine would play again on their own; one said "maybe".

## What worked

- **Competition is the draw.** Almost every free-text answer named it: "da competição com
  pessoas próximas", "de ser uma competição", "gostei de poder competir".
- **Speed.** Two testers liked being forced to think quickly, and one said it made them want
  to keep playing.
- **Difficulty is roughly right.** Six of nine said the questions were "na medida", three
  said "um pouco difíceis". Nobody found them too easy.
- **The solo timer is right.** Eight of nine said the time to answer alone was "na medida".
- **Stability.** Seven of nine had no problem at all during their session.

## What needs fixing

1. **Text is too small — the single clearest finding.** Four testers raised it
   independently: bigger question text, bigger answer text, and the Profile page's font and
   match-history spacing described as "muito grande" and badly laid out. This matches the
   lowest two scores in the whole survey (visual and readability, both 3.7).
   *Done since the test:* every label now wraps and shrinks to fit its box with a floor of 36
   units, about Apple's 11pt minimum, and the question text can shrink properly inside its
   scaled box.
2. **Answers side by side don't fit big phones.** The Samsung S26 Ultra tester asked for the
   four answers stacked vertically instead of in a horizontal row.
3. **Whose turn it is in 2v2 is unclear.** One tester who played with four people could not
   tell when it was their turn.
4. **Long word problems reward fast readers, not maths.** One tester pointed out that the
   analogy questions ("uma porta está para a casa como um carro está para…") are won by
   whoever reads quickest.
5. **The tutorial explains the wrong-answer rule late for some.** Five understood it during
   the tutorial; four only after their first round.
6. **One broken session.** The Samsung tester's first attempt had unreadable questions, which
   made the game unplayable, plus occasional trouble starting matches. The text bug is fixed;
   the match-start problem still needs watching.
7. **More solo time.** One tester wanted longer to answer after the opponent gets it wrong.

## Features testers asked for

- **Play alone against a bot,** asked for by three of nine, with difficulty rising as you
  improve. One described it as "tipo um kumon de jogo": practise against the computer, then
  move on to real people. A time trial that gets harder was also suggested.
- **Questions that adapt to the player's level** — which the game already does through IQ,
  so it was not visible enough to the tester.
- **More colours between the red and the blue.**

## Decisions this supports

- **Login:** seven of nine would use Google to save their progress, two Apple. Google sign-in
  is worth adding first, on top of the username login already built.
- **Where to publish:** testers find new games through friends and school (4), the app store
  (3), Instagram (2), then TikTok, YouTube and WhatsApp (1 each). Word of mouth plus a store
  listing matters more than any single social network, and the launch post should go where
  school friends already are.
- **What to build next:** the bot mode is the most requested feature and also removes the
  game's biggest dependency — needing a second person online to play at all.
