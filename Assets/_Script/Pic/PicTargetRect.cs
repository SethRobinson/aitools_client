using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using static UnityEngine.UI.AspectRatioFitter;

public class PicTargetRect : MonoBehaviour
{
    Rect m_targetRectInPixels = new Rect(0,0,512,512);
    public SpriteRenderer m_picRenderer;
    public LineRenderer m_lineRen;
    public SpriteRenderer m_picMaskRenderer;


    // Start is called before the first frame update
    void Start()
    {
        UpdatePoints();
    }
    public int GetWidth()
    {
        return (int)m_targetRectInPixels.width;
    }
    public int GetHeight()
    {
        return (int)m_targetRectInPixels.height;
    }
    public int GetOffsetX()
    {
        return (int)m_targetRectInPixels.x;
    }
    public int GetOffsetY()
    {
        return (int)m_targetRectInPixels.y;
    }

    public Rect GetOffsetRect()
    {
        return m_targetRectInPixels;
    }

    public void OnMoveToPixelLocation(Vector2 vPixelPos)
    {
        //let's move our hardcoded rect size there
        m_targetRectInPixels.x = vPixelPos.x;
        m_targetRectInPixels.y = vPixelPos.y;

        //force within bounds

        if (m_targetRectInPixels.x < 0) m_targetRectInPixels.x = 0;
        if (m_targetRectInPixels.y < 0) m_targetRectInPixels.y = 0;

        if (m_targetRectInPixels.x +GetWidth() > m_picRenderer.sprite.texture.width)
        {
            m_targetRectInPixels.x = m_picRenderer.sprite.texture.width - GetWidth();
        }

        if (m_targetRectInPixels.y + GetHeight() > m_picRenderer.sprite.texture.height)
        {
            m_targetRectInPixels.y = m_picRenderer.sprite.texture.height - GetHeight();
        }
        UpdatePoints();
    }
    public Rect ConvertPixelRectToWorldRect(Rect targetRectToDraw)
    {
        var picRect = m_picRenderer.sprite.textureRect;
        var spriteSize = m_picRenderer.size;

        float trueWorldY = m_picRenderer.bounds.max.y - m_picRenderer.bounds.min.y;
        float trueWorldX = m_picRenderer.bounds.max.x - m_picRenderer.bounds.min.x;

        float sizeModX = picRect.size.x / spriteSize.x;
        float sizeModY = picRect.size.y / spriteSize.y;
        float aspectYMod = trueWorldY / spriteSize.y;
        float aspectXMod = trueWorldX / spriteSize.x;

        float aspectRatio = picRect.size.x / picRect.size.y;

        picRect.size = new Vector2( (targetRectToDraw.size.x / sizeModX)* aspectXMod, (targetRectToDraw.size.y / sizeModY) * aspectYMod);

        //offset based on where we've moved the rect
        picRect.x =  (targetRectToDraw.x/ sizeModX) * aspectXMod;
        picRect.y =  (targetRectToDraw.y / sizeModY) * aspectYMod;
        //Debug.Log("PicMain Size: " + m_picRenderer.sprite.textureRect + " TrueworldX: " + trueWorldX+" Renderer bounds:"+ m_picRenderer.bounds+" aspect:"+aspectRatio);
        //Debug.Log("PicMain offset: "+ targetRectToDraw+" AspectX: " + aspectXMod+" AspectY:"+aspectYMod);
        //offset added to align it with the image
        picRect.x -= (trueWorldX / 2);
        picRect.y -= (trueWorldY /2);
        return picRect;
    }
    
    public void UpdatePoints()
    {
        var picRect = ConvertPixelRectToWorldRect(m_targetRectInPixels);

        int lengthOfLineRenderer = 5;
        var points = new Vector3[lengthOfLineRenderer];
        m_lineRen.positionCount = lengthOfLineRenderer;

        points[0] = new Vector3(picRect.xMin, -picRect.yMax, 0);
        points[1] = new Vector3(picRect.xMax, -picRect.yMax, 0);
        points[2] = new Vector3(picRect.xMax, -picRect.yMin, 0);
        points[3] = new Vector3(picRect.xMin, -picRect.yMin, 0);
        points[4] = new Vector3(picRect.xMin, -picRect.yMax, 0);

        //if we wanted the actual entire pic based on its collider I guess
        /*
        points[0] = new Vector3(m_picRenderer.bounds.min.x, m_picRenderer.bounds.max.y, 0);
        points[1] = new Vector3(m_picRenderer.bounds.max.x, m_picRenderer.bounds.max.y, 0);
        points[2] = new Vector3(m_picRenderer.bounds.max.x, m_picRenderer.bounds.min.y, 0);
        points[3] = new Vector3(m_picRenderer.bounds.min.x, m_picRenderer.bounds.min.y, 0);
        points[4] = new Vector3(m_picRenderer.bounds.min.x, m_picRenderer.bounds.max.y, 0);
        */

        m_lineRen.SetPositions(points);
    }

// Update is called once per frame
void Update()
    {
        m_lineRen.enabled = !m_picMaskRenderer.forceRenderingOff;
    }
}
