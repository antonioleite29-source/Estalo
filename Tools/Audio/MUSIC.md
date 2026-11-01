# Music: where to get it, and where it goes

The game looks for four clips in `Assets/Resources/Music`, by exact name:

| File | When it plays | Length |
|---|---|---|
| `Lobby` | Lobby, learning path, profile, settings — any page outside a match | 45–90 s, loops |
| `Match` | During a 1v1, a 2v2, a lesson and the tutorial | 60–120 s, loops |
| `Win` | The match ends and this player won | 3–6 s, plays once |
| `Lose` | The match ends and this player lost | 3–6 s, plays once |

Drop the files in with those names, any of `.ogg`, `.mp3` or `.wav`, and they play with nothing to
wire up. A missing file is silent and logs one line. Som: desligado on the More page silences the
music as well, because it drops the whole audio listener.

## Where to download, free and legal

Listed best first for this project. **Check the licence on the page before you download** — a few
of these sites host tracks under mixed terms.

1. **Pixabay Music** — https://pixabay.com/music/
   Free, no attribution required, commercial use allowed, which includes a paid app store listing.
   Best single source for the two loops. Search: `8-bit loop`, `chiptune game`, `puzzle game
   background`, `quiz show`.
2. **Kenney** — https://kenney.nl/assets?q=audio
   Public domain (CC0), made for games. The **Music Jingles** pack is a set of short stingers —
   exactly what `Win` and `Lose` need. Also **Interface Sounds** if you want to replace button
   clicks later.
3. **FreePD** — https://freepd.com/
   Public domain, no attribution. The Electronic and Upbeat pages hold usable loops.
4. **Mixkit** — https://mixkit.co/free-stock-music/
   Free licence, no attribution, but no redistribution of the raw file — fine inside a game.
5. **Incompetech (Kevin MacLeod)** — https://incompetech.com/music/royalty-free/music.html
   CC-BY 4.0: free, **but you must credit him**. Lots of classic game loops. Named tracks that suit
   Estalo: *Bit Shift*, *Cipher*, *Pixel Peeker Polka*, *Monkeys Spinning Monkeys*, *Wallpaper*.
6. **OpenGameArt** — https://opengameart.org/art-search?keys=music
   Mixed licences. Filter to CC0 to avoid attribution work.
7. **Freesound** — https://freesound.org/
   Per-sound licences; filter to CC0. Needs a free account. Best for short stingers, not loops.

## What to pick

- **Lobby:** calm, no strong melody, nothing that peaks. It plays while people read and pick
  lessons, so a loop that draws attention gets switched off.
- **Match:** faster, steady pulse, still no big melodic hook — the questions need the attention.
  Quiz-show urgency works well because the game already has a timer.
- **Win:** rising, 3–4 notes, ends resolved.
- **Lose:** same shape falling, and keep it light. These are students doing maths; a sad sting reads
  as being told off.
- Volume is set in code at 0.35 against 0.8 for the effects (`GameMusic.Volume`), so the existing
  sounds stay on top. Pick tracks that are already mixed quietly rather than loud ones.

## Import settings in Unity

Select the file in `Assets/Resources/Music`, then in the Inspector:

- **Lobby and Match:** Load Type **Streaming**, Compression Format **Vorbis**, Quality ~70. Keeps
  them out of memory.
- **Win and Lose:** Load Type **Decompress On Load**, Compression Format **ADPCM** or **PCM**. They
  are short and must start instantly.
- Leave **Force To Mono** off for music.

## If you use a CC-BY track

Add the credit to the Créditos panel on the More page (`CreditsPanel/Body` in the scene, built by
`SettingsPage`). The wording Kevin MacLeod asks for is:

```
"Bit Shift" Kevin MacLeod (incompetech.com)
Licensed under Creative Commons: By Attribution 4.0
creativecommons.org/licenses/by/4.0/
```

Google Play does not check this, but a missing credit on a CC-BY track is a licence breach, so
either credit it or pick a CC0 track instead.
