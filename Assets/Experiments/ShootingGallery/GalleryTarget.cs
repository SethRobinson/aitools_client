
using DG.Tweening;
using UnityEngine;


public class GalleryTarget : MonoBehaviour
{
   float m_speed;

   SpawnPoint m_spawnScript;
   string m_directionToMove;
   public Animation m_animation;
   public SpriteRenderer m_spriteRenderer;
   public PolygonCollider2D m_polyCollider;
   Sequence m_shootSequence = null;
   
  
    bool m_dead = false;

    // Start is called before the first frame update
   
    public void InitImage(Material mat, Texture2D tex)
    {
        m_spriteRenderer.material = mat;

        float biggestSize = Mathf.Max(tex.width, tex.height);

        m_spriteRenderer.sprite = UnityEngine.Sprite.Create(tex,
          new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), (biggestSize / 5.12f),0, SpriteMeshType.Tight, new Vector4(0,0,0,0), true);

        m_speed = UnityEngine.Random.Range(1.0f, 5.0f);

        m_polyCollider.TryUpdateShapeToAttachedSprite();
    }

    public void StartAI(SpawnPoint spawnParent, string directionToMove)
    {
        m_spawnScript = spawnParent;
        m_directionToMove = directionToMove;

        StartPopOutAnim();
    }

    public void OnShot()
    {
        if (gameObject.tag == "Opponent")
        {
            //give them a point even if it's already dead, more fun
            ShootingGalleryLogic.Get().ModScore(1);
        }
       
        RTMessageManager.Get().Schedule(0.05f, RTAudioManager.Get().PlayEx, "squish", 0.4f, Random.Range(0.9f, 1.1f), false, 0.0f);

        if (m_dead) return;

        m_dead = true;
        m_animation.Stop();

        KillShoot();

        //float ang = Random.Range(- (Mathf.PI/2), Mathf.PI/2 );
        float ang = 0;
        float timeToTake = 0.9f;
        float stunDelay = 0.4f;
        var hitColor = new Color(1, 1, 1, 1);

        if (gameObject.tag == "Friend")
        {

            ShootingGalleryLogic.Get().ModScore(-10);
            RTAudioManager.Get().PlayEx("bloopdown", 1.0f, 1.0f);

            var messageObj = Instantiate(ShootingGalleryLogic.Get().m_textOverlayPrefab, m_spriteRenderer.transform);
            messageObj.GetComponent<SpriteRenderer>().sortingOrder = m_spriteRenderer.sortingOrder + 1;
            
            ang = Mathf.PI;
            timeToTake = 1.3f;
            stunDelay = 0.7f;

            hitColor = new Color(0.5f, 1, 0.5f, 1);
        }

        Vector2 vDir =new Vector2(Mathf.Sin(ang), Mathf.Cos(ang));
        float distance = 25;
        Vector3 vCurPos = gameObject.transform.position;
        
        Vector3 vTarget = vCurPos + new Vector3( vDir.x*distance, vDir.y * distance, 0);
        //fly off in a random direction
        
        Sequence mySequence = DOTween.Sequence();

        //shoot off the screen and remove self at the end
        Tween tween = transform.DOMove(vTarget, timeToTake).SetEase(Ease.Linear);
        float curScale = transform.localScale.x;
        tween.SetDelay(stunDelay);
        tween.onComplete = KillUs;

        mySequence.Append(tween);

        //turn red
        mySequence.Insert(0, m_spriteRenderer.DOColor(hitColor, 0));
        mySequence.Insert(stunDelay, m_spriteRenderer.DOColor(new Color(1, 1, 1, 1), 0));

        //grow bigger?
        mySequence.Insert(stunDelay, transform.DOScale(curScale * 2, timeToTake));

  


    }
    void RemoveAllClips()
    {
        while (m_animation.GetClipCount() > 0)
        {
            m_animation.RemoveClip("default");
        }
    }
    void StartPopOutAnim()
    {

        AnimationClip clip = ShootingGalleryLogic.Get().GetRandomAnimationClip(m_directionToMove);
        if (clip == null)
        {
            Debug.Log("No anim to going " + m_directionToMove);
            return;
        }

        RemoveAllClips();
        m_animation.AddClip(clip, "default");
        
        m_animation.clip = clip;
        m_animation.Play("default");
        m_animation.Rewind();
       
    }

    void KillUs()
    {
      
        if (m_spawnScript == null) return;
         m_spawnScript.OnTargetDeInitted();
        GameObject.Destroy(gameObject);
    }

    void Update()
    {
        /*
        Vector3 vPos = transform.position;
        vPos += new Vector3(Time.deltaTime * m_speed, 0, 0);
        transform.position = vPos;
        */

        if (m_dead)
        {
            return;
        }

        if (!m_animation.isPlaying)
        {
            //guess we're done
            KillUs();
        }
    }

    public void KillShoot()
    {
        if (m_shootSequence != null)
        {
            m_shootSequence.Kill();
            m_shootSequence = null;
        }

    }
    public void OnWillShoot()
    {

       
        //Debug.Log("Tagged with " + gameObject.tag + " is shooting");

        if (gameObject.tag == "Friend")
        {
            return; //friends don't fire (or lie)
        }

        //flash red to warn
        m_shootSequence = DOTween.Sequence();
        var flashColor = new Color(1, 0.6f, 0.6f, 1);

        //flash a lot
        for (int i=0; i < 5; i++)
        {
            m_shootSequence.Append(m_spriteRenderer.DOColor(flashColor, 0.1f));
            m_shootSequence.Append(m_spriteRenderer.DOColor(Color.white, 0.1f));
        }

        m_shootSequence.InsertCallback(0.7f, () => RTAudioManager.Get().PlayEx("splat2", 1.0f, 0.5f));
        m_shootSequence.InsertCallback(0.7f, () => ShootingGalleryLogic.Get().OnPlayerDamage(this, 3));
        m_shootSequence.Insert(0.6f, transform.DOPunchPosition(new Vector3(0,0.3f,0), 0.6f, 0, 0, false).SetRelative());

    }


}
