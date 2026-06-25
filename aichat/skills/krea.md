---
id: krea
summary: Krea 2 Turbo image generation recipe. Triggered by "krea", "krea2", "krea 2", or "krea turbo". It is a recipe, not an executable action: never emit skill="krea"; emit generate_image with the Krea 2 Turbo preset.
inputs: none
autoload: true
triggers: krea, krea2, krea 2, krea turbo, krea2 turbo
template: <aitools_action skill="read_skill" id="krea"/>
---
# Krea 2 Turbo image generation

When the user asks for an image "with krea", "using krea", "krea2", or
"Krea 2 Turbo", use the normal `generate_image` action with the Krea preset.
Do not emit `skill="krea"`; this file only teaches the recipe.

## Invocation

```
<aitools_action skill="generate_image" preset="{{Prompt To Image (Krea 2 Turbo).txt}}" prompt="<self-contained Krea 2 Turbo prompt>"/>
```

Use `width` and `height` only when the user asks for a concrete aspect ratio,
format, or size. Otherwise let the preset default to 1024x1024.

## Prompt style

Krea 2 Turbo is fast and aesthetic-focused. Write a compact, art-directed,
self-contained prompt. The model sees only this prompt, not chat history.

Include:

1. Subject identity and distinguishing visible traits.
2. Setting and important objects.
3. Composition, pose/action, and camera viewpoint.
4. Lighting and color palette.
5. Medium/style direction, such as editorial photograph, graphic design,
   concept art, anime, watercolor, product render, or architectural study.

Prefer a polished visual brief around 80-180 words. Use direct concrete
language. Krea is good for strong style exploration, campaign/art direction,
posters without precise text demands, product/architecture concepts, fashion,
illustration, and graphic image ideas.

## Rules

- Use Krea only when the user explicitly asks for Krea/Krea 2/Krea Turbo.
  Otherwise the normal default remains Z-Image.
- Do not rely on chat-image memory. If an exact existing person/object must
  recur, use `image_to_image` with a chat image reference instead.
- Avoid negative prompts for this preset unless a future tested recipe proves
  they help.
- For rendered text, signage, comic pages, or strict layouts where text
  accuracy matters, prefer Ideogram when the user asked for `ideo`/`ideogram`.
