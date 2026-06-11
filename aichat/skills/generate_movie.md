---
id: generate_movie
summary: Generate a short video clip from a text prompt. LTX 2.3 has audio - include ONE short quoted line of in-scene dialog (with language+accent) in the motion beat unless the scene has no plausible speaker. Wan 2.2 preset = higher quality but slow and silent (no dialog/sound tags).
inputs: none
template: <aitools_action skill="generate_movie" preset="{{Prompt To Video (LTX) 5s.txt}}" prompt="4-8 sentences: subject, then motion with ONE short line of in-scene dialog in double-quotes (language+accent), then one camera move, then mood/lighting, then ambient sound"/>
---
# Generate a movie

Use this skill when the user asks for a short video / animation / clip /
movie from a text prompt (no input image required).

## Available presets

- `{{Prompt To Video (Wan22).txt}}` - high-quality 5s, slow (Wan 2.2, no audio)
- `{{Prompt To Video (LTX) 5s.txt}}` - fast 5s clip (LTX 2.3, with audio)

Default to LTX. Pick Wan 2.2 when the user asks for it by name or wants
maximum visual quality and doesn't need sound/dialog.

## Invocation

```
<aitools_action skill="generate_movie" preset="{{Prompt To Video (LTX) 5s.txt}}" prompt="vivid visual description with motion"/>
```

## Writing good LTX 2.3 prompts (`{{Prompt To Video (LTX) 5s.txt}}`)

Source: [docs.ltx.video](https://docs.ltx.video/api-documentation/prompting-guide).

### Structure

**4-8 sentences, single flowing paragraph.** Tuned for this length -
longer pads / smears. Write in this order:

1. **Subject** - what the viewer notices, with physical detail (age,
   clothing, emotional cues like "shoulders lowered"). No style words yet.
2. **Motion + dialog** - concrete verbs (walking, turning, exhaling),
   not abstract energy words. **Fold ONE short line of in-scene dialog
   into this beat** (see below).
3. **Camera** - one explicit move (push-in, tracking, orbit, static).
4. **Mood / lighting** - cinematic tone, lighting, atmosphere.
5. **Ambient sound tag** - one short closing phrase.

### Dialog (DEFAULT ON)

LTX 2.3 generates real audio for quoted lines - a 5s clip with no
dialog wastes the model. **Add ONE short line by default** (~3-12
words), double quotes, language + accent, tucked INTO the motion
sentence - e.g. `she murmurs "I'll be right there" in English with a
soft Irish accent`. Write the EXACT words, never "she says something".
Skip ONLY when the scene has no plausible speaker (alone and silent,
face hidden, empty landscape / abstract, or user said "silent").

### Don'ts

- No aesthetic-only padding ("cinematic, epic" with no motion = still video).
- No jump-cut words ("suddenly", "flashes", "cuts to") - LTX can't cut.
- One priority per scene. No competing ideas.

### Example

User asked: "a woman writing at a desk". Ship something like:

> A woman in her early 30s, half-Korean half-French, in a cream cable-knit
> sweater, sits hunched over an old oak writing desk in a converted
> Brooklyn loft at night, an open leather-bound journal and a brass
> beeswax candle in front of her. She is mid-sentence with a fountain pen,
> then slowly pauses, lifts her head, brushes a strand of espresso bob
> hair behind her right ear, and murmurs to herself "if he reads this,
> he'll understand" in English with a faint French accent, before
> exhaling softly and returning to writing. The camera holds a very slow
> dolly-in - just a few centimetres of push - from chest height, 50mm
> equivalent, shallow depth of field. Warm amber candlelight from
> camera-right is the key, cool moonlight from a tall arched window
> camera-left is the fill, with the candle flame gently flickering and
> shifting soft shadows on the brick wall behind her. Style of a Roger
> Deakins night interior, Portra 400 film grain, natural skin tones;
> ambient sound of distant traffic and a slow ceiling fan.

## Writing good Wan 2.2 prompts (`{{Prompt To Video (Wan22).txt}}`)

Source: [wan2-2.app/prompt](https://wan2-2.app/prompt).

- Formula: **Subject → Scene → Motion → Aesthetic Control →
  Stylization**. No strict word count - "more complete = higher quality".
- Handles longer multi-beat motion than LTX. 200-400 words of timed
  motion + environmental motion + lighting evolution work well.
- **No audio.** Wan 2.2 generates silent video - do NOT add the quoted
  dialog line or an ambient-sound tag; spend those words on motion and
  lighting instead.
- There's no source image, so the first subject sentence must be fully
  self-contained: physical detail, wardrobe, expression, posture.
- **Wan 2.2 uses negative prompts** (unlike LTX / Z-Image). The preset
  ships a good default; only pass `negative_prompt="..."` to override it
  for a specific need.
- Same hard-cut rule: avoid "suddenly", "flashes", "cuts to".

## Scenario / recurring characters

Detailed roleplay, scenario, character-sheet, and identity-anchor
workflows live in `scenario_storytelling`. If that skill is auto-loaded,
follow it for story prose, visual pacing, reference characters, and
GPU-aware multi-shot planning.

Text-to-video has no source image to anchor identity. For recurring
fictional characters, keep the FIRST subject sentence fully
self-contained: apparent age, ethnicity/complexion, build, hair, face,
wardrobe, expression, and posture. Never prompt only with a character
name. For user-supplied reference identities, do not use `generate_movie`;
use `image_to_image` followed by `image_to_movie` with `chain="true"` as
described in `scenario_storytelling`.

## Rules

- User asked for a video → spawn it, no confirmation.
- Default to the LTX preset; use Wan 2.2 when asked for it or for
  max-quality silent clips.
- LTX 2.3: 4-8 sentences, order Subject → Motion+Dialog → Camera → Mood →
  Ambient. Don't pass the user's 1-liner verbatim; don't pad past 8
  sentences. **Always include ONE short quoted in-scene line** (language +
  accent) in the motion beat, unless no plausible speaker. Write EXACT words.
- Wan 2.2: longer multi-beat motion (200-400 words) is fine, but NO
  dialog or ambient-sound tags - it renders silent video.
- Default to 5s unless the user asks longer.
