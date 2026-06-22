---
id: inspect_image
summary: Run a real vision inspection/caption pass on an existing chat image or current attachment. Use when the user asks you to check your work, verify an output, describe a generated image, or look for layout/artifact problems. The result appears in chat and is fed back into the next turn.
inputs: attachment_optional
autoload: true
triggers: check your work, inspect image, examine image, verify image, self check, self-check, real caption, caption image, describe generated image, what is in this image, what is wrong with this image
template: <aitools_action skill="inspect_image" chat_image="N" resume="true" prompt="QA inspect this image. Start with PASS or FAIL. Check for visible layout/text defects, mismatches, artifacts, and unreadable text."/>
---
# Inspect an image with vision

Use this when you need the app to run a real vision/caption sidecar on
an image instead of assuming that the render prompt describes the result.
This is for checking actual pixels: blank panels, black rectangles,
missing subjects, unreadable text, bad layout, wrong character, wrong
object, or whether an image matches the user's request.

For layout-heavy outputs such as comics, posters, covers, storyboards, grids,
and captioned images, use a QA prompt instead of a pleasant caption prompt.
The vision result should find defects first, not reassure.

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
<aitools_action skill="inspect_image" chat_image="5" resume="true" prompt="QA inspect this final comic page. Start with PASS or FAIL. Check whether it is a finished two-panel comic about the Commodore 64. Mark FAIL for blank panels, black rectangles, missing speech bubbles, unreadable text, title/text touching or overlapping panel art, duplicated panels, bad gutters, or wrong subject matter. Name the affected region."/>
```

The prompt must be self-contained: the vision sidecar sees the image and
your `prompt=`, not the whole chat history.

When inspecting transparency/cutouts, ask explicitly whether the checkerboard
regions are on the intended side of the subject/background. The host sends PNGs
with alpha as a checkerboard-composited inspection copy, with a note explaining
that checkerboard means transparent pixels.

Add `resume="true"` when the user expects you to answer or take the next
step after the vision result. The host will wait for all pending inspections
and then give you one automatic `(continue)` turn with the result, so do not
ask the user to click Send/Continue. Omit `resume` only for background
inspections whose result you do not need to act on.

## Rules

- Do not use this to edit the image. Use `image_to_image` or composition
  skills after the inspection result comes back.
- For checking/fixing a layout, the prompt must ask for PASS/FAIL and list the
  defects to check. Include title/text overlap, clipped text, duplicated text,
  bad gutters, unreadable bubbles, and blank/black panels when relevant.
- If any title, caption, label, or speech bubble touches/overlaps unrelated art
  or panel content, the inspection should count that as FAIL even if the rest
  of the image looks good.
- Prefer the final composed bubble, not the loose source panels.
- If you inspect the image you just created earlier in the same reply, omit
  `chat_image` only when you mean the latest spawned image. The host will
  auto-pick that latest chat image, wait until real rendered pixels are
  readable, then run the inspection. Use an explicit `chat_image="N"` or named
  anchor when inspecting anything other than the latest image.
