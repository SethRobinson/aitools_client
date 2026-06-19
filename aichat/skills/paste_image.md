---
id: paste_image
summary: Composite one image (the source) onto another (the canvas) at a top-left rect. Foundation for collages, storyboards, comic panels, before/after diptychs. The CANVAS is picked the standard way (chat_image / attachment / chain). The image being PASTED is picked separately via source_chat_image="N" or source_attachment="N".
inputs: attachment
template: <aitools_action skill="paste_image" chat_image="1" source_chat_image="2" x="0" y="0" width="50%" height="100%" mode="fill"/>
---
# Paste an image onto another image

Composites a SOURCE image onto a CANVAS image inside an axis-aligned rect.
Honors source alpha. Produces a NEW chat bubble unless `chain="true"`.

The most common use is building a multi-panel layout: start with
`new_canvas` to make a blank, then `paste_image` each generated scene
into a known rect, then `draw_text` captions per panel.

## Coordinate convention

`(0,0)` is the **top-left** of the canvas. `x`, `y`, `width`, `height`
accept either pixels or percent of the canvas (e.g. `width="50%"`).

## Attributes

- `chat_image="N"` / `attachment="N"` / `chain="true"` - the **canvas**
  (REQUIRED, exactly one). This is the image being painted on.
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

Paste image #2 into the left half of a blank canvas (#1):

```
<aitools_action skill="paste_image" chat_image="1" source_chat_image="2" x="0" y="0" width="50%" height="100%" mode="fill"/>
<aitools_action skill="paste_image" chat_image="1" source_chat_image="3" x="50%" y="0" width="50%" height="100%" mode="fill"/>
```

Paste a generated panel into a 2x2 storyboard grid (top-right cell):

```
<aitools_action skill="paste_image" chat_image="5" source_chat_image="2" x="50%" y="0" width="50%" height="50%" mode="fill"/>
```

Add a small watermark logo in the bottom-right corner of an image:

```
<aitools_action skill="paste_image" chat_image="4" source_attachment="1" x="80%" y="80%" width="18%" height="18%" mode="fit" opacity="0.7"/>
```

Same-reply anchored source - composite a just-generated image onto a named
canvas without guessing future numeric ids:

```
<aitools_action skill="new_canvas" width="2048" height="1152" color="#111111" anchor="layout_canvas"/>
<aitools_action skill="generate_image" preset="{{Prompt To Image (Z-Image).txt}}" prompt="<full description for panel content>" anchor="panel_a"/>
<aitools_action skill="paste_image" chat_image="layout_canvas" source_chat_image="panel_a" x="0" y="50%" width="50%" height="50%" mode="fill"/>
```

Note: `chain="true"` is for the canvas being modified, not for the source
being pasted. When the canvas and source are both created in this reply,
name them with anchors and use those names.

## Rules

- Canvas: pick exactly ONE of chat_image / attachment / chain.
- Source: pick exactly ONE of source_chat_image / source_attachment.
  `source_chat_image` may be a numeric slot or an anchor name.
- For fit mode, transparent letterbox is intentional - draw a colored
  rect first or use new_canvas with a non-transparent color.
- Source bubbles must still exist (the underlying world Pic must not have
  been deleted). The CHAT IMAGES line in the system context shows how
  many are reachable.
