#!/usr/bin/env python3
"""Flags trivia questions that cannot fairly be answered in the few seconds a round gives.

The rules below come from what the game actually asks of a player: read the question, pick one of
four answers, inside a four-second solo window. An item that needs long multiplication, a recalled
formula for an uncommon shape, or university vocabulary is not hard, it is unanswerable -- and the
round it lands in is decided by who guesses.

    python3 Tools/Questions/audit.py Assets/Resources/TriviaQuestions.txt [--list]
"""

import re
import sys
from collections import Counter

HEAVY_WORDS = (
    "fatorial", "grafo", "cromático", "cromatico", "permutaç", "combinaç", "logaritmo",
    "derivada", "integral", "matriz", "vetor", "radiano", "seno", "cosseno", "tangente",
    "raiz cúbica", "raiz cubica", "trapézio", "trapezio", "losango", "icosaedro", "dodecaedro",
    "eneágono", "eneagono", "decágono", "decagono", "undecágono", "icoságono",
    "progressão geométrica", "progressao geometrica", "número primo de", "fibonacci de",
    "bhaskara", "equação do segundo grau", "equacao do segundo grau",
    "porcentagem composta", "juros compostos", "desvio padrão", "desvio padrao", "mediana de",
)

def numbers(text):
    return [int(n) for n in re.findall(r"\d+", text.replace(".", ""))]

def words(text):
    return len(text.split())

def reasons(row):
    difficulty, topic, question = int(row[0]), row[1], row[2]
    answers = row[3:7]
    found = []
    low = question.lower()

    for word in HEAVY_WORDS:
        if word in low:
            found.append(f"vocabulary: {word}")
            break

    big = [n for n in numbers(question) if n > 1000]
    if big:
        found.append(f"number too big: {max(big)}")

    # Multiplication where neither side is a friendly one (10, 11, 5, 9, 2).
    for a, b in re.findall(r"(\d+)\s*[x×*]\s*(\d+)", question):
        a, b = int(a), int(b)
        # A product is fine when one side is friendly: a table fact, or a multiple of 5 or 10.
        friendly = a <= 12 or b <= 12 or a % 5 == 0 or b % 5 == 0
        if not friendly:
            found.append(f"multiplication {a} x {b}")

    # Powers and roots outside what anybody holds in their head.
    for base, power in re.findall(r"(\d+)\s*(?:elevado a|\^|ao quadrado|ao cubo)\s*(\d*)", question):
        if int(base) > 20:
            found.append(f"power of {base}")

    for root in re.findall(r"raiz quadrada de (\d+)", low):
        if int(root) > 400:
            found.append(f"root of {root}")

    # Long division: a three-digit dividend by anything that is not 2, 5 or 10.
    for top, bottom in re.findall(r"(\d{3,})\s*(?:÷|/|dividido por)\s*(\d+)", question):
        if int(bottom) not in (2, 5, 10, 100):
            found.append(f"division {top} / {bottom}")

    # MMC and MDC are school words here, and fine on small numbers; past that they are long division
    # wearing a hat.
    for which in ("mmc", "mdc"):
        if which in low:
            pair = [n for n in numbers(question) if n > 60]
            if pair:
                found.append(f"{which} of {max(pair)}")

    if words(question) > 18:
        found.append(f"too long: {words(question)} words")

    if len(set(answers)) != 4:
        found.append("repeated answers")

    return found

def main():
    path = sys.argv[1]
    show = "--list" in sys.argv

    flagged = []
    total = 0

    with open(path, encoding="utf-8") as handle:
        next(handle)
        for line in handle:
            row = line.rstrip("\n").split("\t")
            if len(row) < 8:
                continue
            total += 1
            why = reasons(row)
            if why:
                flagged.append((row, why))

    print(f"{len(flagged)} of {total} questions flagged")
    counts = Counter(why.split(":")[0].split(" ")[0] for _, reason in flagged for why in reason)
    for reason, count in counts.most_common():
        print(f"  {count:4d}  {reason}")

    if show:
        print()
        for row, why in flagged:
            print(f"L{row[0]} {row[1]:14s} {row[2][:80]:82s} <- {'; '.join(why)}")

if __name__ == "__main__":
    main()
