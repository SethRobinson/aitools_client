---
id: crop_resize
summary: Resize or crop an image to a target width/height. mode picks the policy (resize/stretch, fit, fill, crop). Use to normalize image sizes before paste_image into a layout, force an aspect ratio, or hard-crop a region. Use chat_image="N" / attachment="N" / chain="true" to pick the source.
inputs: attachment
template: <aitools_action skill="crop_resize" chat_image="N" width="1024" height="1024" mode="fill"/>
---
# Crop or resize an image

Reshape an image to a target width/height. Produces a NEW chat bubble
unless `chain="true"`. The original image is never modified.

The `mode` attribute picks the policy:

- `resize` (default) - stretch to exactly width x height (changes aspect).
  Alias: `stretch`.
- `fit` - preserve aspect, letterbox to fill the target with `bg_color`.
  Whole image is visible; bars on the long axis.
- `fill` - preserve aspect, crop to cover the target. Whole target is
  filled; ends of the long axis are cropped.
- `crop` - hard crop a `width` x `height` rect starting at `x`, `y`
  (top-left). No scaling.

## Coordinate convention

`width`, `height`, `x`, `y` are pixels OR percent of the source.
`(0,0)` is top-left for `crop`. Default x/y is 0,0.

## Attributes

- `chat_image="N"` / `attachment="N"` / `chain="true"` - the source
  (REQUIRED, exactly one).
- `width`, `height` - target size, REQUIRED. Pixels or percent of source.
- `mode` - one of `resize` / `stretch` / `fit` / `fill` / `crop`. Default
  `resize`.
- `bg_color` - background color for the letterbox in `mode="fit"`.
  Default fully transparent.
- `x`, `y` - crop offset (top-left), only used in `mode="crop"`.

## Examples

Force a 1024x1024 square (stretch):

```
<aitools_action skill="crop_resize" chat_image="3" width="1024" height="1024" mode="resize"/>
```

Center-crop a 16:9 image down to a square (preserves aspect, crops the
sides):

```
<aitools_action skill="crop_resize" chat_image="3" width="1024" height="1024" mode="fill"/>
```

Letterbox a portrait image into a 16:9 frame with black bars:

```
<aitools_action skill="crop_resize" chat_image="3" width="1920" height="1080" mode="fit" bg_color="#000000"/>
```

Hard-crop the top-left 800x600 region:

```
<aitools_action skill="crop_resize" chat_image="3" width="800" height="600" mode="crop" x="0" y="0"/>
```

Half the size:

```
<aitools_action skill="crop_resize" chat_image="3" width="50%" height="50%" mode="resize"/>
```

## Rules

- `width` and `height` are required and must be > 0.
- `mode="crop"` clamps `x`, `y`, `width`, `height` to the source image's
  bounds (no error - just a smaller crop).
- For an inpainting / outpainting style edit (let the model regenerate
  cropped-out content) use the existing image_to_image preset instead.
  This skill is purely a pixel-level transform.
