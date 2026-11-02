#!/usr/bin/env python3
"""Re-does the sums in the question file and complains when the marked answer is not the right one.

Only the items it can parse -- plain arithmetic, percentages, doubles and halves -- but that is most
of the bank, and a wrong key there is the worst bug a quiz game can have: the player is right and
the game says no.
"""

import re
import sys

PATTERNS = [
    (re.compile(r"^Quanto é (\d+) \+ (\d+)\?$"), lambda a, b: a + b),
    (re.compile(r"^Quanto é (\d+) - (\d+)\?$"), lambda a, b: a - b),
    (re.compile(r"^Quanto é (\d+) [x×] (\d+)\?$"), lambda a, b: a * b),
    (re.compile(r"^Quanto é (\d+) ÷ (\d+)\?$"), lambda a, b: a // b),
    (re.compile(r"^Quanto é (\d+)% de (\d+)\?$"), lambda p, n: n * p // 100),
    (re.compile(r"^Qual é a metade de (\d+)\?$"), lambda n: n // 2),
    (re.compile(r"^Qual é o dobro de (\d+)\?$"), lambda n: n * 2),
    (re.compile(r"^Quanto é (\d+) ao quadrado\?$"), lambda n: n * n),
    (re.compile(r"^Qual é a raiz quadrada de (\d+)\?$"), lambda n: int(n ** 0.5)),
    (re.compile(r"^Qual é o resto de (\d+) ÷ (\d+)\?$"), lambda a, b: a % b),
]

def main():
    checked = wrong = 0

    with open(sys.argv[1], encoding="utf-8") as handle:
        next(handle)
        for number, line in enumerate(handle, start=2):
            row = line.rstrip("\n").split("\t")
            if len(row) < 8:
                continue

            question, answers, index = row[2], row[3:7], int(row[7])

            for pattern, work in PATTERNS:
                match = pattern.match(question)
                if not match:
                    continue

                checked += 1
                expected = str(work(*(int(g) for g in match.groups())))

                if answers[index] != expected:
                    wrong += 1
                    print(f"line {number}: {question} marked {answers[index]}, should be {expected}")
                elif answers.count(expected) > 1:
                    wrong += 1
                    print(f"line {number}: {question} has {expected} twice")
                break

    print(f"checked {checked} questions, {wrong} wrong")
    return 1 if wrong else 0


if __name__ == "__main__":
    sys.exit(main())
