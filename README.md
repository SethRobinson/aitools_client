# Seth's AI Tools: A Unity based front-end for ComfyUI and Ollama (and others) that does things like generate photos and movies, Twine games, quizzes, posters and more.

License:  BSD style attribution, see LICENSE.md

<p float="left">
<a href="Media/aitools_v2_aiguide.png"><img align="top" src="Media/aitools_v2_aiguide.png"></a>
</p>


## Download the latest public version: V2.12 (Dec 12th, 2025) [AI Tools Client (Windows, 62 MB)](https://www.rtsoft.com/files/SethsAIToolsWindows.zip)

Need an old version? The last pre-V2 version (that still supports Auto1111) can be downloaded [here](https://www.rtsoft.com/files/SethsAIToolsWindowsV095.zip).

To use this, you'll need to connect to something that can generate images, and hopefully an LLM too.

## Features

- It's not a web app, it's a native locally run Windows .exe (Unity with C#)
- It's kind of like lego, is can run ComfyUI workflows that chain together.  You need to understand ComyUI and workflow tweaking as you'll still need to install all the needed custom nodes and models there. (you do have ComfyUI Manager installed, right?!)
- Drag and drop images in as well as paste images from the windows clipboard
- Built-in image processing like cropping and resizing, mask painting
- Pan/zoom with thousands of images on the screen
- Built to utilize many ComfyUI servers at once
- Privacy respected - does not phone home or collect any statistics, purely local usage. (it does check a single file on github.com to check for newer versions, but that's it)
- Includes "experiments", little built-in games and apps designed to test AI
- AI Guide feature harnesses the power of AI to create motivational posters, illustrated stories or whatever
- Adventure mode has presets to various modes - generate ready to upload illustrated web quiz from prompt, simple Twine game project from a prompt, and "Adventure", a sort of illustrated AI Dungeon type of toy
- Includes presets and workflows I currently find useful -  I keep delete the old outdated ones, but you can always make your own
- By default strips<think> tags when continuing LLM work for Deepseek/thinking models


## Recent changes

* A lot of misc improvements, the biggest one being we only work with full workflows now, no more needing API versions
* Improved llama.cpp support

## Known issues

- Lack of documentation etc due to laziness
- CrazyCam (where the webcam is being used for realtime image processing) is broken, it actually runs a halloween photobooth thing currently, sorry
- Things tend to get broken if I haven't used that particular feature in a while.  It's probably the app, not you


You only need to download [the zip](https://www.rtsoft.com/files/SethsAIToolsWindows.zip) and run the .exe to use this, However, the source might be useful to generate a build for other platforms, fork or steal pieces to use for yourself.  Go ahead!

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

After running it, a config.txt will be made. There is an config edit button inside the app, it's kind of self-documented, just go through it and update as neccesary.

# Setting up with ComfyUI

First, install [ComfyUI](https://github.com/comfyanonymous/ComfyUI) and get it rendering stuff.

# Install my [Workflow to API Converter Endpoint](https://github.com/SethRobinson/comfyui-workflow-to-api-converter-endpoint) via ComfyUI Manager

This node allows us to only work with normal workflow json files, and never have to export "API" versions.  The API conversions will happen under the hood automatically when needed, makes life much better.

Next, just for a test to make sure the workflows included with AITools are going to work, inside ComfyUI's web GUI, drag in aitools_client/ComfyUI/FullWorkflowVersions/text_to_img_flux.json or any others.  The neat thing about ComfyUI is it will read this and convert it to its visual workflow format, ready to run.  (you might want to change the prompt from <AITOOLS_PROMPT> to something else during testing here) - Click Queue.  Does it work?  Oh, if you see an Image Loader set to the file "<AITOOLS_INPUT_1>" you'll need to change that to a file on your ComfyUI server if you want to test.

You'll probably see a bunch of red nodes and errors - no problem!  Make sure you have ComfyUI-Manager installed, you can use it to install any missing nodes.  You'll probably have to track down various model files though, but at least when you try to render it will shows exactly the filenames that are missing. (look for red boxes around certain nodes)

Adjust it until it works (change paths or models or whatever you need, you could even start with a totally different workflow you found somewhere else), and make sure the prompt is set to <AITOOLS_PROMPT>.  (<AITOOLS_NEGATIVE_PROMPT> can be used if your workflow has a place for that too)  Then do Workflow->Export if you wanted to save your own. (note: we no longer use "API" versions at all, thank god, just normal workflows)

So you don't have to create custom workflows for every checkpoint/filesize etc, you can use AITools' "@replace" to change any part of a workflow before it's sent to ComfyUI's API.  You'll see it used in various presets.

Check discussions for some more info [here](https://github.com/SethRobinson/aitools_client/discussions/18)

# Building from source

- Requires Unity 6+
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
- Special thanks to the awesome people working on AUTOMATIC1111's [stable-diffusion-webui](https://github.com/AUTOMATIC1111/stable-diffusion-webui) project
