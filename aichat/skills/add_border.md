---
id: add_border
summary: Add a colored border with independent left/right/top/bottom thickness around an image. Use for poster framing, motivational-poster bottom bands, page margins, magazine bleed mats. Thickness is pixels OR percent of canvas WIDTH (e.g. left="15%"). Use chat_image="N" / attachment="N" / chain="true" to pick the canvas.
inputs: attachment
template: <aitools_action skill="add_border" chat_image="N" left="5%" right="5%" top="5%" bottom="35%" color="#FFFFFF"/>
---
# Add a border to an image

Wraps the canvas in a colored border by extending the texture in each
direction. Each side is independent - asymmetric borders are the whole
point (e.g. an oversized bottom band for a motivational poster's text
area). Produces a NEW chat bubble unless `chain="true"`.

The original image content stays inside the new larger texture; nothing
inside the original is modified. The result is `(orig_w + left + right)`
x `(orig_h + top + bottom)`.

## Coordinate convention

`left`, `right`, `top`, `bottom` are thicknesses in pixels OR a
percentage. **left / right** are percent of source WIDTH; **top /
bottom** are percent of source HEIGHT. (Different reference dim per
axis is what keeps a "bottom=25%" band a stable ~20% of the FINAL
canvas regardless of source aspect - portrait, square, landscape all
end up with the same band proportion.) `0` (or omitted) means no
border on that side.

## Attributes

- `chat_image="N"` / `attachment="N"` / `chain="true"` - the canvas
  (REQUIRED, exactly one).
- `left`, `right` - thickness in pixels or "N%" of source WIDTH.
- `top`, `bottom` - thickness in pixels or "N%" of source HEIGHT.
- `color="#RRGGBB"` (or named color). Default white.

## Examples

Even white frame for a printable poster (5% all sides):

```
<aitools_action skill="add_border" chat_image="2" left="5%" right="5%" top="5%" bottom="5%" color="#FFFFFF"/>
```

Motivational-poster layout - thin sides + top, big bottom band for
title + body. Bottom = 25% of source HEIGHT becomes ~20% of the final
canvas height (the band lives at y=81% to y=100% of the final canvas
in percent terms - aim text rects there):

```
<aitools_action skill="add_border" chat_image="2" left="6%" right="6%" top="6%" bottom="25%" color="#FFFFFF"/>
```

Black mat for a gallery look:

```
<aitools_action skill="add_border" chat_image="4" left="40" right="40" top="40" bottom="40" color="#111111"/>
```

Chained right after a generate_image (one final bubble):

```
<aitools_action skill="generate_image" preset="{{Prompt To Image (Z-Image).txt}}" prompt="<full scene description>"/>
<aitools_action skill="add_border" chain="true" left="6%" right="6%" top="6%" bottom="6%" color="#000000"/>
```

## Rules

- At least one side must be > 0; an all-zero call is a no-op (info bubble
  only).
- Exactly one source: chat_image / attachment / chain. `chain="true"`
  must NOT be combined with the other two.
- After add_border the canvas is LARGER than before. If you then call
  draw_text and use percentages, those percentages are of the NEW (post-
  border) dimensions - which is usually what you want for placing text in
  the bottom band you just added.
- For an outpaint-style border (mask-aware), you want the existing
  image_to_image inpainting workflow instead - this skill is purely a
  visual border, not an outpaint mask.
