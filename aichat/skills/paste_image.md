---
id: paste_image
summary: Composite one image (the source) onto another (the canvas) at a top-left rect. Foundation for collages, storyboards, comic panels, before/after diptychs. The CANVAS is picked the standard way (chat_image / attachment / chain). The image being PASTED is picked separately via source_chat_image="N" or source_attachment="N".
inputs: attachment
template: <aitools_action skill="paste_image" chat_image="1" source_chat_image="2" x="0" y="0" width="50%" height="100%" mode="fill"/>
---
# Paste an image onto another image

Composites a SOURCE image onto a CANVAS image inside an axis-aligned rect.
Honors source alpha. Produces a NEW chat bubble unless `chain="true"`.

The most common use is building a multi-panel layout: generate or pick the
source images, create a `new_canvas` immediately before final assembly,
then use `paste_image chain="true"` for each scene and `draw_text
chain="true"` for captions. Unchained `paste_image` creates a new bubble;
chained `paste_image` keeps building the same working canvas.

Use this as the final step for literal flat logos, decals, watermarks, UI
marks, and transparent PNG stickers. It preserves the source pixels and alpha.
When the user wants the mark to look physically part of the subject (painted
into scales, branded hide, embroidered fabric, engraved metal, tattooed skin,
fit onto a chest/back/body, etc.), use `paste_image` only as an intermediate
placement guide, then run a 2-input Klein `image_to_image` pass with the
guide composite as input 1 and the original logo as input 2.

## Coordinate convention

`(0,0)` is the **top-left** of the canvas. `x`, `y`, `width`, `height`
accept either pixels or percent of the canvas (e.g. `width="50%"`).

## Attributes

- `chat_image="N"` / `chat_image="Name"` / `attachment="N"` /
  `chain="true"` - the **canvas** (REQUIRED, exactly one). This is the image
  being painted on. Use `chat_image="Name"` when the canvas was created as a
  named anchor earlier in the same reply but the paste is not immediately
  adjacent to it.
- `source_chat_image="N"` / `source_chat_image="Name"` /
  `source_attachment="N"` - the **source**
  image being pasted (REQUIRED, exactly one). DIFFERENT slot from
  chat_image/attachment so you can paste e.g. chat_image=#3 onto
  chat_image=#1. Use the name form when a source image was created with
  `anchor="Name"` earlier in the same reply.
- `x`, `y` - top-left position of the rect on the canvas (default 0,0).
- `width`, `height` - rect size on the canvas (default = source's natural
  size, no scaling).
- `mode` - how the source fits inside the rect:
  - `fit` (default): preserve source aspect, letterbox inside the rect.
    Rect pixels outside the fitted source are untouched (transparent
    letterbox - call draw_shape or new_canvas with a color first if you
    want a visible bar).
  - `fill`: preserve source aspect, crop the source to fully cover the
    rect.
  - `stretch`: ignore aspect, scale source to exactly fill the rect.
- `opacity="0..1"` - multiplies the pasted source's alpha (1.0 default,
  use < 1 for ghost / overlay effects).
- `align` - horizontal alignment WITHIN the rect when `mode="fit"`
  (`left` / `center` / `right`, default center). Also picks the kept
  region for `mode="fill"`.
- `valign` - same but vertical (`top` / `middle` / `bottom`, default
  middle).

## Examples

Paste image #2 and #3 into a new two-panel canvas:

```
<aitools_action skill="new_canvas" width="2400" height="1200" color="#111111" anchor="layout_canvas"/>
<aitools_action skill="paste_image" chain="true" source_chat_image="2" x="1%" y="5%" width="48%" height="82%" mode="fill"/>
<aitools_action skill="paste_image" chain="true" source_chat_image="3" x="51%" y="5%" width="48%" height="82%" mode="fill"/>
```

Paste a generated panel into a 2x2 storyboard grid (top-right cell):

```
<aitools_action skill="paste_image" chat_image="5" source_chat_image="2" x="50%" y="0" width="50%" height="50%" mode="fill"/>
```

Add a small watermark logo in the bottom-right corner of an image:

```
<aitools_action skill="paste_image" chat_image="4" source_attachment="1" x="80%" y="80%" width="18%" height="18%" mode="fit" opacity="0.7"/>
```

Place an attached transparent logo/decal on the chest of a generated character
in the same reply as a literal flat decal. The generated character is the
canvas, so the paste uses `chain="true"`; the uploaded logo is the source:

```
<aitools_action skill="generate_image" preset="{{Prompt To Image (Z-Image).txt}}" prompt="<clean character portrait without the logo>" anchor="Character"/>
<aitools_action skill="paste_image" chain="true" source_attachment="1" x="43%" y="45%" width="14%" height="14%" mode="fit" opacity="1" anchor="Character"/>
```

For an existing chat image, use it as the canvas directly:

```
<aitools_action skill="paste_image" chat_image="5" source_attachment="1" x="43%" y="45%" width="14%" height="14%" mode="fit" opacity="1"/>
```

Use a paste as an integration guide, not the final output, when the user asks
for the mark to become part of the material:

```
<aitools_action skill="generate_image" preset="{{Prompt To Image (Z-Image).txt}}" prompt="<clean dragon portrait without logo>" anchor="Dragon"/>
<aitools_action skill="paste_image" chain="true" source_attachment="1" x="43%" y="45%" width="14%" height="14%" mode="fit" opacity="1"/>
<aitools_action skill="image_to_image" preset="{{Image To Image Klein Edit 2 Input.txt}}" prompt="Image 1 is only a placement guide. Integrate image 2's logo onto the dragon's chest as a natural inlaid scale pattern following curvature and lighting, not a flat pasted overlay; preserve the logo geometry and colors as much as possible." chain="true" attachment2="1" anchor="Dragon"/>
```

Same-reply anchored source - generate the source first, then create the
canvas and chain the paste without guessing future numeric ids:

```
<aitools_action skill="generate_image" preset="{{Prompt To Image (Z-Image).txt}}" prompt="<full description for panel content>" anchor="panel_a"/>
<aitools_action skill="new_canvas" width="2048" height="1152" color="#111111" anchor="layout_canvas"/>
<aitools_action skill="paste_image" chain="true" source_chat_image="panel_a" x="0" y="50%" width="50%" height="50%" mode="fill"/>
```

Note: `chain="true"` is for the canvas being modified, not for the source
being pasted. Use `source_chat_image="Name"` for the anchored source.

## Rules

- Canvas: pick exactly ONE of chat_image / attachment / chain.
- Source: pick exactly ONE of source_chat_image / source_attachment.
  `source_chat_image` may be a numeric slot or an anchor name.
- For fit mode, transparent letterbox is intentional - draw a colored
  rect first or use new_canvas with a non-transparent color.
- Source bubbles must still exist (the underlying world Pic must not have
  been deleted). The CHAT IMAGES line in the system context shows how
  many are reachable.
- For several paste/text/shape operations that should become one final
  layout, use `chain="true"` after the canvas exists. Several unchained
  `paste_image chat_image="<same canvas>"` calls produce several partial
  outputs instead of one complete layout.
