---
id: draw_text
summary: Render text into a rect on an image (titles, captions, labels, dialog). Composes with add_border / new_canvas / paste_image to build posters, books, comic panels, magazine covers. Coordinates are top-left, in pixels OR percent of the canvas (e.g. y="80%"). Use chat_image="N" / attachment="N" / chain="true" to pick the canvas. For a legibility band behind the text on a busy/photographic background, use the attribute name `bg_color` (e.g. `bg_color="#000000AA"`) - the attribute is NOT called background_color / backgroundColor / bgcolor. When labeling SEVERAL existing chat images in a single new reply, give EACH draw_text its own `chat_image="N"` - do NOT use `chain="true"` on the first one (chain only works within the same reply, anchored to a Pic spawned earlier in THAT reply).
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
- `text="..."` - the text to render. REQUIRED, non-empty.
- `x`, `y`, `width`, `height` - the rect to render into, top-left origin,
  pixels or percent. Defaults: full canvas.
- `font_size` - **upper CAP** when `auto_size="true"` (the default).
  draw_text measures the text and scales it to fit the rect; font_size
  prevents it from growing beyond this value (so a single short word
  doesn't blow up to fill a giant rect). When `auto_size="false"`,
  font_size is used exactly as the TMP fontSize. Sensible caps:
  600-1000 for titles, 300-500 for body. Going higher rarely matters
  because the rect dimensions usually constrain first.
- `min_font_size` - lower bound (floor). When the auto-fit calculation
  would produce text smaller than this, it's clamped UP - text may
  overflow the rect rather than shrink past readability. Sensible
  floors: 80-120 for poster body, 150-200 for poster titles. Ignored
  when `auto_size="false"`.
- `font_name` - TMP font asset name (e.g. `LiberationSans SDF`). Optional;
  defaults to the app's default UI font. Common available names: see the
  list at the bottom of this doc - they're the same TMP fonts the AI
  Guide poster system uses.
- `color="#RRGGBB"` or `"#RRGGBBAA"` or named (e.g. `"white"`). Default white.
- `bg_color` - optional fill color drawn behind the text rect (use a
  semi-transparent black like `"#00000080"` to add a legibility band
  behind text on a busy image). Omit for no background.
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
  text's preferred bounds and scales font_size to fit the rect (clamped
  to [min_font_size, font_size]). This is much more reliable than
  TMP's built-in auto-sizer in our render-to-texture path. Pass
  `"false"` to use font_size as the EXACT TMP fontSize value (useful
  for matching a precise design spec; rare).

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
