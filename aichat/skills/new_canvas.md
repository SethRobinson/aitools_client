---
id: new_canvas
summary: Spawn a blank colored canvas as a new chat bubble. Use as the FOUNDATION for collages, storyboards, multi-panel layouts, magazine covers - then paste_image / draw_text / draw_shape onto it. Cannot chain (no source needed).
inputs: none
template: <aitools_action skill="new_canvas" width="2048" height="1152" color="#FFFFFF"/>
---
# Create a blank canvas

Spawn a brand-new chat bubble containing a blank, single-color texture
of the requested size. This is the starting point for any composition
that doesn't begin with a generated image - storyboards, comic-book
pages, multi-panel posters, info graphics, before/after diptychs, photo
contact sheets, etc.

`new_canvas` does NOT take a source image and does NOT support
`chain="true"`. It always creates a fresh bubble.

## Attributes

- `width` - canvas width in pixels (default 1024). Hard cap 8192.
- `height` - canvas height in pixels (default 1024). Hard cap 8192.
- `color="#RRGGBB"` (or named color, or `"#RRGGBBAA"` for transparent
  / semi-transparent backgrounds). Default white.

## Picking a size

Match the final aspect you want; the rest of the composition will
inherit it. Common choices:

- 1024x1024 - square poster, social-media post, comic page sketch
- 2048x1152 - 16:9 widescreen for movie posters / storyboards
- 1080x1920 - portrait phone-screen aspect (9:16)
- 1500x2100 - book / magazine page (5:7)
- 2400x1200 - filmstrip / 2-panel diptych (2:1)

## Examples

A 16:9 white canvas to build a 2x2 storyboard on:

```
<aitools_action skill="new_canvas" width="2048" height="1152" color="#FFFFFF"/>
```

A black portrait canvas for a movie poster:

```
<aitools_action skill="new_canvas" width="1080" height="1620" color="#000000"/>
```

A transparent canvas for a logo / watermark layer:

```
<aitools_action skill="new_canvas" width="512" height="512" color="#00000000"/>
```

## Typical follow-up sequence

```
<aitools_action skill="new_canvas" width="2048" height="1152" color="#222222"/>
<aitools_action skill="paste_image" chain="true" source_chat_image="2" x="2%" y="5%" width="46%" height="80%" mode="fit"/>
<aitools_action skill="paste_image" chain="true" source_chat_image="3" x="52%" y="5%" width="46%" height="80%" mode="fit"/>
<aitools_action skill="draw_text" chain="true" text="BEFORE" x="2%" y="86%" width="46%" height="10%" font_size="64" color="#FFFFFF" align="center"/>
<aitools_action skill="draw_text" chain="true" text="AFTER" x="52%" y="86%" width="46%" height="10%" font_size="64" color="#FFFFFF" align="center"/>
```

(Each `chain="true"` step stacks onto the new_canvas Pic, so the chat
shows ONE bubble that builds up over the sequence.)

## Rules

- `width` and `height` must be > 0 and <= 8192 each.
- No source image, no chain. If you want to start from an existing image
  use `add_border` or `crop_resize` instead.
- The bubble appears immediately (no GPU work). chain="true" follow-ups
  in the same reply will see the canvas as their input.
