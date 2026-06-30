---
id: help
summary: Answer user help, capability, and examples questions from the current loaded SKILLS list. This is a text-only guide, not an executable action.
inputs: none
autoload: true
triggers: help, what can you do, how do i use this, how do i use you, examples, capabilities, render options, what skills, skills available
exclude_triggers: help me, help create, help make, help generate, help edit, help fix, help write, help with
template: <aitools_action skill="read_skill" id="help"/>
---
# Help response guide

Use this skill when the user asks for help, examples, capabilities, render
options, or available skills. Reply in normal chat text only. Do not emit
image/video/composition actions for a help answer.
Use plain text bullets only; do not use emoji or decorative icons.

Build the answer from the current `SKILLS` section in the system prompt, not
from a hardcoded stale list. The `SKILLS` section is the source of truth for
which capabilities are currently loaded.

## Default format

Keep the first help answer brief and easy to scan:

1. Say they can ask in plain English.
2. Give a few example requests, such as:
   - `Create an amazing image of robots fighting.`
   - `Create an amazing image of robots fighting using zimage.`
   - `Make this image into a short movie using ltx.`
   - `Restyle movie 1 to look like winter.` (edit/restyle an existing clip)
   - `Edit image 2 so it is raining at night.`
   - `Edit image 2 with bernini so it is raining at night.` (alternative edit model)
   - `Combine images 1 and 2 into one scene.`
   - `Make a comic page/poster/book page about ...`
   - `Remove the background from this image.`
   - `Make a seamless tileable stone texture.`
   - `Check this image and tell me if anything looks wrong.`
3. Mention optional render hints only at the user level:
   images: `zimage`, `krea`, `ideogram`;
   image editing: `bernini` (alternative to the default edit model);
   movies: `ltx`, `wan`.
4. Mention: for consistent characters, objects, or styles across multiple
   images, ask to `use anchors`.
5. Add a short "Available areas" list based on the loaded SKILLS summaries.
   Group related internal skills into user-facing areas instead of dumping every
   internal action name. For example:
   - images and image editing
   - movies and animation (from text, from an image, or restyling an existing clip)
   - comics, books, posters, and layouts
   - background removal, tiling, crop/resize, text/borders/shapes
   - image inspection/checking
   - storytelling/roleplay

## Follow-up questions

If the user asks about a specific capability or skill, answer directly from the
current skill summary. If the summary is not enough for a detailed how-to, call
`read_skill` for the specific skill whose details are needed, then continue.

Do not present low-level action names such as `draw_shape`, `paste_image`, or
`describe_image` as commands the user must type. You may mention them only if
the user asks about internals or debugging.

If the user asks "help me make/create/edit..." and clearly wants a task done,
do the task instead of showing the overview.
