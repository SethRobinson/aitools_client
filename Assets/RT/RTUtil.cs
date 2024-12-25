using System.Linq;
using UnityEngine;
using System.Collections.Generic;
using System.Text;
using DG.Tweening;
using UnityEngine.SceneManagement;
using System.Runtime.InteropServices;
using System.IO;
using UnityEngine.Rendering;
using System.Globalization;
using System.Text.RegularExpressions;
using UnityEngine.Assertions;
using UnityEngine.UI;
using TMPro;
using UnityEngine.Profiling;


//Adapted from https://stackoverflow.com/questions/46237984/how-to-emulate-statically-the-c-bitfields-in-c
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct RTBitField
{
   public System.Byte _data; // We actually store Byte

    public RTBitField(System.Byte data)
    {
        _data = data;
    }

    public bool GetBit(int index)
    {
        return (_data & (1 << index)) != 0;
    }
    public void Clear()
    {
        _data = 0;
    }
    public void SetBit(int index, bool value)
    {
        byte v = (byte)(1 << index);

        if (value)
            _data |= v;
        else
            _data = (byte)((_data | v) ^ v);
    }

}

//based on code from https://stackoverflow.com/questions/6651554/random-number-in-long-range-is-this-the-way
public static class Extensions
{
    //returns a uniformly random ulong between ulong.Min inclusive and ulong.Max inclusive
    public static ulong NextULong(this System.Random rng)
    {
        byte[] buf = new byte[8];
        rng.NextBytes(buf);
        return System.BitConverter.ToUInt64(buf, 0);
    }

    //returns a uniformly random ulong between ulong.Min and Max without modulo bias
    public static ulong NextULong(this System.Random rng, ulong max, bool inclusiveUpperBound = false)
    {
        return rng.NextULong(ulong.MinValue, max, inclusiveUpperBound);
    }

    //returns a uniformly random ulong between Min and Max without modulo bias
    public static ulong NextULong(this System.Random rng, ulong min, ulong max, bool inclusiveUpperBound = false)
    {
        ulong range = max - min;

        if (inclusiveUpperBound)
        {
            if (range == ulong.MaxValue)
            {
                return rng.NextULong();
            }

            range++;
        }

        if (range <= 0)
        {
            throw new System.ArgumentOutOfRangeException("Max must be greater than min when inclusiveUpperBound is false, and greater than or equal to when true", "max");
        }

        ulong limit = ulong.MaxValue - ulong.MaxValue % range;
        ulong r;
        do
        {
            r = rng.NextULong();
        } while (r > limit);

        return r % range + min;
    }

  

    //returns a uniformly random long between long.Min inclusive and long.Max inclusive
    public static long NextLong(this System.Random rng)
    {
        byte[] buf = new byte[8];
        rng.NextBytes(buf);
        return System.BitConverter.ToInt64(buf, 0);
    }

    //returns a uniformly random long between long.Min and Max without modulo bias
    public static long NextLong(this System.Random rng, long max, bool inclusiveUpperBound = false)
    {
        return rng.NextLong(long.MinValue, max, inclusiveUpperBound);
    }

    //returns a uniformly random long between Min and Max without modulo bias
    public static long NextLong(this System.Random rng, long min, long max, bool inclusiveUpperBound = false)
    {
        ulong range = (ulong)(max - min);

        if (inclusiveUpperBound)
        {
            if (range == ulong.MaxValue)
            {
                return rng.NextLong();
            }

            range++;
        }

        if (range <= 0)
        {
            throw new System.ArgumentOutOfRangeException("Max must be greater than min when inclusiveUpperBound is false, and greater than or equal to when true", "max");
        }

        ulong limit = ulong.MaxValue - ulong.MaxValue % range;
        ulong r;
        do
        {
            r = rng.NextULong();
        } while (r > limit);
        return (long)(r % range + (ulong)min);
    }


    //These two funcs below by PeachyPixels: https://forum.unity.com/threads/convert-screen-width-to-distance-in-world-space.1072694/
    public static Rect GetScreenRect(this Camera value)
    {
        Vector3 topRight = value.ViewportToWorldPoint(new Vector3(1f, 1f, 0f));
        return (new Rect(0f, 0f, topRight.x * 2f, topRight.y * 2f));
    }
    public static Rect GetScreenWorldRect(this Camera value)
    {
        Vector3 bottomLeft = value.ViewportToWorldPoint(new Vector3(0f, 0f, 0f));
        Vector3 topRight = value.ViewportToWorldPoint(new Vector3(1f, 1f, 0f));
        return (new Rect(bottomLeft.x, bottomLeft.y, topRight.x * 2f, topRight.y * 2f));
    }

}



//From comment left by NinjaISV in Unity answer area:  https://answers.unity.com/questions/722748/refreshing-the-polygon-collider-2d-upon-sprite-cha.html
public static class Collider2DExtensions
{
    public static void TryUpdateShapeToAttachedSprite(this PolygonCollider2D collider)
    {
        collider.UpdateShapeToSprite(collider.GetComponent<SpriteRenderer>().sprite);
    }

    public static void UpdateShapeToSprite(this PolygonCollider2D collider, Sprite sprite)
    {
        // ensure both valid
        if (collider != null && sprite != null)
        {
            
         
            // update count
            collider.pathCount = sprite.GetPhysicsShapeCount();


            // new paths variable
            List<Vector2> path = new List<Vector2>();

            // loop path count
            for (int i = 0; i < collider.pathCount; i++)
            {
                // clear
                path.Clear();
                // get shape
                sprite.GetPhysicsShape(i, path);
                // set path
                collider.SetPath(i, path.ToArray());
            }
        }
    }
}


//based on code from https://forum.unity.com/threads/how-to-edit-texture-pixels-at-run-time-through-c-code.1127939/
public static class Tex2DExtension
{
    private static int rSquared;
    private static int xMin, xMax;
    private static int yMin, yMax;

    public static void SetPixelsWithinRadius(this Texture2D tex, int x, int y, int r, Color color)
    {
        rSquared = r * r;

        if (x - r < 0)
        {
            xMin = 0;
        }
        else
        {
            xMin = x - r;
        }

        if (x + r > tex.width)
        {
            xMax = tex.width;
        }
        else
        {
            xMax = x + r;
        }

        if (y - r < 0)
        {
            yMin = 0;
        }
        else
        {
            yMin = y - r;
        }

        if (y + r > tex.height)
        {
            yMax = tex.height;
        }
        else
        {
            yMax = y + r;
        }

        for (int u = xMin; u < xMax; u++)
        {
            for (int v = yMin; v < yMax; v++)
            {
                if ((x - u) * (x - u) + (y - v) * (y - v) < rSquared)
                {
                    tex.SetPixel(u, v, color);
                }
            }
        }

    }

    //do something like the above function, but we set a rect area instead of a circle
    public static void SetPixelsWithinRect(this Texture2D tex, int x, int y, int width, int height, Color color)
    {
        if (x < 0)
        {
            x = 0;
        }
        if (x + width > tex.width)
        {
            width = tex.width - x;
        }
        if (y < 0)
        {
            y = 0;
        }
        if (y + height > tex.height)
        {
            height = tex.height - y;
        }

        for (int u = x; u < x + width; u++)
        {
            for (int v = y; v < y + height; v++)
            {
                tex.SetPixel(u, v, color);
            }
        }
    }

    //Same as above, but accept a rect
    public static void SetPixelsWithinRect(this Texture2D tex, Rect rect, Color color)
    {
        int x = (int)rect.x;
        int y = (int)rect.y;
        int width = (int)rect.width;
        int height = (int)rect.height;
        
        //call the above function
        SetPixelsWithinRect(tex, x, y, width, height, color);

    }

    public static Texture2D GetAlphaMask(this Texture2D tex, out bool bAlphaWasUsed) //grabs the alpha mask, replacing all colors
    {
        Texture2D newtex = new Texture2D(tex.width, tex.height, TextureFormat.RGBA32, false);
        bAlphaWasUsed = false;

        for (int x = 0; x < newtex.width; x++)
        {
            for (int y = 0; y < newtex.height; y++)
            {
                if (tex.GetPixel(x, y).a <= 0.01f) //no real reason I'm doing this, == 0 is probably fine
                {
                    newtex.SetPixel(x, y, new Color(1, 1, 1, 0));  //drop the alpha
                }
                else
                {
                    var color = tex.GetPixel(x, y);
                    bAlphaWasUsed = true;
                    /*
                    if (x % 100 == 0)
                    {
                        Debug.Log("Pixel: " + color);
                    }
                    */

                    color.r = 1;
                    color.g = 1;
                    color.b = 1;
                  
                    newtex.SetPixel(x, y, color);
                }
            }
        }

        return newtex;

    }

    //seth wrote the functions below here
    public static Texture2D ConvertTextureToBlackAndWhiteRGBMask(this Texture2D tex)
    {
        Texture2D newtex = new Texture2D(tex.width, tex.height, TextureFormat.RGB24, false);

        for (int x = 0; x < newtex.width; x++)
        {
            for (int y = 0; y < newtex.height; y++)
            {
                if (tex.GetPixel(x, y).a <= 0.1f)
                {
                    newtex.SetPixel(x, y, new Color(0, 0, 0, 1));  //drop the alpha
                }
                else
                {
                    var color = tex.GetPixel(x, y);

                    /*
                    if (x % 100 == 0)
                    {
                        Debug.Log("Pixel: " + color);
                    }
                    */
                    
                    color.r = 1;
                    color.g = 1;
                    color.b = 1;
                    //color.a = 1;

                    newtex.SetPixel(x, y, color);  
                }
            }
        }

        return newtex;
    }



    //Don't forget to .Apply() after doing this!
    public static Texture2D FillAlpha(this Texture2D newtex, float alphaZeroToOne)
    {
       
        for (int x = 0; x < newtex.width; x++)
        {
            for (int y = 0; y < newtex.height; y++)
            {

                Color c = newtex.GetPixel(x, y);
                c.a = alphaZeroToOne;
                newtex.SetPixel(x, y, c); 
            }
        }

        return newtex;
    }


    public static Texture2D Duplicate (this Texture2D tex)
    {
        Texture2D copyTexture = new Texture2D(tex.width, tex.height, tex.format, false);
        copyTexture.SetPixels(tex.GetPixels());
        copyTexture.Apply();
        copyTexture.filterMode = tex.filterMode;
        copyTexture.wrapMode = tex.wrapMode;
        
        //copyTexture.alphaIsTransparency = tex.alphaIsTransparency;
        return copyTexture;
    }

    //range checking is up to you.  Block copy, alpha etc is fully replaced with no processing
    //You may need to .Apply() yourself
    public static void Blit(this Texture2D dst, int dstOffsetX, int dstOffsetY, Texture2D src, int srcOffsetX, int srcOffsetY, int width, int height)
    {
        //textures are upside down, so flip the coords
       // dstOffsetY = dst.height - dstOffsetY;
        //srcOffsetY = src.height - srcOffsetY;

        Debug.Assert(dstOffsetX >= 0 && dstOffsetX + width <= dst.width);
        Debug.Assert(dstOffsetY >= 0 && dstOffsetY + height <= dst.height);

        Debug.Assert(srcOffsetX >= 0 && srcOffsetX + width <= src.width);
        Debug.Assert(srcOffsetY >= 0 && srcOffsetY + height <= src.height);

        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
              dst.SetPixel(dstOffsetX+x, (dst.height-1)-(dstOffsetY + y), src.GetPixel(srcOffsetX+x, (src.height-1)-(srcOffsetY+y)));
            }
        }

    }

    public static void BlitWithAlpha(this Texture2D dst, int dstOffsetX, int dstOffsetY, Texture2D src, int srcOffsetX, int srcOffsetY, int width, int height)
    {
        Debug.Assert(dstOffsetX >= 0 && dstOffsetX + width <= dst.width);
        Debug.Assert(dstOffsetY >= 0 && dstOffsetY + height <= dst.height);

        Debug.Assert(srcOffsetX >= 0 && srcOffsetX + width <= src.width);
        Debug.Assert(srcOffsetY >= 0 && srcOffsetY + height <= src.height);

        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                Color dstColor = dst.GetPixel(dstOffsetX + x, (dst.height - 1) - (dstOffsetY + y));
                Color srcColor = src.GetPixel(srcOffsetX + x, (src.height - 1) - (srcOffsetY + y));

                float alpha = srcColor.a;
                Color blendedColor = new Color(srcColor.r * alpha + dstColor.r * (1 - alpha),
                                               srcColor.g * alpha + dstColor.g * (1 - alpha),
                                               srcColor.b * alpha + dstColor.b * (1 - alpha),
                                               alpha + dstColor.a * (1 - alpha));

                dst.SetPixel(dstOffsetX + x, (dst.height - 1) - (dstOffsetY + y), blendedColor);
            }
        }

       
    }

    //Don't forget to .Apply() after doing this!
    public static void Fill(this Texture2D tex, Color color)
    {
        for (int x = 0; x < tex.width; x++)
        {
            for (int y = 0; y < tex.height; y++)
            {
                tex.SetPixel(x, y,color);
            }
        }

    }

    //inverts both color and alpha  //Don't forget to .Apply() after doing this!
    public static void Invert(this Texture2D tex)
    {
        for (int x = 0; x < tex.width; x++)
        {
            for (int y = 0; y < tex.height; y++)
            {
                Color pix = tex.GetPixel(x, y);
                pix.r = 1 - pix.r;
                pix.g = 1 - pix.g;
                pix.b = 1 - pix.b;
                pix.a = 1 - pix.a;

                tex.SetPixel(x, y, pix);
            }
        }

    }


    /*
        //we'll use a convolution filter to blur everything, including alpha if available
        public static Texture2D GetBlurredVersion(this Texture2D dest)
        {
            dest.Apply();

            var blurFilter = new ConvFilter.BoxBlurFilter();
            var processor = new ConvFilter.ConvolutionProcessor(dest);
            Texture2D newTex = null;

            StartCoroutine(processor.ComputeWith((myReturnValue) => { }), blurFilter) ;



            newTex.Apply();
            return newTex.Duplicate();
        }
        public static Texture2D GetGaussianBlurredVersion(this Texture2D dest)
        {
            var blurFilter = new ConvFilter.GaussianBlurFilter();
            var processor = new ConvFilter.ConvolutionProcessor(dest);
            var newTex = processor.ComputeWith(blurFilter);
            newTex.Apply();
            return newTex;
        }
    */

    public static void SetPixelsFromTextureWithAlphaMask(this Texture2D dest, Texture2D src, Texture2D alphaMask, float contentWriteAlphaMod = 1.0f, bool bUseMaskLumaAsAlpha = false)
    {
        //use assert to make sure all textures have the same dimensions
        Debug.Assert(dest.width == src.width && dest.width == alphaMask.width);
        Debug.Assert(dest.height == src.height && dest.height == alphaMask.height);

        float finalAlpha;
        float alpha;
        Color alphaMaskPixel;


        if (bUseMaskLumaAsAlpha)
        {
            for (int x = 0; x < dest.width; x++)
            {
                for (int y = 0; y < dest.height; y++)
                {
                    alphaMaskPixel = alphaMask.GetPixel(x, y);

                    alpha = (alphaMaskPixel.r + alphaMaskPixel.g + alphaMaskPixel.b) / 3.0f;


                    if (alpha == 0)
                    {
                        //Debug.Log("skipping pixel");
                    }
                    else
                    {

                        //OPTIMIZE - fix this later
                        finalAlpha = contentWriteAlphaMod * alpha;
                        Color srcPixel = src.GetPixel(x, y);
                        Color origPixel = dest.GetPixel(x, y);
                        //srcPixel = new Color(1.0f, 0,0);
                        Color finalPixel = new Color();
                        
                        finalPixel.r = (origPixel.r * (1-finalAlpha)) + (srcPixel.r * (finalAlpha));
                        finalPixel.g = (origPixel.g * (1 - finalAlpha)) + (srcPixel.g * (finalAlpha));
                        finalPixel.b = (origPixel.b * (1 - finalAlpha)) + (srcPixel.b * (finalAlpha));
                        finalPixel.a = 1.0f;
                       
                        dest.SetPixel(x, y, finalPixel);
                    }
                }
            }

        }
        else
        {


            for (int x = 0; x < dest.width; x++)
            {
                for (int y = 0; y < dest.height; y++)
                {
                    alpha = alphaMask.GetPixel(x, y).a;

                    if (alpha == 0)
                    {
                        //Debug.Log("skipping pixel");
                    }
                    else
                    {
                        finalAlpha = contentWriteAlphaMod * alpha;
                        dest.SetPixel(x, y, (src.GetPixel(x, y) * finalAlpha) + (dest.GetPixel(x, y) * (1 - finalAlpha)));
                    }
                }
            }
        }


        dest.Apply();
    }


    public static void SetColorAndAlphaFromAlphaChannel(this Texture2D dest, Texture2D src)
    {
        //use assert to make sure all textures have the same dimensions
        Debug.Assert(dest.width == src.width);
        Debug.Assert(dest.height == src.height);

        float finalAlpha;
        float alpha;
        

            for (int x = 0; x < dest.width; x++)
            {
                for (int y = 0; y < dest.height; y++)
                {
                    alpha = src.GetPixel(x, y).a;

                    if (alpha == 0)
                    {
                        //Debug.Log("skipping pixel");
                    }
                    else
                    {
                        finalAlpha = alpha;
                        dest.SetPixel(x, y, new Color(1.0f*finalAlpha, 1.0f * finalAlpha, 1.0f * finalAlpha, 1.0f * finalAlpha));
                    }
                }
            }
        

        dest.Apply();
    }

}


public static class StringExt
{
    public static string Truncate(this string value, int maxLength)
    {
        if (string.IsNullOrEmpty(value)) return value;
        return value.Length <= maxLength ? value : value.Substring(0, maxLength);
    }

    public static int NthIndexOf(this string s, char c, int n)
    {
        var takeCount = s.TakeWhile(x => (n -= (x == c ? 1 : 0)) > 0).Count();
        return takeCount == s.Length ? -1 : takeCount;
    }
}

/*
useage:

 
    string key = "crap";
    key = _someDict.ForceUnique(key);

    Debug.Log("Key "+key+" will now be unique if it's added to _someDict."
    
 */


public static class DictionaryExt
{
    //Note:  Do not use anything random, should be deterministic
    public static string ForceUnique<TValue>(this Dictionary<string, TValue> dic, string newKey, bool bSpaceBeforeNumbers = false)
    {
      
        while (dic.ContainsKey(newKey))
        {
            var numSt = RTUtil.GetNumberFromEndOfString(newKey);
            int num = 0;
            int.TryParse(numSt, out num);

            num++;//turn to number:

            newKey = newKey.Substring(0, newKey.Length - numSt.Length);

            if (bSpaceBeforeNumbers && numSt.Length == 0)
            {
                newKey += " "; //a space before the # looks nice
            }
            newKey = newKey + num.ToString();
        }

        return newKey;
    }
}



public class RTUtil
{
#if UNITY_WEBGL && !UNITY_EDITOR
    [DllImport("__Internal")]
    private static extern void JLIB_PopupUnblockSendMessage(string callbackObjectStr, string callbackFunctionStr, string callbackParmStr);

    [DllImport("__Internal")]
    private static extern void JLIB_PopupUnblockOpenURL(string URLStr);

    [DllImport("__Internal")]
    private static extern bool JLIB_IsOnMobile();

#endif

    public static byte[] g_buffer = new byte[1024*100];
	public static int[] g_intArray = new int[3];
    public static uint[] g_uintArray = new uint[3];
    public static short[] g_shortArray = new short[3];
    public static System.UInt16[] g_ushortArray = new System.UInt16[3];
    public static float[] g_floatArray = new float[4];
    public static bool[] g_boolArray = new bool[3];
    public static byte[] g_byteArray = new byte[3];

    static public bool DeleteFileIfItExists(string file)
    {
        if (File.Exists(file))
        {
            File.Delete(file);
            return true;
        } else
        {
            return false;
        }
    }

    public static string FilteredFilenameSafeToUseAsFileName(string fileName)
    {
        // Replace spaces with underscores
        fileName = fileName.Replace(' ', '-'); //I'm not using _ because twine reserves that for variables, uhh

        // Remove invalid characters (only allow alphanumeric, underscores, hyphens, and dots)
        fileName = Regex.Replace(fileName, @"[^a-zA-Z0-9_.-]", "");

        // Optionally, you can also limit the length of the filename if necessary
        int maxLength = 255; // Typical filesystem limit
        if (fileName.Length > maxLength)
        {
            fileName = fileName.Substring(0, maxLength);
        }

        return fileName;
    }

    public static uint Color32ToUInt(Color32 color)
    {
        return (uint)((color.a << 24) | (color.r << 16) | (color.g << 8) | (color.b << 0));
    }

    public static Color32 UIntToColor32(uint color)
    {
        return new Color32((byte)(color >> 16), (byte)(color >> 8), (byte)(color >> 0), (byte)(color >> 24));
    }

    //thanks to sol_hsa at http://sol.gfxile.net/interpolation/index.html
    //#define SMOOTHSTEP(x) ((x) * (x) * (3 - 2 * (x))) 

    /*
    
#define EASE_FROM(x) ((x)*(x))
#define SMOOTHSTEP_INVERSE(x) pow( ((x)/0.5)-1,3)
    */
    public static float SMOOTHSTEP(float x)
    {
        return (x) * (x) * (3 - 2 * (x));
    }

    //https://stackoverflow.com/questions/13169393/extract-number-at-end-of-string-in-c-sharp
    static public string GetNumberFromEndOfString(string text)
    {
        int i = text.Length - 1;
        while (i >= 0)
        {
            if (!char.IsNumber(text[i])) break;
            i--;
        }
        return text.Substring(i + 1);
    }


    //want a texture size to be a multiple of 16 or something?  Use this
    static public int ConvertNumToNearestMultiple(int value, int factor)
    {
        return (int)System.Math.Round(
                (value / (double)factor),
                System.MidpointRounding.AwayFromZero
            ) * factor;
    }

    //Note, these swaps don't work on a LOT of stuff in C#... don't trust at all unless you're doing an int or something
    static public void Swap<T>(ref T lhs, ref T rhs)
    {
        T temp;
        temp = lhs;
        lhs = rhs;
        rhs = temp;
    }

    public static int CountSubscribersInAction(System.Action act)
    {
        if (act == null) return 0;
        return act.GetInvocationList().Length;
    }

    public static float EASE_TO(float x)
    {
        return (1 - (1 - (x)) * (1 - (x)));
    }


    public static void Shuffle<T>(T[] array)
    {
        int n = array.Length;
        while (n > 1)
        {
            int k = Random.Range(0, n--);
            T temp = array[n];
            array[n] = array[k];
            array[k] = temp;
        }
    }

    private static StringBuilder AppendColor(StringBuilder sb, int r, int g, int b, int alpha)
    {
        sb.Append("<color=#" + r.ToString("x2") + g.ToString("x2") + b.ToString("x2") + alpha.ToString("x2") + ">");
        //sb.Append("<color=#0000ffff>");
        
        return sb;
    }

	public static float RoundToOneDec(float f)
	{
		return Mathf.Round(f * 10) / 10;
	}
	public static float RoundToTwoDec(float f)
	{
		return Mathf.Round(f * 100) / 100;
	}

	public static string ConvertSansiToUnityColors(string s)
    {
        System.Text.StringBuilder sb = new System.Text.StringBuilder("");
        int colorDepth = 0;

        for (int i = 0; i < s.Length; i++)
        {

            if (s[i] == '{')
            {
                //found sansi control code

                int closingIndex = s.IndexOf('}', i);
                if (closingIndex == -1)
                {
                    //RTConsole.Log("Malformed SANSI detected in " + s);
                    continue;
                }

                //a 0 length link will screw up if it's at the ending and beginning of a line.. using a zero width unicode space fixes it without
                //actually adding a character.  I hope
                sb.Append("<link=\"" + s.Substring(i + 1, (closingIndex - i)-1)+"\">"+System.Convert.ToChar(0x200b)+"</link>");
                i = closingIndex; //move past
                continue;
            }

            if (s[i] == '`')
            {
                //found special code
                if (s.Length-1 > i)
                {
                    i++; //move it past the second control code
              
                    if (s[i] == '`')
                    {
                        if (colorDepth > 0)
                        {
                            sb.Append("</color>");
                            colorDepth--;
                        }
                        continue;
                    }

                   colorDepth++;
                    //enough room for a code here
                    if (s[i] == '0')
                    {
                        AppendColor(sb, 100, 255, 0, 255);
                        continue;
                    }
                    if (s[i] == '1')
                    {
                        AppendColor(sb, 80, 80, 200, 255);
                        continue;
                    }
                    if (s[i] == '2')
                    {
                        AppendColor(sb, 50, 160, 0, 255);
                        continue;
                    }
                    if (s[i] == '3')
                    {
                        AppendColor(sb, 191, 218, 255, 255);
                        continue;
                    }
                    if (s[i] == '4')
                    {
                        AppendColor(sb, 180, 0,0, 255);
                        continue;
                    }
                    if (s[i] == '5')
                    {
                        AppendColor(sb, 51, 0, 51, 255);
                        continue;
                    }
                    if (s[i] == '6')
                    {
                        AppendColor(sb, 255, 202, 111, 255);
                        continue;
                    }
                    if (s[i] == '7')
                    {
                        AppendColor(sb, 120, 120, 120, 255);
                        continue;
                    }
                    if (s[i] == '8')
                    {
                        AppendColor(sb, 40, 40, 40, 255);
                        continue;
                    }
                    if (s[i] == '9')
                    {
                        AppendColor(sb, 255, 238, 12, 255);
                        continue;
                    }
                    if (s[i] == '!')
                    {
                        AppendColor(sb, 209, 255, 249, 255);
                        continue;
                    }
                    if (s[i] == '@')
                    {
                        AppendColor(sb, 255, 205, 201, 255);
                        continue;
                    }
                    if (s[i] == '#')
                    {
                        AppendColor(sb, 255, 143, 243, 255);
                        continue;
                    }
                    if (s[i] == '$')
                    {
                        AppendColor(sb, 255, 252, 197, 255);
                        continue;
                    }
                    if (s[i] == '^')
                    {
                        AppendColor(sb, 181, 255, 151, 255);
                        continue;
                    }
                    if (s[i] == '&')
                    {
                        AppendColor(sb, 254, 235, 255, 255);
                        continue;
                    }
                    if (s[i] == 'w')
                    {
                        AppendColor(sb, 255, 255, 255, 255);
                        continue;
                    }
                    if (s[i] == 'o')
                    {
                        AppendColor(sb, 252, 230, 186, 255);
                        continue;
                    }
                    if (s[i] == 'b')
                    {
                        AppendColor(sb, 0, 0, 0, 255);
                        continue;
                    }
                    if (s[i] == 'p')
                    {
                        AppendColor(sb, 255, 223, 241, 255);
                        continue;
                    }
                  


                    //uh oh, unknown color code
                    colorDepth--; //Remove the one we had added earlier
                    continue;
                }
            }
            sb.Append(s[i]);
        }

        while (colorDepth > 0)
        {
            colorDepth--;
            sb.Append("</color>");
        }
        return sb.ToString();
    }

    public static string NameFilter(string str)
    {
        char[] arr = str.Where(c => (char.IsLetterOrDigit(c))).ToArray();
        return new string(arr);
    }

    public static string ChatFilter(string str)
    {
        char[] arr = str.Where(c => (char.IsLetterOrDigit(c) ||
        char.IsPunctuation(c) ||
        char.IsWhiteSpace(c)
        || c == '`')).ToArray();
        return new string(arr);
    }


    //write
    public static void SerializeString(string value, byte[] byteBuff, ref int index)
	{

		//first write the size of it
		SerializeInt32(value.Length, byteBuff, ref index); 

		//now write the actual string data
		//OPTIMIZE - couldn't we do this without allocating new data here?
		byte[] stringBuff = Encoding.ASCII.GetBytes(value);

		System.Buffer.BlockCopy(stringBuff, 0, byteBuff, index, value.Length); 
		index += value.Length;
	}

	//read
	public static bool SerializeString(ref string value, byte[] byteBuff, ref int index)
	{
	//first get the size
		int tempInt = 0;

        try
        {
            SerializeInt32(ref tempInt, byteBuff, ref index);

            value = System.Text.Encoding.Default.GetString(byteBuff, index, tempInt);

            index += tempInt;
        }
        catch (System.Exception e)
        {
            
            RTConsole.Log("Config: " + e.Message);
            return false; //signal error
        }

        return true;
	}

	//write
	public static void SerializeInt32(int value, byte[] byteBuff, ref int index)
	{
		g_intArray[0] = value;
		System.Buffer.BlockCopy(g_intArray, 0, byteBuff, index, 4); //raw copy over 4 bytes
		index += 4;
	}

	//read
	public static void SerializeInt32(ref int value, byte[] byteBuff, ref int index)
	{
		System.Buffer.BlockCopy(byteBuff, index, g_intArray, 0, 4); //raw copy over 4 bytes
		value = g_intArray[0];
		index += 4;
	}

    //write
    public static void SerializeUInt32(uint value, byte[] byteBuff, ref int index)
    {
        g_uintArray[0] = value;
        System.Buffer.BlockCopy(g_uintArray, 0, byteBuff, index, 4); //raw copy over 4 bytes
        index += 4;
    }

    //read
    public static void SerializeUInt32(ref uint value, byte[] byteBuff, ref int index)
    {
        System.Buffer.BlockCopy(byteBuff, index, g_uintArray, 0, 4); //raw copy over 4 bytes
        value = g_uintArray[0];
        index += 4;
    }

    //write
    public static void SerializeBool(bool value, byte[] byteBuff, ref int index)
    {
        g_boolArray[0] = value;
        System.Buffer.BlockCopy(g_boolArray, 0, byteBuff, index, 1); //raw copy over 4 bytes
        index += 1;
    }

    //read
    public static void SerializeBool(ref bool value, byte[] byteBuff, ref int index)
    {
        System.Buffer.BlockCopy(byteBuff, index, g_boolArray, 0, 1); //raw copy over 4 bytes
        value = g_boolArray[0];
        index += 1;
    }

    //write
    public static void SerializeInt16(short value, byte[] byteBuff, ref int index)
	{
		g_shortArray[0] = value;
		System.Buffer.BlockCopy(g_shortArray, 0, byteBuff, index, 2); //raw copy over 2 bytes
		index += 2;
	}
	
	//read
	public static void SerializeInt16(ref short value, byte[] byteBuff, ref int index)
	{
		System.Buffer.BlockCopy(byteBuff, index, g_shortArray, 0, 2); //raw copy over 2 bytes
		value = g_shortArray[0];
		index += 2;
	}

    public static void SerializeUInt16(System.UInt16 value, byte[] byteBuff, ref int index)
    {
        g_ushortArray[0] = value;
        System.Buffer.BlockCopy(g_ushortArray, 0, byteBuff, index, 2); //raw copy over 2 bytes
        index += 2;
    }

    //read
    public static void SerializeUInt16(ref System.UInt16 value, byte[] byteBuff, ref int index)
    {
        System.Buffer.BlockCopy(byteBuff, index, g_ushortArray, 0, 2); //raw copy over 2 bytes
        value = g_ushortArray[0];
        index += 2;
    }


    //write
    public static void SerializeUInt8(byte value, byte[] byteBuff, ref int index)
    {
        g_byteArray[0] = value;
        System.Buffer.BlockCopy(g_byteArray, 0, byteBuff, index, 1); //raw copy over 1 byte
        index += 1;
    }

    //read
    public static void SerializeUInt8(out byte value, byte[] byteBuff, ref int index)
    {
        System.Buffer.BlockCopy(byteBuff, index, g_byteArray, 0, 1); //raw copy over 1 byte
        value = g_byteArray[0];
        index += 1;
    }


    //write
    public static void SerializeFloat(float value, byte[] byteBuff, ref int index)
	{
		g_floatArray[0] = value;
		System.Buffer.BlockCopy(g_floatArray, 0, byteBuff, index, 4); //raw copy over 4 bytes
		index += 4;
	}



    //read
    public static void SerializeFloat(out float value, byte[] byteBuff, ref int index)
	{
		System.Buffer.BlockCopy(byteBuff, index, g_floatArray, 0, 4); //raw copy over 4 bytes
		value = g_floatArray[0];
		index += 4;
	}

	//write
	public static void SerializeVector2(Vector2 value, byte[] byteBuff, ref int index)
	{
		g_floatArray[0] = value.x;
		g_floatArray[1] = value.y;
		System.Buffer.BlockCopy(g_floatArray, 0, byteBuff, index, 8); 
		index += 8;
	}
	
	//read
	public static void SerializeVector2(out Vector2 value, byte[] byteBuff, ref int index)
	{
		System.Buffer.BlockCopy(byteBuff, index, g_floatArray, 0, 8); 
		value.x = g_floatArray[0];
		value.y = g_floatArray[1];
		index += 8;
	}

    //write
    public static void SerializeVector3(Vector3 value, byte[] byteBuff, ref int index)
    {
        g_floatArray[0] = value.x;
        g_floatArray[1] = value.y;
        g_floatArray[2] = value.z;
        System.Buffer.BlockCopy(g_floatArray, 0, byteBuff, index, 12);
        index += 12;
    }

    //read
    public static void SerializeVector3(ref Vector3 value, byte[] byteBuff, ref int index)
    {
        System.Buffer.BlockCopy(byteBuff, index, g_floatArray, 0, 12);
        value.x = g_floatArray[0];
        value.y = g_floatArray[1];
        value.z = g_floatArray[2];
        index += 12;
    }

    public static void SerializeQuaternion(Quaternion value, byte[] byteBuff, ref int index)
    {
        g_floatArray[0] = value.x;
        g_floatArray[1] = value.y;
        g_floatArray[2] = value.z;
        g_floatArray[3] = value.w;
        System.Buffer.BlockCopy(g_floatArray, 0, byteBuff, index, 16);
        index += 16;
    }

    //read
    public static void SerializeQuaternion(ref Quaternion value, byte[] byteBuff, ref int index)
    {
        System.Buffer.BlockCopy(byteBuff, index, g_floatArray, 0, 16);
        value.x = g_floatArray[0];
        value.y = g_floatArray[1];
        value.z = g_floatArray[2];
        value.w = g_floatArray[3];
        index += 16;
    }

    //OPTIMIZE:  This is slow, I wrote it in a hurry because I didn't care.  Story of my programming life
    public static bool GetJSONStringByNameFromString(string line, string keyName, out string data)
    {
        line = line.Trim();
        if (line.StartsWith("\""+keyName+"\""))
        {
            int dataPosStart = line.NthIndexOf('\"', 3);
            int dataPosEnd = line.NthIndexOf('\"', 4);

            if (dataPosStart != -1 && dataPosEnd != -1)
            {
                data = line.Substring(dataPosStart+1, dataPosEnd- (dataPosStart+1));
                return true;
            }
            Debug.Log("GetJSONStringFromLine: Malformed JSON while looking for key " + keyName);
        }

        data = "";
        return false;
    }
    //write
    public static void SerializeVector2Int(Vector2Int value, byte[] byteBuff, ref int index)
    {
        g_intArray[0] = value.x;
        g_intArray[1] = value.y;
        System.Buffer.BlockCopy(g_intArray, 0, byteBuff, index, 8);
        index += 8;
    }

    //read
    public static void SerializeVector2Int(ref Vector2Int value, byte[] byteBuff, ref int index)
    {
        System.Buffer.BlockCopy(byteBuff, index, g_intArray, 0, 8);
        value.x = g_intArray[0];
        value.y = g_intArray[1];
        index += 8;
    }

    public static Vector2 Vector2Lerp(Vector2 from, Vector2 to, float t)
	{
		return new Vector2(from.x - ((from.x - to.x) * t), from.y - ((from.y - to.y) * t));
	}


    public static float SinGamePulseBySeconds(float intervalSeconds)
    {
        return Mathf.Sin(  (Time.time/ (intervalSeconds/2)));
        //return Mathf.Sin(Time.time);
    }

    //a helper to use with the above functions when you just want 0-1 range
    public static float SinToZeroToOneRange(float sinIn)
    {
        return (sinIn + 1.0f) / 2;
    }

    public static string GetTimeAsMinutesSeconds(float timeSeconds)
    {
        int minutes = Mathf.FloorToInt(timeSeconds / 60f);
        int seconds = Mathf.FloorToInt(timeSeconds % 60f);
        return $"{minutes:00}:{seconds:00}";
    }
    static public string SetFirstCharacterToUpperCase(string s)
    {
        if (string.IsNullOrEmpty(s))
        {
            return string.Empty;
        }
        char[] a = s.ToCharArray();
        a[0] = char.ToUpper(a[0]);
        return new string(a);
    }

    static public string GetRandomName()
    {
        int i = Random.Range(0, 10+1);
        string r = "";

        switch (i)
        {
            case 0: r = "gar"; break;
            case 1: r = "num"; break;
            case 2: r = "gren"; break;
            case 3: r = "dar"; break;
            case 4: r = "fal"; break;
            case 5: r = "kor"; break;
            case 6: r = "bol"; break;
            case 7: r = "dor"; break;
            case 8: r = "ken"; break;
            case 9: r = "ren"; break;
            case 10: r = "zar"; break;

            default:
                r = "Crap";
                break;
        }
        return r;
    }

    //gets whatever is after the parm you sent in.  Ie, "-name" would return "seth" if the command line was "-name seth"
    public static string GetCommandLineSetting(string name)
    {
        var line = System.Environment.GetCommandLineArgs();
        for (int i = 0; i < line.Length; i++)
        {
            if (line[i] == name && line.Length > i + 1)
            {
                return line[i + 1];
            }
        }
        return null;
    }

    //checks for the existence of a specific word in the command line
    public static bool DoesCommandLineWordExist(string name)
    {
        var line = System.Environment.GetCommandLineArgs();
        for (int i = 0; i < line.Length; i++)
        {
            if (line[i] == name)
            {
                return true;
            }
        }
        return false;
    }

    static public bool SetActiveByNameIfExists(string name, bool bActive)
    {
        GameObject obj = FindIncludingInactive(name);
        if (obj != null)
        {
            obj.SetActive(bActive);
            return true; //found it
        }
        return false; //couldn't find it
    }

    //hideously slow as it iterates all objects, so don't overuse!
    public static GameObject FindInChildrenIncludingInactive(GameObject go, string name)
    {

        for (int i=0; i < go.transform.childCount; i++)
        {
            if (go.transform.GetChild(i).gameObject.name == name) return go.transform.GetChild(i).gameObject;
            GameObject found = FindInChildrenIncludingInactive(go.transform.GetChild(i).gameObject, name);
            if (found != null) return found;
        }

        return null;  //couldn't find crap
    }

    public static void AddObjectsToListByNameIncludingInactive(GameObject parentObj, string name, bool bIsWildcardString, List<GameObject> objs)
    {

        if (parentObj == null)
        {
            //ok, special case, we'll run this on everything

            Scene scene = SceneManager.GetActiveScene();
            if (!scene.isLoaded)
            {
                //no scene loaded
                Debug.Assert(false);
                return;
            }

            var game_objects = new List<GameObject>();
            scene.GetRootGameObjects(game_objects);

            foreach (GameObject obj in game_objects)
            {
                if (
              (!bIsWildcardString && obj.gameObject.name == name) ||
                  (bIsWildcardString && obj.gameObject.name.StartsWith(name))
                  )
                {
                    objs.Add(obj);
                }

                //but what about their children, marty?


                AddObjectsToListByNameIncludingInactive(obj, name, bIsWildcardString, objs);

            }

            return;
        }

        for (int i = 0; i < parentObj.transform.childCount; i++)
        {
            if (
                (!bIsWildcardString && parentObj.transform.GetChild(i).gameObject.name == name) ||
                    (bIsWildcardString && parentObj.transform.GetChild(i).gameObject.name.StartsWith(name))
                    )
            {
                objs.Add(parentObj.transform.GetChild(i).gameObject);
            }

            AddObjectsToListByNameIncludingInactive(parentObj.transform.GetChild(i).gameObject, name, bIsWildcardString, objs);
        }

        
    }

    //hideously slow as it iterates all objects, so don't overuse!
    public static GameObject FindIncludingInactive(string name)
    {
        Scene scene = SceneManager.GetActiveScene();
        if (!scene.isLoaded)
        {
            //no scene loaded
            return null;
        }

        var game_objects = new List<GameObject>();
        scene.GetRootGameObjects(game_objects);

        foreach (GameObject obj in game_objects)
        {
            if (obj.transform.name == name) return obj;

            GameObject found = FindInChildrenIncludingInactive(obj, name);
            if (found) return found;
         }

        return null;
    }

    public static GameObject FindObjectOrCreate(string name)
    {
        GameObject temp = FindIncludingInactive(name);
        if (temp ==  null)
        {
            //create it
            temp = new GameObject();
            temp.name = name;
        }

        return temp;
    }

    public static bool KillObjectByName(string name, float delayTime = 0)
    {
        GameObject obj = FindIncludingInactive(name);

        if (obj)
        {
            GameObject.Destroy(obj, delayTime);
            return true;
        } else
        {
            //couldn't find it
            return false;
        }
    }
    public static int KillAllObjectsByName(GameObject parent, string name, bool bIsWildcardString, float delayTime = 0)
    {
        List<GameObject> temp = new List<GameObject>();

        
        AddObjectsToListByNameIncludingInactive(parent, name, bIsWildcardString, temp);

        foreach(GameObject obj in temp) 
        {
            GameObject.Destroy(obj, delayTime);
        }

        return temp.Count;
    }

    public static void KillAllObjectsByNameWrapper(GameObject parent, string name, bool bIsWildcardString, float delayTime = 0)
    {
        RTUtil.KillAllObjectsByName(parent, name, bIsWildcardString, delayTime);
    }


    //Below snippet taken from https://forum.unity.com/threads/deleting-all-chidlren-of-an-object.92827/, credit to mwk888
    /// <summary>
    /// Calls GameObject.Destroy on all children of transform. and immediately detaches the children
    /// from transform so after this call tranform.childCount is zero.
    /// </summary>
    public static void DestroyChildren(Transform transform)
    {
        for (int i = transform.childCount - 1; i >= 0; --i)
        {
            GameObject.Destroy(transform.GetChild(i).gameObject);
        }
        transform.DetachChildren();
    }

    //Note: This will also activate/inactivate the object
    static public void FadeGUIPanelByName(string name, float alphaTarget, float duration = 1.0f, float delayBeforeStart = 0.0f)
    {

        GameObject panelObj = FindIncludingInactive(name);
        
        if (panelObj == null)
        {
            RTConsole.Log("Can't find panel by the name of " + name + ", aborting FadeGUIPanelByName");
            return;
        }

        if (panelObj == null)
        {
            Debug.Log("FadeGUIPanelByName: Can't find " + name);
            return;
        }

        if (!panelObj.activeInHierarchy)
        {
            panelObj.SetActive(true); //can't fade anything if it's off!
        }
     

        CanvasGroup canvasGroup = panelObj.GetComponent<CanvasGroup>();

        if (canvasGroup == null)
        {
          //  Debug.Log("Adding canvas group, wonder if this works..");
            canvasGroup = panelObj.AddComponent<CanvasGroup>();
        }

        /*
        TweenTracker tracker = panelObj.GetComponent<TweenTracker>();
        if (tracker == null)
        {
            //  Debug.Log("Adding canvas group, wonder if this works..");
            tracker = panelObj.AddComponent<TweenTracker>();
        }

    */

        DOTween.Kill(panelObj); //kill any outstanding commands...
        Sequence mySequence = DOTween.Sequence();
        mySequence.SetId(panelObj);

        mySequence.AppendInterval(delayBeforeStart);
        //RTConsole.Log("Setting panel " + panelObj.ToString() + " to alpha " + alphaTarget + " in " + duration);

        mySequence.Append(canvasGroup.DOFade(alphaTarget, duration));

        //canvasGroup.interactable = false; //stop additional clicking
        if (alphaTarget == 0)
        {
            canvasGroup.blocksRaycasts = false;
        } else
        {
            canvasGroup.blocksRaycasts = true;
        }

        //let's also inactivate it so it won't waste cycles
        mySequence.OnComplete(() =>
           {

               if (alphaTarget == 0)
               {
                  panelObj.SetActive(false);
               } 
              
             // canvasGroup.interactable = true; //turn this back on
               canvasGroup.blocksRaycasts = true;
           });
       

        mySequence.SetAutoKill(true);
        mySequence.Play();
      
    }

    //Note: This will also activate/inactivate the object
    static public void FadeGUIImageByName(string name, float alphaTarget, float duration = 1.0f, float delayBeforeStart = 0.0f)
    {

        GameObject image = FindIncludingInactive(name);

        if (image == null)
        {
            RTConsole.Log("Can't find Image by the name of " + name + ", aborting FadeGUIImageByName");
            return;
        }

        if (image == null)
        {
            Debug.Log("FadeGUIImageByName: Can't find " + name);
            return;
        }

        if (!image.activeInHierarchy)
        {
            image.SetActive(true); //can't fade anything if it's off!
        }


        UnityEngine.UI.Image imageComp = image.GetComponent<UnityEngine.UI.Image>();

        if (imageComp == null)
        {
            //  Debug.Log("Adding canvas group, wonder if this works..");
            RTConsole.Log("Error, can't find image in object named " + name);
            return;
        }

        /*
        TweenTracker tracker = image.GetComponent<TweenTracker>();
        if (tracker == null)
        {
            //  Debug.Log("Adding canvas group, wonder if this works..");
            tracker = image.AddComponent<TweenTracker>();
        }

    */

        DOTween.Kill(image); //kill any outstanding commands...
        Sequence mySequence = DOTween.Sequence();
        mySequence.SetId(image);

        mySequence.AppendInterval(delayBeforeStart);
        //RTConsole.Log("Setting Image " + image.ToString() + " to alpha " + alphaTarget + " in " + duration);

        mySequence.Append(imageComp.DOFade(alphaTarget, duration));

        //canvasGroup.interactable = false; //stop additional clicking

/*
        if (alphaTarget == 0)
        {
            imageComp.raycastTarget
        }
        else
        {
            imageComp.blocksRaycasts = true;
        }
        */

        //let's also inactivate it so it won't waste cycles
        mySequence.OnComplete(() =>
        {

            if (alphaTarget == 0)
            {
                image.SetActive(false);
            }

            // canvasGroup.interactable = true; //turn this back on
           // imageComp.blocksRaycasts = true;
        });

        mySequence.SetAutoKill(true);
        mySequence.Play();

    }

    //Note: This will also activate/inactivate the object
    static public void SetFadeOfPanelByName(string name, float alphaTarget, float duration = 1.0f, float delayBeforeStart = 0.0f)
    {
        GameObject panelObj = FindIncludingInactive(name);

        if (panelObj == null)
        {
            RTConsole.Log("Can't find panel by the name of " + name + ", aborting SetFadeOfPanelByName");
            return;
        }
       
        if (!panelObj.activeInHierarchy)
        {
            panelObj.SetActive(true); //can't fade anything if it's off!

            HideGUI guiScript = panelObj.GetComponent<HideGUI>();
            if (guiScript != null)
            {
                //kill this, we don't actually want it to trigger now and hide the GUI.  This can be left on if the
                //panel was disabled manually, so this script never got be run
                GameObject.Destroy(guiScript);
            }
        }
       
        if (panelObj == null)
        {
            Debug.Log("SetFadeOfPanelByName: Can't find " + name);
            return;
        }

        CanvasGroup canvasGroup = panelObj.GetComponent<CanvasGroup>();

        if (canvasGroup == null)
        {
            //  Debug.Log("Adding canvas group, wonder if this works..");
            canvasGroup = panelObj.AddComponent<CanvasGroup>();
        }

        DOTween.Kill(panelObj); //kill any outstanding commands...
        Sequence mySequence = DOTween.Sequence();
        mySequence.SetId(panelObj);

        mySequence.AppendInterval(delayBeforeStart);
        //RTConsole.Log("Setting Image " + image.ToString() + " to alpha " + alphaTarget + " in " + duration);

        mySequence.Append(canvasGroup.DOFade(alphaTarget, duration));

        //canvasGroup.interactable = false; //stop additional clicking

        //let's also inactivate it so it won't waste cycles
        mySequence.OnComplete(() =>
        {

            if (alphaTarget == 0)
            {
                panelObj.SetActive(false);
            }

        });

        mySequence.SetAutoKill(true);
        mySequence.Play();

    }
    //Note: On webgl builds, this will allow things that create new browser windows (a facebook logon, for example) to work properly.  However, it MUST
    //be called from a callback from an OnPointerDown().  Use RTButton instead of Button for this.

    static public void PopupUnblockSendMessage(string objectName, string functionName, string optionalParm)
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        JLIB_PopupUnblockSendMessage(objectName, functionName, optionalParm);
#else
        //just do it the normal way, it's not needed
        GameObject o = GameObject.Find(objectName);

        if (!o)
        {
            Debug.Log("PopupUnblockSendMessage: Can't find gameobject " + objectName);
            return;
        }

        o.SendMessage(functionName, optionalParm);
#endif
    }

    static public void PopupUnblockOpenURL(string URL)
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        JLIB_PopupUnblockOpenURL(URL);
#else
        Application.OpenURL(URL);
#endif
    }

    static public void AccelerateTo(Rigidbody body, Vector3 targetVelocity, float maxAccel)
    {
        Vector3 deltaV = targetVelocity - body.linearVelocity;
        Vector3 accel = deltaV;

        if (accel.sqrMagnitude > maxAccel * maxAccel)
            accel = accel.normalized * maxAccel;

        body.AddForce(accel, ForceMode.Acceleration);
    }

    static public byte[] StreamToBytes(Stream input)
    {
        using (MemoryStream ms = new MemoryStream())
        {
            input.CopyTo(ms);
            return ms.ToArray();
        }
    }

    static public object StringToObjectOfType(string value, System.Type type)
    {

        if (type == typeof(bool))
        {
            bool b;
            System.Boolean.TryParse(value, out b);
            return b;
        }

        if (type == typeof(int))
        {
            int b;
            System.Int32.TryParse(value, out b);
            return b;
        }

        if (type == typeof(uint))
        {
            uint b;
            System.UInt32.TryParse(value, out b);
            return b;
        }

        if (type == typeof(float))
        {
            float b;
            System.Single.TryParse(value, NumberStyles.Any, CultureInfo.CurrentCulture, out b);
            return b;
        }

        if (type == typeof(System.String))
        {
            return value;
        }

        RTConsole.Log("What the hell is type " + type.ToString());
        Debug.Assert(false);
        return null;
    }
    static public void SetButtonText(string buttonName, string newText)
    {
        GameObject buttonObj = RTUtil.FindIncludingInactive(buttonName);

        if (!buttonObj)
        {
            RTConsole.Log("Can't find button named " + buttonName);
            return;
        }

        //RTConsole.Log("Setting button to " + newText);

#if !RT_NO_TEXMESH_PRO
        TMPro.TextMeshProUGUI textMeshPro = buttonObj.transform.GetComponentInChildren<TMPro.TextMeshProUGUI>();
        textMeshPro.text = newText;
#else
        RTConsole.Log("Add support for normal UGUI button text changing here");
#endif
    }


    //Partially taken from https://stackoverflow.com/questions/60806966/unity-webgl-check-if-mobile but rewritten to work with Unity 2020 on the js side
    static public bool IsOnMobile()
    {
#if !UNITY_EDITOR && UNITY_WEBGL
                                 return JLIB_IsOnMobile();
#endif
        return false;
    }

    //https://answers.unity.com/questions/973606/how-can-i-tell-if-the-mouse-is-over-the-game-windo.html (LW)
    static public bool IsMouseOverGameWindow
    {
        get { 
            return !(0 > Input.mousePosition.x || 0 > Input.mousePosition.y || Screen.width < Input.mousePosition.x || Screen.height < Input.mousePosition.y); 
        }
    }

    // detect headless mode (which has graphicsDeviceType Null)
    //From https://noobtuts.com/unity/detect-headless-mode
   
    public static string RemoveColorAndFontTagsEverything(string input) //Not SANSI `2 codes, but normal TMPro codes
    {
        // Match any color or font tag, including their attributes
        Regex regex = new Regex("<.*?>"); //while this works, it also removes ANYTHING between <> which may not be what you need
        // Replace matches with an empty string
        return regex.Replace(input, "");
    }

    //credit for below code goes to GPT-4 and Seth A. Robinson (3-21-23)

    public static string ConvertAnsiToTextMeshProColors(string input)
    {
        // Map ANSI color codes to TextMeshPro color hex values.
        Dictionary<int, string> ansiToTextMeshProColors = new Dictionary<int, string>
    {
        { 30, "000000" }, // Black
        { 31, "800000" }, // Red
        { 32, "008000" }, // Green
        { 33, "808000" }, // Yellow
        { 34, "000080" }, // Blue
        { 35, "800080" }, // Magenta
        { 36, "008080" }, // Cyan
        { 37, "C0C0C0" }, // White
        { 39, "FFFFFF" }, // Default foreground color (white)
        { 90, "808080" }, // Bright black (gray)
        { 91, "FF0000" }, // Bright red
        { 92, "00FF00" }, // Bright green
        { 93, "FFFF00" }, // Bright yellow
        { 94, "0000FF" }, // Bright blue
        { 95, "FF00FF" }, // Bright magenta
        { 96, "00FFFF" }, // Bright cyan
        { 97, "FFFFFF" }, // Bright white
        { 49, "000000" }, // Default background color (black)
    };

        // Add background colors to the dictionary.
        for (int i = 40; i <= 47; i++)
        {
            ansiToTextMeshProColors.Add(i, ansiToTextMeshProColors[i - 10]);
            ansiToTextMeshProColors.Add(i + 60, ansiToTextMeshProColors[i + 50]);
        }

        // Replace ANSI color codes with TextMeshPro color tags.
        return Regex.Replace(input, @"\x1B\[([\d;]+)m", match =>
        {
            string[] codes = match.Groups[1].Value.Split(';');
            string result = "";
            bool isBright = false;

            foreach (string codeStr in codes)
            {
                if (int.TryParse(codeStr, out int code))
                {
                    if (code == 1)
                    {
                        isBright = true;
                    }
                    else if (code >= 30 && code <= 37 || code >= 90 && code <= 97 || code == 39)
                    {
                        int colorCode = isBright && code <= 37 ? code + 60 : code;
                        result += $"<color=#{ansiToTextMeshProColors[colorCode]}>";
                        isBright = false;
                    }
                    else if (code == 0)
                    {
                        result += "</color>";
                        isBright = false;
                    }
                }
            }

            return result;
        });
    }

    //credit for the below code goes to  Jayden Chapman ( https://gitlab.com/-/snippets/2031682 ) and Psycho8Vegemite ( https://forum.unity.com/threads/removing-rich-text-tags-from-a-string.530360/#post-6439283 )
    public static string RemoveColorAndFontTags(string input)
    {

        input = RemoveRichTextDynamicTag(input, "color");

        input = RemoveRichTextTag(input, "b");
        input = RemoveRichTextTag(input, "i");
        input = RemoveRichTextTag(input, "u");

        // TMP
        input = RemoveRichTextDynamicTag(input, "align");
        input = RemoveRichTextDynamicTag(input, "size");
    
        /*
        input = RemoveRichTextDynamicTag(input, "cspace");
        input = RemoveRichTextDynamicTag(input, "font");
        input = RemoveRichTextDynamicTag(input, "indent");
        input = RemoveRichTextDynamicTag(input, "line-height");
        input = RemoveRichTextDynamicTag(input, "line-indent");
        input = RemoveRichTextDynamicTag(input, "link");
        input = RemoveRichTextDynamicTag(input, "margin");
        input = RemoveRichTextDynamicTag(input, "margin-left");
        input = RemoveRichTextDynamicTag(input, "margin-right");
        input = RemoveRichTextDynamicTag(input, "mark");
        input = RemoveRichTextDynamicTag(input, "mspace");
        input = RemoveRichTextDynamicTag(input, "noparse");
        input = RemoveRichTextDynamicTag(input, "nobr");
        input = RemoveRichTextDynamicTag(input, "page");
        input = RemoveRichTextDynamicTag(input, "pos");
        input = RemoveRichTextDynamicTag(input, "space");
        input = RemoveRichTextDynamicTag(input, "sprite index");
        input = RemoveRichTextDynamicTag(input, "sprite name");
        input = RemoveRichTextDynamicTag(input, "sprite");
        input = RemoveRichTextDynamicTag(input, "style");
        input = RemoveRichTextDynamicTag(input, "voffset");
        input = RemoveRichTextDynamicTag(input, "width");

   ;
        input = RemoveRichTextTag(input, "s");
        input = RemoveRichTextTag(input, "sup");
        input = RemoveRichTextTag(input, "sub");
        input = RemoveRichTextTag(input, "allcaps");
        input = RemoveRichTextTag(input, "smallcaps");
        input = RemoveRichTextTag(input, "uppercase");
        */
        // TMP end


        return input;

    }


    public static Texture2D RenderTextToTexture2D(string text, int width, int height, TMP_FontAsset font, float fontSize, Color color, bool bAutoSize, Vector2 vTextRectSizeMod, FontStyles fontStyles = 0)
    {
        //Debug.Log("Creating tex sized " + width + "x" + height);
        // Create GameObject and TextMeshPro components
        GameObject go = new GameObject();
        go.layer = 31; // Use an unused layer
        TextMeshPro tmp = go.AddComponent<TextMeshPro>();

        // Setup TextMeshPro settings
        tmp.text = text;
        tmp.font = font;
        tmp.fontSize = fontSize;
        tmp.color = color;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.rectTransform.pivot = new Vector2(0.5f, 0.5f);
        tmp.rectTransform.anchoredPosition3D = Vector3.zero;

        //text wrap size

        tmp.rectTransform.sizeDelta = new Vector2(width * vTextRectSizeMod.x, height * vTextRectSizeMod.y);

        tmp.enableAutoSizing = bAutoSize;
        tmp.fontSizeMax = 9999999;
        //set the font to be bold
        tmp.fontStyle = fontStyles;
        tmp.name = "TextMeshProATemp";
        tmp.enableWordWrapping = true;
        //set largest allowed font size
        // Create a RenderTexture
        RenderTexture renderTexture = new RenderTexture(width, height, 24);

        // Create a new temporary Camera
        GameObject tempCameraObject = new GameObject();
        Camera tempCamera = tempCameraObject.AddComponent<Camera>();

        // Position the camera to capture the text object
        tempCamera.transform.position = Vector3.zero;
        tempCamera.transform.position -= new Vector3(0, 0, 10);  // move back a bit
        tempCamera.clearFlags = CameraClearFlags.Color;
        tempCamera.backgroundColor = Color.clear; // transparent background
        tempCamera.orthographic = true;

        float maxSize = width;
        if (height > maxSize)
        {
            maxSize = height;
        }

        float minSize = width;
        if (height < minSize)
        {
            minSize = height;
        }

        tempCamera.orthographicSize = minSize / 2;  // set orthographic size
        tempCamera.targetTexture = renderTexture;  // set target texture
        tempCamera.cullingMask = 1 << 31;  // Set camera to only render layer 31
        tempCamera.name = "TextCamera";
        //tempCamera.nearClipPlane = 0;
        //tempCamera.farClipPlane = 100000;

        // Wait for the camera to finish rendering
        tempCamera.Render();

        // Create a Texture2D to hold the captured image
        Texture2D tex2D = new Texture2D(width, height, TextureFormat.RGBA32, false);

        // Copy from the RenderTexture to the Texture2D
        RenderTexture.active = renderTexture;
        tex2D.ReadPixels(new Rect(0, 0, renderTexture.width, renderTexture.height), 0, 0);
        tex2D.Apply();

        // Deactivate the render texture
        RenderTexture.active = null;

        // Clean up objects.  To debug positions, comment out below so you can see then in the scene
        
        GameObject.Destroy(tempCameraObject);
        GameObject.Destroy(go);

        return tex2D;
    }

   public static bool IsMemoryLow()
    {
        // Check system memory
        long totalMemory = SystemInfo.systemMemorySize * 1024L * 1024L; // Convert to bytes
        long reservedMemory = Profiler.GetTotalReservedMemoryLong();
        long threshold = totalMemory * 90 / 100; // 90% usage as threshold

        return reservedMemory >= threshold;
    }

    public static Color GetARandomBrightColor()
    {
        //return a random bright color
        return new Color(UnityEngine.Random.Range(0.5f, 1.0f), UnityEngine.Random.Range(0.5f, 1.0f), UnityEngine.Random.Range(0.5f, 1.0f), 1.0f);
    }


    private static string RemoveRichTextDynamicTag(string input, string tag)
    {
        int index = -1;
        while (true)
        {
            index = input.IndexOf($"<{tag}=");
            //Debug.Log($"{{{index}}} - <noparse>{input}");
            if (index != -1)
            {
                int endIndex = input.Substring(index, input.Length - index).IndexOf('>');
                if (endIndex > 0)
                    input = input.Remove(index, endIndex + 1);
                continue;
            }
            input = RemoveRichTextTag(input, tag, false);
            return input;
        }
    }
    private static string RemoveRichTextTag(string input, string tag, bool isStart = true)
    {
        while (true)
        {
            int index = input.IndexOf(isStart ? $"<{tag}>" : $"</{tag}>");
            if (index != -1)
            {
                input = input.Remove(index, 2 + tag.Length + (!isStart).GetHashCode());
                continue;
            }
            if (isStart)
                input = RemoveRichTextTag(input, tag, false);
            return input;
        }
    }

    static public bool IsHeadless()
    {
        return SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null;
    }

    //By Unitycoder, snippet from https://gist.github.com/unitycoder/58f4b5d80f423d29e35c814a9556f9d9 
    static public void DrawDebugBounds(Bounds b, float delay = 0)
    {
        // bottom
        var p1 = new Vector3(b.min.x, b.min.y, b.min.z);
        var p2 = new Vector3(b.max.x, b.min.y, b.min.z);
        var p3 = new Vector3(b.max.x, b.min.y, b.max.z);
        var p4 = new Vector3(b.min.x, b.min.y, b.max.z);

        Debug.DrawLine(p1, p2, Color.blue, delay);
        Debug.DrawLine(p2, p3, Color.red, delay);
        Debug.DrawLine(p3, p4, Color.yellow, delay);
        Debug.DrawLine(p4, p1, Color.magenta, delay);

        // top
        var p5 = new Vector3(b.min.x, b.max.y, b.min.z);
        var p6 = new Vector3(b.max.x, b.max.y, b.min.z);
        var p7 = new Vector3(b.max.x, b.max.y, b.max.z);
        var p8 = new Vector3(b.min.x, b.max.y, b.max.z);

        Debug.DrawLine(p5, p6, Color.blue, delay);
        Debug.DrawLine(p6, p7, Color.red, delay);
        Debug.DrawLine(p7, p8, Color.yellow, delay);
        Debug.DrawLine(p8, p5, Color.magenta, delay);

        // sides
        Debug.DrawLine(p1, p5, Color.white, delay);
        Debug.DrawLine(p2, p6, Color.gray, delay);
        Debug.DrawLine(p3, p7, Color.green, delay);
        Debug.DrawLine(p4, p8, Color.cyan, delay);
    }

    static public void SetButtonColor(Button but, Color col)
    {
        var colors = but.colors;
        colors.normalColor = col;
        colors.selectedColor = col;
        colors.highlightedColor = col;
        but.colors = colors;
    }
    /// <summary>
    /// Copies a file from source to destination, creating directories if needed.
    /// </summary>
    /// <param name="fileSource">Source file path</param>
    /// <param name="fileDestination">Destination file path</param>
    /// <returns>True if copy was successful, false if there was an error</returns>
    public static bool CopyFile(string fileSource, string fileDestination)
    {
        try
        {
            if (string.IsNullOrEmpty(fileSource) || string.IsNullOrEmpty(fileDestination))
            {
                Debug.LogError("RTUtil.CopyFile: Source or destination path is null or empty");
                return false;
            }

            if (!File.Exists(fileSource))
            {
                Debug.LogError($"RTUtil.CopyFile: Source file does not exist: {fileSource}");
                return false;
            }

            // Create the destination directory if it doesn't exist
            string destDirectory = Path.GetDirectoryName(fileDestination);
            if (!string.IsNullOrEmpty(destDirectory) && !Directory.Exists(destDirectory))
            {
                Directory.CreateDirectory(destDirectory);
            }

            // If destination file exists, delete it first
            if (File.Exists(fileDestination))
            {
                File.Delete(fileDestination);
            }

            File.Copy(fileSource, fileDestination);
            return true;
        }
        catch (System.Exception e)
        {
            Debug.LogError($"RTUtil.CopyFile: Error copying file from {fileSource} to {fileDestination}: {e.Message}");
            return false;
        }
    }

}
