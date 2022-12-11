
# Seth's AI Tools: A Unity based front-end for Stable Diffusion WebUI (and other AI stuff)

License:  BSD style attribution, see LICENSE.md

# Download the [AI Tools Client (Windows, 36 MB)](https://www.rtsoft.com/files/SethsAIToolsWindows.zip)

To use this, you need to be running AUTOMATIC1111's [stable-diffusion-webui](https://github.com/AUTOMATIC1111/stable-diffusion-webui) server(s) or my own tweaked [version of it](https://github.com/SethRobinson/aitools_server) which has a few extra features. (background removal and some other things that gamedevelopers might be more interested in)

<p float="left">
<a href="Media/aitools_v050_screenshot2.png"><img align="top" src="media/aitools_v050_screenshot2.png"></a>
</p>

Current version: **V0.50** (released Dec 12th 2022)

# Features #

* It's not a web app, it's a native locally run Windows .exe
* Live update integration (image and masking) with Photoshop or other image editors
* text to image, inpainting, image interrogation, face fixing, upscaling, tiled texture generation with preview, alpha mask subject isolation (background removal)
* Drag and drop images in as well as paste images from the windows clipboard
* Pan/zoom with thousands of images on the screen
* Mask painting with controllable brush size
* Can utilize multiple servers (three video cards on one machine? Run three servers!) allowing seamless use of all remote GPUs for ultra fast generation and a single click to change the active model
* Neat workflow that allows evolving images with loopback while live-selecting the best alteratives to shape the image in real-time
* Open source, uses the Unity game engine and C# to do stuff with AI art
* Privacy respected - does not phone home or collect any statistics, purely local usage

# Recent changes #

* Now also compatible with AUTOMATIC1111's [stable-diffusion-webui](https://github.com/AUTOMATIC1111/stable-diffusion-webui) server
* Models and samplers are now queried and populated directly from your AI server(s)
* Can now change model with a dropdown box, all connected servers will switch over
* New display of each server's status, clicking it brings up the standard Web UI
* Improved support for SD 2.1

You only need to download [the zip](https://www.rtsoft.com/files/SethsAIToolsWindows.zip) and run the .exe to use this, However, the source might be useful to generate a build for other platforms, fork or steal pieces to use for yourself.  Go ahead!

# Examples #

<a href="https://www.youtube.com/watch?v=2TB4f8ojKYo"><img align="top" src="Media/apple_youtube_thumbnail.png" width=300></a>
<a href="https://www.youtube.com/watch?v=3PmZ_9QfrE0"><img align="top" src="Media/remove_bg_youtube.png" width=300></a>
<a href="https://www.youtube.com/watch?v=FoYY_90KlyE"><img align="top" src="Media/ai_paintball_youtube.png" width=300></a>
# Setup #

If using AUTOMATIC1111's Stable Diffusion WebUI, make sure it has been started with the --api parm.  (additionally, with the --listen parm if it isn't on the local machine)

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