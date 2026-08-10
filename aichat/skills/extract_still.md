---
id: extract_still
summary: Extract a single frame from an existing Movie #N chat bubble as a new still image (local FFmpeg, no GPU). Main use - grab identity/photo references from a clip before an H3 Reference Video To Video regeneration that must keep the SAME people: extract a close-up frame per person, give each an anchor, then reference them via chat_image2+ (they become <Picture 1>..). Also for the user asking to pull/grab a frame from a video.
inputs: none
autoload: true
triggers: extract a still, extract a frame, extract still, extract frame, grab a frame, grab a still, frame from the video, frame from the clip, frame from the movie, still from the video, still from the clip, still from the movie, freeze frame, screenshot of the video, screenshot from the video, same actors, same actress, same actor, keep the actors, look like the original, look like the actress, look like the actor, keep their faces, same faces, same people as the video, same people as the clip
template: <aitools_action skill="extract_still" chat_image="N" time="2.5" anchor="face1"/>  # chat_image must be an existing Movie #N bubble; time is seconds into the clip. ALWAYS set anchor="name" so the SAME reply can reference the still (e.g. video_to_video chat_image2="face1"). Extract one frame per action; pick moments where the face is large and frontal. time is a GUESS (captions have no timecodes) - before an H3 render, verify each frame same-reply with inspect_image (last one resume="true") and render on the continue turn, re-extracting any frame that missed.
---
# Extract still frame

Pull ONE frame out of an existing Movie bubble as a new still image bubble. This
is a LOCAL FFmpeg operation: no GPU, no ComfyUI, nearly instant.

Two main uses:

1. **Identity references for H3 regeneration (the important one).** When the
   user wants a new video with the SAME people/characters as a source clip
   ("more versions but keep the actors", "make her look like she does in the
   clip"), the clip alone is a weak identity lock. Extract a close-up frame of
   EACH person first, then run `video_to_video` with the H3 Reference preset,
   staging those stills in `chat_image2+` so they become `<Picture 1>`,
   `<Picture 2>`, ... photo references.
2. **The user explicitly asks for a frame** ("grab the frame at 12 seconds",
   "give me a still from that clip").

## Invocation

```
<aitools_action skill="extract_still" chat_image="1" time="2.5" anchor="face1"/>
```

- `chat_image` must point at an existing Movie bubble from CHAT IMAGES (number
  or anchor name). Defaults to the latest chat medium, but be explicit.
- `time` is seconds into the CLIP (not the original source video). Clamped to
  the clip length. Default `0`.
- `anchor` - ALWAYS set one. Same-reply actions cannot guess the new bubble
  number, and `chain="true"` on a following `video_to_video` would wrongly make
  the STILL the primary source. The anchor is the only reliable same-reply
  reference: `chat_image2="face1"`.
- One frame per action; emit several actions for several frames/people.

## Picking good timestamps - and VERIFYING the frames

The Movie bubble's caption usually describes the shots in order ("opens with
tight close-ups...", "then a wider shot...") but gives NO timecodes, so your
`time` values are guesses. Pick times where the target face is LARGE and
frontal - close-ups beat wide two-shots. For two people, extract one frame per
person at their clearest moment; two or three frames of the SAME person
(different angles) strengthen the identity lock further.

Every extracted still is captioned automatically (async - the caption shows in
CHAT IMAGES by your next turn). Because timestamps are guesses, a frame can
easily miss its target (a wrong shot, or nobody in it at all), and an empty
reference silently ruins the render. So VERIFY before spending a multi-minute
H3 render on guessed frames: in the SAME reply, run `inspect_image` on each
extracted still (last one with `resume="true"`), then emit the render action on
the continue turn only if the frames show the right people - otherwise
re-extract at different times first. Skipping verification is reasonable only
when the clip caption pins the shot confidently (e.g. "opens with tight
close-ups of the couple" and you extract inside the opening second).

## Full identity-preserving regeneration example

User: "Make another version of movie 1 with different dialog, but keep the
two actors looking exactly like the originals."

Reply 1 - extract the guessed frames and verify them:
```
<aitools_action skill="extract_still" chat_image="1" time="1.0" anchor="man_face"/>
<aitools_action skill="extract_still" chat_image="1" time="2.5" anchor="woman_face"/>
<aitools_action skill="inspect_image" chat_image="man_face" prompt="Does this frame clearly show a man's face, large and mostly frontal? Describe his visible hair, wardrobe, and anything on his neck/head."/>
<aitools_action skill="inspect_image" chat_image="woman_face" prompt="Does this frame clearly show a woman's face, large and mostly frontal? Describe her visible hair color and wardrobe." resume="true"/>
```

Continue turn - if both frames are good, render (re-extract instead if one
missed):
```
<aitools_action skill="video_to_video" preset="{{Reference Video To Video (MiniMax H3) 5s.txt}}" prompt="The man from <Picture 1> and the woman from <Picture 2>, exactly as they appear there - same faces, hair, and wardrobe - stand beside the vintage car from <Video 1>, matching its steady two-shot framing and warm daylight. He says 'new line here'. She laughs and replies 'another line'. Ambient wind and birdsong." chat_image="1" chat_image2="man_face" chat_image3="woman_face"/>
```

The clip stays `chat_image="1"` (motion/camera/audio reference); the stills ride
slots 2+ (identity references). Describe each person ONLY from what is visible
in the frames and the inspection results - never from outside knowledge of the
film or actor. The inspection wording ("anything on his neck/head") is how
details like a red neckerchief make it into the prompt faithfully.

## Limits

- The frame comes from the imported chat clip (already transcoded, max
  ~832x480), not the original source file. That is fine for close-up identity
  refs. If the user wants a maximum-quality still, they can use the Import
  still button in the video import/export chooser, which cuts from the
  original source at native resolution.
- The extracted still is a normal image bubble: it can also feed
  `image_to_image`, `image_to_movie`, `inspect_image`, etc. on later turns.
