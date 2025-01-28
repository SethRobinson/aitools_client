
# Seth's AI Tools: A Unity based front-end that talks to various AI APIs to do experimental things like generate Twine games, quizzes, posters and more.

License:  BSD style attribution, see LICENSE.md

<p float="left">
<a href="Media/ai_tools_ai_guide.jpg"><img align="top" src="Media/ai_tools_ai_guide.jpg"></a>
</p>

# Download the latest [AI Tools Client (Windows, 62 MB)](https://www.rtsoft.com/files/SethsAIToolsWindows.zip)

To use this, you'll need to connect to something that can generate images, and hopefully an LLM too.  A single OpenAI key is enough to do a lot (LLM and Dale3 rendering), you can also use Claude (via API key), or your own local servers.  You can mix and match by connecting to local or remote ComfyUI servers for rendering as well as Ollama, Text Generation WebUI and TabbyAPI servers for LLMs.

Note:  A1111 as well as [Seth's modified version](https://github.com/SethRobinson/aitools_server) are being phased out in favor of just focusing on ComfyUI as the rendering backend due to its power and flexibility.  They still work for now though.

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
* Experimental video support (Includes ComfyUI workflows that can generate videos via Hunyuan or LTX.  Still basic support, but we can view them just like the images)


## Current version: **V0.96** (released Jan 28th 2025) ##

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

V0.95: (Jan 13th, 2025)

* Better error reporting with LLMs
* Console scrollbar works better
* Added support for Ollama server (Added "add_generic_llm_parm" command to config.txt, so add_generic_llm_parm|model|"llama3.3"| can be added so it works with Ollama servers (they need to know which model to use).  This would also allow you to add other custom openai api style parms if needed).
Also, the default config sets parms for context.  I also added "add_generic_llm_parm|num_ctx|131072|" and "add_generic_llm_parm|max_tokens|4096".  You can add more as needed, but those make ollama work great with llama3.3.  Keep in mind non instruct (or chat-instruct) models like mixtral won't work well.
* Config.txt now sets default parms before every load, previously, if you removed for example, a command to set the llm api key, it would still be in memory until the next restart instead of being set to "none" by default
* BUGFIX: Fixed issue where a textbox could have text overwriting itself in a buggy way
* BUGFIX: Fixed issue where Dalle3 could be used even if "Local only" renderers were selected, during the time config was re-initted and there were no local renderers with tasks waiting to render
* BUGFIX: Special characters in the prompt (including ") no longer break the render on ComfyUI, oops
* Some ai guide/adventure profiles have dropped the "simple description", I'm not really planning to supporting A1111 anymore, I think ComfyUI only is the future for the visual rendering.  Some features like AI Camera and the paintball demo still require it as I haven't created Comfy workflows (and may never) as this entire project just kind of gets updated to work with what I want to play with at the time
* Added some new ComfyUI workflows that use WaveSpeed (it's fast), you'll need the Comfy-WaveSpeed node installed to use 'em
* Fixed Japanese fonts (any special fonts need to be added as fallback fonts to the default ones, that had gotten removed when I updated to Unity 6 I guess)
* Added a folder icon by the ComfyUI workflow selector, it just opens file explorer into that dir, makes it easier to hand edit or add a new workflow by cut and pasting an old one
* Added a "Rescan ComfyUI workflow folder" button to the ComfyUI settings dialog, so you don't have to restart the .exe after adding a new workflow
* Added support for ComfyUI negative prompts, any workflow that has <AITOOLS_NEGATIVE_PROMPT> in it will be set the current active negative prompt.  However, none of my Flux/Hunyuan prompt workflows even have a place to put in a negative prompt, although I know it is possible (kind of a hack though?) so if you find a workflow that does have one, you can just replace it with <AITOOLS_NEGATIVE_PROMPT>  and it should work. I did add it to the ltx video one though.
* Server/GPUs now have a checkbox so you can disable one temporarily if need be, old method was having to edit the config.txt and apply which would stop renders in progress.  Server properties dialog now shows its remote URL as well

V0.96: (Jan 13th, 2025)

* BUGFIX: Setting the "con_txt" context length as shown in the config.txt actually works for Ollama now
* AI Guide images created before a renderer is available now properly queue up instead of just being weird black images that can never be rendered.  So if you were limited on VRAM you could create a bunch of things with the LLM only, then load the ComfyUI renderer later to render them (apply the config again and it will detect it without restarting the app)
* Some minor tweaks to comfy and adventure profiles

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

set_image_editor|C:\Program Files\Adobe\Adobe Photoshop 2025\Photoshop.exe

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

#what we tell the model to use. If you notice the llm is forgetting things or messing up, your model might not be an instruct-compatible model, try llama 3.3 with Ollama as a test.
set_generic_llm_mode|chat-instruct|

#this is needed if using an ollama server, otherwise you'll see a "model is required" error.  Note that might cause the model to be loaded which means a huge delay at first.
add_generic_llm_parm|model|"llama3.3"|
add_generic_llm_parm|num_ctx|131072|#needed for ollama, the context the model supports
add_generic_llm_parm|max_tokens|4096|

#somethings you could play with
#add_generic_llm_parm|stop|["<`eot_id`>", "<`eom_id`>", "<`end_header_id`>"]|#Note that ` gets turned into |
#add_generic_llm_parm|stopping_strings|["<`eot_id`>", "<`eom_id`>", "<`end_header_id`>"]|

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

**Don't move on until it's working and you can generate images and/or videos in ComfyUI directly**! (without AITools)

Next, just for a test to make sure the workflows included with AITools are going to work, inside ComfyUI's web GUI, drag in aitools_client/ComfyUI/flux_api.json or maybe video_hunyuan_t2v_480_api.json.  The neat thing about ComfyUI is it will read this and convert it to its visual workflow format, ready to run.  (you might want to change the prompt from <AITOOLS_PROMPT> to something else during testing here) - Click Queue.  Does it work? If so, you're done, just go run AITools, it should work. 

Make sure you have ComfyUI-Manager installed, you can use it to install any missing nodes.  You'll probably have to track down various model files though, but at least when you try to render it will shows exactly the filenames that are missing. (look for red boxes around certain nodes)

Adjust it until it works (change paths or models or whatever you need, you could even start with a totally different workflow you found somewhere else), and make sure the prompt is set to <AITOOLS_PROMPT>.  (<AITOOLS_NEGATIVE_PROMPT> can be used if your workflow has a place for that too)  Then do Workflow->Export (API).

You can now add/overwrite that in your aitools_client/ComfyUI dir.  Inside of AITools, select that workflow under the ComfyUI workflows dropdown list.  It should now render right!  Images/videos are auto detected when being displayed.  AITools is will dynamically modify the workflow when using it by changing <AITOOLS_PROMPT>, and also the total length of frames if applicable (for videos).  Not much else right now, so to even change the resolution you need to modify the workflow as described.  (or faster, just use a text editor on the .json directly)

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
