yt-dlp (Windows standalone build) - bundled helper for AI Chat's web_video skill.

Version: 2026.08.19
Source:  https://github.com/yt-dlp/yt-dlp  (release asset yt-dlp.exe)
License: The Unlicense (public domain), see LICENSE in this folder.

The app resolves utils/yt-dlp/yt-dlp.exe from the app root (falling back to a
yt-dlp.exe on PATH) and runs it with --ffmpeg-location pointing at the bundled
utils/ffmpeg/bin (for merging video + audio). It downloads the whole video at up
to 480p and cuts the wanted seconds locally (yt-dlp's --download-sections is
throttled by YouTube). YouTube also needs a JavaScript runtime: the app passes
--js-runtimes for deno, node, or bun when one is found on PATH.
To update, replace yt-dlp.exe with a newer release and bump the version line above.
