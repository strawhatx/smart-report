# Chart palette

Four categorical slots, in fixed order: **blue, green, purple, orange**.

```
--color-series-1  #1d5ba6   blue
--color-series-2  #15803d   green
--color-series-3  #6d28d9   purple
--color-series-4  #c2410c   orange
```

## Why four

Four is the largest set that passed every check against this surface. It is not a stylistic cap.

Green is the constraint. Beside any warm hue — orange, amber, pink — it collapses under red-green
deficiency: green↔orange measured ΔE 3.4–7.0 across protan and deutan in the sets tried, well under
the floor of 8. Green therefore sits between two **cool** neighbours, and the warm slot goes last.

Attempts at five and six failed on one of three things every time: a teal below the chroma floor
(reads as grey), two warm hues too close even in **normal** vision (amber↔orange ΔE 0.6 under deutan,
8.8 normal), or green adjacent to a warm hue again.

## Rules

- **Assign by slot, never by rank.** Filtering a series out must not repaint the survivors — a reader
  who learned "Shopify is blue" is misled otherwise.
- **Never generate a fifth hue.** Fold the tail into "Other", or facet.
- **Re-run the validator before changing anything here**, including a "small" shade tweak:

  ```
  node <dataviz-skill>/scripts/validate_palette.js "#1d5ba6,#15803d,#6d28d9,#c2410c" --mode light
  ```

- Status colours (`--color-good`, `--color-warn`, `--color-danger`) are reserved. They mean a state,
  never a series, and they always ship with a label rather than relying on colour alone.
- Single-series charts — every bar chart here — use one ink or brand colour, no palette and no legend.
  The title says what is plotted.

## Validator output

```

Palette (light, surface #fcfcfb, categorical): 4 slots
  [PASS] Lightness band         all 4 inside L 0.43–0.77
  [PASS] Chroma floor           all 4 >= 0.1
  [PASS] CVD separation         worst adjacent #15803d↔#1d5ba6 ΔE 21.2 (deutan) · tritan 5.8
  [PASS] Normal-vision floor    worst adjacent #15803d↔#1d5ba6 ΔE 22.4 (normal)
  [PASS] Contrast vs surface    all 4 >= 3:1

  → ALL CHECKS PASS  (CVD in the 6–8 floor band is legal ONLY with secondary encoding: direct labels, gaps, or texture)
  scope: categorical palettes only. For a lone status/text color check text contrast with this script's apca() or contrast(); for a sequential ramp, lightness monotonicity.

```

_No dark mode: the reference design is light-only. Adding one means re-validating every slot
against the dark surface — an automatic inversion will not pass._
