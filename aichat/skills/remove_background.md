---
id: remove_background
summary: Remove an image background / make a transparent PNG cutout. This is a RECIPE, not an executable action - emit image_to_image with preset "{{Image To Image Mask Subject.txt}}" for an existing source, or generate_image with "{{Prompt To Transparent Sprite.txt}}" for a brand-new transparent sprite/cutout.
inputs: attachment_optional
autoload: true
triggers: remove background, remove the background, background removal, transparent background, make background transparent, make the background transparent, make it transparent, make this transparent, no background, cut out, cutout, cut-out, isolate subject, alpha mask, transparent png
template: <aitools_action skill="image_to_image" preset="{{Image To Image Mask Subject.txt}}" prompt="Remove the background and keep the foreground subject unchanged as a transparent-background cutout." chat_image="N"/>
---
# Remove Background / Transparent Cutout

Use this when the user asks to remove a background, isolate a subject, make a
transparent-background PNG, or create a cutout from an existing image.

The actual executable action is `image_to_image` with
`{{Image To Image Mask Subject.txt}}`. That preset uploads the source image,
resizes it down only if larger than 1024, runs BiRefNet subject masking, and
post-processes the workflow alpha so the subject remains opaque and the
background becomes transparent. It does not need a creative edit prompt, but
`prompt` must still be non-empty because all image actions require it.

## Invocation

Existing chat image:

```
<aitools_action skill="image_to_image" preset="{{Image To Image Mask Subject.txt}}" prompt="Remove the background and keep the foreground subject unchanged as a transparent-background cutout." chat_image="N"/>
```

Image pasted this turn:

```
<aitools_action skill="image_to_image" preset="{{Image To Image Mask Subject.txt}}" prompt="Remove the background and keep the foreground subject unchanged as a transparent-background cutout." attachment="1"/>
```

Same-reply generated image -> transparent cutout:

```
<aitools_action skill="generate_image" preset="{{Prompt To Image (Z-Image).txt}}" prompt="<full prompt for the subject on a simple clean background>"/>
<aitools_action skill="image_to_image" preset="{{Image To Image Mask Subject.txt}}" prompt="Remove the background and keep the foreground subject unchanged as a transparent-background cutout." chain="true"/>
```

Brand-new transparent sprite/cutout from scratch:

```
<aitools_action skill="generate_image" preset="{{Prompt To Transparent Sprite.txt}}" prompt="<full prompt for a single isolated subject, centered, no cropped edges, clean silhouette>"/>
```

## Source selection

- If the user pasted an image in the current turn, use `attachment="1"`.
- If the user refers to an existing chat bubble, use `chat_image="N"` or an
  anchor name.
- If you just generated the source earlier in the same reply, use
  `chain="true"` on the remove-background step.
- Do not use Klein / Flux edit presets for background removal. This is a mask
  operation, not a creative redraw.

## Prompt guidance

For existing-image background removal, keep the prompt short and operational:
"Remove the background and keep the foreground subject unchanged as a
transparent-background cutout."

For brand-new transparent sprites, make the generation prompt describe a single
foreground subject with a clean silhouette, centered composition, no cropped
edges, and no extra scene objects. If the user asks for a sticker, icon, decal,
game sprite, or product cutout, a transparent-background output is usually the
right default.

## Rules

- Preserve the source image's subject; do not restyle or redraw it unless the
  user explicitly asks for an edit too.
- The output should have alpha transparency, not a white/black/checkerboard
  painted background.
- If the user asks to remove one object from a scene while keeping the scene
  background, use `image_to_image` / inpaint-style editing instead; this skill
  is for removing the entire background around the foreground subject.
