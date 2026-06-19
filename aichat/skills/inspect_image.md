---
id: inspect_image
summary: Run a real vision inspection/caption pass on an existing chat image or current attachment. Use when the user asks you to check your work, verify an output, describe a generated image, or look for layout/artifact problems. The result appears in chat and is fed back into the next turn.
inputs: attachment_optional
autoload: true
triggers: check your work, inspect image, examine image, verify image, self check, self-check, real caption, caption image, describe generated image, what is in this image, what is wrong with this image
template: <aitools_action skill="inspect_image" chat_image="N" prompt="Describe the actual image and note mismatches or artifacts."/>
---
# Inspect an image with vision

Use this when you need the app to run a real vision/caption sidecar on
an image instead of assuming that the render prompt describes the result.
This is for checking actual pixels: blank panels, black rectangles,
missing subjects, unreadable text, bad layout, wrong character, wrong
object, or whether an image matches the user's request.

## When to use

- The user says "check your work", "inspect it", "what did you make?",
  "does this match?", "caption the image", or "what went wrong?"
- You need to describe a generated chat image and generated-image auto
  captioning is off.
- You want a vision model to compare a result against a concrete request.

## How to call it

Use `chat_image="N"` for an existing generated or composed image. Use
`attachment="N"` only for a user image pasted this turn.

```
<aitools_action skill="inspect_image" chat_image="5" prompt="Describe the actual image. Check whether it is a finished two-panel comic about the Commodore 64; mention blank panels, black rectangles, missing speech bubbles, unreadable text, or wrong subject matter."/>
```

The prompt must be self-contained: the vision sidecar sees the image and
your `prompt=`, not the whole chat history.

## Rules

- Do not use this to edit the image. Use `image_to_image` or composition
  skills after the inspection result comes back.
- Prefer the final composed bubble, not the loose source panels.
- If the image was just created in the same reply and is still rendering,
  wait until the next turn and reference its visible `chat_image="N"`.
