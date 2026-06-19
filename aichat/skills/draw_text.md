---
id: draw_text
summary: Render text into a rect on an image (titles, captions, labels, dialog). Composes with add_border / new_canvas / paste_image to build posters, books, comic panels, magazine covers. Coordinates are top-left, in pixels OR percent of the canvas (e.g. y="80%"). Use chat_image="N" / attachment="N" / chain="true" to pick the canvas. For a legibility band behind the text on a busy/photographic background, use the attribute name `bg_color` (e.g. `bg_color="#000000AA"`) - the attribute is NOT called background_color / backgroundColor / bgcolor. When redoing overlays on a composed image, use clean_base="true" with chat_image="N" if CHAT IMAGES says clean_base=available. When labeling SEVERAL existing chat images in a single new reply, give EACH draw_text its own `chat_image="N"` - do NOT use `chain="true"` on the first one (chain only works within the same reply, anchored to a Pic spawned earlier in THAT reply).
inputs: attachment
triggers: name them, name each, label, label them, label each, caption, captions, add text, add a caption, add a label, add labels, watermark, title each, write a name on, write text on, put text on, put a label on, give each a name, give them names, named labels
template: <aitools_action skill="draw_text" chat_image="N" text="HELLO" x="0" y="80%" width="100%" height="20%" font_size="120" color="#FFFFFF" bg_color="#000000AA" bold="true" align="center" valign="middle"/>
---
# Draw text on an image

Render text into a rect on a canvas image and produce a NEW chat bubble
(unless `chain="true"` - then it stacks onto the most recent Pic this
reply, no new bubble). The original bubble is never modified.

Useful for poster titles, motivational poster body text, captions under
storyboard panels, page numbers in books, magazine cover headlines,
comic-book speech text, watermarks, etc. See `read_skill id="composition_recipes"`
for full worked examples.

## Coordinate convention

`(0,0)` is the **top-left** of the canvas. Y increases downward.
`x`, `y`, `width`, `height` accept either:

- pure pixels: `x="40"`
- percent of the source dimension: `x="10%"` (10% of the canvas width
  for x/width, 10% of canvas height for y/height)

`font_size` and `outline_width` accept the same forms (percent is of
canvas height for font_size, canvas width for outline_width).

## Attributes

- `chat_image="N"` / `attachment="N"` / `chain="true"` - the canvas
  (REQUIRED, exactly one). Mirrors image_to_image's source semantics.
- `clean_base="true"` - optional with `chat_image="N"` only. Uses the
  preserved pre-overlay pixels for that composed image, so a redo of
  speech-bubble text, labels, or captions does not draw over baked-in old
  text. Use it on the FIRST replacement local op, then `chain="true"` for
  the follow-up shape/text steps.
- `text="..."` - the text to render. REQUIRED, non-empty.
- `x`, `y`, `width`, `height` - the rect to render into, top-left origin,
  pixels or percent. Defaults: full canvas.
- `font_size` - the text's pixel height, or a percent of canvas height
  (e.g. `font_size="12%"`). It is the **UPPER CAP** when `auto_size="true"`
  (the default): draw_text measures the text and scales it to fit the rect,
  and font_size only stops a short line from growing bigger than this (so one
  word doesn't blow up to fill a giant rect). The box ALWAYS WINS - the text
  auto-shrinks to fit and never overflows, overlaps the next element, or
  clips at the edge - so just set the cap near (or a little above) the rect
  height. When `auto_size="false"`, font_size is used exactly, no fitting.
- `min_font_size` - soft hint only, kept for backward compatibility; you can
  omit it. The box always wins now: when the rect can hold the text at this
  size the fitted size comes out at least this big anyway, and when it can't
  the text shrinks below it to fit rather than overflowing the rect.
- `font_name` - TMP font asset name (e.g. `LiberationSans SDF`). Optional;
  defaults to the app's default UI font. Common available names: see the
  list at the bottom of this doc - they're the same TMP fonts the AI
  Guide poster system uses.
- `color="#RRGGBB"` or `"#RRGGBBAA"` or named (e.g. `"white"`). Default white.
- `bg_color` - optional fill color drawn behind the text rect (use a
  semi-transparent black like `"#00000080"` to add a legibility band
  behind text on a busy image). Omit for no background. For speech bubbles,
  normally omit `bg_color` because the rounded `draw_shape` already supplies
  the background; a square bg fill can cover the rounded corners.
- `bg_corner_radius` / `corner_radius` - optional rounded-corner radius for
  the `bg_color` fill. Only needed when using `bg_color` directly instead
  of a separate `draw_shape` bubble.
- `outline_color` + `outline_width` - draw a halo of `outline_color`
  around the text at `outline_width` pixels in each of 8 directions
  before the main fill. Strongest readability on busy/photographic
  backgrounds. Omit both to skip outlining.
- `bold="true"` / `italic="true"` - font style (default false).
- `align` - horizontal alignment: `left` / `center` / `right` (default
  `center`).
- `valign` - vertical alignment: `top` / `middle` / `bottom` (default
  `middle`).
- `wrap="true"` - word-wrap inside the rect (default true). Set false to
  force a single line that may overflow.
- `auto_size` - default `"true"`. When on, draw_text measures the
  text's preferred bounds and scales the font DOWN to fit the rect (up to
  the `font_size` cap, shrinking as small as needed so the text always fits
  - the box wins). This is much more reliable than TMP's built-in auto-sizer
  in our render-to-texture path. Pass `"false"` to use font_size as the
  EXACT size with no fitting (useful for matching a precise design spec; rare).

## Available fonts

Use any of the project's loaded TMP fonts; you can pass either the bare
name or with the `SDF` suffix - both work. Some commonly-shipped names:

- `LiberationSans SDF` (default UI sans)
- `NotoSansCJKip-FV SDF` (multi-language sans, used by AIGuide posters)
- Any other TMP_FontAsset present in the project's font array.

If you pass a font name that isn't found, the default UI font is used
(no error, no bubble). When in doubt, omit `font_name` entirely.

## Examples

Add a centered white title near the top of a 1024-wide image:

```
<aitools_action skill="draw_text" chat_image="3" text="MOTIVATION" x="0" y="2%" width="100%" height="14%" font_size="350" color="#FFFFFF" bold="true" outline_color="#000000" outline_width="6"/>
```

Caption strip across the bottom of a freshly-generated image (chained,
same-reply):

```
<aitools_action skill="generate_image" preset="{{Prompt To Image (Z-Image).txt}}" prompt="<full scene description>"/>
<aitools_action skill="draw_text" chain="true" text="In an alternate 1985..." x="0" y="88%" width="100%" height="10%" font_size="180" color="#FFFFFF" bg_color="#00000099" align="center" valign="middle"/>
```

Redo text on a composed image without stacking over old baked-in text:

```
<aitools_action skill="draw_shape" chat_image="3" clean_base="true" shape="rect" x="55%" y="5%" width="40%" height="20%" fill_color="#FFFFFFEE" outline_color="#000000" outline_width="4" corner_radius="32"/>
<aitools_action skill="draw_text" chain="true" text="NEW LINE" x="55%" y="5%" width="40%" height="20%" font_size="48" color="#000000" bold="true" align="center" valign="middle"/>
```

Page-number in the bottom-right corner of a book page:

```
<aitools_action skill="draw_text" chat_image="5" text="3" x="92%" y="94%" width="6%" height="5%" font_size="100" color="#222222" align="right"/>
```

## Rules

- `text` must be non-empty.
- Pick exactly ONE canvas source (chat_image / attachment / chain).
- `chain="true"` must NOT be combined with attachment / chat_image.
- For multi-line text, just put `\n` (a literal newline character) inside
  the `text` attribute string - TMP wraps and respects line breaks.
- For multiple text elements on the same image, emit one `draw_text`
  per element (the second one chains onto the first via `chain="true"`,
  or just references the same `chat_image="N"` if you want each text
  pass to land in its own bubble - usually you want chain="true" so
  there's just ONE final result bubble).
- For speech bubbles, prefer `draw_shape` rounded rect followed by
  `draw_text chain="true"` with NO `bg_color`; otherwise the text background
  may cover the rounded bubble.
