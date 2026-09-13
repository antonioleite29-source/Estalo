# Estalo user-testing survey

A Google Form for testers to fill in **right after** their session. It takes about 5 minutes
and is anonymous. The questions are in Portuguese for the testers. English versions are
below so Stallon can review them.

## Making the form

`CreateSurvey.gs` builds the whole form, so nobody has to type it in by hand.

1. Open <https://script.google.com> → **New project**.
2. Paste in all of `CreateSurvey.gs`, replacing what's there.
3. Choose `createSurvey` from the function menu → **Run** → allow access.
4. **View → Logs** shows two links: one to share with testers, one to edit.

To make an English copy, change `LANG = 'pt'` to `LANG = 'en'` and run it again.

## Linking answers to the testing log

The first question asks for a **test number**. Give each tester the same number as their
row in `Assets/Docs/TestingLog.md`. Then the report can put what someone *said* next to what
you *saw* them do.

## The questions

Question types: **MC** = multiple choice (pick one) · **Check** = checkboxes (pick any) ·
**1–5 / 0–10** = linear scale · **Text** = short answer · **Para** = paragraph.
\* = required.

### 1. Sobre você — About you
| # | Question | Type | What it tells us |
|---|---|---|---|
| 1 | Test number | Text | Links the form to the testing log |
| 2 | How old are you?\* | MC | Who the game works for |
| 3 | Do you know the person who made the game?\* | MC | Splits **family/friends** from **outside** testers. Friends tend to be kinder |
| 4 | How often do you play games on your phone?\* | MC | Gamer vs. non-gamer |
| 5 | How do you feel about maths?\* | 1–5 | Does the game win over people who dislike maths? |
| 6 | Which phone did you use? | Text | Device-specific bugs |

### 2. Primeiros minutos — First minutes
| # | Question | Type | What it tells us |
|---|---|---|---|
| 7 | Did the tutorial make it clear how to play?\* | 1–5 | Onboarding quality |
| 8 | When did you understand the wrong-answer / solo-turn rule?\* | MC | Whether the core rule gets through |
| 9 | Was anything confusing at the start? | Para | |

### 3. A partida — The match
| # | Question | Type | What it tells us |
|---|---|---|---|
| 10 | What did you play? (1v1, 2v2, practise mistakes, lesson path)\* | Check | Which features got tried |
| 11 | How fun was the match?\* | 1–5 | Core fun |
| 12 | The questions were… (too easy → too hard)\* | MC | Whether adaptive difficulty is working |
| 13 | The solo-turn timer was… (too short / right / too long)\* | MC | Tuning `soloTimeSeconds` |
| 14 | Did anything go wrong? (disconnect, lag, dead tap, cut-off text, slow matchmaking, other)\* | Check | Bug count by kind |
| 15 | If something went wrong, what were you doing? | Para | Reproduction steps |

### 4. Visual — Look and feel
| # | Question | Type | What it tells us |
|---|---|---|---|
| 16 | The look feels… amateur → professional\* | 1–5 | Baseline **before** the UI/UX polish task |
| 17 | Was the text easy to read?\* | 1–5 | Readability on small phones |
| 18 | What would you change about how it looks? | Para | |

### 5. Para terminar — To finish
| # | Question | Type | What it tells us |
|---|---|---|---|
| 19 | Does the game help you practise maths?\* | 1–5 | The learning goal |
| 20 | Would you play again on your own?\* | MC | Retention |
| 21 | 0–10, would you recommend Estalo to a friend?\* | 0–10 | Net Promoter Score |
| 22 | To save progress you'd make an account with… Google / Apple / email / none\* | MC | **Informs the Google/Apple login decision** |
| 23 | Where do you find new games? TikTok / Instagram / YouTube / WhatsApp / friends / app store / other\* | Check | **Informs which social network to use** |
| 24 | What did you like most? | Para | |
| 25 | If you could change one thing, what would it be?\* | Para | Top improvement |

## Reading the results

- **Net Promoter Score (Q21):** % who answered 9–10 minus % who answered 0–6.
- Compare **family/friends vs. outside** (Q3) on Q11, Q16 and Q21. A big gap means the
  friendly scores are too generous.
- Q16 is the "before" score for the design work. Run the same question after the polish
  to show it improved.
