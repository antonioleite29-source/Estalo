# The question bank

`Assets/Resources/TriviaQuestions.txt` — tab separated, one question per line:

```
difficulty  topic  question  answerA  answerB  answerC  answerD  correctIndex
```

## What makes a question fit this game

A round gives a player a few seconds, and a solo turn gives four. An item that needs long
multiplication, a recalled formula for an uncommon shape, or university vocabulary is not hard, it
is unanswerable, and the round it lands in is decided by whoever guesses first. Three rules follow
from the literature on fluency and number sense:

1. **Answerable by a strategy, not by grinding.** Fluency is efficiency *and* accuracy, so the
   question has to yield to a mental approach — halving, a known fact, a benchmark — inside the
   time. `53 ao quadrado` does not.
2. **Mix number sense in with computation.** Estimating, comparing, rounding and benchmarks measure
   something different from recall, and students score far lower on them, so they are worth asking.
3. **Distractors are the mistakes people make.** Off by one, the other operation, the forgotten
   carry, the reversed digits. A random wrong number is free to eliminate and teaches nothing.

A fourth rule is this game's own: timed drill raises anxiety when it is nothing but recall, so every
level mixes reasoning, patterns and short word problems in with the arithmetic.

## The three tools

```
python3 Tools/Questions/audit.py Assets/Resources/TriviaQuestions.txt [--list]
python3 Tools/Questions/generate.py > generated.tsv
python3 Tools/Questions/merge.py Assets/Resources/TriviaQuestions.txt generated.tsv > new.txt
python3 Tools/Questions/check.py Assets/Resources/TriviaQuestions.txt
```

- **audit** flags what cannot be answered in time: heavy vocabulary, numbers past mental range,
  products of two large numbers, long division, squares above 20, questions over 18 words.
- **generate** writes the computed half of the bank, with misconception distractors, by level.
- **merge** drops whatever the audit flags from the old file and adds the generated one, keeping the
  first copy of any repeated question.
- **check** re-does every sum it can parse and fails if the marked answer is wrong — the worst bug a
  quiz can have is the player being right and the game saying no.

Run **audit** and **check** after any hand edit.

## Not repeating

Each account remembers its last 80 questions per level, in `accounts.json`
(`PlayerAccounts.RememberQuestion`). The server picks a question nobody in the match has seen
recently; only when the whole level has been seen does it fall back to "not the same as last round".
`RecentQuestions` is the same memory on the phone, for Solo and the first-run round, saved in
PlayerPrefs.
