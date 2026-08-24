Inter — Rasmus Andersson
https://github.com/rsms/inter

Licensed under the SIL Open Font License 1.1 (see OFL.txt). Free to embed in a
commercial app on either store, no attribution required inside the game.

These two files are static cuts taken from the official variable font
Inter[opsz,wght].ttf, at:

    Inter-Black.ttf      wght 900, opsz 14   <- the game font
    Inter-SemiBold.ttf   wght 600, opsz 14   <- lighter alternative

Static, not variable, because TextMeshPro bakes its atlas from one fixed outline
set: handed the variable file it silently uses the default instance, which for
Inter is Regular 400 — far lighter than intended, and hard to spot as a cause.

opsz is pinned at 14, Inter's text optical size, rather than the 32 display cut.
Most labels in this game are small — answer buttons at 17px, player names at 14 —
and the display cut tightens spacing in a way that hurts exactly there.

ApplyGameFont.cs decides which one the game uses. Swap the weight there, not here.
