#!/usr/bin/env python3
"""Writes the question bank: short items that can be answered by a strategy, not by grinding.

What the research says, and what it means here:

* Fluency is efficiency AND accuracy, so an item has to be answerable in the few seconds a round
  gives. Long multiplication and recalled formulas for uncommon shapes fail that on their own.
* Number sense (estimating, comparing, benchmarks) and mental computation are different things, and
  students score far lower on the first. Both belong in the bank, mixed.
* Distractors should be the mistakes people actually make -- off by one, the other operation, the
  forgotten carry, the reversed digits -- not random numbers, which are free to eliminate.
* Timed drill raises anxiety when it is nothing but recall, so every level mixes reasoning and
  pattern items in with the arithmetic.

    python3 Tools/Questions/generate.py > Tools/Questions/generated.tsv
"""

import random
import sys

random.seed(20261102)

rows = []
seen = set()


def add(level, topic, question, correct, wrong):
    """One item. `wrong` are candidate mistakes; the first three distinct ones are used."""
    key = (level, question)
    if key in seen:
        return

    answers = [str(correct)]
    for candidate in wrong:
        text = str(candidate)
        if text not in answers:
            answers.append(text)
        if len(answers) == 4:
            break

    if len(answers) < 4:
        return

    order = answers[:]
    random.shuffle(order)
    seen.add(key)
    rows.append([str(level), topic, question, *order, str(order.index(str(correct)))])


def near(value, *extra):
    """The usual slips: one either side, ten either side, and anything handed in."""
    return [value + 1, value - 1, value + 10, value - 10, value + 2, value - 2, *extra]


# --- level 1: facts inside 20, and what a number is ----------------------------------------------

for a in range(1, 11):
    for b in range(1, 11):
        add(1, "adicao", f"Quanto é {a} + {b}?", a + b, near(a + b))
        if a >= b:
            add(1, "subtracao", f"Quanto é {a} - {b}?", a - b, near(a - b, a + b))

for n in range(1, 11):
    add(1, "adicao", f"Quanto é {n} + {n}?", 2 * n, near(2 * n, 2 * n + n))
    add(1, "sequencias", f"Qual vem depois: {n}, {n + 1}, {n + 2}, ...?", n + 3, near(n + 3))
    add(1, "raciocinio", f"Qual é maior: {n} ou {n + 3}?", n + 3, [n, n + 1, n + 2, n + 4])

for shape, sides in (("triângulo", 3), ("quadrado", 4), ("pentágono", 5), ("hexágono", 6)):
    add(1, "geometria", f"Quantos lados tem um {shape}?", sides, near(sides))

for n in (2, 4, 6, 8, 10, 12, 14, 16, 18, 20):
    add(1, "classificacao", f"{n} é par ou ímpar?", "par", ["ímpar", "os dois", "nenhum"])
    add(1, "classificacao", f"{n + 1} é par ou ímpar?", "ímpar", ["par", "os dois", "nenhum"])


# --- level 2: inside 100, the easy tables, halves --------------------------------------------------

for a in range(11, 60, 3):
    for b in range(2, 10):
        add(2, "adicao", f"Quanto é {a} + {b}?", a + b, near(a + b, a + b + 10))
        add(2, "subtracao", f"Quanto é {a} - {b}?", a - b, near(a - b, a - b - 10))

for n in range(2, 11):
    for by in (2, 5, 10):
        add(2, "multiplicacao", f"Quanto é {n} × {by}?", n * by, near(n * by, n * by + by, n + by))
    add(2, "divisao", f"Quanto é {n * 2} ÷ 2?", n, near(n, n * 2))
    add(2, "divisao", f"Quanto é {n * 10} ÷ 10?", n, near(n, n * 10))
    add(2, "raciocinio", f"Qual é a metade de {n * 2}?", n, near(n, n * 4))
    add(2, "raciocinio", f"Qual é o dobro de {n}?", n * 2, near(n * 2, n + 2))

for start, step in ((2, 2), (5, 5), (10, 10), (3, 3), (4, 4), (6, 6), (7, 7), (9, 9)):
    run = [start + step * i for i in range(4)]
    add(2, "sequencias", f"Complete: {run[0]}, {run[1]}, {run[2]}, ...", run[3],
        near(run[3], run[3] + step))

for n in range(20, 100, 7):
    add(2, "raciocinio", f"{n} está mais perto de {n // 10 * 10} ou de {n // 10 * 10 + 10}?",
        n // 10 * 10 if n % 10 < 5 else n // 10 * 10 + 10,
        [n // 10 * 10 + 10 if n % 10 < 5 else n // 10 * 10, n, n + 5])


# --- level 3: the tables, carrying, a quarter and a half -------------------------------------------

for a in range(2, 11):
    for b in range(2, 11):
        add(3, "multiplicacao", f"Quanto é {a} × {b}?", a * b,
            near(a * b, a * b + a, a * b - a, a + b))
        add(3, "divisao", f"Quanto é {a * b} ÷ {b}?", a, near(a, b, a * b))

for a in range(23, 90, 6):
    for b in (8, 9, 17, 26):
        add(3, "adicao", f"Quanto é {a} + {b}?", a + b, near(a + b, a + b - 10, a + b + 10))
        if a > b:
            add(3, "subtracao", f"Quanto é {a} - {b}?", a - b, near(a - b, a - b + 10, b - a))

for n in (12, 16, 20, 24, 28, 32, 36, 40, 48, 60, 80, 100):
    add(3, "raciocinio", f"Qual é a metade de {n}?", n // 2, near(n // 2, n * 2, n // 4))
    if n % 4 == 0:
        add(3, "raciocinio", f"Quanto é um quarto de {n}?", n // 4, near(n // 4, n // 2, n * 4))

for n in (10, 20, 30, 40, 50, 60, 70, 80, 90, 100):
    add(3, "raciocinio", f"Quanto é 10% de {n}?", n // 10, near(n // 10, n // 2, n // 5))

for hands, item in ((3, "lápis"), (4, "figurinhas"), (5, "balas"), (6, "moedas")):
    for each in (2, 3, 4, 5):
        add(3, "raciocinio", f"{hands} caixas com {each} {item}. Quantos ao todo?", hands * each,
            near(hands * each, hands + each))


# --- level 4: tables to 12, the 9 and 11 tricks, percentages of friendly numbers --------------------

for a in range(2, 13):
    for b in (9, 11, 12):
        add(4, "multiplicacao", f"Quanto é {a} × {b}?", a * b,
            near(a * b, a * b + a, a * b - a, a * (b - 1)))

for n in (200, 300, 400, 500, 600, 800, 1000):
    add(4, "raciocinio", f"Quanto é 10% de {n}?", n // 10, near(n // 10, n // 100, n // 2))
    add(4, "raciocinio", f"Quanto é 50% de {n}?", n // 2, near(n // 2, n // 4, n // 10))
    add(4, "raciocinio", f"Quanto é 25% de {n}?", n // 4, near(n // 4, n // 2, n // 5))

for n in range(13, 100, 4):
    add(4, "divisao", f"Qual é o resto de {n} ÷ 5?", n % 5, [x for x in range(5) if x != n % 5])
    add(4, "divisao", f"Qual é o resto de {n} ÷ 2?", n % 2, [1 - n % 2, 2, 3])

for a, b in ((3, 4), (5, 6), (7, 8), (9, 10), (11, 12), (6, 9), (8, 12)):
    add(4, "geometria", f"Um retângulo mede {a} por {b}. Qual é a área?", a * b,
        near(a * b, 2 * (a + b), a + b))
    add(4, "geometria", f"Um retângulo mede {a} por {b}. Qual é o perímetro?", 2 * (a + b),
        near(2 * (a + b), a * b, a + b))

for start, step in ((3, 4), (7, 5), (2, 6), (5, 7), (4, 8), (6, 9)):
    run = [start + step * i for i in range(4)]
    add(4, "sequencias", f"Complete: {run[0]}, {run[1]}, {run[2]}, ...", run[3],
        near(run[3], run[3] + step))

for start in (1, 2, 3, 5):
    run = [start * 2 ** i for i in range(4)]
    add(4, "sequencias", f"Complete: {run[0]}, {run[1]}, {run[2]}, ...", run[3],
        near(run[3], run[2] + run[1], run[3] * 2))


# --- level 5: two steps, squares to 15, angles, averages --------------------------------------------

for n in range(2, 16):
    add(5, "potencias", f"Quanto é {n} ao quadrado?", n * n, near(n * n, n * 2, n * n + n))

for a in (12, 15, 18, 24, 25, 36, 48, 50):
    for b in (4, 6, 8):
        if a % b == 0:
            add(5, "divisao", f"Quanto é {a} ÷ {b}?", a // b, near(a // b, a - b, a * b))
            add(5, "raciocinio", f"Quanto é {a} ÷ {b} + 7?", a // b + 7,
                near(a // b + 7, a // b, a // b + 70))

for n in (20, 40, 60, 80, 120, 200):
    add(5, "raciocinio", f"Quanto é 15% de {n}?", int(n * 0.15), near(int(n * 0.15), n // 10, n // 5))
    add(5, "raciocinio", f"Quanto é 75% de {n}?", int(n * 0.75), near(int(n * 0.75), n // 4, n // 2))

for a, b, c in ((2, 4, 6), (3, 5, 7), (10, 20, 30), (4, 8, 12), (5, 10, 15), (6, 12, 18)):
    add(5, "raciocinio", f"Qual é a média de {a}, {b} e {c}?", (a + b + c) // 3,
        near((a + b + c) // 3, a + b + c, b + c))

for first in (30, 45, 50, 60, 70, 80, 100, 110):
    add(5, "geometria", f"Dois ângulos de um triângulo medem {first}° e 60°. Quanto mede o terceiro?",
        180 - first - 60, near(180 - first - 60, 180 - first, first))
    add(5, "geometria", f"Um ângulo mede {first}°. Quanto falta para 180°?", 180 - first,
        near(180 - first, 90 - first if first < 90 else first - 90, first))

for a, b in ((1, 2), (1, 4), (3, 4), (2, 5), (3, 5), (1, 5), (2, 3)):
    add(5, "raciocinio", f"Qual é maior: {a}/{b} ou 1/2?",
        f"{a}/{b}" if a / b > 0.5 else "1/2",
        ["1/2" if a / b > 0.5 else f"{a}/{b}", "são iguais", "não dá para saber"])


# --- level 6: divisibility, rounding, one-step algebra, friendly rates ------------------------------

for n in (24, 36, 45, 56, 64, 72, 81, 90, 96, 108, 120, 144):
    for by in (3, 4, 6, 9):
        answer = "sim" if n % by == 0 else "não"
        add(6, "divisao", f"{n} é divisível por {by}?", answer,
            ["não" if answer == "sim" else "sim", "só se for par", "só se for ímpar"])

for n in range(115, 900, 37):
    hundred = round(n / 100) * 100
    add(6, "raciocinio", f"Arredonde {n} para a centena mais próxima.", hundred,
        near(hundred, hundred + 100, hundred - 100))
    ten = round(n / 10) * 10
    add(6, "raciocinio", f"Arredonde {n} para a dezena mais próxima.", ten, near(ten, ten + 10, n))

for a in range(2, 10):
    for b in range(1, 10):
        x = b
        add(6, "algebra", f"Resolva: {a}x = {a * x}.", x, near(x, a, a * x))
        add(6, "algebra", f"Resolva: x + {a} = {a + b}.", b, near(b, a, a + b))
        add(6, "algebra", f"Resolva: x - {a} = {b}.", a + b, near(a + b, b - a, b))

for price, off in ((40, 10), (50, 20), (60, 50), (80, 25), (100, 30), (200, 15), (120, 50)):
    answer = price - price * off // 100
    add(6, "raciocinio", f"Um jogo de R$ {price} com {off}% de desconto. Quanto fica?", answer,
        near(answer, price * off // 100, price))

for km, hours in ((60, 2), (90, 3), (120, 4), (150, 5), (100, 2), (180, 3)):
    add(6, "raciocinio", f"Um carro faz {km} km em {hours} h. Qual é a velocidade média?",
        f"{km // hours} km/h", [f"{km} km/h", f"{km // hours + 10} km/h", f"{km * hours} km/h"])

for minutes in (90, 120, 150, 180, 210, 240):
    add(6, "raciocinio", f"Quantas horas são {minutes} minutos?",
        f"{minutes // 60}h{minutes % 60:02d}", [f"{minutes // 60}h", f"{minutes // 30}h",
                                                f"{minutes // 60 + 1}h{minutes % 60:02d}"])


# --- level 7: two-step algebra, ratio, probability, short logic -------------------------------------

for a in range(2, 10):
    for b in (3, 5, 7, 11):
        x = a
        total = a * x + b
        add(7, "algebra", f"Resolva: {a}x + {b} = {total}.", x, near(x, total - b, b))
        add(7, "algebra", f"Resolva: {a}x - {b} = {a * x - b}.", x, near(x, a, b))

for parts_a, parts_b, total in ((1, 2, 30), (2, 3, 50), (1, 3, 40), (3, 4, 70), (1, 4, 60)):
    share = total // (parts_a + parts_b) * parts_a
    add(7, "raciocinio",
        f"R$ {total} divididos na razão {parts_a}:{parts_b}. Quanto fica a menor parte?", share,
        near(share, total - share, total // 2))

for sides, want in ((6, "um número par"), (6, "um número maior que 4"), (6, "o 6")):
    chance = {"um número par": "1/2", "um número maior que 4": "1/3", "o 6": "1/6"}[want]
    add(7, "probabilidade", f"Num dado de 6 faces, qual é a chance de sair {want}?", chance,
        [c for c in ("1/2", "1/3", "1/6", "1/4", "2/3") if c != chance])

for total, red in ((10, 4), (12, 3), (20, 5), (8, 2), (15, 5)):
    chance = f"{red}/{total}"
    add(7, "probabilidade", f"Numa caixa com {total} bolas, {red} são vermelhas. Qual é a chance?",
        chance, [f"{total - red}/{total}", f"{red}/{total - red}", f"{total}/{red}"])

for n in (13, 17, 19, 23, 29, 31, 37, 41, 43, 47):
    add(7, "classificacao", f"{n} é primo?", "sim", ["não", "só é divisível por 3", "é quadrado"])

for n in (21, 27, 33, 39, 49, 51, 57, 63, 91):
    add(7, "classificacao", f"{n} é primo?", "não", ["sim", "é primo e par", "não dá para saber"])

for people in (3, 4, 5, 6):
    shakes = people * (people - 1) // 2
    add(7, "logica", f"{people} pessoas se cumprimentam uma vez cada. Quantos cumprimentos?",
        shakes, near(shakes, people * people, people * (people - 1)))

for teams in (4, 8, 16):
    add(7, "logica", f"Num mata-mata com {teams} times, quantos jogos até o campeão?", teams - 1,
        near(teams - 1, teams, teams // 2))

for a, b in ((2, 6), (3, 9), (4, 12), (5, 20), (6, 18)):
    add(7, "raciocinio", f"Se {a} cadernos custam R$ {b * 2}, quanto custam {a * 2}?", b * 4,
        near(b * 4, b * 2, b * 3))


# --- more of levels 4, 5 and 7, which were thin next to the rest ----------------------------------

# Level 4: adding and taking two-digit numbers with a carry, and the missing part.
for a in range(24, 96, 5):
    for b in (18, 27, 35, 46):
        add(4, "adicao", f"Quanto é {a} + {b}?", a + b, near(a + b, a + b - 10, a + b + 10))
        if a > b:
            add(4, "subtracao", f"Quanto é {a} - {b}?", a - b, near(a - b, a - b + 10, b - a))
        add(4, "algebra", f"Quanto falta para {a} chegar a {a + b}?", b, near(b, a, a + b))

for n in range(15, 96, 6):
    add(4, "raciocinio", f"Qual é o dobro de {n}?", n * 2, near(n * 2, n + 2, n * 2 + 10))
    if n % 2 == 0:
        add(4, "raciocinio", f"Qual é a metade de {n}?", n // 2, near(n // 2, n * 2, n // 4))

for a, b in ((6, 7), (7, 8), (8, 9), (6, 8), (7, 9), (4, 12), (5, 11)):
    add(4, "raciocinio", f"Qual é maior: {a} × {b} ou {a + 1} × {b - 1}?",
        f"{a} × {b}" if a * b > (a + 1) * (b - 1) else f"{a + 1} × {b - 1}",
        [f"{a + 1} × {b - 1}" if a * b > (a + 1) * (b - 1) else f"{a} × {b}",
         "são iguais", "não dá para saber"])

for n in (3, 4, 5, 6, 7, 8, 9, 10, 12):
    add(4, "geometria", f"Um quadrado tem lado {n}. Qual é o perímetro?", 4 * n,
        near(4 * n, n * n, 2 * n))
    add(4, "geometria", f"Um quadrado tem lado {n}. Qual é a área?", n * n,
        near(n * n, 4 * n, 2 * n))

# Level 5: friendly products, fractions of a quantity, and what a shape gives back.
for a, b in ((25, 4), (50, 6), (20, 15), (12, 25), (40, 5), (15, 6), (24, 5), (18, 5)):
    add(5, "multiplicacao", f"Quanto é {a} × {b}?", a * b,
        near(a * b, a * b + a, a * b - b, a * (b - 1)))

for whole, top, bottom in ((30, 2, 3), (40, 3, 4), (45, 2, 5), (60, 5, 6), (24, 3, 8),
                           (36, 2, 9), (50, 4, 5), (28, 3, 7)):
    answer = whole * top // bottom
    add(5, "raciocinio", f"Quanto é {top}/{bottom} de {whole}?", answer,
        near(answer, whole // bottom, whole - answer))

for n in (16, 25, 36, 49, 64, 81, 100, 121, 144):
    add(5, "potencias", f"Qual é a raiz quadrada de {n}?", int(n ** 0.5),
        near(int(n ** 0.5), n // 2, int(n ** 0.5) + 2))
    add(5, "geometria", f"Um quadrado tem área {n}. Quanto mede o lado?", int(n ** 0.5),
        near(int(n ** 0.5), n // 4, n // 2))

for n in (20, 30, 40, 50, 60, 70, 90):
    add(5, "raciocinio", f"Quanto é 20% de {n}?", n // 5, near(n // 5, n // 10, n // 2))
    add(5, "raciocinio", f"Quanto é 30% de {n}?", n * 3 // 10, near(n * 3 // 10, n // 3, n // 10))

for metres in (2, 3, 5, 7, 10, 12):
    add(5, "raciocinio", f"Quantos centímetros são {metres} metros?", metres * 100,
        near(metres * 100, metres * 10, metres * 1000))

for start, step in ((2, 3), (3, 5), (10, 7), (4, 6), (1, 9)):
    run = [start + step * i for i in range(5)]
    add(5, "sequencias", f"Qual é o próximo: {run[0]}, {run[1]}, {run[2]}, {run[3]}, ...?", run[4],
        near(run[4], run[4] + step, run[3] + run[2]))

# Level 7: proportion, percentage change, averages with a hole in them, parity reasoning.
for a, b, c in ((2, 5, 8), (3, 4, 12), (5, 2, 20), (4, 7, 16), (6, 5, 18)):
    answer = b * c // a
    add(7, "raciocinio", f"Se {a} kg custam R$ {b * a}, quanto custam {c} kg?", c * b,
        near(c * b, b * a, c + b))

for price, change in ((50, 20), (80, 25), (200, 10), (60, 50), (150, 20)):
    answer = price + price * change // 100
    add(7, "raciocinio", f"Um preço de R$ {price} sobe {change}%. Quanto fica?", answer,
        near(answer, price - price * change // 100, price * change // 100))

for a, b, mean in ((4, 8, 6), (10, 20, 15), (5, 15, 10), (12, 18, 15), (3, 9, 6)):
    add(7, "algebra", f"A média de {a}, {b} e x é {mean}. Quanto é x?", 3 * mean - a - b,
        near(3 * mean - a - b, mean, a + b))

for n in (7, 9, 11, 13, 15):
    add(7, "logica", f"A soma de {n} números ímpares é par ou ímpar?",
        "par" if n % 2 == 0 else "ímpar", ["ímpar" if n % 2 == 0 else "par", "depende", "zero"])

for pens, cost in ((4, 12), (5, 20), (6, 18), (8, 24), (9, 27)):
    add(7, "raciocinio", f"{pens} canetas custam R$ {cost}. Quanto custa uma?", cost // pens,
        near(cost // pens, cost - pens, cost))

for a in (12, 18, 24, 30, 36):
    for b in (8, 9, 15):
        from math import gcd
        add(7, "classificacao", f"Qual é o MDC de {a} e {b}?", gcd(a, b),
            near(gcd(a, b), a * b // gcd(a, b), min(a, b)))

for first, ratio in ((2, 3), (3, 2), (1, 5), (5, 2), (2, 4)):
    run = [first * ratio ** i for i in range(4)]
    add(7, "sequencias", f"Qual é o próximo: {run[0]}, {run[1]}, {run[2]}, ...?", run[3],
        near(run[3], run[2] + run[1], run[3] * ratio))


out = sys.stdout
for row in rows:
    out.write("\t".join(row) + "\n")

print(f"{len(rows)} questions", file=sys.stderr)
levels = {}
for row in rows:
    levels[row[0]] = levels.get(row[0], 0) + 1
for level in sorted(levels):
    print(f"  level {level}: {levels[level]}", file=sys.stderr)
