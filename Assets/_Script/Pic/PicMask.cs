using System;
using System.Collections;
using System.Collections.Generic;

using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using static UnityEngine.Rendering.HableCurve;

public class PicMask : MonoBehaviour
{

    Camera m_cam;
    public SpriteRenderer m_spriteMask;
    public SpriteRenderer m_pic;
    bool m_boolMaskHasBeenSet = false;
    bool m_bMaskModified;
    public PicTargetRect m_targetRectScript;
    public LineRenderer m_brushSizeLineRenderer;

    // Start is called before the first frame update
    void Start()
    {
        //m_spriteRenderer.color = Color.black;
         m_spriteMask.color = new Color(1.0f, 0.0f, 0.0f, 0.4f);

        if (!m_boolMaskHasBeenSet)
        //if (m_spriteMask.sprite == null || m_spriteMask.sprite.texture == null)
        {
            
            RecreateMask();
        }

     
        SetMaskVisible(false); //start with it off
    }

    private void Awake()
    {
        m_cam = Camera.allCameras[0];
    }
    // Update is called once per frame
    //based on code from https://answers.unity.com/questions/609629/how-to-get-pixel-color-for-sprite-u43.html


    public void FillAlphaMaskIfBlank()
    {
        if (!m_bMaskModified)
        {
            m_spriteMask.sprite.texture.Fill(new Color(1, 1, 1, 1));
            m_spriteMask.sprite.texture.Apply();
            m_bMaskModified = true;
        }
    }
    public bool GetSpritePixelColorUnderMousePointer(SpriteRenderer spriteRenderer, out Color color, out Vector2 vTexPos)
    {
        color = new Color();
        vTexPos = new Vector2();
        Vector2 mousePos = Input.mousePosition;
        Vector2 viewportPos = m_cam.ScreenToViewportPoint(mousePos);
        if (viewportPos.x < 0.0f || viewportPos.x > 1.0f || viewportPos.y < 0.0f || viewportPos.y > 1.0f) return false; // out of viewport bounds
                                                                                                                        // Cast a ray from viewport point into world
        Ray ray = m_cam.ViewportPointToRay(viewportPos);

        // Check for intersection with sprite and get the color
        return IntersectsSprite(spriteRenderer, ray, out color, out vTexPos);
    }
    private bool IntersectsSprite(SpriteRenderer spriteRenderer, Ray ray, out Color color, out Vector2 vTexPos)
    {
        vTexPos = new Vector2();
        color = new Color();
        if (spriteRenderer == null) return false;
        Sprite sprite = spriteRenderer.sprite;
        if (sprite == null) return false;
        Texture2D texture = sprite.texture;
        if (texture == null) return false;
        // Check atlas packing mode
        if (sprite.packed && sprite.packingMode == SpritePackingMode.Tight)
        {
            // Cannot use textureRect on tightly packed sprites
            Debug.LogError("SpritePackingMode.Tight atlas packing is not supported!");
            // TODO: support tightly packed sprites
            return false;
        }
        // Craete a plane so it has the same orientation as the sprite transform
        Plane plane = new Plane(transform.forward, transform.position);
        // Intersect the ray and the plane
        float rayIntersectDist; // the distance from the ray origin to the intersection point
        if (!plane.Raycast(ray, out rayIntersectDist)) return false; // no intersection
                                                                     // Convert world position to sprite position
                                                                     // worldToLocalMatrix.MultiplyPoint3x4 returns a value from based on the texture dimensions (+/- half texDimension / pixelsPerUnit) )
                                                                     // 0, 0 corresponds to the center of the TEXTURE ITSELF, not the center of the trimmed sprite textureRect
        Vector3 spritePos = spriteRenderer.worldToLocalMatrix.MultiplyPoint3x4(ray.origin + (ray.direction * rayIntersectDist));
        Rect textureRect = sprite.rect; //sprite.textureRect; changed because I want to include blank areas
        float pixelsPerUnit = sprite.pixelsPerUnit;
        float halfRealTexWidth = texture.width * 0.5f; // use the real texture width here because center is based on this -- probably won't work right for atlases
        float halfRealTexHeight = texture.height * 0.5f;
        // Convert to pixel position, offsetting so 0,0 is in lower left instead of center
        int texPosX = (int)(spritePos.x * pixelsPerUnit + halfRealTexWidth);
        int texPosY = (int)(spritePos.y * pixelsPerUnit + halfRealTexHeight);
        // Check if pixel is within texture
        if (texPosX < 0 || texPosX < textureRect.x || texPosX >= Mathf.FloorToInt(textureRect.xMax)) return false; // out of bounds
        if (texPosY < 0 || texPosY < textureRect.y || texPosY >= Mathf.FloorToInt(textureRect.yMax)) return false; // out of bounds
                                                                                                                   // Get pixel color
        color = texture.GetPixel(texPosX, texPosY);
        vTexPos = new Vector2(texPosX, texPosY);
        return true;
    }

    public void SetMaskVisible(bool bNew)
    {
        m_spriteMask.gameObject.SetActive(bNew);

        //m_spriteMask.forceRenderingOff = !bNew;
    }
    public bool IsMaskVisible()
    {
        return m_spriteMask.gameObject.activeSelf;
    }
    public void OnToggleMaskViewButton()
    {
        SetMaskVisible(!IsMaskVisible());
    }
    
    public void OnClearMaskButton()
    {
        //m_spriteMask.sprite.texture.Fill(new Color(0, 0, 0, 0));
        //m_spriteMask.sprite.texture.Apply();
        //um for the above to actually work you'd need to rebuild the sprite I guess.  Easier to just do this:
        RecreateMask();
        m_boolMaskHasBeenSet = true; 
        m_bMaskModified = false;
    }

    public void OnDestroy()
    {
        KillMask();
    }

    public void KillMask()
    {
        if (m_spriteMask.sprite != null && m_spriteMask.sprite.texture != null)
        {
            Destroy(m_spriteMask.sprite.texture);
            Destroy(m_spriteMask.sprite);
        }
    }
    public void SetMaskFromSprite(Sprite s)
    {
        KillMask();

        m_spriteMask.sprite = s;
        m_boolMaskHasBeenSet = true;
        m_bMaskModified = true; //it's possible it's still blank, but has been modified from default
    }
    public void SetMaskFromTexture(Texture2D copyTexture)
    {
        KillMask();


        float biggestSize = Math.Max(copyTexture.width, copyTexture.height);
        m_spriteMask.sprite = Sprite.Create(copyTexture, new Rect(0, 0, copyTexture.width, copyTexture.height), new Vector2(0.5f, 0.5f), biggestSize / 5.12f, 0, SpriteMeshType.FullRect);
        m_boolMaskHasBeenSet = true;
        m_bMaskModified = true;

        //Debug.Log("Recreated mask");
    }

    public void SetMaskFromTextureAlpha(Texture2D copyTexture)
    {
        RecreateMask();

        m_spriteMask.sprite.texture.SetColorAndAlphaFromAlphaChannel(copyTexture);
        m_spriteMask.sprite.texture.Apply();
        //float biggestSize = Math.Max(copyTexture.width, copyTexture.height);
        //m_spriteMask.sprite = Sprite.Create(copyTexture, new Rect(0, 0, copyTexture.width, copyTexture.height), new Vector2(0.5f, 0.5f), biggestSize / 5.12f, 0, SpriteMeshType.FullRect);

        m_boolMaskHasBeenSet = true;
        m_bMaskModified = true;

    }

    public void ForceMaskRectToBeWithinImageBounds()
    {

        var maskRect = m_targetRectScript.GetOffsetRect();

        if (maskRect.x < 0)
        {
            maskRect.x = 0;
        }
        if (maskRect.y < 0)
        {
            maskRect.y = 0;
        }


        while (maskRect.width > m_spriteMask.sprite.texture.width && m_spriteMask.sprite.texture.width > 64)
        {
            //still?  Fine

            maskRect.width -= 1;
            int temp = (int)maskRect.width;
            maskRect.width = RTUtil.ConvertNumToNearestMultiple((int)maskRect.width, 64);

            if (maskRect.width > m_spriteMask.sprite.texture.width)
            {
                maskRect.width = temp;
            }
        }

        while (maskRect.height > m_spriteMask.sprite.texture.height && m_spriteMask.sprite.texture.height > 64)
        {
            //still?  Fine

            maskRect.height -= 1;
            int temp = (int)maskRect.height;
            maskRect.height = RTUtil.ConvertNumToNearestMultiple((int)maskRect.height, 64);

            if (maskRect.height > m_spriteMask.sprite.texture.height)
            {
                maskRect.height = temp;
            }
        }


        if (maskRect != m_targetRectScript.GetOffsetRect())
        {
            m_targetRectScript.SetOffsetRect(maskRect);
        }
    }
    public void RecreateMask()
    {
        Texture2D copyTexture = new Texture2D(m_pic.sprite.texture.width, m_pic.sprite.texture.height, TextureFormat.RGBA32, false);
        float biggestSize = Math.Max(copyTexture.width, copyTexture.height);

        KillMask();

        copyTexture.Fill(new Color(0, 0, 0, 0));
        copyTexture.Apply();

        m_spriteMask.sprite = Sprite.Create(copyTexture, new Rect(0, 0, copyTexture.width, copyTexture.height), new Vector2(0.5f, 0.5f), biggestSize / 5.12f, 0, SpriteMeshType.FullRect);
        
        m_boolMaskHasBeenSet = true;
        m_bMaskModified = false;

        //Debug.Log("Recreated mask");
    }
    public void ResizeMaskIfNeeded()
    {
        if (m_spriteMask.sprite == null || m_spriteMask.sprite.texture == null)
        {
            RecreateMask();

        }
        else
        {
            if (m_spriteMask.sprite.texture.width != m_pic.sprite.texture.width || m_spriteMask.sprite.texture.height != m_pic.sprite.texture.height)
            {
                RecreateMask();
                //Debug.Log("Mask resized to " + m_spriteMask.sprite.texture.width);
            }
        }
        
        ForceMaskRectToBeWithinImageBounds();
    }

    void DrawCircle(float radius, int segments)
    {
        float x;
        float y;
        float z = 0f;

        float angle = 0;

        // Set the number of vertices in the line renderer
        m_brushSizeLineRenderer.positionCount = segments + 1;

        for (int i = 0; i < (segments + 1); i++)
        {
            x = Mathf.Sin(Mathf.Deg2Rad * angle) * radius;
            y = Mathf.Cos(Mathf.Deg2Rad * angle) * radius;

            m_brushSizeLineRenderer.SetPosition(i, new Vector3(x, y, z));

            angle += (360f / (float)segments);
        }
    }

    void Update()
    {

       
        if (IsMaskVisible())
        {
            ResizeMaskIfNeeded();

           

            if (GameLogic.Get().GetPicWereHoveringOver() == gameObject && !GameLogic.Get().GUIIsBeingUsed())
            {
                m_brushSizeLineRenderer.enabled = true;

                //we're hovering over this image, let's draw the brush size
                int segments = 64;
                Color color;
             
                color = new Color();
             
                    // Cast a ray from viewport point into world
                    var vOrigin = gameObject.transform.position;
                    vOrigin.y += 2.5f; //5.12/2 minus some extra leeway
                    vOrigin.z = 0.3f;

                    Ray ray = new Ray(vOrigin, new Vector3(0, 0, 1));

                    Vector2 vTexPos1 = new Vector2();
                    Vector2 vTexPos2 = new Vector2();

                    if (IntersectsSprite(m_spriteMask, ray, out color, out vTexPos1))
                    {
                        vOrigin.x += 0.1f;
                        ray.origin = vOrigin;

                        if (IntersectsSprite(m_spriteMask, ray, out color, out vTexPos2))
                        {
                            float ratio = vTexPos2.x - vTexPos1.x;

                        //Debug.Log("Texpos 1: " + vTexPos1+" Textpos2: "+vTexPos2+" Ratio: "+ratio);
                        float radius = (GameLogic.Get().GetPenSize() * 0.1f) / ratio;

                        Vector3 vClickWorldPos = m_cam.ScreenToWorldPoint(Input.mousePosition);
                        var vTempPos = m_brushSizeLineRenderer.gameObject.transform.position;
                        vTempPos.x = vClickWorldPos.x;
                        vTempPos.y = vClickWorldPos.y;

                        m_brushSizeLineRenderer.gameObject.transform.position = vTempPos;
                        DrawCircle(radius, segments);
                    }

                }

                    
            } else
            {
                //mouse isn't over this pic, so no reason to show the brush size overlay
                m_brushSizeLineRenderer.enabled= false;
            }

        }

      

        if ((Input.GetMouseButton(0) || Input.GetMouseButtonUp(0)) && !EventSystem.current.IsPointerOverGameObject())
        {
            GameObject go = GameLogic.Get().GetPicWereHoveringOver();
            if (go != gameObject) return;

            Vector2 vTexPos;
            Vector3 vClickWorldPos = m_cam.ScreenToWorldPoint(Input.mousePosition);
            Vector2 vWorldClickPos = new Vector2(vClickWorldPos.x, vClickWorldPos.y);
            Color color;

            if (GetSpritePixelColorUnderMousePointer(m_spriteMask, out color, out vTexPos))
            {
                //Debug.Log("HIT: " +color+" at vTexPos: "+vTexPos);

                SetMaskVisible(true);
                var oldTex = m_spriteMask.sprite.texture;

             
                int brushSize = (int)GameLogic.Get().GetPenSize();
                Color drawColor = new Color(1, 1, 1, 1);

                Vector2 clickedPos = new Vector2(vTexPos.x, m_spriteMask.sprite.texture.height - vTexPos.y);

                if (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl))
                {
                    //move target rect if possible
                    var vCenteredRectPos = new Vector2(vTexPos.x - (m_targetRectScript.GetWidth() / 2), (m_spriteMask.sprite.texture.height - vTexPos.y) - (m_targetRectScript.GetHeight() / 2));
                    
                    m_targetRectScript.OnMoveToPixelLocation(vCenteredRectPos);
                    return;
                }

                if (Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt))
                {
                    //hold alt for erase mask
                    drawColor = new Color(0, 0, 0, 0);
                }
                if (Input.GetMouseButtonDown(0))
                {
                    m_targetRectScript.OnClickedPos(clickedPos);
                } else
                {
                    m_targetRectScript.OnMovedPos(clickedPos);
                }

                if (m_targetRectScript.IsMovingRect())
                {
                    //moving the rect around, don't screw with the actual mask
                    return;
                }

                m_bMaskModified = true;

                //modify the our mask with the brush
                m_spriteMask.sprite.texture.SetPixelsWithinRadius((int)vTexPos.x, (int)vTexPos.y, brushSize, drawColor);
                m_spriteMask.sprite.texture.Apply();

                m_boolMaskHasBeenSet = true;
            }


        }
    }
}
