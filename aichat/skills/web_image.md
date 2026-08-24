---
id: web_image
summary: Search the web (Brave) for a photo and add it to chat as a normal image bubble #N. Main use - reference photos of REAL people, named characters, places, products, or logos BEFORE an H3 Reference To Video or Klein edit (the image models cannot render real likenesses from memory); also when the user asks to find / fetch / download a picture. One action per subject, ALWAYS anchor="name", then reference the anchor in the SAME reply via chat_image2..9 (<Picture N>). The host ranks results by source quality and VISION-CHECKS every download (wrong subject, AI art, paintings, photos of screens/framed pictures, crowds are rejected automatically and the next result is tried), so an added image is already verified and captioned; add resume="true" only when you must read the captions before deciding. Every search, download and verdict is shown in a Web bubble.
inputs: none
autoload: true
triggers: find a photo, find a picture, find an image, find photos, find pictures, find images, search for a photo, search for a picture, search for an image, search the web, search the internet, web search, look up a photo, look up a picture, look up an image, download a photo, download a picture, download an image, download photos, download images, fetch a photo, fetch a picture, fetch an image, grab a photo, grab a picture, get a photo of, get a picture of, get an image of, photo from the internet, picture from the internet, image from the internet, photo from the web, picture from the web, image from the web, from google, google images, real photo of, actual photo of, reference photo, reference photos, reference image, reference images, what he looks like, what she looks like, what they look like, look like the real, looks like the real, the real actor, the real actress, celebrity, celebrities, famous person, real person, movie character, tv character, tv show, sitcom
template: <aitools_action skill="web_image" query="Jerry Seinfeld portrait photo face" anchor="jerry"/>  # one subject per action. Or url="https://..." (direct image), result="S1:3" (from a web_search list), or result="P1:3" (an image listed by web_page). count="2" adds name + name_2. Optional criteria="full body, standing" (extra requirements for the vision check), min_width="256", safesearch="off", verify="false" (skip the vision check), resume="true" (auto-continue once captions are in).
---
# Web image fetch

Search the web with the Brave Search API and download a photo into chat as a
normal still bubble `#N`. It is captioned automatically, gets the anchor you
name, and works everywhere a pasted image works: `chat_image` slots, Klein
`image_to_image`, `image_to_movie`, `inspect_image`, local composition.

## When to use it

1. **Reference photos for real / named subjects.** The image and video models
   cannot draw Jerry Seinfeld, the Eiffel Tower at night, a specific product, or a
   brand logo faithfully from a text prompt. Fetch a photo first, then use it as
   a `<Picture N>` reference (H3 Reference To Video) or a Klein edit input.
2. **The user asks for a picture from the web**: "find a photo of a 1986 Honda
   Civic", "grab a picture of the Mona Lisa", "show me what Kramer looks like".

Do NOT use it for things the generators handle well on their own (generic
scenes, styles, imaginary subjects). Do NOT invent URLs; use `query=`.

## Invocation

```
<aitools_action skill="web_image" query="Jerry Seinfeld portrait photo face" anchor="jerry"/>
<aitools_action skill="web_image" url="https://upload.wikimedia.org/wikipedia/commons/.../Eiffel.jpg" anchor="tower"/>
<aitools_action skill="web_image" result="S1:3" anchor="pick"/>
<aitools_action skill="web_image" result="P1:2" anchor="console"/>
```

- Exactly ONE of `query` / `url` / `result` per action. `result` takes a
  `web_search` hit (`S1:3`) or an image from a `web_page` image list (`P1:2`);
  both skip the Brave search and go straight to download + vision check.
- `anchor="name"` - ALWAYS set one. Same-reply actions cannot guess the new
  bubble number; the anchor is the only reliable same-reply reference.
- `count="N"` (default 1, max 4) adds several photos; anchors become `name`,
  `name_2`, `name_3`... Two or three photos of the SAME person strengthen an H3
  identity lock.
- `criteria="..."` adds requirements to the vision check ("full body, standing",
  "the car's front three-quarter view", "no sunglasses"). The query itself is
  always the subject being checked for.
- `min_width` (default 256) skips tiny results (measured on the DECODED pixels).
- `safesearch="off"` only when the user explicitly wants unfiltered results.
- `verify="false"` skips the vision check (only when the user asks for raw
  results or no vision LLM is available; the host skips it automatically then).
- `resume="true"` on the LAST web action of the reply when you need to read the
  captions before acting (verify the photo really shows the right person, answer
  a question about it). The host waits for every fetch AND its caption, then
  sends you one automatic `(continue)` turn; do not also emit `continue`.

Query tips: `<name> portrait photo face` for people, `<name> full body photo`
for wardrobe/build, `<product> product photo white background`, `<place>
landmark photo daytime`. Add the show/film name for characters ("Cosmo Kramer
Seinfeld Michael Richards").

## What happens (shown in a Web bubble)

The host searches Brave images (20 results), lists every result (title, size,
host, URL), ranks them by source quality (Wikimedia / official sources first;
AI-art, clipart, wallpaper, stock and merchandise hosts last) and shows the
download order with scores. Then it downloads candidates in that order:
hotlink-blocked originals fall back to Brave's 500 px thumbnail, HTML pages and
tiny images are skipped, webp/gif/avif are converted to PNG, oversized
originals are downscaled to 2048 px. Each download is then judged by the vision
LLM against the query (and your `criteria`): wrong subject, AI art, paintings,
caricatures, photos of screens or framed pictures, crowds, watermarked or
extreme images are marked UNSUITABLE with a reason and the next candidate is
tried (up to 12 downloads). The same vision call produces the caption, so an
added `#N` is already verified and captioned. A URL fetched earlier this session
is reused, not re-downloaded. The CHAT IMAGES line reads `#N: web image, WxH,
anchor="jerry", web image: "query" -> host/path, caption: ...`.

If the summary says fewer images were added than requested, refine the query
(full name, "official portrait", the show/film name, a decade) rather than
repeating the same one.

## Recipe: a video starring several real people

User: "make a Seinfeld episode where they play retro games"

One reply - fetch each person, then reference the anchors:
```
<aitools_action skill="web_image" query="Jerry Seinfeld portrait photo face" anchor="jerry"/>
<aitools_action skill="web_image" query="George Costanza Jason Alexander portrait photo face" anchor="george"/>
<aitools_action skill="web_image" query="Elaine Benes Julia Louis-Dreyfus Seinfeld portrait photo" anchor="elaine"/>
<aitools_action skill="web_image" query="Cosmo Kramer Michael Richards Seinfeld portrait photo" anchor="kramer"/>
<aitools_action skill="image_to_movie" preset="{{Reference To Video (MiniMax H3) 5s.txt}}" prompt="The man from <Picture 1>, the shorter balding man from <Picture 2>, the woman from <Picture 3>, and the tall wild-haired man from <Picture 4> crowd a 90s apartment couch around a CRT TV playing an NES game, passing the controller and bickering. <Picture 1> says 'Who plays a plumber for fun?' <Picture 4> lunges for the controller. Sitcom lighting, laugh track, 4:3 era framing." chat_image="jerry" chat_image2="george" chat_image3="elaine" chat_image4="kramer" width="864" height="480"/>
```
The fetches run first (the host parks later actions until each download is
done), so the anchors resolve when the movie action runs. Describe each person
only by what is visible in the photos; do not rely on outside knowledge of the
actors beyond the query.

The host's vision check already rejects wrong or unusable photos, so the
anchors are normally trustworthy in the same reply. Use the `resume="true"`
variant (on the last `web_image`, then check captions on the continue turn and
re-fetch with a different query) only when the summary reports a missing
subject or the user asked you to confirm the photos first.

## Limits

- Needs a Brave Search API key (Settings > Web). Without one, or if Brave rejects
  the key / the credit is exhausted, the host shows a red Error bubble with the
  fix; relay it to the user and do not retry until they say it is fixed
  (`url=` still works).
- SafeSearch defaults to strict (configurable). Results are whatever the web has:
  check captions, never assume the first hit is right.
- Only public http/https URLs are accepted.
- Downloaded files live in the session temp cache; the user can save a bubble
  to keep it.
