---
id: tiling_image
summary: Generate seamless / tileable SDXL images and textures. This is a RECIPE, not an executable action - emit generate_image using TileXY by default, TileX for horizontal-only wrapping, or TileY for vertical-only wrapping.
inputs: none
autoload: true
triggers: tiling, tiled image, tiled texture, tileable, tile texture, seamless, seamless texture, seamless pattern, seamless tile, seamless background, repeatable texture, repeating texture, repeating pattern, wallpaper pattern, wraparound texture, wrap around texture, game texture
exclude_triggers: seamless transition, seamless edit, seamlessly edit, seamlessly blend
template: <aitools_action skill="generate_image" preset="{{Prompt To Image (SDXL) TileXY.txt}}" width="1024" height="1024" prompt="seamless tileable texture of ..."/>
---
# Tiling / Seamless Images

Use this when the user asks for a tiling, seamless, repeating, wraparound, or
tileable image/texture/pattern.

The actual executable action is `generate_image` with one of the SDXL tiling
presets:

- `{{Prompt To Image (SDXL) TileXY.txt}}` - seamless on both X and Y axes.
  This is the default for "tileable", "seamless", "repeating pattern",
  wallpapers, game textures, and material textures.
- `{{Prompt To Image (SDXL) TileX.txt}}` - seamless horizontally only. Use for
  side-scrolling backgrounds, panoramic strips, horizontal borders, or when the
  user explicitly says X-axis / horizontal only.
- `{{Prompt To Image (SDXL) TileY.txt}}` - seamless vertically only. Use for
  vertical scrolling textures, columns, vertical borders, or when the user
  explicitly says Y-axis / vertical only.

The presets default to 1024x1024, SDXL Juggernaut XL Ragnarok, 28 steps, CFG 7,
DDIM sampler. You may pass `width` and `height`; dimensions are clamped to the
app's workflow limits and rounded to multiples of 32.

## Invocation

Both axes / default seamless tile:

```
<aitools_action skill="generate_image" preset="{{Prompt To Image (SDXL) TileXY.txt}}" width="1024" height="1024" prompt="seamless tileable texture of weathered mossy stone floor blocks, varied rectangular stones, fine cracks, small tufts of moss in the joints, neutral top-down lighting, no perspective, no border, no text"/>
```

Horizontal-only tile:

```
<aitools_action skill="generate_image" preset="{{Prompt To Image (SDXL) TileX.txt}}" width="1280" height="512" prompt="horizontally seamless side-scrolling forest background strip, layered tree trunks and ferns, soft morning mist, painterly game art, no hard left or right edge, no text"/>
```

Vertical-only tile:

```
<aitools_action skill="generate_image" preset="{{Prompt To Image (SDXL) TileY.txt}}" width="512" height="1280" prompt="vertically seamless climbing ivy texture on old brick, repeating upward growth, varied leaf sizes, diffuse overcast lighting, flat orthographic view, no top or bottom edge, no text"/>
```

## Prompt guidance

Write SDXL-style texture prompts, not Z-Image character-sheet prompts. Be
specific about material, scale, pattern structure, lighting, and view angle.

Good details:

- "top-down", "orthographic", "flat material scan", or "straight-on" when the
  output is a texture map.
- "even diffuse lighting", "no shadows crossing tile edges", "no border", "no
  frame", "no text", "no logo".
- For natural patterns, ask for varied but evenly distributed elements so the
  repeat is not obvious.
- For game assets, name the use: floor tile, wall texture, side-scroller
  background, vertical scroll strip, wallpaper pattern, fabric, terrain.

Avoid prompts that create one centered object on a plain background. Seamless
textures should fill the canvas edge to edge unless the user explicitly asks
for a sparse pattern.

## Rules

- Default to TileXY unless the user explicitly asks for only horizontal/X or
  vertical/Y wrapping.
- Use `generate_image`, not `image_to_image`, unless the user supplied a source
  image and explicitly wants it transformed into a tileable texture. There is
  no dedicated image-to-image tiling preset documented here.
- Do not use `Prompt To Image (Z-Image).txt` for tileable textures; it does not
  enable SDXL tiling.
