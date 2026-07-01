---
id: clip_video
summary: Create a short local MP4 clip from an existing Movie #N chat bubble using FFmpeg. Use when the user asks for a specific start time/duration such as "make a 5 second clip starting at 30 seconds". The result is a new Movie #N bubble that later video_to_video can edit.
inputs: none
autoload: true
triggers: clip the video, clip this video, trim the video, trim this clip, cut a clip, make a clip, 5 second clip, five second clip, starting at, starts at, from 30 seconds, at 30 seconds, first 5 seconds, next 5 seconds
template: <aitools_action skill="clip_video" chat_image="N" start="30" duration="5"/>  # chat_image must be an existing Movie #N bubble. start/duration are seconds. Optional: fps="24" and audio="false". To edit that new clip in the SAME reply, follow with video_to_video chain="true".
---
# Clip video

Use this skill when the user wants a SHORT CLIP from an existing video already
in AI Chat, especially when they mention a start time or duration:

- "make a 5 second clip starting at 30 seconds"
- "use the first 3 seconds"
- "trim movie 2 from 12s for 5s"

This is a LOCAL FFmpeg operation. It does not call ComfyUI and does not edit the
content. It creates a new Movie bubble that can then be used as a source for
`video_to_video`.

## Invocation

```
<aitools_action skill="clip_video" chat_image="1" start="30" duration="5"/>
```

- `chat_image` must point at an existing Movie bubble from CHAT IMAGES.
- `start` is seconds from the beginning; default is `0`.
- `duration` is seconds; default is `5`.
- `fps` is optional; omit it to keep the source video's frame rate. If specified,
  use a normal positive number like `24` or `30`.
- `audio` is optional and defaults to `true`; set `audio="false"` or
  `no_audio="true"` only when the user explicitly wants a silent clip.

## Same-reply edit

When the user asks to clip AND edit in one request, emit `clip_video` first, then
run `video_to_video` on the just-created clip with `chain="true"`:

```
<aitools_action skill="clip_video" chat_image="1" start="30" duration="5"/>
<aitools_action skill="video_to_video" preset="{{Video To Video (Bernini).txt}}" prompt="Remove the hands holding the baby so the baby appears to dance independently, while preserving the original baby motion, camera framing, timing, and home-video lighting." chain="true"/>
```

Do not guess the new Movie number in the same reply. Use `chain="true"`.
