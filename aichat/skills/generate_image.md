---
id: generate_image
summary: Generate a brand-new still image from a text prompt. Use when the user asks for a picture.
inputs: none
template: <aitools_action skill="generate_image" preset="Prompt To Image (Z-Image).txt" prompt="vivid visual description of the scene"/>
---
# Generate an image

Use this skill when the user asks you to create or show a still image
(no input image required).

## Available presets

Pick the preset whose strengths best match the user's request. If unsure,
default to `Prompt To Image (Z-Image).txt` (high-quality general purpose).

- `Prompt To Image (Z-Image).txt` - balanced quality/speed


## Invocation

```
<aitools_action skill="generate_image" preset="Prompt To Image (Z-Image).txt" prompt="vivid visual description of the scene"/>
```

## Writing good Z-Image prompts

Source: the official Tongyi-MAI prompting guidance on the Z-Image-Turbo
HuggingFace discussion (Nov 2025) and the model card.

### What the authors actually say

> "Z-Image-Turbo works best with long and detailed prompts. You may
> consider first manually writing the prompt and then feeding it to an
> LLM to enhance it." (You ARE that LLM in this chat - so write the
> already-enhanced version directly.)

 The description should be very detailed, up to 500 words.

### What to actually include

The PE template the authors ship covers these axes - decide every one
of them yourself rather than leaving them blank:

1. **Subject identity** - apparent age, ethnicity, build, complexion,
   distinguishing features, eyes, hair (colour / length / style /
   condition), facial hair / makeup, expression / microexpression.
2. **Wardrobe** - top + bottom + footwear + accessories, with fabric
   and condition for each piece.
3. **Pose + body language** - exact hand / weight / head / shoulder
   positions; gaze direction.
4. **Setting** - specific place (not "city"), concrete props,
   foreground / mid / background detail, season, weather, time of day.
5. **Lighting** - direction, source, colour temperature, hardness, mood.
6. **Camera** - shot type, lens length, height, angle, depth of field.
7. **Style** - a specific visual reference (e.g. "1980s neo-noir film",
   "Roger Deakins colour palette", "Studio Ghibli watercolour", "Portra
   400 film grain") rather than vague words like "cinematic".

### Don'ts

- No quality boosters ("masterpiece, 8k, best quality")
- No negative_prompt content for this preset (ignored).
- No synonym repetition ("a woman, a female, a girl") - pick one.
- No vague aesthetic words ("beautiful", "epic"). Replace each with a
  concrete observation.
- Don't leave the user's vagueness in. If they say "a girl on the
  beach", YOU pick age, ethnicity, build, hair, clothes, pose, time
  of day, weather, etc.
- Be direct and factual and use common, clear words to describe things, don't be poetic or vague

### Example

User asked: "a woman smoking on a rooftop". Ship something like:

> a candid medium close-up of a woman in her early 30s, half-Korean
> half-French, slim athletic build with broad shoulders, sun-warmed
> skin with a faint dusting of freckles across her cheekbones, sharp
> jawline, a small mole below her left eye, dark brown almond eyes
> with a hint of sleepy puffiness, full lips slightly parted around
> an unfiltered cigarette, no makeup; her hair is a messy chest-length
> bob in deep espresso brown, swept across her forehead by the wind,
> a few strands stuck to her cheek; she wears an oversized vintage
> Nirvana t-shirt tucked loosely into faded high-waisted Levis, a
> worn denim jacket draped over her shoulders, scuffed black leather
> Doc Martens, a thin silver chain on her neck, three small stud
> earrings in her left ear; she stands at a low concrete parapet of
> a Brooklyn rooftop at golden hour, weight on her right leg, left
> foot crossed behind, right hand holding the cigarette near her
> mouth, left forearm resting on the parapet, head tilted slightly
> back, gaze just above the camera, a faint smirk; behind her the
> Manhattan skyline glows in warm amber backlight, the sun a low
> blowout behind a water tower on the next roof, a thin coil of
> cigarette smoke catching the light, soft haze from city pollution;
> key light is the warm low sun behind her acting as a rim light
> around her hair and shoulders, fill from the warm-grey bounced
> light off the rooftop concrete, overall warm-honey colour palette
> with deep cool shadows; medium close-up, 50mm equivalent, camera
> at her chest height, very shallow depth of field with the skyline
> rendered as soft hexagonal bokeh; shot in the style of a mid-2010s
> Vogue editorial / Annie Leibovitz, Portra 400 film grain, natural
> skin tones, no retouching

## Stacking with a follow-up step (chain="true")

If the user asks for something like "make a movie with Z-Image and LTX" or
"image-to-image change the weather, then animate it" - emit `generate_image`
first, then a follow-up action with `chain="true"` (image_to_movie /
image_to_image) IN THE SAME REPLY. Both steps run on the SAME Pic, so the
chat shows ONE bubble that updates from still -> edited / animated as each
stage finishes. See `image_to_movie` / `image_to_image` for the chained
syntax. The chained step inherits this image's output automatically - do
not pass attachment / chat_image alongside chain="true".

## Roleplay / recurring characters - critical

Z-Image has NO memory of chat history. Every prompt must re-describe
every visible attribute of every subject FROM SCRATCH, every time. In a
roleplay where the same character (e.g. "Sara") recurs across turns:

- NEVER put just the name in the prompt - "Sara walks down the alley"
  generates a stranger because the model has never heard of Sara.
- ALWAYS paste the full character sheet (age, ethnicity, build, hair,
  eyes, distinguishing features, wardrobe) directly into every prompt
  that includes the character, and layer per-shot details (pose,
  location, action) on top. Names belong in your chat reply for the
  human reader; the prompt= attribute uses ONLY visual descriptions.
- A 1-line prompt referencing an established character is the SINGLE
  most common reason roleplay images "look like a different person".
  Defaulting to long, fully-described prompts every turn fixes it.

## Rules

- If the user asked for an image - or one would obviously help - just spawn
  it. Don't ask for confirmation.
- Write the prompt as English natural-language prose covering all 7
  axes above. Decide every detail the user left out. Don't pass the
  user's 1-liner to the model verbatim.
- `gpu="N"` is optional - omit to let the scheduler pick the best free GPU.

