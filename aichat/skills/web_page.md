---
id: web_page
summary: Read ONE web page - fetch its readable text (article body, headings, infobox rows) into your context and list its candidate images as P<n>:<i> so you can fetch one with web_image result="P<n>:<i>". Sources - url="https://..." (a page, not an image file), result="S1:3" (a web_search kind="web" hit), or query="..." (Brave web search; the best reference-quality hit is read, Wikipedia first). Use it to RESEARCH real things before rendering them, to answer "what does this page/article say", or to pull the pictures from a specific article. Auto-continues with the text in your prompt. One fetch per action; links inside the page are never followed. Needs the Web checkbox on.
inputs: none
autoload: true
triggers: read this page, read the page, read that page, read the article, read this article, read the wikipedia, wikipedia article, wikipedia page, on wikipedia, according to wikipedia, what does this article say, what does the page say, what does this page say, summarize this url, summarize this page, summarize this article, summarize the article, open this link, open this url, open the link, research, look up online, look it up online, from the web page, from this page, from this article, read the url, read this url, web page, webpage
template: <aitools_action skill="web_page" url="https://en.wikipedia.org/wiki/Atari_2600"/>  # or query="Atari 2600 history" (Brave web search, best reference site is read) or result="S1:3" (a web_search kind="web" hit). Optional max_chars="6000" (cap 20000), images="false", max_images="12", resume="false". Then: <aitools_action skill="web_image" result="P1:3" anchor="console"/>
---
# Web page read

Fetches ONE HTML (or plain-text) page, extracts the readable text and puts it in
your prompt on the automatic continue turn, and lists the page's candidate images
as a numbered `P<n>` list you can fetch from with `web_image`. Everything the
host did (URL, HTTP status, bytes, extracted characters, the image list) is shown
to the user in a Web bubble; the text itself is only sent to you.

## Invocation

```
<aitools_action skill="web_page" url="https://en.wikipedia.org/wiki/Atari_2600"/>
<aitools_action skill="web_page" query="Atari 2600 history"/>
<aitools_action skill="web_page" result="S1:2"/>
<aitools_action skill="web_page" url="https://example.com/article" max_chars="12000" images="false"/>
```

- Exactly ONE of `url` / `query` / `result` per action.
- `url` must be a public http(s) PAGE. An image or video URL is refused with a
  hint to use `web_image` / `web_video`; PDFs cannot be read.
- `query` runs a Brave web search and reads the best reference-quality hit
  (Wikipedia, .edu/.gov, Britannica up; forums, social media, shops, PDFs down).
  If that fetch fails or is not a page the next ranked hit is tried (up to 3).
  The hit list is also stored as `S<n>` so you can read a different one with
  `result="S<n>:<i>"`.
- `result="S1:3"` reads a hit from an earlier `web_search kind="web"` list.
- `max_chars` (default 6000, max 20000): how much text reaches you. Longer pages
  are cut at a paragraph boundary with `[truncated, N more chars]`; re-run with a
  bigger `max_chars` only if the missing part matters.
- `images="false"` skips the image list; `max_images` (default 12, max 40).
- `resume="false"` opts out of the automatic continue turn.

## What you get (on the continue turn)

```
[Web page: Atari 2600 - Wikipedia (en.wikipedia.org)] https://en.wikipedia.org/wiki/Atari_2600
# Atari 2600
The Atari 2600 is a home video game console developed and produced by Atari, Inc. ...
Manufacturer: Atari, Inc.
Release date: NA: September 11, 1977
## History
...
[Page images P1 - NOT downloaded yet; fetch one with web_image result="P1:N" anchor="name"]
P1:1 https://upload.wikimedia.org/wikipedia/commons/0/02/Atari-2600-Wood-4Sw-Set.png (alt: "...", 1200x734)
P1:2 https://upload.wikimedia.org/wikipedia/commons/4/4a/Atari-2600-Woody-FL.jpg (alt: "...", 5850x3180, wikimedia thumb -> original)
```

Headings arrive as `#`/`##` lines, list items as `- `, infobox rows as
`Label: value`, table rows as `a | b`. Navigation, footers, reference lists,
"[edit]" links and icons are already removed.

## Research recipe (real things, then render them)

1. `web_search kind="web" query="..."` only if you need to choose among sources;
   otherwise go straight to `web_page query="..."` or a known `url=`.
2. `web_page` on 1-3 pages. Read the text; note the `P<n>:<i>` images you want.
3. `web_image result="P1:3" anchor="console"` for each picture you want as a
   reference (one action per image, ALWAYS an anchor). The host downloads,
   vision-checks and captions it like any web image.
4. Render from the anchors (Klein edit, H3 Reference To Video `<Picture N>`),
   describing each subject only from its fetched photo's caption.

Facts you quote must come from the fetched text, not from memory; say which page
they came from. Do not re-fetch the same URL in the same conversation - the text
is already in your history.

## Limits

- JavaScript-rendered apps, login walls and anti-bot pages yield little or no
  text; the host reports "no readable text" and you should try another source.
- One page per action, no crawling: the page's own links are never followed.
  Emit another `web_page` if you need a second page.
- `query=` needs a Brave Search API key (Settings > Web); `url=` and
  `result=` do not.
- Everything web-related is off while the user's Web checkbox (AI Chat header)
  is off; CURRENT STATE says `WEB ACCESS: OFF` in that case, so do not emit it.
