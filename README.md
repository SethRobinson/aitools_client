
# Seth's AI Tools: A Unity based front-end for Stable Diffusion WebUI (and other AI stuff)

License:  BSD style attribution, see LICENSE.md

<p float="left">
<a href="Media/aitools_v050_screenshot2.png"><img align="top" src="Media/aitools_v050_screenshot2.png"></a>
</p>

# Download the latest [AI Tools Client (Windows, 36 MB)](https://www.rtsoft.com/files/SethsAIToolsWindows.zip)

To use this, you'll need at least one Stable Diffusion WebUI server running somewhere. (same machine as the client is ok) This client supports either of the following servers:

## [Seth's AI Tools Server](https://github.com/SethRobinson/aitools_server) (Same as below but with a few extra features, including background removal) ##

 or

##  [AUTOMATIC1111's Stable Diffusion WebUI](https://github.com/AUTOMATIC1111/stable-diffusion-webui) (must run with the --api parm) ##


# Features #

* It's not a web app, it's a native locally run Windows .exe
* Live update integration (image and masking) with Photoshop or other image editors
* text to image, inpainting, image interrogation, face fixing, upscaling, tiled texture generation with preview, alpha mask subject isolation (background removal)
* Drag and drop images in as well as paste images from the windows clipboard
* Pan/zoom with thousands of images on the screen
* Mask painting with controllable brush size
* Can utilize multiple servers (three video cards on one machine? Run three servers!) allowing seamless use of all GPUs for ultra fast generation and a single click to change the active model
* Neat workflow that allows evolving images with loopback while live-selecting the best alternatives to shape the image in real-time
* Open source, uses the Unity game engine and C# to do stuff with AI art
* Privacy respected - does not phone home or collect any statistics, purely local usage. (it does check a single file on github.com to check for newer versions, but that's it)
* Includes "experiments", little built-in games and apps designed to test using AI/SD for specific things: CrazyCam is a realtime webcam filter with 30+ presets, Shooting Gallery tests realtime craetion of sprites during a game, etc

## Current version: **V0.60** (released Feb 9th 2023) Recent changes: ##

*  Added support for Instruct pix2pix style transfer stuff that the latest servers support, the "Image CFG Scale Pix2Pix" slider becomes active if a pix2pix model is detected (detected by finding pix2pix in the filename of the model)  Won't allow normal renders in this mode as they aren't support in auto1111/etc.
* Max CFG Scale now 30, instead of 20 (actually need it this high for some pix2pix stuff)
* FEAT: Added auto new version notification. On startup, it downloads "latest_version_checker.json" directly from github to see if a new version has been released and offers to open the URL to download it (suggested by cooperdk, although I only notify, actually updating is up to the user)
* FEAT: Added "Camera follow" mode, when rendering, the camera will move vertically automatically when a new line is reached, allowing a kind of screensaver mode, for example, zoom out and set 10 images per row for a type writer effect, or zoom way in and set 1 image per 1 row for a single image on screen at once, updating each render.  (note: you can position camera to second to last image so you only see finished images, it's flexible like that)
* Can hide/show the main gui tool panel, useful for screenshots or the Camera Follow feature
* FEAT: Added "Autosave everything" toggle, will write a copy of every image generated to the "autosave" sub directory.  Uses bmp and keeps masks information. (for example, if remove background is used, there will be a mask of the subject that shows up in the alpha channel in photoshop) (suggested by C.Jenkins)
* CrazyCamera now supports three mask modes instead of two: "process everything", "process foreground",  "process backrgound"
* CrazyCamera has new presets for pix2pix models, try "(pix2pix) Snowy", it's different from how normal SD filters work because it doesn't change the existing picture much and can change specific things, like "change hair to bananas" kind of works
* Crazycamera gives (easily missed) warnings when the current SD model is the wrong kind (most need inpainting or pix2pix models to look right)

NOTE:  For pix2pix stuff, you need to add the [7 gb model](https://huggingface.co/timbrooks/instruct-pix2pix/resolve/main/instruct-pix2pix-00-22000.safetensors) to your models/Stable-diffusion folder. Apparently you can use the server web interface to merge models or stuff but I haven't played with that yet.

You only need to download [the zip](https://www.rtsoft.com/files/SethsAIToolsWindows.zip) and run the .exe to use this, However, the source might be useful to generate a build for other platforms, fork or steal pieces to use for yourself.  Go ahead!

# Media (outdated videos of the app) #

<a href="https://www.youtube.com/watch?v=2TB4f8ojKYo"><img align="top" src="Media/apple_youtube_thumbnail.png" width=300></a>
<a href="https://www.youtube.com/watch?v=3PmZ_9QfrE0"><img align="top" src="Media/remove_bg_youtube.png" width=300></a>
<a href="https://www.youtube.com/watch?v=FoYY_90KlyE"><img align="top" src="Media/ai_paintball_youtube.png" width=300></a>
<a href="https://www.youtube.com/watch?v=VKj-x25-04E"><img align="top" src="Media/live_webcam_test.png" width=300></a>

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

#kids around?  Then uncomment below to turn on the NSFW filter. 
#enable_safety_filter

#Set the below path and .exe to an image editor to use the Edit option. Changed files will auto
#update in here.

set_image_editor|C:\Program Files\Adobe\Adobe Photoshop 2023\Photoshop.exe

#set_default_sampler|DDIM
#set_default_steps|50
```

If your Stable Diffusion WebUI server isn't running locally or at port 7860, change the http://localhost:7860 part to where it is.  Add multiple add_server commands for multiple servers.

**NOTE:** Using automatic1111, on the server side, you will see a scary error saying "RuntimeError: File at path D:\pro\stable-diffusion-webui\aitools\get_info.json does not exist.", this is ok, the app checks for the file to see what kind of server it is once at the start.  It doesn't break anything.

# Building from source

* Requires Unity 2022.2+
* Open the scene "Main" and click play to run

---

Credits and links

- Audio: "Chee Zee Jungle"
Kevin MacLeod (incompetech.com)
Licensed under Creative Commons: By Attribution 3.0
http://creativecommons.org/licenses/by/3.0/
- Audio: JOHN_MICHEL_CELLO-BACH_AVE_MARIA.mp3 performed by John Michel. Licensed under Creative Commons: By Attribution 3.0
http://creativecommons.org/licenses/by/3.0/

- Written by Seth A. Robinson (seth@rtsoft.com) twitter: @rtsoft - [Codedojo](https://www.codedojo.com), Seth's blog
- Special thanks to the awesome people working on AUTOMATIC1111's [stable-diffusion-webui](https://github.com/AUTOMATIC1111/stable-diffusion-webui) project