---
id: web_search
summary: List Brave web results WITHOUT downloading anything - kind="images" (default), "videos", or "web" (page titles + snippets for quick facts). The numbered list is stored as S1, S2... and auto-continued to you so you can pick one via web_image / web_video result="S1:3" (or read a kind="web" hit in full with web_page result="S1:3"). Usually skip it - web_image / web_video search and download on their own; use web_search only to choose among candidates, to answer a factual question from snippets, or when the user asks to "search" without wanting a download.
inputs: none
autoload: false
template: <aitools_action skill="web_search" query="1986 Honda Civic hatchback" kind="images" count="10"/>  # kind = images | videos | web. count max 20. Auto-continues with the list unless resume="false". Then: <aitools_action skill="web_image" result="S1:3" anchor="car"/> or, for kind="web", <aitools_action skill="web_page" result="S1:3"/>
---
# Web search (list only)

Runs one Brave Search API query and shows the full numbered result list in a
Web bubble and in your next turn. Nothing is downloaded.

```
<aitools_action skill="web_search" query="1986 Honda Civic hatchback" kind="images" count="10"/>
<aitools_action skill="web_search" query="Kramer entrance compilation" kind="videos"/>
<aitools_action skill="web_search" query="when did Seinfeld first air" kind="web" count="5"/>
<aitools_action skill="web_page" result="S1:2"/>   # read one of the kind="web" hits in full
```

- `kind`: `images` (default) lists image URLs with sizes; `videos` lists page
  URLs with durations; `web` lists pages with a one-line description each
  (pages are NOT fetched; quote only what the snippet says, or read one in
  full with `web_page result="S1:3"`).
- The list is stored as `S1`, `S2`... for this session. Follow up with
  `web_image result="S1:3"`, `web_video result="S2:1"`, or `web_page
  result="S3:1"` (kind="web" hits only).
- It auto-continues (one synthetic turn) so you can act on the list; pass
  `resume="false"` if you only want the user to see it.

Prefer `web_image` / `web_video` directly: they run the same search and take
the first usable result, which is what you want most of the time.
