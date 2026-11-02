#!/usr/bin/env python3
"""Builds the shipped question file: the old bank minus what the audit flags, plus the generated one.

    python3 Tools/Questions/merge.py old.txt generated.tsv > new.txt
"""

import sys

sys.path.insert(0, "Tools/Questions")
from audit import reasons   # noqa: E402

HEADER = "difficulty\ttopic\tquestion\tanswerA\tanswerB\tanswerC\tanswerD\tcorrectIndex"


def read(path):
    rows = []
    with open(path, encoding="utf-8") as handle:
        for line in handle:
            row = line.rstrip("\n").split("\t")
            if len(row) >= 8 and row[0].isdigit():
                rows.append(row[:8])
    return rows


def main():
    old = read(sys.argv[1])
    new = read(sys.argv[2])

    kept = [row for row in old if not reasons(row)]
    dropped = len(old) - len(kept)

    seen = set()
    out = []

    for row in kept + new:
        key = (row[0], row[2].strip().lower())
        if key in seen:
            continue
        seen.add(key)
        out.append(row)

    print(HEADER)
    for row in out:
        print("\t".join(row))

    levels = {}
    for row in out:
        levels[row[0]] = levels.get(row[0], 0) + 1

    print(f"kept {len(kept)} of {len(old)} old questions ({dropped} dropped), "
          f"added {len(out) - len(kept)} new, {len(out)} in all", file=sys.stderr)
    for level in sorted(levels):
        print(f"  level {level}: {levels[level]}", file=sys.stderr)


if __name__ == "__main__":
    main()
