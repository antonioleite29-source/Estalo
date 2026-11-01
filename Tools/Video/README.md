# The trailer

`estalo-trailer.mp4` — 1080 x 1920, 30 fps, about a minute, Portuguese narration over the game's own
match music.

## How it is made

```bash
./Tools/Video/narrate.sh          # placeholder narration (macOS voice Luciana, pt-BR) into voice/mac
swift Tools/Video/trailer.swift   # draws every frame and writes the mp4
```

Both need only what comes with macOS. The renderer reads the game's real art — the duel board, the
2v2 board, the button states, the twelve avatars, the logo, the Inter faces — and the music in
`Assets/Resources/Music`, so when the art changes the trailer changes with it.

## What this is, and is not

Every screen is placed with the numbers taken straight out of `ProjectCapstone.unity`: the board is
the game's own 1179 x 2556 layout, the question sits at (22.885, 974) at a scale of 4, the four
answer buttons are 200 square at y 229, the players at (-241, -285) and (250, -295), the solo rings
where the donuts are. The sprites are the ones the board really uses, out of `Assets/Data/Themes`.

It is **drawn, not captured**. Unity cannot be driven frame by frame from outside, so every screen
here is a reconstruction of the real one: the same art, the same layout, the same rules, real
questions. Nothing is invented about how the game behaves, but no frame of it came off a phone.

For a submission that has to show the app itself running, record the screen instead and follow
`MVP_SCRIPT.md`, which is the shot list for exactly that.

## Changing it

- **Words:** the lines are in `narrate.sh`; record again and the scenes re-time themselves to the new
  lengths.
- **A human voice:** `voice/lines.txt` holds the eight lines and the voice brief for Higgsfield, the
  same way the LightZ Out trailer was voiced. Drop `v1.mp3` … `v8.mp3` into `Tools/Video/voice` and
  they beat the Mac voice in `voice/mac`; the scenes re-time themselves to the new lengths.
- **Scenes:** each one is a closure in `trailer.swift` given its own clock in seconds. The order of
  `scenes` is the order on screen, and `voiceOrder` says which line plays over which.
- **Music:** `Match.mp3` under the voice at 22%. Change `musicURL` for the lobby loop instead.
