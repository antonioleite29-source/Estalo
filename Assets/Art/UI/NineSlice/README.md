# Nine-sliced buttons

One sprite, any button size. The corners and the edges keep their own size however far the button is
stretched, so the chamfer never goes oval -- which is what happens when a wide button and a narrow
one are two separate pictures of the same slab (`Startbutton2.png` and `1v1Button.png` today).

| File | What it is for |
|---|---|
| `ButtonNineSlice.png` | White, band as mid-grey at half alpha. Tint it with the Image's own Color and the band comes out right: #7379A1 gives exactly the original's #3A3D51. |
| `ButtonSlateNineSlice.png` | The slate the Jogar button is painted in (#7379A1). |
| `ButtonCyanNineSlice.png` | The cyan a chosen mode (1x1, 2x2) is painted in (34, 184, 207). |

Each is 128 x 128 with a 44-pixel border on all four sides, already set in the importer, as a
single sprite. The corners come out at 35 units and the top band at 10 -- what the original
2399 x 500 art comes to on a 150-tall button.

## Using one

Select the buttons in the Hierarchy, then **Trivia Duel > UI > Nine Slice Selected Buttons**. The
white one keeps whatever colour the Image already had; the slate and cyan menu items set the colour
back to white, because the colour is in the sprite.

By hand, on the button's Image:

1. **Source Image**: one of the three above.
2. **Image Type**: Sliced.
3. **Fill Center**: on.
4. **Pixels Per Unit Multiplier**: 1 (one sprite pixel per canvas unit, so the corners measure 35).

## Changing how it looks

The sprites are drawn by `Tools/UI/nineslice.swift`, measured off `Startbutton2.png`: a flat slab with
its corners cut, and a band along the top edge at half alpha, which is what gives the slab its lip.
Nothing else -- no gradient, no outline. Change the numbers at the top of that file and run:

```
swift Tools/UI/nineslice.swift Assets/Art/UI/NineSlice
```

The `.meta` files keep the borders, so regenerating the images does not lose them.
