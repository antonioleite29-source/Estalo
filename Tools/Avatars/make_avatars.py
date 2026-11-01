# Twelve avatars drawn here rather than downloaded: flat geometric marks in the game's own palette,
# 256 x 256 PNG, no faces and no licence to carry. Written with zlib and struct so it needs nothing
# installed.
import math, struct, zlib

SIZE = 256
# The duel board's two sides, plus the lobby's blues and the answer-button purple.
PALETTE = [
    ((0x7A, 0x8A, 0xC4), (0xF2, 0xF4, 0xFB)),
    ((0xCE, 0x6F, 0x6A), (0xFF, 0xEC, 0xE8)),
    ((0x6F, 0xB7, 0xC9), (0xEF, 0xFA, 0xFC)),
    ((0x9A, 0x86, 0xC0), (0xF6, 0xF1, 0xFF)),
    ((0x4C, 0x9A, 0x7A), (0xEC, 0xFA, 0xF4)),
    ((0xD8, 0x9B, 0x4A), (0xFF, 0xF3, 0xE2)),
]

def blank(bg):
    return [[bg for _ in range(SIZE)] for _ in range(SIZE)]

def put(px, x, y, c):
    if 0 <= x < SIZE and 0 <= y < SIZE:
        px[y][x] = c

def disc(px, cx, cy, r, c):
    for y in range(max(0, int(cy - r)), min(SIZE, int(cy + r) + 1)):
        for x in range(max(0, int(cx - r)), min(SIZE, int(cx + r) + 1)):
            if (x - cx) ** 2 + (y - cy) ** 2 <= r * r:
                put(px, x, y, c)

def ring(px, cx, cy, r, w, c):
    for y in range(SIZE):
        for x in range(SIZE):
            d = math.hypot(x - cx, y - cy)
            if r - w <= d <= r:
                put(px, x, y, c)

def band(px, test, c):
    for y in range(SIZE):
        for x in range(SIZE):
            if test(x, y):
                put(px, x, y, c)

def write(path, px):
    raw = b"".join(b"\x00" + b"".join(struct.pack("BBB", *px[y][x]) for x in range(SIZE)) for y in range(SIZE))
    def chunk(tag, data):
        c = struct.pack(">I", len(data)) + tag + data
        return c + struct.pack(">I", zlib.crc32(tag + data) & 0xFFFFFFFF)
    png = b"\x89PNG\r\n\x1a\n"
    png += chunk(b"IHDR", struct.pack(">IIBBBBB", SIZE, SIZE, 8, 2, 0, 0, 0))
    png += chunk(b"IDAT", zlib.compress(raw, 9))
    png += chunk(b"IEND", b"")
    open(path, "wb").write(png)

C = SIZE / 2

def rings(fg, bg):
    px = blank(bg)
    for i, r in enumerate((112, 84, 56, 28)):
        ring(px, C, C, r, 14, fg if i % 2 == 0 else bg)
    return px

def chevrons(fg, bg):
    px = blank(bg)
    band(px, lambda x, y: (abs(x - C) + y) % 72 < 36, fg)
    return px

def hexagon(fg, bg):
    px = blank(bg)
    def inside(x, y):
        dx, dy = abs(x - C) / 104, abs(y - C) / 104
        return dy <= 0.86 and 0.86 * dx + 0.5 * dy <= 0.86
    band(px, inside, fg)
    return px

def burst(fg, bg):
    px = blank(bg)
    band(px, lambda x, y: math.hypot(x - C, y - C) < 112 and
         int((math.atan2(y - C, x - C) + math.pi) / (math.pi / 6)) % 2 == 0, fg)
    return px

def waves(fg, bg):
    px = blank(bg)
    band(px, lambda x, y: y > C + 26 * math.sin(x / 26) and y > C - 40, fg)
    disc(px, C, C - 52, 34, fg)
    return px

def dots(fg, bg):
    px = blank(bg)
    for gy in range(4):
        for gx in range(4):
            disc(px, 48 + gx * 54, 48 + gy * 54, 18 if (gx + gy) % 2 == 0 else 11, fg)
    return px

def split(fg, bg):
    px = blank(bg)
    band(px, lambda x, y: y > x, fg)
    disc(px, C, C, 46, bg)
    return px

def peak(fg, bg):
    px = blank(bg)
    band(px, lambda x, y: y > 210 - abs(x - C) * 1.1, fg)
    disc(px, 176, 74, 28, fg)
    return px

def squares(fg, bg):
    px = blank(bg)
    for i, s in enumerate((104, 74, 44, 20)):
        band(px, lambda x, y, s=s: abs(x - C) <= s and abs(y - C) <= s, fg if i % 2 == 0 else bg)
    return px

def cross(fg, bg):
    px = blank(bg)
    band(px, lambda x, y: abs(x - C) < 26 or abs(y - C) < 26, fg)
    disc(px, C, C, 40, bg)
    return px

def bars(fg, bg):
    px = blank(bg)
    for i, h in enumerate((70, 128, 96, 160)):
        band(px, lambda x, y, i=i, h=h: 38 + i * 48 <= x < 38 + i * 48 + 34 and SIZE - 42 - h <= y < SIZE - 42, fg)
    return px

def bubbles(fg, bg):
    px = blank(bg)
    for cx, cy, r in ((96, 104, 46), (168, 88, 30), (140, 166, 38), (80, 180, 22)):
        disc(px, cx, cy, r, fg)
    return px

DESIGNS = [rings, chevrons, hexagon, burst, waves, dots, split, peak, squares, cross, bars, bubbles]

for i, draw in enumerate(DESIGNS):
    fg, bg = PALETTE[i % len(PALETTE)]
    write(f"web/mine{i + 1}.png", draw(fg, bg))
    print("mine%d %s" % (i + 1, draw.__name__))
