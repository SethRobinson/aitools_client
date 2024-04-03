
# Seth's AI Tools: A Unity based front-end for Stable Diffusion WebUI, Textgen WebUI and OpenAI - allows LLM based stories, image editing, fun experiments like a paintball game with dynamically created enemies

License:  BSD style attribution, see LICENSE.md

<p float="left">
<a href="Media/ai_tools_ai_guide.jpg"><img align="top" src="Media/ai_tools_ai_guide.jpg"></a>
</p>

# Download the latest [AI Tools Client (Windows, 56 MB)](https://www.rtsoft.com/files/SethsAIToolsWindows.zip)

To use this, you'll need at least one Stable Diffusion WebUI server running somewhere (it can run on Windows or linux, the same machine as the client is ok) This client supports either of the following servers:

## [Seth's AI Tools Server](https://github.com/SethRobinson/aitools_server) (Same as below but with a few extra features, including background removal) ##

 or

##  [AUTOMATIC1111's Stable Diffusion WebUI](https://github.com/AUTOMATIC1111/stable-diffusion-webui) (must run with the --api parm) ##

or

## For the AI Guide feature, you can use your OpenAI api key to use GPT4 and Dalle 3 and not need a local server at all ##

# Features #

* It's not a web app, it's a native locally run Windows .exe
* Live update integration (image and masking) with Photoshop or other image editors
* text to image, img2img, image interrogation, face fixing, upscaling, tiled texture generation with preview, automasking of subject, background removal
* Drag and drop images in as well as paste images from the windows clipboard
* Built-in image processing like cropping and resizing
* Pan/zoom with thousands of images on the screen
* Mask painting with controllable brush size
* Option to automatically save all generations, with accompanying .txt file that includes all settings
* Supports pix2pix and ControlNet (For ControlNet, the server requires [Mikubill's sd-webui-controlnet extension](https://github.com/Mikubill/sd-webui-controlnet)) and its models to be installed, otherwise the option is grayed out
* Can utilize multiple servers (three video cards on one machine? Run three servers!) allowing seamless use of all GPUs for ultra fast generation and a single click to change the active model
* Neat workflow that allows evolving images with loopback while live-selecting the best alternatives to shape the image in real-time
* Open source, uses the Unity game engine and C# to do stuff with AI art
* Privacy respected - does not phone home or collect any statistics, purely local usage. (it does check a single file on github.com to check for newer versions, but that's it)
* Includes "experiments", little built-in games and apps designed to test using AI/SD for specific things: CrazyCam is a realtime webcam filter with 30+ presets, Shooting Gallery tests realtime craetion of sprites during a game, etc
* AI Guide feature harnesses the power of GPT-4 or open source LLMs (via [Text generation web UI](https://github.com/oobabooga/text-generation-webui)) to create motivational posters, illustrated stories or whatever, with presets and web-slideshower viewer. Comes with presets like [Pixel Art Gaming Lies](https://www.rtsoft.com/ai/lies/) and [Random Story That Teaches Japanese](https://www.rtsoft.com/ai/jtest/)
* Can optionally use Dalle 3 for rendering with AI Guide


## Current version: **V0.80** (released April 3rd 2024) ##

**Recent changes**:

* AI Guide now supports "streaming" from OpenAI or Text generation web UI and allows rendering stories and posters while the generation is happening
* AI Guide now supports three modes: GPT-4 (OpenAI), Raw Completion (Texgen WebUI), and Instruct (Texgen WebUI) mode.
* AI Guide examples tweaked and improved, the render processer is much more forgiving of misformatted data.  I don't recommend using the JSON style ones as they don't support live-rendering and are more error prone.
* Some defaults tweaked to get with the times, like DPM++ SDE Karras is the default sampler now.

BTW, local LLMs have come a long way and they work GREAT now.  It's amazing that you can have unlimited stories and posters/etc. designed & rendered by AI in real-time using only computers in your own house.  The future is now!  I've done most of my LLM tests with 70B parm models but smaller ones will probably work ok too.


NOTE:  For pix2pix stuff, you need to add the [7 gb model](https://huggingface.co/timbrooks/instruct-pix2pix/resolve/main/instruct-pix2pix-00-22000.safetensors) to your models/Stable-diffusion folder

For ControlNet options, you need to install [Mikubill's sd-webui-controlnet extension](https://github.com/Mikubill/sd-webui-controlnet) and at least one of [its models](https://huggingface.co/lllyasviel/ControlNet/tree/main/models).  You should make sure it's working from the webUI interface on the server first.  You must run the server with the parm "--xformers", this is required by the extension.  Also, if you see "Protocol errors" anywhere, remove the Dreambooth extension, lately it's been causing API issues.  (as of March 2nd/2023 at least)

You only need to download [the zip](https://www.rtsoft.com/files/SethsAIToolsWindows.zip) and run the .exe to use this, However, the source might be useful to generate a build for other platforms, fork or steal pieces to use for yourself.  Go ahead!

# Media (outdated videos of the app) #

<a href="https://www.youtube.com/watch?v=2TB4f8ojKYo"><img align="top" src="Media/apple_youtube_thumbnail.png" width=300></a>
<a href="https://www.youtube.com/watch?v=3PmZ_9QfrE0"><img align="top" src="Media/remove_bg_youtube.png" width=300></a>
<a href="https://www.youtube.com/watch?v=FoYY_90KlyE"><img align="top" src="Media/ai_paintball_youtube.png" width=300></a>
<a href="https://www.youtube.com/watch?v=VKj-x25-04E"><img align="top" src="Media/live_webcam_test.png" width=300></a>
<a href="https://www.youtube.com/watch?v=YQMWflU1v-U"><img align="top" src="Media/aiguide_youtube.png" width=300></a>

# Setup #

If using AUTOMATIC1111's Stable Diffusion WebUI, make sure it has been started with the --api parm.  (additionally, with the --listen parm if it isn't on the local machine)

On Windows, an easy way to do that is to edit webui-user.bat and add them after the "set COMMANDLINE_ARGS=" part.  Start the server by double clicking webui-user.bat.

Next run aitools_client.exe.  Click on the "Configuration" button and a text editor will open with the default settings:

```bash
#add as many add_server commands as you want, just replace the localhost:7860 part with the
#server name/ip and port.  You can control any number of servers at the same time.

#You need at least one server running to work. It can be either an automatic1111 Stable Diffusion WebUI server or
#a Seth's AI Tools server which supports a few more features.  It will autodetect which kind it is.

add_server|http://localhost:7860

#Set the below path and .exe to an image editor to use the Edit option. Changed files will auto
#update in here.

set_image_editor|C:\Program Files\Adobe\Adobe Photoshop 2023\Photoshop.exe

#set_default_sampler|DDIM
#set_default_steps|50

#To generate text with the AI Guide features, you need to set your OpenAI GPT4 key and/or a Text Generation web IO API url (presumably your own local server).

set_openai_gpt4_key|<key goes here>|
set_openai_gpt4_endpoint|https://api.openai.com/v1/chat/completions|
set_openai_gpt4_model|gpt-4

set_texgen_webui_address|localhost:5000|
```

If your Stable Diffusion WebUI server isn't running locally or at port 7860, change the http://localhost:7860 part to where it is.  Add multiple add_server commands for multiple servers.

**NOTE:** Using automatic1111, on the server side, you will see a scary error saying "RuntimeError: File at path D:\pro\stable-diffusion-webui\aitools\get_info.json does not exist.", this is ok, the app checks for the file to see what kind of server it is once at the start.  It doesn't break anything.

# Building from source

* Requires Unity 2022.3.22+
* Open the scene "Main" and click play to run
* Assets/GUI/GOTHIC.TFF and Assets/GUI/times.ttf are not included and might break the build because I was having trouble and switched some settings around that might require them now (dynamic vs static TMPro font settings...)

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