# The trailer

`estalo-trailer.mp4` — 1080 x 1920, 30 fps, about a minute, Portuguese narration over the game's own
match music.

## How it is made

```bash
./Tools/Video/narrate.sh          # records the narration (macOS voice Luciana, pt-BR)
swift Tools/Video/trailer.swift   # draws every frame and writes the mp4
```

Both need only what comes with macOS. The renderer reads the game's real art — the duel board, the
2v2 board, the button states, the twelve avatars, the logo, the Inter faces — and the music in
`Assets/Resources/Music`, so when the art changes the trailer changes with it.

## What this is, and is not

It is **drawn, not captured**. Unity cannot be driven frame by frame from outside, so every screen
here is a reconstruction of the real one: the same art, the same layout, the same rules, real
questions. Nothing is invented about how the game behaves, but no frame of it came off a phone.

For a submission that has to show the app itself running, record the screen instead and follow
`MVP_SCRIPT.md`, which is the shot list for exactly that.

## Changing it

- **Words:** the lines are in `narrate.sh`; record again and the scenes re-time themselves to the new
  lengths.
- **A human voice:** replace any file in `Tools/Video/voice` with a recording of the same name.
- **Scenes:** each one is a closure in `trailer.swift` given its own clock in seconds. The order of
  `scenes` is the order on screen, and `voiceOrder` says which line plays over which.
- **Music:** `Match.mp3` under the voice at 22%. Change `musicURL` for the lobby loop instead.
