using UnityEngine;

public class PicTargetRect : MonoBehaviour
{
    Rect m_targetRectInPixels = new Rect(0, 0, 768, 768);
    public SpriteRenderer m_picRenderer;
    public LineRenderer m_lineRen;
    public SpriteRenderer m_picMaskRenderer;
    public GameObject m_sizeDragIcon;
    public GameObject m_posDragIcon;

    bool m_bMovingRect = false;
    bool m_bMovingPosition = false;
    static float iconOffset = 0.045f;
    int iconOffsetPixels = 15;

    // Start is called before the first frame update
    void Start()
    {
        UpdatePoints();

    }

    public void MoveRectToTopLeft()
    {
        m_targetRectInPixels.x = 0;
        m_targetRectInPixels.y = 0;
        UpdatePoints();
    }
    public bool IsMovingRect() { return m_bMovingRect; }

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

    public void SetOffsetRect(Rect rect)
    {
        m_targetRectInPixels = rect;
        UpdatePoints();
        OnMoveToPixelLocation(m_targetRectInPixels.position);

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
            m_targetRectInPixels.x = RTUtil.ConvertNumToNearestMultiple((int)(m_picRenderer.sprite.texture.width - GetWidth()), 64);
        }

        if (m_targetRectInPixels.y + GetHeight() > m_picRenderer.sprite.texture.height)
        {
            m_targetRectInPixels.y = RTUtil.ConvertNumToNearestMultiple( (int)(m_picRenderer.sprite.texture.height - GetHeight()), 64);
        }
        UpdatePoints();
    }
    public Rect ConvertPixelRectToWorldRect(Rect targetRectToDraw)
    {
        //make sure there really is a sprite
        if (m_picRenderer.sprite == null) return new Rect(0, 0, 0, 0);
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

        var color = Color.yellow;

        if (m_targetRectInPixels.width % 256 != 0)
        {
            color.g -= 0.25f;
            color.r -= 0.25f;
        }

        if (m_targetRectInPixels.height % 256 != 0)
        {
            color.g -= 0.25f;
            color.r -= 0.25f;
        }

        m_lineRen.startColor = color;
        m_lineRen.endColor = color;

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

        //update icon positions
  
        m_posDragIcon.transform.localPosition = new Vector3(picRect.position.x+ iconOffset, -(picRect.position.y+ iconOffset), m_sizeDragIcon.transform.localPosition.z);
        m_sizeDragIcon.transform.localPosition = new Vector3(picRect.xMax- iconOffset, -(picRect.yMax- iconOffset), m_sizeDragIcon.transform.localPosition.z);


    }

    // Update is called once per frame
    public void OnClickedPos(Vector2 clickedPos)
   {
        float distanceFromRectNeeded = 24; //bigger makes the corners easier to click, but the bigger the image the
                                           //more pixels we need or it's too hard to click
        var picRect = m_picRenderer.sprite.textureRect;

        float scaleRatio = picRect.width / 512;
        if (scaleRatio > 1.1f)
        {
            scaleRatio -= 1.0f;

            distanceFromRectNeeded *= 1+ (scaleRatio*0.25f);
        }

       
        //did they click one of the corners?
        Vector2 vBottomRightOfRect = new Vector2(m_targetRectInPixels.xMax- iconOffsetPixels, m_targetRectInPixels.yMax- iconOffsetPixels);
        float distFromCorner = (vBottomRightOfRect - clickedPos).magnitude;
        if (distFromCorner < distanceFromRectNeeded)
        {
            m_bMovingRect = true;
            m_bMovingPosition = false;
            return; //they are dragging the bottom right
        }

        //check for top left too
        Vector2 vTopLeftOfRect = new Vector2(m_targetRectInPixels.xMin+ iconOffsetPixels, m_targetRectInPixels.yMin+ iconOffsetPixels);

        distFromCorner = (vTopLeftOfRect - clickedPos).magnitude;
        if (distFromCorner < distanceFromRectNeeded)
        {
            m_bMovingRect = true;
            m_bMovingPosition = true;
            return; //they are dragging the bottom right
        }

    }

    public void OnMovedPos(Vector2 movedPos)
    {

        if (!m_bMovingRect) return;

        if (m_bMovingPosition)
        {
            movedPos.x -= iconOffsetPixels;
            movedPos.y -= iconOffsetPixels;

            OnMoveToPixelLocation(movedPos);
            return;
        }

        //else, they are dragging out the size here
        movedPos.x += iconOffsetPixels;
        movedPos.y += iconOffsetPixels;

        Rect newRect = new Rect(m_targetRectInPixels.position, movedPos - m_targetRectInPixels.position);
       // Debug.Log("updating rect: " + movedPos);

        
        newRect.width = RTUtil.ConvertNumToNearestMultiple((int)newRect.width, 64);
        newRect.height = RTUtil.ConvertNumToNearestMultiple((int)newRect.height, 64);

        if (m_targetRectInPixels.x + newRect.width > m_picRenderer.sprite.texture.width
            ||
            m_targetRectInPixels.y + newRect.height > m_picRenderer.sprite.texture.height
            )
        {
            return; //no good
        }


        if (newRect.width >= 64 && newRect.height >= 64 && (newRect.width != m_targetRectInPixels.width || newRect.height != m_targetRectInPixels.height))
        {
            RTQuickMessageManager.Get().ShowMessage("Inpaint size: " + newRect.size);
            m_targetRectInPixels = newRect;
            OnMoveToPixelLocation(m_targetRectInPixels.position);
            UpdatePoints();
            this.GetComponent<PicMain>().SetNeedsToUpdateInfoPanelFlag();
        }

    }
    public void OnClickRelease()
    {
        m_bMovingRect = false;
    }

    void Update()
    {
       
        /*
        if (GameLogic.Get().GetMaskWidth() != m_targetRectInPixels.width || GameLogic.Get().GetMaskHeight() != m_targetRectInPixels.height)
        {
            m_targetRectInPixels.width = GameLogic.Get().GetMaskWidth();
            m_targetRectInPixels.height = GameLogic.Get().GetMaskHeight();
            UpdatePoints();
            OnMoveToPixelLocation(m_targetRectInPixels.position);
        }

        */
        if (m_bMovingRect)
        {
            if (Input.GetMouseButtonUp(0))
            {
                OnClickRelease();
            }
        }
       
    }
}
