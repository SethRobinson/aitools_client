/* LICENSE
Copyright (c) 2019 Adrian Babilinski

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
*/

// Texture2D.Resize - https://docs.unity3d.com/ScriptReference/Texture2D.Resize.html
// Texture2D.Apply - https://docs.unity3d.com/ScriptReference/Texture2D.Apply.html
// Texture2D.ReadPixels - https://docs.unity3d.com/ScriptReference/Texture2D.ReadPixels.html
// Graphics.Blit - https://docs.unity3d.com/ScriptReference/Graphics.Blit.html

/* This class allows you to resize an image on the GPU. 
Resizing an image from 1024px to 8196px 100 times took this method: 00:00:40.8884790
Resizing an image from 1024px to 8196px 100 times with Unity.Texture2D.Resize() took: 01:08:08.55
*/

using UnityEngine;

public class ResizeTool
{

 //copy the texture around so if it was a tile all the seams would be in the middle, makes it easy to see the problems with it
 //Pretty sure I wrote the function below? - Seth
    public static void SetupAsTile(Texture2D texture2D,  FilterMode filter = FilterMode.Bilinear)
    {
        //create a temporary RenderTexture with the target size
        int width = texture2D.width;
        int height = texture2D.height;
        
        RenderTexture rt = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default);

        //set the active RenderTexture to the temporary texture so we can read from it
        RenderTexture.active = rt;

        //Copy the texture data on the GPU - this is where the magic happens
        var vScale = new Vector2(1, 1);
        var vOffset = new Vector2(0, 0);

        Graphics.Blit(texture2D, rt, vScale, vOffset, 0, 0);
        
        //resize the texture to the target values (this sets the pixel data as undefined)
        texture2D.Reinitialize(width, height, texture2D.format, false);
     
        texture2D.filterMode = filter;

        try
        {
            //reads the pixel values from the temporary RenderTexture onto the resized texture
            
            //keep in mind Y is backwards from what you would think.  (0,0 is bottom left of image)
            texture2D.ReadPixels(new Rect(width/2, height/2, width/2, height/2), 0, height/2); //upper left block
            texture2D.ReadPixels(new Rect(0, height / 2, width / 2, height/2), width/2, height/2); //upper right block
            texture2D.ReadPixels(new Rect(width/2, 0, width / 2, height / 2), 0, 0); //lower left block
            texture2D.ReadPixels(new Rect(0, 0, width / 2, height / 2), width / 2, 0); //lower right block

            //actually upload the changed pixels to the graphics card
            texture2D.Apply();
        }
        catch
        {
            Debug.LogError("Read/Write is not enabled on texture " + texture2D.name);
        }

        RenderTexture.ReleaseTemporary(rt);
    }
    //Seth wrote this (with ChatGTP help)
    public static Texture2D CropTexture(Texture2D texture, Rect cropRect)
    {
        // Clamp the crop rectangle to fit within the texture's bounds
        cropRect.x = Mathf.Clamp(cropRect.x, 0f, texture.width - cropRect.width);
        cropRect.y = Mathf.Clamp(cropRect.y, 0f, texture.height - cropRect.height);
        cropRect.width = Mathf.Min(cropRect.width, texture.width - cropRect.x);
        cropRect.height = Mathf.Min(cropRect.height, texture.height - cropRect.y);

        // Create a new texture to hold the cropped image
        Texture2D croppedTexture = new Texture2D((int)cropRect.width, (int)cropRect.height, TextureFormat.RGBA32, false);

        // Calculate the flipped Y-coordinate of the top-left pixel in the crop rectangle
        int startY = texture.height - (int)(cropRect.y + cropRect.height);

        // Copy the cropped pixels from the original texture to the new texture
        Color[] pixels = texture.GetPixels((int)cropRect.x, startY, (int)cropRect.width, (int)cropRect.height);
        croppedTexture.SetPixels(pixels);

        // Apply the changes to the new texture
        croppedTexture.Apply();

        // Return the cropped texture
        return croppedTexture;
    }

    //Seth wrote this (with ChatGTP help)
    public static Texture2D CropTextureToAspectRatio(Texture2D texture, int newWidth, int newHeight)
    {
        float srcAspectRatio = (float)texture.width / (float)texture.height;
        float dstAspectRatio = (float)newWidth / (float)newHeight;

        int width = texture.width;
        int height = texture.height;
        int x = 0, y = 0;

        if (dstAspectRatio < srcAspectRatio)
        {
            // crop the width
            width = (int)(height * dstAspectRatio);
            x = (texture.width - width) / 2;
        }
        else
        {
            // crop the height
            height = (int)(width / dstAspectRatio);
            y = (texture.height - height) / 2;
        }

        Color[] pixels = texture.GetPixels(x, y, width, height);
        Texture2D croppedTexture = new Texture2D(width, height);
        croppedTexture.SetPixels(pixels);

        // Apply the changes to the new texture
        croppedTexture.Apply();

        // Return the cropped texture
        return croppedTexture;
    }

    public static void Resize(Texture2D texture2D, int targetX, int targetY, bool mipmap = true, FilterMode filter = FilterMode.Bilinear)
    {
        //create a temporary RenderTexture with the target size
        RenderTexture rt = RenderTexture.GetTemporary(targetX, targetY, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default);

        //set the active RenderTexture to the temporary texture so we can read from it
        RenderTexture.active = rt;

        //Copy the texture data on the GPU - this is where the magic happens [(;]
        Graphics.Blit(texture2D, rt);
        //resize the texture to the target values (this sets the pixel data as undefined)
        texture2D.Reinitialize(targetX, targetY, texture2D.format, mipmap);
        texture2D.filterMode = filter;

        try
        {
            //reads the pixel values from the temporary RenderTexture onto the resized texture
            texture2D.ReadPixels(new Rect(0.0f, 0.0f, targetX, targetY), 0, 0);
            //actually upload the changed pixels to the graphics card
            texture2D.Apply();
        }
        catch
        {
            Debug.LogError("Read/Write is not enabled on texture " + texture2D.name);
        }

        RenderTexture.ReleaseTemporary(rt);
    }

}

