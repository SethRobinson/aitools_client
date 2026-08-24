---
id: web_video
summary: Download a SHORT clip (default 5s, max 15s) from the web into chat as a Movie #N bubble - from a page URL (YouTube, Vimeo, etc. via bundled yt-dlp) or a direct media file URL, found via a Brave video search (query=) or given (url=). The host ranks results (scenes/clips up, interviews/podcasts/reactions down), cuts the section, and VISION-CHECKS each cut (talk shows, intros, wrong subject are rejected; a later part of the source, then the next result, is tried). It ALWAYS auto-continues: the clip, its anchor and caption are in CHAT IMAGES on your next turn, so a request like "find a clip of X and make a video" is two steps - fetch now, emit video_to_video on the continue turn. Main use - a motion / appearance / VOICE reference for Reference Video To Video (MiniMax H3) (<Video 1>). H3 clones the clip's AUDIO for the generated voice, so whenever the new video has the character TALKING, set speech="true": the host then also checks the audio (ffmpeg + Whisper) and rejects music-only / silent cuts.
inputs: none
autoload: true
triggers: download a clip, download a video, download the video, download this video, clip from youtube, youtube clip, youtube video, from youtube, grab a clip, grab a video, fetch a video, fetch a clip, get a clip of, get a video of, video from the web, video from the internet, clip from the web, clip from the internet, find a video of, find a clip of, find footage, real footage, actual footage, movie clip of, scene from the movie, scene from the show, reference clip, reference clips, reference video, reference videos, stills and video, stills and videos, vimeo, tiktok, youtu.be, sound correct, sound right, sound like, sounds like, sound like themselves, their voices, real voices, actual voices, voice reference, look and sound, in their own voice
template: <aitools_action skill="web_video" query="Seinfeld Kramer talking scene" speech="true" duration="5" anchor="clip1"/>  # speech="true" whenever the render will have the character TALK (the clip is the voice reference; music-only cuts are rejected). Or url="https://www.youtube.com/watch?v=..." / url="https://.../file.mp4" / result="S2:1". start/duration are seconds into the SOURCE video (default 0 / 5, max 15). Optional criteria="Kramer bursting through the apartment door" (what the vision check must see), audio="false", max_source_minutes="20", verify="false", resume="false" (it auto-continues by default). Then on the continue turn: video_to_video with {{Reference Video To Video (MiniMax H3) 5s.txt}} chat_image="clip1".
---
# Web video fetch

Fetch a short section of a web-hosted video into chat as a `Movie #N` bubble.
Page URLs (YouTube, Vimeo, and everything else yt-dlp supports) are downloaded
with the bundled yt-dlp at up to 480p (whole video, a few seconds for a 10 minute
source; anything longer than `max_source_minutes` is skipped by yt-dlp itself),
then the requested section is cut locally; direct media file URLs
(.mp4/.webm/.mov/.gif) download straight from the app. Either way the
result is normalized by FFmpeg exactly like a dragged-in video (max 832x480,
source fps, audio kept) and captioned.

## When to use it

- A **reference clip** for `video_to_video` with
  `{{Reference Video To Video (MiniMax H3) 5s.txt}}` (`<Video 1>`): motion,
  camera move, timing, or audio the user wants reproduced ("make her dance like
  the famous Kramer entrance").
- The user explicitly asks to **grab / download a clip** from YouTube or the web.
- Prefer `url=` whenever the user pasted a link; use `query=` (Brave video
  search) only when they described the video.

## Invocation

```
<aitools_action skill="web_video" url="https://www.youtube.com/watch?v=abc123" start="12" duration="5" anchor="clip1"/>
<aitools_action skill="web_video" query="Seinfeld Kramer entrance scene" start="0" duration="5" anchor="clip1"/>
<aitools_action skill="web_video" result="S2:3" duration="4"/>
```

- Exactly ONE of `query` / `url` / `result`.
- `start` = seconds into the source; `duration` = clip length (0.5 to 15 s,
  default 5). You usually cannot know timestamps for a searched video: default to
  the start, or ask the user for a time if the moment matters.
- `anchor="name"` so a same-reply `video_to_video` can use `chat_image="name"`
  (do not guess the Movie number). `chain="true"` also works for the very next
  action.
- `audio="false"` for a silent clip. `max_source_minutes` (default 20) skips
  sources longer than that (search results by their listed length, page URLs by
  yt-dlp's duration check); raise it for a user-pasted long video when the
  wanted moment is deep into it.
- `speech="true"` - REQUIRED whenever the video you will generate has this
  character speaking. H3 Reference Video To Video copies the reference clip's
  audio for the voice, so a clip with only music or sound effects yields a
  garbled / wrong voice. With `speech="true"` the host extracts each cut's
  audio and runs it through Whisper; silent or music-only cuts are rejected and
  a later part of the source (then the next result) is tried. The accepted
  clip's caption ends with `Audio transcript: "..."`. Phrase the query for
  dialogue ("Kramer talking scene", "Kramer yells at Jerry", "Kramer rant")
  rather than "entrance" or "montage". If the summary says the speech check was
  unavailable (no OpenAI key for Whisper), warn the user that the voice may be
  wrong.
- `criteria="..."` tells the vision check what the frames must show ("Kramer
  entering through the apartment door", "a wide shot of the whole car").
- It auto-continues by default (no `continue` needed): the next turn's CHAT
  IMAGES lists the Movie with its caption and anchor. `resume="false"` only
  when the user just wants the clip and nothing else.
- `verify="false"` skips the vision check (raw download, user explicitly wants
  whatever the source is).

## Two-step requests ("find a clip of X and make a video of him...")

Emit ONLY the `web_video` action in the first reply (plus a one-line note),
with `speech="true"` because he will talk:
```
<aitools_action skill="web_video" query="Seinfeld Kramer talking to Jerry scene" speech="true" duration="5" anchor="clip1"/>
```
Let the host fetch and auto-continue, then on the `(continue)` turn read the
Movie's caption (and its `Audio transcript`) in CHAT IMAGES and emit the render:
```
<aitools_action skill="video_to_video" preset="{{Reference Video To Video (MiniMax H3) 5s.txt}}" prompt="The tall wild-haired man from <Video 1>, same face, hair, wardrobe and VOICE, sits on a couch holding an NES controller and says 'Zelda? Jerry, the Triforce is a pyramid scheme.' Keep the sitcom lighting and laugh-track energy." chat_image="clip1" chat_image2="kramer"/>
```
Never end a turn with "I'll fetch the clip first, then make the video" and no
follow-up: the host only continues automatically after a web fetch, so the
render must be emitted on that continue turn (or chained same-reply with
`chain="true"` when no caption check is needed). If the fetch summary says no
usable clip was found, refine the query (name the scene, episode, or "best of
<character>") before giving up.

## What happens (shown in a Web bubble)

The search terms and hit count, then per source: title + URL, the exact yt-dlp
command line, its progress and exit code, each FFmpeg cut (`start`..`start+
duration`, then +30 s and +90 s if a cut is rejected), the vision verdict with
its reason, the audio check (`mean volume -21 dB; Whisper: 14 words "..." ->
speech present`) when `speech="true"`, then the resulting `Movie #N` and its
caption. Up to 4 sources are tried; failures list the reason for each attempt
verbatim. If yt-dlp reports a sign-in / bot check, tell the user: Settings > Web
has a "cookies from browser" option (Firefox is the most reliable). If it warns
that no JavaScript runtime was found, YouTube downloads will be throttled or
fail: tell the user to install Deno or Node.js (Settings > Web shows which one
was detected).

## Limits

- Searching needs a Brave Search API key (Settings > Web); `url=` does not.
- Page downloads need `utils/yt-dlp/yt-dlp.exe` (bundled); the bubble says so if
  it is missing. Sites may block automated downloads; that is not retryable.
- Only public http/https URLs. The clip lives in the session temp cache; the
  user can save the Movie bubble to keep it.
