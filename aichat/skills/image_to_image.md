---
id: image_to_image
summary: Edit an image (user-pasted OR a previously-generated chat image) with a prompt. Result spawns as a new image; the original is unchanged.
inputs: attachment
template: <aitools_action skill="image_to_image" preset="Image To Image Qwen Edit.txt" prompt="describe the change you want" chain="true"/>  # default: stack onto generate_image emitted earlier in THIS reply. For an existing chat bubble use chat_image="N" instead; for a freshly-pasted image use attachment="N". Never combine.
---
# Image-to-image

Use this skill when the user wants you to TRANSFORM an existing image
(edit, restyle, alter, "make this person a robot", "add a hat", etc.)
into a new still image. The original is never modified - the result is
a brand-new image bubble below.

You must specify EXACTLY ONE source via either:

- `attachment="N"` - the Nth image the user pasted/dragged INTO THE CURRENT
  message (1-based). Use this when the user just attached something fresh.
- `chat_image="N"` - the Nth chat-image bubble already in this conversation
  (matches the "Image #N" / "Movie #N" labels visible above each bubble).
  Use this when the user says "edit the image you just made" or "tweak
  picture #2". The system prompt's CHAT IMAGES line tells you the highest
  N currently reachable.

## Available presets

- `Image To Image Qwen Edit.txt` - prompt-driven semantic edit (Qwen-Image-Edit-2511)

## Invocation

DEFAULT - stack onto an image generated EARLIER IN THE SAME REPLY (chain="true"):
```
<aitools_action skill="generate_image" preset="Prompt To Image (Z-Image).txt" prompt="<full Z-Image scene description>"/>
<aitools_action skill="image_to_image" preset="Image To Image Qwen Edit.txt" prompt="Keep everything identical except change the time of day to dusk." chain="true"/>
```
This stacks the edit onto the SAME Pic, so the chat shows ONE bubble that
updates from base image -> edited version. Do NOT also pass attachment /
chat_image when you set chain="true" - the prior step's output is inherited
automatically. chain="true" only works as a follow-up to a generate action
emitted earlier in the same reply. This is the right form for any
"generate then edit" combo in one reply.

Edit a freshly-pasted image (user dropped/pasted an image THIS turn):
```
<aitools_action skill="image_to_image" preset="Image To Image Qwen Edit.txt" prompt="add a sunhat to the woman" attachment="1"/>
```

Edit an image already in the chat from earlier (numbered bubble):
```
<aitools_action skill="image_to_image" preset="Image To Image Qwen Edit.txt" prompt="add a sunhat to the woman" chat_image="1"/>
```

## Writing good Qwen-Image-Edit (2511) prompts

Source: the official Qwen Image Edit 2511 model card + community guidance
([Piclumen tutorial](https://www.piclumen.com/tutorial/qwen-image-handbook/),
[HuggingFace discussion #7](https://huggingface.co/Qwen/Qwen-Image-Edit-2511/discussions/7)).

### How this model thinks

Qwen-Edit-2511 uses an **LLM as its text encoder** (not CLIP), so
natural-language conversational instructions work very well. The
community consensus (see HF discussion) is:

> "It is a LOT more flexible than all the various guides suggest. We're
> long past 'THIS is how to do it'. Don't hesitate to just experiment.
> You can basically just talk to it and tell it what you want in natural
> language and get often very good results."

Length isn't strictly bounded; the model card and community emphasise
**clarity and specificity over length**. A few well-aimed sentences
beats a 500-word essay.

### Recommended structure (from the official tutorial)

Cover these in plain English:

1. **What to KEEP unchanged** - explicitly state. This is the most
   important and most frequently skipped step.
2. **What to MODIFY** - describe the change precisely (item, material,
   colour, position, size).
3. **Mood and tone** - any atmospheric / lighting changes.
4. **Positional hints when relevant** - "top-right corner", "behind
   her", "low on her ears", etc.

The official sample prompt structure is:

> "Keep the [original element] identical, replace the [element to
> change] with [new description], and [additional edits like text or
> lighting adjustments]."

### Don'ts

- Don't say "add hat" and stop. Be specific about what KIND of hat.
- Don't redescribe the entire scene. You're describing the DELTA.
- Don't forget the preservation clause - without "keep X identical"
  Qwen-Edit will often drift the face, lighting, or background.
- Don't overload with many simultaneous edits. The model reportedly
  prioritises and may ignore later instructions when many compete.

### Example

User says: "give the woman a hat".

Bad prompt: `add a hat`

Good prompt:

> Keep her face, hair colour, hair style outside the hat, expression,
> clothing, pose, and background completely identical. Add a wide-brimmed
> black straw sunhat with a faded pink silk ribbon tied around the crown,
> sitting at a slight tilt over her right brow. The brim casts a soft
> shadow across the upper half of her face that catches a faint pink
> reflected light from the ribbon. The hat is gently weathered with a
> few loose straw fibres at the edges.

That's ~70 words: short, specific, and pinned. Qwen-Edit handles this
much better than either "add a hat" or a 500-word over-specified essay.

### Advanced uses (optional, only if the user asks)

Per the community guidance, Qwen-Edit can also do:

- Multi-image fusion ("isolate the subject in image 1 and put them in
  the scene from image 2").
- Style transfer ("redraw in the style of a pencil sketch").
- Text editing within the image (Chinese / English supported).
- Character rotation and angle changes.

Don't volunteer these unless the user asks - they need extra setup.

## Rules

- Pick exactly ONE source: `attachment`, `chat_image`, OR `chain="true"`.
  If both `attachment` and `chat_image` are set, `chat_image` wins. `chain="true"`
  must NOT be combined with the others - the chained step inherits the prior
  step's output automatically.
- Write a concise, specific edit prompt. ALWAYS include a "keep X
  identical" preservation clause for the parts that must not drift -
  this is the single biggest determinant of edit quality.
- The prompt describes the CHANGE, not the whole image (the model
  already sees the image).
- `chat_image="N"` only works while the world Pic for that bubble still
  exists. If the user deleted it, the CHAT IMAGES line in the system
  prompt will show fewer reachable images - reference accordingly, or
  if none are reachable just say so in chat.
- If the user asks for variations, emit multiple action tags in the
  same reply (each spawns its own bubble in stream order, with
  deliberately different choices for the change).
