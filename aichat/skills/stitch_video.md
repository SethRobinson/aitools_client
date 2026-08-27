---
id: stitch_video
summary: Join two or more existing Movie bubbles into ONE video, back to back, in the order listed (local FFmpeg, no GPU; mixed sizes letterboxed, audio kept). MULTI-CLIP FILM / EPISODE RECIPE ("make N clips that tell a story, then stitch them", "a 1 minute episode"): do it in ONE reply - emit every clip's action, put anchor="sceneN" on each movie-producing action, and END the reply with ONE stitch_video chat_images="scene1,...,sceneN". The host parks the stitch until every clip has rendered: never wait a turn, never emit continue to "check on" the clips, never guess Movie numbers. HOW EACH CLIP IS MADE depends on the cast - (a) REAL / NAMED / EXISTING people (web_image anchors or any anchors listed in ANCHORS): ONE action per clip, image_to_movie preset="Reference To Video (MiniMax H3) 5s.txt" with every person's photo in chat_image, chat_image2.. (they are <Picture 1>, <Picture 2>.. in the prompt), or video_to_video preset="Reference Video To Video (MiniMax H3) 5s.txt" with a talking web_video clip of the speaker as chat_image (<Video 1>/<Audio 1> = their VOICE) plus the photos in chat_image2+; NEVER a generate_image still of a lookalike. (b) Invented characters only: generate_image -> image_to_movie chain="true" pairs. LENGTH MATH: total = clips x duration, and every clip action takes duration="N" (5-15 s), so "about a minute" = 4 clips x duration="15" (or 6 x duration="10"); if you tell the user a clip is 15 s you MUST put duration="15" on its action. Optional crossfade="0.5" for dissolves instead of hard cuts.
inputs: none
autoload: true
triggers: stitch, stitched, stitching, concatenate, concat, join the clips, join the videos, join them together, join them into, combine the clips, combine the videos, combine them into one, combine the movies, merge the clips, merge the videos, merge the movies, into one video, into one movie, into one long, into a single video, into a single movie, one long video, one long movie, one continuous video, back to back, back-to-back, put them together, put the clips together, sequence of clips, series of clips, series of videos, clips that tell a story, videos that tell a story, short film, mini movie, mini-movie, full movie, feature, episode, an episode of, multi-clip, multiple clips, several clips, 10 videos, ten videos, 5 videos, five videos, 10 clips, ten clips, 5 clips, five clips, each 10 seconds, each 5 seconds, minute long, 1 minute, one minute, 2 minute, two minute, 30 second, thirty second
template: <aitools_action skill="stitch_video" chat_images="scene1,scene2,scene3" anchor="film"/>  # chat_images = comma list, in playback order (min 2, max 60): Movie numbers ("3,5,7"), ranges ("3-12"), anchor names, or "all". Same-reply clips: give each movie-producing action an anchor="sceneN" (real people: image_to_movie with Reference To Video (MiniMax H3) 5s.txt + the photo anchors in chat_image..chat_image9, duration="N"; invented characters: generate_image -> image_to_movie chain="true") and list those names here - the host waits for their renders. Optional: crossfade="0.5" (seconds; default hard cuts), audio="false", width/height/fps overrides, resume="true" to get a turn once the film is done.
---
# Stitch video (join clips into one film)

Joins existing `Movie #N` bubbles into ONE new Movie bubble, played back to
back in the order you list them. This is a LOCAL FFmpeg operation: no GPU, no
ComfyUI, it takes seconds once the source clips exist. Every clip is fit
inside one shared canvas (letterboxed if a size differs), resampled to one
frame rate, and its audio is carried over (silent clips get a silent track so
the timing stays exact). The result is a normal Movie bubble: it can be
clipped, RIFE-smoothed, frame-extracted, or used as `<Video 1>` later, and
the user can save it from its bubble.

## Invocation

```
<aitools_action skill="stitch_video" chat_images="3,5,7"/>
```

- `chat_images` - the playlist, comma separated, in playback order. Each item
  is a Movie number (`7`), an inclusive range (`3-12`), an anchor name
  (`scene1`), or `all` (every Movie bubble in the chat, oldest first). At
  least 2 items, at most 60. A clip may be listed twice to repeat it.
- `anchor="name"` - optional name for the finished film, so later actions can
  reference it (`chat_image="film"`).
- `crossfade="0.5"` - optional dissolve length in seconds between clips.
  Default is a hard cut, which is what a story with distinct shots normally
  wants. Each crossfade shortens the film by that much and needs every clip
  to be at least twice as long as the fade.
- `audio="false"` - optional; drop all audio. Default keeps it.
- `width`/`height` and `fps` - optional overrides. By default the canvas is
  the size most of the clips share and the frame rate is the highest input
  fps, so nothing is cropped or dropped. Only pass them when the user asks.
- `resume="true"` - optional; the host gives you one automatic `(continue)`
  turn after the film lands, if you want to comment on it. Otherwise the
  finished Movie simply appears and CHAT IMAGES describes it next turn.

Slot form also works for a few clips: `chat_image="3" chat_image2="5"
chat_image3="7"`. Prefer `chat_images` for anything longer.

## Planning a film: length math first

Decide the clip count and per-clip duration from the requested total BEFORE
emitting anything, and put `duration="N"` (5-15 seconds) on EVERY
movie-producing action:

| requested total | plan |
|---|---|
| ~30 s | 3 clips x `duration="10"` or 2 x `duration="15"` |
| ~1 minute | 4 clips x `duration="15"` (fewer renders) or 6 x `duration="10"` |
| ~2 minutes | 8 clips x `duration="15"` |
| unspecified | 3-5 clips x `duration="10"` |

Without `duration=` every clip is 5 s, so "4 clips" stitch to 20 s - not a
minute. If you tell the user "four 15-second clips", each action MUST carry
`duration="15"`. Reference generations (real people) run an 8-step turbo
distill by default but still take a minute or more each (longer at
`duration="15"` or Quality); that is fine, the stitch waits.

## Which action makes each clip

**(a) The cast are REAL, NAMED, or EXISTING people** - there are anchors for
them in the ANCHORS line (web_image fetches, user photos, minted portraits).
Each clip is ONE reference action that takes the photos directly. Do NOT
generate a Z-Image still of a lookalike and animate it: text alone produces a
stranger and throws the references away.

- Photos only (looks right):
  `image_to_movie` + `{{Reference To Video (MiniMax H3) 5s.txt}}`, the
  people's photo anchors in `chat_image`, `chat_image2`.. (up to 9; they are
  `<Picture 1>`, `<Picture 2>`.. in slot order), `duration="N"`,
  `anchor="sceneN"`. Refer to each person by their `<Picture N>` tag and
  describe them ONLY from the photo's caption.
- Photos + VOICES (looks AND sounds right): first fetch one talking clip per
  speaking character with `web_video query="<show> <character> talking scene"
  speech="true" anchor="<name>_clip"` (it auto-continues), then on the
  continue turn each clip is `video_to_video` +
  `{{Reference Video To Video (MiniMax H3) 5s.txt}}` with the speaker's clip
  in `chat_image` (`<Video 1>` / `<Audio 1>` - H3 clones that voice), an
  optional second speaker's clip in `chat_image2` (`<Video 2>` / `<Audio 2>`;
  max 2 clips per render), and the photo anchors in the following slots
  (`<Picture 1>`..). Say in the prompt whose voice comes from which
  `<Audio N>` and write the exact spoken lines.

**(b) Invented characters** (nobody in ANCHORS, no real names): the standard
still -> movie pair per clip, `duration="N"` and `anchor="sceneN"` on the
MOVIE action. Restate the character's full appearance in every prompt.

Either way: an explicit clip count or total length from the user overrides
the roleplay pacing rule about idle GPUs - emit every clip now.

## Worked example - real cast, photos only

User: "Make a 1 minute mini episode where these two argue about pizza, use
their real looks." ANCHORS: `alex=#1, sam=#2` (web_image fetches).

Plan: 4 clips x 15 s. ONE reply:

```
Scene 1 - <one line of story for the user>
<aitools_action skill="image_to_movie" preset="{{Reference To Video (MiniMax H3) 5s.txt}}" prompt="The man from <Picture 1> (describe him from his caption: age, hair, wardrobe) and the woman from <Picture 2> (from her caption) sit at a diner booth. He slides a pizza box across and says 'Pineapple. Deal with it.' in English with a flat New York accent; she pushes it back and snaps 'Absolutely not.' One slow push-in at table height. Warm diner light; ambient clatter of plates." chat_image="alex" chat_image2="sam" duration="15" width="864" height="480" anchor="scene1"/>

Scene 2 - <one line>
<aitools_action skill="image_to_movie" preset="{{Reference To Video (MiniMax H3) 5s.txt}}" prompt="<Picture 1> and <Picture 2> again, same diner ... 'new line' ..." chat_image="alex" chat_image2="sam" duration="15" width="864" height="480" anchor="scene2"/>

... scenes 3 and 4 the same way ...

<aitools_action skill="stitch_video" chat_images="scene1,scene2,scene3,scene4" anchor="pizza_episode"/>
```

## Worked example - real cast with their voices

Reply 1 (fetch voice references; web_video auto-continues):
```
<aitools_action skill="web_video" query="<show> <character A> talking scene" speech="true" duration="5" anchor="a_clip"/>
<aitools_action skill="web_video" query="<show> <character B> talking scene" speech="true" duration="5" anchor="b_clip"/>
```

Continue turn (each scene one rv2v action, then the stitch):
```
<aitools_action skill="video_to_video" preset="{{Reference Video To Video (MiniMax H3) 5s.txt}}" prompt="The woman from <Picture 1> - same face, hair, wardrobe - speaks with the voice from <Audio 1>; the man from <Picture 2> speaks with the voice from <Audio 2>. They sit on a couch ... she shouts 'exact line' ... he mutters 'exact line'. One slow push-in. Sitcom lighting; ambient apartment sound." chat_image="a_clip" chat_image2="b_clip" chat_image3="charA" chat_image4="charB" duration="15" anchor="scene1"/>
... more scenes ...
<aitools_action skill="stitch_video" chat_images="scene1,scene2,scene3,scene4" anchor="episode"/>
```

## Worked example - invented characters

User: "Make 5 clips, 10 seconds each, that tell a story about a lighthouse
keeper, then stitch them together into one video."

```
Scene 1 - <one line of story text for the user>
<aitools_action skill="generate_image" preset="{{Prompt To Image (Z-Image).txt}}" prompt="<full self-contained Z-Image scene 1>" width="864" height="480"/>
<aitools_action skill="image_to_movie" preset="{{Image To Video (MiniMax H3 Turbo Cache) 5s.txt}}" prompt="<H3 motion + one camera move + one short quoted line + ambient sound>" chain="true" width="864" height="480" duration="10" anchor="scene1"/>

... scenes 2-5 the same way ...

<aitools_action skill="stitch_video" chat_images="scene1,scene2,scene3,scene4,scene5" anchor="lighthouse_film"/>
```

Why this shape works:

- The host runs actions in order and PARKS the `stitch_video` until every
  listed clip has finished rendering. You do NOT need `continue`, you do NOT
  need to check CHAT IMAGES for "STILL RENDERING", and you must NOT guess
  future Movie numbers - the anchor names are the only reliable same-reply
  reference.
- Keep the film coherent: the SAME setting/palette/wardrobe description in
  every prompt; for invented characters restate their full appearance in
  EVERY prompt (the models have no memory between clips). Vary the camera
  and the beat, not the look.
- Give each clip ONE or two short lines of dialog or a clear sound cue; the
  stitched film keeps all audio in order.

Say in the chat text that the finished film will appear as a new Movie once
all clips are rendered; the host posts the stitched Movie bubble by itself.

## Stitching clips that already exist

On a later turn, use the real numbers from CHAT IMAGES:

```
<aitools_action skill="stitch_video" chat_images="4,6,8,10"/>
<aitools_action skill="stitch_video" chat_images="3-12" crossfade="0.5"/>
<aitools_action skill="stitch_video" chat_images="all"/>
```

A listed clip that is STILL RENDERING is fine - the host waits for it. A
still image is not: animate it first (`image_to_movie` with `anchor=`) and
list the anchor instead.

## Rules

- Minimum 2 clips, maximum 60 per action; for more, stitch in batches, then
  stitch the batch results.
- Order in `chat_images` is playback order. Same-reply clips are referenced
  by anchor name, earlier clips by Movie number; never by a predicted number.
- Emit the `stitch_video` AFTER all of its clips' actions in the same reply.
- Do not add `continue` "to wait for the clips" - the stitch waits by itself.
  The user can keep chatting while it waits; Stop or Clear cancels it.
- Do not put `chain="true"` on `stitch_video`; it takes a list, not a chain.
  Later actions in the same reply MAY chain onto the finished film (for
  example `rife_video chain="true"`).
- Crossfades are opt-in (`crossfade="S"`), hard cuts are the default.
