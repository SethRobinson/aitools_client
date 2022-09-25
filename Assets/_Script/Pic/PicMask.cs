using System;
using System.Collections;
using System.Collections.Generic;

using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
public class PicMask : MonoBehaviour
{

    Camera m_cam;
    public SpriteRenderer m_spriteMask;
    public SpriteRenderer m_pic;
    bool m_boolMaskHasBeenSet = false;
    bool m_bMaskModified;
    public PicTargetRect m_targetRectScript;

    // Start is called before the first frame update
    void Start()
    {
        //m_spriteRenderer.color = Color.black;
         m_spriteMask.color = new Color(1.0f, 0.0f, 0.0f, 0.4f);

        if (!m_boolMaskHasBeenSet)
        {
            RecreateMask();
        }

        SetMaskVisible(false); //start with it off
    }

    private void Awake()
    {
        m_cam = RTUtil.FindObjectOrCreate("Camera").GetComponent<Camera>();
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
        m_spriteMask.sprite.texture.Fill(new Color(0, 0, 0, 0));
        m_spriteMask.sprite.texture.Apply();
        m_bMaskModified = false;
    }

    public void SetMaskFromSprite(Sprite s)
    {
        m_spriteMask.sprite = s;
        m_boolMaskHasBeenSet = true;
        m_bMaskModified = true; //it's possible it's still blank, but has been modified from default
    }
    public void RecreateMask()
    {
        Texture2D copyTexture = new Texture2D(m_pic.sprite.texture.width, m_pic.sprite.texture.height, TextureFormat.RGBA32, false);
        float biggestSize = Math.Max(copyTexture.width, copyTexture.height);
        m_spriteMask.sprite = Sprite.Create(copyTexture, new Rect(0, 0, copyTexture.width, copyTexture.height), new Vector2(0.5f, 0.5f), biggestSize / 5.12f);
        copyTexture.Fill(new Color(0, 0, 0, 0));
        copyTexture.Apply();
        m_boolMaskHasBeenSet = true;
        m_bMaskModified = false;

        //Debug.Log("Recreated mask");
    }
    public void ResizeMaskIfNeeded()
    {
        if (m_spriteMask.sprite.texture.width != m_pic.sprite.texture.width || m_spriteMask.sprite.texture.height != m_pic.sprite.texture.height)
        {
            RecreateMask();
            //Debug.Log("Mask resized to " + m_spriteMask.sprite.texture.width);
        }

    }

    void Update()
    {

        ResizeMaskIfNeeded();

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

                Texture2D copyTexture = new Texture2D(oldTex.width, oldTex.height, TextureFormat.RGBA32, false);
                copyTexture.SetPixels(oldTex.GetPixels());

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
                copyTexture.SetPixelsWithinRadius((int)vTexPos.x, (int)vTexPos.y, brushSize, drawColor);
                copyTexture.Apply();
                float biggestSize = Math.Max(copyTexture.width, copyTexture.height);
                m_spriteMask.sprite = Sprite.Create(copyTexture, new Rect(0, 0, copyTexture.width, copyTexture.height), new Vector2(0.5f, 0.5f), biggestSize / 5.12f);
                m_boolMaskHasBeenSet = true;
            }


        }
    }
}
