
# Seth's AI Tools: A Unity based front-end that talks to various AI APIs to do experimental things like generate Twine games, quizzes, posters and more.

License:  BSD style attribution, see LICENSE.md

<p float="left">
<a href="Media/ai_tools_ai_guide.jpg"><img align="top" src="Media/ai_tools_ai_guide.jpg"></a>
</p>

# Download the latest [AI Tools Client (Windows, 62 MB)](https://www.rtsoft.com/files/SethsAIToolsWindows.zip)

To use this, you'll need to connect to something that can generate images, and hopefully an LLM too.  A single OpenAI key is enough to do a lot, you can also mix and match by connecting to local or remote A1111 and ComfyUI servers as well as Text Generation WebUI and TabbyAPI servers for LLMs.

Note:  Instead of A1111, you can use [Seth's modified version](https://github.com/SethRobinson/aitools_server) that has a few special features for use with this (like background removal which is used in the Paintball game test).

https://github.com/user-attachments/assets/a4d0f2db-79f6-46f1-8229-28e93e8053bc

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
* Privacy respected - does not phone home or collect any statistics, purely local usage. (it does check a single file on github.com to check for newer versions, but that's it)
* Includes "experiments", little built-in games and apps designed to test using AI/SD for specific things: CrazyCam is a realtime webcam filter with 30+ presets, Shooting Gallery tests realtime craetion of sprites during a game, etc
* AI Guide feature harnesses the power of AI to create motivational posters, illustrated stories or whatever
* Adventure mode has presets to various modes - generate ready to upload illustrated web quiz from prompt, simple Twine game project from a prompt, and "Adventure", a sort of illustrated AI Dungeon type of toy
* Experimental video support (ComfyUI can generate videos via Hunyuan or LTX.  Still basic support, but we can view them just like the images)


## Current version: **V0.94** (released Jan 4th 2025) ##

**Recent changes**:

* FEAT: early support added for AI generated video! (well, mp4s created in ComfyUI with Hunyuan and LTX)  While you can see them in the app, and click S button to save them in output, not much else is supported (for example, the AI Guide and Adventure stuff works with them, but exporting for web doesn't work right for them) In the future, you'll probably be able to to image to video or video to video, but right now none of the image controls do anything to a video
* Time taken now shows minutes and seconds instead of just seconds
* Integration with ComfyUI improved, now shows progress of steps and renders will properly cancel themselves if their pic window is closed (that started to matter when you're talking about a ten minute video renders...)
* Added set_generic_llm_system_keyword (and assistant & user) in Config.txt, if the LLM you're using doesn't work right, you can try changing the keywords, different llms are trained to work with different words
* Some misc changes to improve LLM compatibility (some have rules like you can't have a user prompt before an assistant prompt, ect)
* ComfyUI workflow feature: If <AITOOLS_PROMPT> is found anywhere in the workflow, it is replaced with the prompt.  All included ComfyUI workflows have been updated to use this format
* There is a new ComfyUI gear/settings button, for now it just lets you set the frames to render (only will apply when movies workflows are used)
* Added "Stop after <num>" control to both AIGuide and Adventure mode, so you can say, do 100 (or however many) llm generations (and all associated pics/movies) and have it shut itself off automatically
* I no longer include the non "API" version ComfyUI workflows, ComfyUI can now load the API versions directly just fine (drag and drop into ComfyUI) so keeping the non-api version around isn't needed
* Updated to Unity 6, I did this because I thought I needed to for wepb support, but I ended up not using it and forgot I updated, now I'm sort of stuck because I don't want to redo the GUI additions I added.  Unity 6 sort of sucks because the licensing changed, but for free stuff like this it doesn't matter I guess
* Added some a few more ComfyUI workflows, AIGuide templates, Adventure templates.  I haven't used A1111 in months so I'm not even sure if the A1111 integration still works (probably does tho).  I kind of just add/work on whatever I need at the time, sorry!

V0.92:
* Fixed a bug with certain GUI settings windows not working sometimes
* Fixed Hunyuan workflow, it was giving ComfyUI errors because an update from a few days ago added required parms
* Each server now has a little settings button that allows you to override which ComfyUI workflow that specific server will use

V0.93:
* Fixed compatibility with text-generation-webui's API, forgot I broke it a while back, oops

V0.94:

* Video memory management implemented, will unload videos that aren't on the screen if needed and load them when they are
* Fixed issue where some video aspect ratios wouldn't be correct when displayed
* Updated hunyuan workflows (as if anyone can keep up with how often these things change) and added a new one for "fast".  (drag into comfy and do install missing nodes, then try to run them to see what models they need)
* Adventure mode "Export" options for the quiz and twine game now work with video too
* AIGuide response text scroll bar works better while it's streaming llm data in
* BUGFIX: "?" Info panel now scrolls with auto-selecting its text
* BUGFIX: Temperature setting in Adventure settings .txt files actually has an effect on the llm now
* The log.txt file is now written in the same dir as the .exe, previously it was in some hard to find spot in /users/<username> etc.

You only need to download [the zip](https://www.rtsoft.com/files/SethsAIToolsWindows.zip) and run the .exe to use this, However, the source might be useful to generate a build for other platforms, fork or steal pieces to use for yourself.  Go ahead!
# Screenshots

<a href="Media/ai_tools_dungeons_generate.png"><img src="Media/ai_tools_dungeons_generate.png" width="300"></a>
<a href="Media/ait_dungeon_twine_export.png"><img src="Media/ait_dungeon_twine_export.png" width="300"></a>
<a href="Media/ait_twine_stored_html2.png"><img src="Media/ait_twine_stored_html2.png" width="300"></a>
<a href="Media/ait_quiz_kyoto.png"><img src="Media/ait_quiz_kyoto.png" width="300"></a>
<a href="Media/ai_tools_birdy_to_bird.jpg"><img src="Media/ai_tools_birdy_to_bird.jpg" width="300"></a>

# Media (mostlyoutdated videos of the app) #

<a href="https://www.youtube.com/watch?v=2TB4f8ojKYo"><img align="top" src="Media/apple_youtube_thumbnail.png" width=300></a>
<a href="https://www.youtube.com/watch?v=3PmZ_9QfrE0"><img align="top" src="Media/remove_bg_youtube.png" width=300></a>
<a href="https://www.youtube.com/watch?v=FoYY_90KlyE"><img align="top" src="Media/ai_paintball_youtube.png" width=300></a>
<a href="https://www.youtube.com/watch?v=VKj-x25-04E"><img align="top" src="Media/live_webcam_test.png" width=300></a>
<a href="https://www.youtube.com/watch?v=YQMWflU1v-U"><img align="top" src="Media/aiguide_youtube.png" width=300></a>
<a href="https://www.youtube.com/watch?v=YQMWflU1v-U"><img align="top" src="Media/aiguide_youtube.png" width=300></a>
# Setup #

If using AUTOMATIC1111's Stable Diffusion WebUI, make sure it has been started with the --api parm.  (additionally, with the --listen parm if it isn't on the local machine)

On Windows, an easy way to do that is to edit webui-user.bat and add them after the "set COMMANDLINE_ARGS=" part.  Start the server by double clicking webui-user.bat.

Next run aitools_client.exe.  Click on the "Configuration" button and a text editor will open with the default settings:

```bash
#add as many add_server commands as you want, just replace the localhost:7860 part with the
#server name/ip and port.  You can control any number of renderer servers at the same time.

#Supported server types:  Seth's AI Tools, A1111, ComfyUI supported.  For Dalle-3, don't set here, just enter your OpenAI key below.

#Uncomment below and put your renderer server.  Add more add_server commands to add as many as you want.
#add_server|http://localhost:7860

#Set the below path and .exe to an image editor to use the Edit option. Changed files will auto
#update in here.

set_image_editor|C:\Program Files\Adobe\Adobe Photoshop 2024\Photoshop.exe

#set_default_sampler|DDIM
#set_default_steps|50

#To generate text with the AI Guide features, you need at least one LLM. (or all, you can switch between them in the app)

#OPENAI (works for LLM and Dalle-3 as renderer)
set_openai_gpt4_key|<key goes here>|
set_openai_gpt4_model|gpt-4o|
set_openai_gpt4_endpoint|https://api.openai.com/v1/chat/completions|

#address of your generic LLM to use, can be local, on your LAN, remote, etc (text-generation-webui or TabbyAPI API format)
set_generic_llm_address|localhost:5000|
#if your generic LLM needs a key, enter it here (or leave as "none")
set_generic_llm_api_key|none|

#the following allow you to override the default system, assistant, and user keywords for the generic LLM, if needed.  
#different LLMs are trained on different words, if the llm server you use doesn't hide this from you, you might notice weird
#or buggy behavior if these aren't changed to match what that specific llm wants
#set_generic_llm_system_keyword|system|#default is system
#set_generic_llm_assistant_keyword|assistant|#default is assistant
#set_generic_llm_user_keyword|user|#default is user
            
#Anthropic LLM
set_anthropic_ai_key|<key goes here>|
set_anthropic_ai_model|claude-3-5-sonnet-latest|
set_anthropic_ai_endpoint|https://api.anthropic.com/v1/messages|
```

# Setting up with ComfyUI (for FLUX images, Hunyuan video or any custom workflow)

First, install [ComfyUI](https://github.com/comfyanonymous/ComfyUI) and get it rendering stuff in Flux and/or Hunyuan using tutorials out there.

Actually, maybe ignore the link above as you probably want something will run on your 3090 or 4090 (I guess there are ways to get going on weaker cards too but..) I used these [download links/workflow](https://www.cognibuild.ai/hunyuan-gguf-necessary-models) for gguf, there is a [tutorial video](https://www.youtube.com/watch?v=CZKZIPGef6s) to go with it but I didn't really use that as I installed to a linux server.

Don't move on until it's working natively and you can **generate videos in ComfyUI directly**! (without AITools)

Next, just for a test to make sure it's going to work, inside ComfyUI's web GUI, drag in aitools_client/ComfyUI/flux_api.json or maybe video_hunyuan_t2v_480_api.json.  The neat thing about ComfyUI is it will read this and convert it to its visual workflow format, ready to run.  (you might want to change the prompt from <AITOOLS_PROMPT> to something else during testing here) - Click Queue.  Does it work? If so, you're done, just go run AITools, it should work. If not, adjust it until it does (change paths or models or whatever you need, you could even start with a totally different workflow you found somewhere else), then change the prompt back to <AITOOLS_PROMPT>.  Then do Workflow->Export (API).  

You can now add/overwrite that in your aitools_client dir.  Inside of AITools, select that workflow under the ComfyUI workflows dropdown list.  It should now render right!  Images/videos are auto detected when being displayed.  AITools is will dynamically modify the workflow when using it by changing <AITOOLS_PROMPT>, and also the total length of frames if applicable (for videos).  Not much else right now, so to even change the resolution you need to modify the workflow as described.  (or faster, just use a text editor on the .json directly)

Check discussions for some more info [here](https://github.com/SethRobinson/aitools_client/discussions/18)

# Building from source

* Requires Unity 6+
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
