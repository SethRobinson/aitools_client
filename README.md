
# Seth's AI Tools: A Unity based front-end for Stable Diffusion WebUI (and other AI stuff)

License:  BSD style attribution, see LICENSE.md

<p float="left">
<a href="Media/aitools_v050_screenshot2.png"><img align="top" src="Media/aitools_v050_screenshot2.png"></a>
</p>

# Download the latest [AI Tools Client (Windows, 36 MB)](https://www.rtsoft.com/files/SethsAIToolsWindows.zip)

To use this, you'll need at least one Stable Diffusion WebUI server running somewhere (it can run on Windows or linux, the same machine as the client is ok) This client supports either of the following servers:

## [Seth's AI Tools Server](https://github.com/SethRobinson/aitools_server) (Same as below but with a few extra features, including background removal) ##

 or

##  [AUTOMATIC1111's Stable Diffusion WebUI](https://github.com/AUTOMATIC1111/stable-diffusion-webui) (must run with the --api parm) ##


# Features #

* It's not a web app, it's a native locally run Windows .exe
* Live update integration (image and masking) with Photoshop or other image editors
* text to image, img2img, image interrogation, face fixing, upscaling, tiled texture generation with preview, automasking of subject, background removal
* Drag and drop images in as well as paste images from the windows clipboard
* Built-in image processing like cropping and resizing
* Pan/zoom with thousands of images on the screen
* Mask painting with controllable brush size
* Option to automatically save all generations, with accompanying .txt file that includes all settings
* Supports pix2pix and ControlNet (For ControlNet, the server requires [Mikubill's sd-webui-controlnet extension](https://github.com/Mikubill/sd-webui-controlnet) and its models to be installed, otherwise the option is grayed out
* Can utilize multiple servers (three video cards on one machine? Run three servers!) allowing seamless use of all GPUs for ultra fast generation and a single click to change the active model
* Neat workflow that allows evolving images with loopback while live-selecting the best alternatives to shape the image in real-time
* Open source, uses the Unity game engine and C# to do stuff with AI art
* Privacy respected - does not phone home or collect any statistics, purely local usage. (it does check a single file on github.com to check for newer versions, but that's it)
* Includes "experiments", little built-in games and apps designed to test using AI/SD for specific things: CrazyCam is a realtime webcam filter with 30+ presets, Shooting Gallery tests realtime craetion of sprites during a game, etc


## Current version: **V0.70** (released March 1st 2023) Recent changes: ##

**What's new in V0.70**:
* Can zoom in farther
* BUGFIX: Negative prompt is no longer ignored until the default text is edited
* BUGFIX: Fixed misc glitches with masks and issues when images change size
* BUGFIX: Live external edits with alpha channels no longer also cause those parts of the image to disappear visually, that wasn't supposed to happen, it's supposed to just move it to the active mask
* BUGFIX: Fixed huge texture memory leaks, oops
* NEW: New navmenu system for image options, easier to add more stuff now. It's a custom thing I wrote so of course it's a bit jank
* NEW: Added experimental ControlNet support (requires that your server has the https://github.com/Mikubill/sd-webui-controlnet extension + its models installed!  note that if its API changes, this will break it, but it shouldn't cause the rest of the program to break)
* NEW:  Added resize and crop to rect on images, including with aspect correction
* NEW: New "Info panel" feature, opens when you click the "?" button. Will show lots of data (sizes, settings like prompt, seed, etc).  Text can be copied. This area will also show the ControlNet image that was generated if applicable.
* "Inpaint" renamed img2img in a few places, I figured it's a better generic term for when an image is modified in a process. This area will allow show the support images created with ControlNet, allowing you to save them out manually
* Added a "restart the last img2img batch mode" button to the main img2img panel, useful for my workflow because I don't want to scroll up to find the "!" button on the last image I was using
* Moved some things around in the RT directory to organize things a bit better
* Tooltips now wait 0.5 seconds before displaying
* When saving images, a <filename>.txt file is now also created which contains prompt/seed/etc info about the render
* If you hold down Shift while clicking "Clear pics" it will delete ALL of them, including locked
* Mask operations like move/adjust/add/subject selection are much faster

NOTE:  For pix2pix stuff, you need to add the [7 gb model](https://huggingface.co/timbrooks/instruct-pix2pix/resolve/main/instruct-pix2pix-00-22000.safetensors) to your models/Stable-diffusion folder

For ControlNet options, you need to install [Mikubill's sd-webui-controlnet extension](https://github.com/Mikubill/sd-webui-controlnet) and at least one of [its models](https://huggingface.co/lllyasviel/ControlNet/tree/main/models).  You should make sure it's working from the webUI interface on the server first.  You must run with the parm "--xformers", without it, the extension will crash when the API asks for option data and AI Tools can't connect at all.

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