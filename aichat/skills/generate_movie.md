---
id: generate_movie
summary: Generate a short video clip from a text prompt. LTX 2.3 has audio - include ONE short quoted line of in-scene dialog (with language+accent) in the motion beat unless the scene has no plausible speaker.
inputs: none
template: <aitools_action skill="generate_movie" preset="Prompt To Video (LTX) 5s.txt" prompt="4-8 sentences: subject, then motion with ONE short line of in-scene dialog in double-quotes (language+accent), then one camera move, then mood/lighting, then ambient sound"/>
---
# Generate a movie

Use this skill when the user asks for a short video / animation / clip /
movie from a text prompt (no input image required).

## Available presets

- `Prompt To Video (LTX) 5s.txt` - fast 5s clip (LTX 2.3)

## Invocation

```
<aitools_action skill="generate_movie" preset="Prompt To Video (LTX) 5s.txt" prompt="vivid visual description with motion"/>
```

## Writing good LTX 2.3 prompts

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

## Roleplay / recurring characters - critical

LTX 2.3 (text-to-video) has NO memory of chat history or earlier
generations. Every prompt must re-describe every visible attribute of
every subject FROM SCRATCH, every time, in the FIRST (subject) sentence.

- NEVER put just a name - "Sara walks down the alley and says X"
  produces a stranger because the model has never heard of Sara.
- ALWAYS paste the full character sheet (age, ethnicity, build, hair,
  eyes, distinguishing features, wardrobe) into the subject sentence,
  THEN move to motion + dialog. Names belong in your chat reply for the
  human reader; the prompt= attribute uses ONLY visual descriptions.
- This applies even when the same character was generated last turn -
  text-to-video has no source image to anchor identity.

## Rules

- User asked for a video → spawn it, no confirmation.
- 4-8 sentences, order Subject → Motion+Dialog → Camera → Mood → Ambient.
  Don't pass the user's 1-liner verbatim; don't pad past 8 sentences.
- **Always include ONE short quoted in-scene line** (language + accent)
  in the motion beat, unless no plausible speaker. Write EXACT words.
- Default to 5s unless the user asks longer.
