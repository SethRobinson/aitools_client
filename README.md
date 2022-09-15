
# Seth's AI Tools: An interactive Unity3D based Stable Diffusion front-end and testbed

License:  BSD style attribution, see LICENSE.md

There are two github respositories, one for the client, one for the server.

aitools_server (lightweight server using uvicorn/fastapi, can run under ubuntu, wsl, google colab, etc)

aitools_client (Unity3D-based client, requires at least one aitools_server running to do GPU work)

(docs coming)
   
---

Credits and links

- Written by Seth A. Robinson (seth@rtsoft.com) twitter: @rtsoft - [Codedojo](https://www.codedojo.com), Seth's blog
- Uses [Stable Diffusion](https://github.com/CompVis/stable-diffusion) via [huggingface's diffusers](https://github.com/huggingface/diffusers).
- Uses [GFPGAN](https://github.com/TencentARC/GFPGAN) and [Real-ESRGAN](https://github.com/xinntao/Real-ESRGAN)
