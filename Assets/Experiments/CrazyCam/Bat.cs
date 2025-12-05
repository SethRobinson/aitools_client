using UnityEngine;
using DG.Tweening;

public class Bat : MonoBehaviour
{

    public SpriteRenderer _bat; //our bat sprite. It's just a single 2d frame of a bat.
    
    private Sequence hoverSequence;
    private Vector3 hoverCenter;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
       

    }

    public void FlyAway()
    {
        Vector2 screenTopRight = new Vector2(Screen.width, Screen.height);
        Vector3 worldTopRight = Camera.main.ScreenToWorldPoint(new Vector3(screenTopRight.x, screenTopRight.y, 10f));
        Vector3 offscreenPosition = new Vector3(worldTopRight.x + 5f, worldTopRight.y + 5f, 0);
        FlyTo(transform.position, offscreenPosition, 1.5f, true);

        //Kill us too
        Destroy(gameObject, 2.0f);
    }

    //world coords, we'll generally fly 10 to 15 units in front of the camera
    public void FlyTo(Vector3 startPosition, Vector3 targetPosition, float speed, bool bStartFromCurrentPos = false)
    {

        //randomize our size a bit
        float randomScale = Random.Range(1.0f, 1.3f);
        transform.localScale = new Vector3(randomScale, randomScale, 1);

        // Kill any existing tweens on this transform
        transform.DOKill();
        if (hoverSequence != null)
        {
            hoverSequence.Kill();
            hoverSequence = null;
        }
        
        // Set starting position (either from parameter or current position)
        if (!bStartFromCurrentPos)
        {
            transform.position = startPosition;
        }
        // else use current transform.position
        
        // Speed parameter is the duration in seconds
        float duration = speed;
        
        // Fly to target with a bouncy ease
        transform.DOMove(targetPosition, duration)
            .SetEase(Ease.OutBack)
            .OnComplete(() => StartHovering(targetPosition));
    }
    
    private void StartHovering(Vector3 centerPosition)
    {
        hoverCenter = centerPosition;
        
        // Create a looping sequence for hovering
        hoverSequence = DOTween.Sequence();
        
        // Random hover offsets for natural movement
        Vector3 offset1 = new Vector3(Random.Range(-0.2f, 0.2f), Random.Range(-0.15f, 0.15f), 0);
        Vector3 offset2 = new Vector3(Random.Range(-0.2f, 0.2f), Random.Range(-0.15f, 0.15f), 0);
        Vector3 offset3 = new Vector3(Random.Range(-0.2f, 0.2f), Random.Range(-0.15f, 0.15f), 0);
        
        hoverSequence.Append(transform.DOMove(hoverCenter + offset1, 1.0f).SetEase(Ease.InOutSine));
        hoverSequence.Append(transform.DOMove(hoverCenter + offset2, 1.2f).SetEase(Ease.InOutSine));
        hoverSequence.Append(transform.DOMove(hoverCenter + offset3, 0.8f).SetEase(Ease.InOutSine));
        hoverSequence.Append(transform.DOMove(hoverCenter, 1.0f).SetEase(Ease.InOutSine));
        
        hoverSequence.SetLoops(-1); // Loop forever
    }
    
    private void OnDestroy()
    {
        // Clean up tweens when destroyed
        transform.DOKill();
        if (hoverSequence != null)
        {
            hoverSequence.Kill();
        }
    }
    
    // Update is called once per frame
    void Update()
    {
        
    }
}
