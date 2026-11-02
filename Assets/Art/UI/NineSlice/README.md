# Nine-sliced buttons

One sprite, any button size. The corners and the edges keep their own size however far the button is
stretched, so the chamfer never goes oval -- which is what happens when a wide button and a narrow
one are two separate pictures of the same slab (`Startbutton2.png` and `1v1Button.png` today).

| File | What it is for |
|---|---|
| `ButtonNineSlice.png` | White. Tint it with the Image's own Color: cyan, white, anything. |
| `ButtonSlateNineSlice.png` | The slate the Jogar button is painted in (#7379A1). |
| `ButtonCyanNineSlice.png` | The cyan a chosen mode (1x1, 2x2) is painted in (34, 184, 207). |

Each is 160 x 160 with a 56-pixel border on all four sides, already set in the importer.

## Using one

Select the buttons in the Hierarchy, then **Trivia Duel > UI > Nine Slice Selected Buttons**. The
white one keeps whatever colour the Image already had; the slate and cyan menu items set the colour
back to white, because the colour is in the sprite.

By hand, on the button's Image:

1. **Source Image**: one of the three above.
2. **Image Type**: Sliced.
3. **Fill Center**: on.
4. **Pixels Per Unit Multiplier**: 2 (the borders then measure 28 units instead of 56, which keeps
   the corners right on a short button).

## Changing how it looks

The sprites are drawn by `Tools/UI/nineslice.swift`: body, a lighter band along the top, a darker lip
along the bottom, a 34-pixel chamfer and a thin dark edge. Change the numbers at the top of that file
and run:

```
swift Tools/UI/nineslice.swift Assets/Art/UI/NineSlice
```

The `.meta` files keep the borders, so regenerating the images does not lose them.
