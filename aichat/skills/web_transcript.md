---
id: web_transcript
summary: Read a video's full caption transcript without downloading its movie, including YouTube subtitles and automatic captions. For a YouTube transcript use THIS action, not web_page or a short web_video speech check. Takes url= and language="en" (or ja, en-orig, etc.). Auto-continues with transcript text; if truncated, fetch the reported next offset before claiming to have read the whole video. For a request for both the transcript and a clip, use web_transcript and web_video separately.
inputs: none
autoload: true
triggers: transcript, transcription, subtitles, captions, what does this video say, summarize this video, summarise this video
template: <aitools_action skill="web_transcript" url="https://www.youtube.com/watch?v=..." language="en"/>  # optional max_chars="20000" (up to 100000), offset="0", resume="false". Read subsequent pages with the exact next offset reported by the host.
---

# Read a video transcript

Fetch a caption track with yt-dlp, without downloading the video. Manual captions
are preferred when available in the requested language; otherwise automatic
captions are used. Captions can contain transcription errors.

- Use `url` for the video page and `language` for one language code (default `en`).
  `en-orig` selects YouTube's original English automatic track when available.
- The text arrives on the auto-continue turn. Default `max_chars` is 20000,
  maximum 100000. The host reports how much text was delivered and the next
  character `offset` when more remains. Subsequent pages use the cached transcript.
  A new request at `offset="0"` refreshes it.
- A transcript request and a clip request are separate actions. To fetch the first
  seven seconds too, emit `web_video` with the same URL, `start="0"` and
  `duration="7"`. Do not require speech or visual suitability just to retrieve
  exactly what the user requested.
- Missing captions, unavailable languages, network errors and authentication
  failures are different outcomes. Report the error actually returned by the host.
  Do not invent a transcript from the video title, search snippets, or visuals.
- An actual sign-in error can be addressed with Settings > Web > Cookie file
  (a local Netscape-format YouTube cookie export) or Cookies from browser.
  These settings persist and are reused for video and transcript requests.
  Never ask the user to paste passwords or cookie contents into chat.

The Web checkbox must be enabled. A Brave Search API key and a speech-to-text
service are not needed. Sites without captions need a separate audio transcription
workflow; this action does not claim to transcribe audio.
