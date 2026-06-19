---
id: draw_shape
summary: Draw a filled / outlined rect or circle on an image. Useful for speech-bubble backgrounds, dividers, label badges, frame mats, legibility bands behind text, color blocks in info graphics. Use chat_image="N" / attachment="N" / chain="true" to pick the canvas. When redoing overlays on a composed image, use clean_base="true" with chat_image="N" if CHAT IMAGES says clean_base=available.
inputs: attachment
template: <aitools_action skill="draw_shape" chat_image="N" shape="rect" x="10%" y="80%" width="80%" height="15%" fill_color="#000000B0" corner_radius="20"/>
---
# Draw a shape on an image

Render a filled and/or outlined rectangle or circle on the canvas image.
Honors color alpha, so semi-transparent fills work great as legibility
bands behind text, ghost speech-bubble backgrounds, etc. Produces a NEW
chat bubble unless `chain="true"`.

## Coordinate convention

Top-left origin. `x`, `y`, `width`, `height` accept pixels or percent of
the canvas. For circles, the circle's center is `x + width/2`,
`y + height/2`, and the radius is `min(width, height) / 2`.

## Attributes

- `chat_image="N"` / `attachment="N"` / `chain="true"` - the canvas
  (REQUIRED, exactly one).
- `clean_base="true"` - optional with `chat_image="N"` only. Uses the
  preserved pre-overlay pixels for that composed image, so a redo of a
  speech bubble or label does not draw over baked-in old text.
- `shape` - `rect` or `circle`. Default `rect`.
- `x`, `y`, `width`, `height` - the shape's bounding rect (REQUIRED for
  rect; circle uses these to derive center+radius).
- `fill_color` - solid fill color. Use `"#RRGGBBAA"` for partial
  transparency. Omit to skip the fill (outline-only).
- `outline_color` - outline color. Omit to skip the outline.
- `outline_width` - outline thickness in pixels (or percent of canvas
  width). Default 1.
- `corner_radius` - rounded-corner radius for rect (pixels or percent of
  canvas width). Default 0 (sharp corners). Ignored for circle.

At least one of `fill_color` or `outline_color` is required.

## Examples

Semi-transparent black band behind a title (then draw_text on top):

```
<aitools_action skill="draw_shape" chat_image="2" shape="rect" x="0" y="80%" width="100%" height="15%" fill_color="#000000A0"/>
<aitools_action skill="draw_text" chain="true" text="THE FINAL ACT" x="0" y="80%" width="100%" height="15%" font_size="80" color="#FFFFFF" bold="true" align="center" valign="middle"/>
```

Rounded rectangle as a comic-book speech bubble background:

```
<aitools_action skill="draw_shape" chat_image="3" shape="rect" x="55%" y="5%" width="40%" height="20%" fill_color="#FFFFFFEE" outline_color="#000000" outline_width="4" corner_radius="32"/>
<aitools_action skill="draw_text" chain="true" text="Quick! Hide!" x="55%" y="5%" width="40%" height="20%" font_size="48" color="#000000" bold="true" align="center" valign="middle"/>
```

Redo a speech bubble on an image that already has bad/old text baked in:

```
<aitools_action skill="draw_shape" chat_image="3" clean_base="true" shape="rect" x="55%" y="5%" width="40%" height="20%" fill_color="#FFFFFFEE" outline_color="#000000" outline_width="4" corner_radius="32"/>
<aitools_action skill="draw_text" chain="true" text="Quick! Hide!" x="55%" y="5%" width="40%" height="20%" font_size="48" color="#000000" bold="true" align="center" valign="middle"/>
```

Outlined circle as a "1" badge for a step indicator:

```
<aitools_action skill="draw_shape" chat_image="4" shape="circle" x="2%" y="2%" width="64" height="64" fill_color="#FF3333" outline_color="#FFFFFF" outline_width="3"/>
<aitools_action skill="draw_text" chain="true" text="1" x="2%" y="2%" width="64" height="64" font_size="40" color="#FFFFFF" bold="true" align="center" valign="middle"/>
```

Divider line between two grid panels (a thin tall rect):

```
<aitools_action skill="draw_shape" chat_image="5" shape="rect" x="49.7%" y="0" width="0.6%" height="100%" fill_color="#222222"/>
```

## Rules

- At least one of `fill_color` or `outline_color` is required.
- For rect, `width` and `height` must be > 0.
- For circle, the radius is `min(width, height) / 2` - so for a 64-pixel
  circle, pass `width="64" height="64"`. The (x,y) is the bounding box's
  top-left, NOT the center.
- corner_radius is clamped to `min(width, height) / 2` (passing a huge
  value gives you a stadium / pill shape).
- Do not use `stroke_color` / `stroke_width` in new calls; use
  `outline_color` / `outline_width`. The executor accepts the old names
  as aliases, but the documented names are clearer.
- Color alpha is honored everywhere - prefer semi-transparent fills for
  legibility bands so the underlying image stays partly visible.
