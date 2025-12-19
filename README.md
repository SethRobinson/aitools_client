# Seth's AI Tools: A Unity based front-end for ComfyUI and Ollama (and others) that does things like generate photos and movies, Twine games, quizzes, posters and more.

License:  BSD style attribution, see LICENSE.md

<p float="left">
<a href="Media/aitools_v2_aiguide.png"><img align="top" src="Media/aitools_v2_aiguide.png"></a>
</p>


## Download the latest public version: V2.12 (Dec 18th, 2025) [AI Tools Client (Windows, 62 MB)](https://www.rtsoft.com/files/SethsAIToolsWindows.zip)

Need an old version? The last pre-V2 version (that still supports Auto1111) can be downloaded [here](https://www.rtsoft.com/files/SethsAIToolsWindowsV095.zip).

NOTE: This software is hard to use because it kind of assumes you already know a lot of about ComfyUI and how to use local LLMs like llama.cpp. 

If this is all weird and strange, this is probably not the place for you to start.  Although, you could just enter an OpenAI API key for both image creation and LLM I guess and this app could do some fun stuff, but the main point of this is to do everything in your home lab locally.

## Features

- It's not a web app, it's a native locally run Windows .exe (Unity with C#)
- It's kind of like lego, it can run ComfyUI workflows that chain together.  You need to understand ComfyUI and workflow tweaking as you'll still need to install all the needed custom nodes and models there. (you do have ComfyUI Manager installed, right?!)
- Drag and drop images in as well as paste images from the windows clipboard
- Built-in image processing like cropping and resizing, mask painting
- Pan/zoom with thousands of images on the screen
- Built to utilize many ComfyUI servers at once
- Privacy respected - does not phone home or collect any statistics, purely local usage. (it does check a single file on github.com to check for newer versions, but that's it)
- Includes "experiments", little built-in games and apps designed to test AI. May be broken
- AI Guide feature harnesses the power of AI to create motivational posters, illustrated stories or whatever
- Adventure mode has presets to various modes - generate ready to upload illustrated web quiz from prompt, simple Twine game project from a prompt, and "Adventure", a sort of illustrated AI Dungeon type of toy


## Recent changes

* A Christmas miracle, I actually updated this!  I figured enough changed that people might want to try it, although I'm still kind of not planning to seriously support this as it's just too niche and hard to make easy to use
* We can now only work with *normal ComfyUI workflows*, hallelujah! No more "API" version exporting!
* Adventure mode now has story and interactive story "AutoPic" modes, it's a newer technique that works better, instead of generating image data with the story, it separately automatically generates image descriptions based on the story. (controlled with AutoPic.txt)
* Erased a lot of presets and added some new ones, the main ones I use these days are Z-Image and Wan
* CrazyCam is replaced by "SpookyCam", a photobooth thing I made, check the preset for it for more info.  Normal crazycam is too broken to use right now
 * LLM support overhauled, it now has a fully GUI based config and improved support for llama.cpp and Ollama
 * While local AI is the main target, ChatGPT 5.2, GPT Image 1.5, Claude 4.5, etc. are also supported (requires API keys to use)


## Known issues

- CrazyCam disabled
- A lot of the mask/editing stuff I haven't used in a while, not sure if it still works
- Things tend to get broken if I haven't used that particular feature in a while.  It's probably the app, not you


# Screenshots

<a href="Media/ai_tools_dungeons_generate.png"><img src="Media/ai_tools_dungeons_generate.png" width="300"></a>
<a href="Media/ait_dungeon_twine_export.png"><img src="Media/ait_dungeon_twine_export.png" width="300"></a>
<a href="Media/ait_twine_stored_html2.png"><img src="Media/ait_twine_stored_html2.png" width="300"></a>
<a href="Media/ait_quiz_kyoto.png"><img src="Media/ait_quiz_kyoto.png" width="300"></a>
<a href="Media/ai_tools_birdy_to_bird.jpg"><img src="Media/ai_tools_birdy_to_bird.jpg" width="300"></a>

# Media (mostly outdated videos of the app) #

<a href="https://www.youtube.com/watch?v=2TB4f8ojKYo"><img align="top" src="Media/apple_youtube_thumbnail.png" width=300></a>
<a href="https://www.youtube.com/watch?v=3PmZ_9QfrE0"><img align="top" src="Media/remove_bg_youtube.png" width=300></a>
<a href="https://www.youtube.com/watch?v=FoYY_90KlyE"><img align="top" src="Media/ai_paintball_youtube.png" width=300></a>
<a href="https://www.youtube.com/watch?v=VKj-x25-04E"><img align="top" src="Media/live_webcam_test.png" width=300></a>
<a href="https://www.youtube.com/watch?v=YQMWflU1v-U"><img align="top" src="Media/aiguide_youtube.png" width=300></a>
# Setup #

Your ComfyUI server needs to be started using the --listen parm, so the API can be accessed.  

After running it, a config.txt will be made. There is a config edit button inside the app, it's kind of self-documented, just go through it and update as necessary.

# Setting up with ComfyUI

First, install [ComfyUI](https://github.com/comfyanonymous/ComfyUI) and get it rendering stuff.

Then install my [Workflow to API Converter Endpoint](https://github.com/SethRobinson/comfyui-workflow-to-api-converter-endpoint) via ComfyUI Manager

This node allows us to only work with normal workflow json files, and never have to export "API" versions.  The API conversions will happen under the hood automatically when needed, makes life much better.  Click its page above for help.

It will work if you don't, but this allows modified or new workflows to work as it can convert them to "API" versions on the fly when needed.

Next, just for a test to make sure the workflows included with AITools are going to work, inside ComfyUI's web GUI, drag in aitools_client/ComfyUI/FullWorkflowVersions/text_to_img_flux.json or any others.  The neat thing about ComfyUI is it will read this and convert it to its visual workflow format, ready to run.  (you might want to change the prompt from <AITOOLS_PROMPT> to something else during testing here) - Click Queue.  Does it work?  Oh, if you see an Image Loader set to the file "<AITOOLS_INPUT_1>" you'll need to change that to a file on your ComfyUI server if you want to test.

You'll probably see a bunch of red nodes and errors - no problem!  Make sure you have ComfyUI-Manager installed, you can use it to install any missing nodes.  You'll probably have to track down various model files though, but at least when you try to render it will shows exactly the filenames that are missing. (look for red boxes around certain nodes)

Adjust it until it works (change paths or models or whatever you need, you could even start with a totally different workflow you found somewhere else), and make sure the prompt is set to <AITOOLS_PROMPT>.  (<AITOOLS_NEGATIVE_PROMPT> can be used if your workflow has a place for that too)  Then do Workflow->Export if you wanted to save your own. (note: we no longer use "API" versions at all, thank god, just normal workflows)

So you don't have to create custom workflows for every checkpoint/filesize etc, you can use AITools' "@replace" to change any part of a workflow before it's sent to ComfyUI's API.  You'll see it used in various presets.

Check discussions for some more info [here](https://github.com/SethRobinson/aitools_client/discussions/18)

# List of keywords that can be used in ComfyUI workflows so AITools can modify things

| Keyword | Description |
|---------|-------------|
| `<AITOOLS_PROMPT>` | Main prompt used to generate whatever (image or movie) |
| `<AITOOLS_NEGATIVE_PROMPT>` | Some image generators use this, but a lot ignore it |
| `<AITOOLS_AUDIO_PROMPT>` | Audio prompt for audio things |
| `<AITOOLS_AUDIO_NEGATIVE_PROMPT>` | What you DON'T want to hear (e.g., to exclude music from audio) |
| `<AITOOLS_SEGMENTATION_PROMPT>` | For SAM3 segmentation - like "head" and the head gets selected |
| `<AITOOLS_INPUT_1>` | For images. Use Load Image (Path) (a V.H.S. node) anywhere a normal image is needed |

# List of Job Script commands

Job scripts are used in presets to chain workflows and manipulate variables. Each line can contain a workflow name followed by commands separated by `@`.  It's kind of rough, but you can call LLMs with text, get something, run workflows, tweak the image and use it on other workflows, etc.

## Basic Syntax

```
workflow_name.json @command|parm1|parm2| @command2|parm1|
```

Example: `img_to_img.json @copy|prompt|segmentation_prompt| @resize_if_larger|x|1024|y|1024|aspect_correct|1|`

## Commands

| Command | Parameters | Description |
|---------|------------|-------------|
| `@copy` | `source\|dest\|` | Copy text/image between variables |
| `@add` | `source\|dest\|` | Append text to a variable |
| `@resize_if_larger` | `x\|width\|y\|height\|aspect_correct\|0 or 1\|` | Resize image only if larger than specified |
| `@resize` | `x\|width\|y\|height\|aspect_correct\|0 or 1\|` | Force resize to specified dimensions |
| `@fill_mask_if_blank` | (none) | Fills the alpha mask if empty |
| `@no_undo` | (none) | Disables undo for this operation |
| `@llm_prompt_reset` | (none) | Reset LLM conversation history |
| `@llm_prompt_set_base_prompt` | `text\|` | Set base system prompt for LLM |
| `@llm_prompt_add_from_user` | `text\|` | Add user message to LLM conversation |
| `@llm_prompt_add_from_assistant` | `text\|` | Add assistant message to LLM conversation |
| `@llm_prompt_pop_first` | (none) | Remove first interaction from history |
| `@llm_prompt_add_to_last_interaction` | `text\|` | Append text to last message |
| `@Comment` | `text\|` | Human-readable comment (ignored by parser) |

## Variable Names (for @copy/@add)

| Variable | Description |
|----------|-------------|
| `prompt` | Current job's main prompt |
| `global_prompt` | Global prompt field in UI |
| `prepend_prompt` | Text prepended to prompts |
| `append_prompt` | Text appended to prompts |
| `negative_prompt` | Negative prompt for generation |
| `audio_prompt` | Audio generation prompt |
| `audio_negative_prompt` | Audio negative prompt |
| `segmentation_prompt` | Prompt for SAM segmentation |
| `llm_prompt` | Prompt sent to LLM |
| `llm_reply` | Response received from LLM |
| `requirements` | Requirements string |
| `image` | Current image (for copying to temp1) |
| `temp1` | Temporary image storage |

## Special Line Prefixes

| Prefix | Description |
|--------|-------------|
| `command` | Execute commands without running a workflow |
| `call_llm` | Call the LLM, response stored in `llm_reply` |
| `img_*` | Workflow that needs the current image uploaded |
| `video_*` or `vid_*` | Workflow that needs the current video uploaded |

## Disabling Commands

Add `-` at the end of a command to skip the next parameter (useful for commenting out):
```
workflow.json -@disabled_command|parm| @enabled_command|parm|
```

# Building from source

You only need to download [the zip](https://www.rtsoft.com/files/SethsAIToolsWindows.zip) and run the .exe to use this, However, the source might be useful to generate a build for other platforms, fork or steal pieces to use for yourself.  Go ahead!

- Requires Unity 6000.3.1f+
- Open the scene "Main" and click play to run
- Assets/GUI/GOTHIC.TFF and Assets/GUI/times.ttf are not included and might break the build because I was having trouble and switched some settings around that might require them now (dynamic vs static TMPro font settings...)

---

Credits and links

- Audio: "Chee Zee Jungle"

Kevin MacLeod (incompetech.com)

Licensed under Creative Commons: By Attribution 3.0

http://creativecommons.org/licenses/by/3.0/

- NotoSansCJKjp-VF font licensed under the Open Font License (OFL)
- Audio: JOHN_MICHEL_CELLO-BACH_AVE_MARIA.mp3 performed by John Michel. Licensed under Creative Commons: By Attribution 3.0

http://creativecommons.org/licenses/by/3.0/

- Written by Seth A. Robinson (seth@rtsoft.com) twitter: @rtsoft - [Codedojo](https://www.codedojo.com), Seth's blog
- Special thanks to the [ComfyUI](https://github.com/comfyanonymous/ComfyUI) and [llama.cpp](https://github.com/ggml-org/llama.cpp) projects