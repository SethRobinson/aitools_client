---
id: web_image
summary: Search the web (Brave) for a photo and add it to chat as a normal image bubble #N. Main use - reference photos of REAL people, named characters, places, products, or logos BEFORE an H3 Reference To Video or Klein edit (the image models cannot render real likenesses from memory); also when the user asks to find / fetch / download a picture. One action per subject, ALWAYS anchor="name", then reference the anchor via chat_image2..9 (<Picture N>). DEFAULT for a named show/film cast when WEB ACCESS is on (no need for the user to ask): count="2" per person with query "<show> <character> scene still" and criteria="in-character scene frame from the show itself, in costume on set - not an interview, talk show, premiere, red carpet, award show, photoshoot, or headshot", plus one web_video speech="true" clip per SPEAKING character for the voice. The host ranks results by source quality and VISION-CHECKS every download (wrong subject, AI art, paintings, photos of screens/framed pictures, crowds are rejected automatically and the next result is tried), so an added image is already verified and captioned; add resume="true" only when you must read the captions before deciding. Every search, download and verdict is shown in a Web bubble.
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
   a `<Picture N>` reference (H3 Reference To Image for stills, Reference To
   Video for movies) or a Klein edit input.
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

Query tips: for a CHARACTER from a show/film, `<show> <character> scene
still` with `count="2"` and `criteria="in-character scene frame from the
show itself, in costume on set - not an interview, talk show, premiere, red
carpet, award show, photoshoot, or headshot"` - the render must look like
the character on the show, and press/interview photos give the wrong hair,
wardrobe, and lighting. `<name> portrait photo face` only for a real person
as themselves (a celebrity, a politician), `<name> full body photo` for
wardrobe/build, `<product> product photo white background`, `<place>
landmark photo daytime`. Always add the show/film name for characters
("Cosmo Kramer Seinfeld").

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
by the SAME query (or given explicitly via url=/result=) is reused, not
re-downloaded; a URL that a DIFFERENT subject's search already claimed is
skipped so each subject gets its own vision-checked photo (castmates of one
show share search results). The CHAT IMAGES line reads `#N: web image, WxH,
anchor="jerry", web image: "query" -> host/path, caption: ...`.

If the summary says fewer images were added than requested, refine the query
(full name, "official portrait", the show/film name, a decade) rather than
repeating the same one.

## Recipe: a video starring a real cast (the DEFAULT when Web is on)

User: "make a Seinfeld scene where Jerry and Kramer argue about retro games"

The user does not have to ask for references - with WEB ACCESS on, fetch
them by default: TWO stills per person from the show itself (`count="2"`),
plus ONE talking clip per SPEAKING character (`web_video speech="true"`) so
they sound right. Reply 1 is fetches only (web fetches auto-continue):
```
<aitools_action skill="web_image" query="Seinfeld Jerry Seinfeld scene still" count="2" criteria="in-character scene frame from the show itself, in costume on set - not an interview, talk show, premiere, red carpet, award show, photoshoot, or headshot" anchor="jerry"/>
<aitools_action skill="web_image" query="Seinfeld Cosmo Kramer scene still" count="2" criteria="in-character scene frame from the show itself, in costume on set - not an interview, talk show, premiere, red carpet, award show, photoshoot, or headshot" anchor="kramer"/>
<aitools_action skill="web_video" query="Seinfeld Jerry talking scene" speech="true" duration="5" anchor="jerry_clip"/>
<aitools_action skill="web_video" query="Seinfeld Kramer talking scene" speech="true" duration="5" anchor="kramer_clip"/>
```
On the continue turn, render with Reference Video To Video: clips first
(`<Video 1>`/`<Audio 1>`, `<Video 2>`/`<Audio 2>`), then the stills
(`<Picture 1>`..); each person's two stills define ONE `<Subject N>`:
```
<aitools_action skill="video_to_video" preset="{{Reference Video To Video (MiniMax H3) 5s.txt}}" chat_image="jerry_clip" chat_image2="kramer_clip" chat_image3="jerry" chat_image4="jerry_2" chat_image5="kramer" chat_image6="kramer_2" width="864" height="480" prompt="subject_definitions:
<Subject 1> is the man in <Picture 1> and <Picture 2>, <a few traits from the captions>.
<Subject 2> is the tall wild-haired man in <Picture 3> and <Picture 4>, <traits from the captions>.
<Video 1> and <Video 2> are talking-scene sources for the voices only.
<Audio 1> is the voice-timbre reference for <Subject 1>; <Audio 2> is the voice-timbre reference for <Subject 2>.

summary:
[reference generation + audio reference] The target video shows <Subject 1> and <Subject 2> arguing over an NES controller on a 90s apartment couch, voices styled by <Audio 1> and <Audio 2>.

retention_analysis:
<Subject 1> / <Subject 2>: fully_preserved - faces, hair, and wardrobe retained. <Video 1> / <Video 2> (voice sources): weak_reference. <Audio 1> / <Audio 2>: reference - timbre only, no signal copied.

detailed_description:
The target video uses a warm multi-camera 90s sitcom style with 4:3-era framing.
[Shot 1] A medium two-shot frames <Subject 1> and <Subject 2> on a worn blue couch in front of a CRT television glowing with an 8-bit game... his voice styled like <Audio 1>, <Subject 1> says 'Who plays a plumber for fun?' in a dry New York cadence; <Subject 2> lunges for the controller and, his voice styled like <Audio 2>, shouts 'Give me that!' ...<complete the scene: room, light, actions, one camera move - ~250-350 words>.

overall_soundscape: Chiptune music from the television, controller buttons clicking, a canned audience laugh after each line.

non_diegetic_music: N/A"/>
```
The host parks later actions until each download is done, so the anchors
resolve in order. Describe each person only by what is visible in the
fetched captions; do not rely on outside knowledge of the actors. With 3+
speakers, the extra speakers' clips go in as standalone audio refs
(`audio="elaine_clip"` -> `<Audio 3>`, up to 3) since a render takes at
most 2 clips. If nobody speaks, use `image_to_movie` +
`{{Reference To Video (MiniMax H3) 5s.txt}}` with the stills alone.

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
