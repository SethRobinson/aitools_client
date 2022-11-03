
using DG.Tweening;
using UnityEngine;

public class Splat : MonoBehaviour
{

    public SpriteRenderer m_spriteRenderer;

    // Start is called before the first frame update
    void Start()
    {
        RTAudioManager.Get().PlayEx("splat2", 1, Random.Range(0.9f, 1.1f), false, 0.1f);
        //rotate us
        transform.rotation = Quaternion.Euler(0, 0, Random.Range(0, 360));

        //fade out and die

        m_spriteRenderer.color = new Color(1, 1, 1, 0.7f);
        Sequence mySequence = DOTween.Sequence();

        mySequence.Append(transform.DOScale(0, 0.3f).From());
        //mySequence.InsertCallback(0.4f, () => RTAudioManager.Get().PlayEx("squish", 1.0f, 1.0f));

        mySequence.Insert(1.0f, m_spriteRenderer.DOFade(0, 6));

        mySequence.OnComplete(() => GameObject.Destroy(gameObject));


    }

    public void InitSplat(RaycastHit2D hitInfo, int sorting)
    {
        SpriteRenderer spriteRenderer = gameObject.GetComponent<SpriteRenderer>();
        spriteRenderer.sortingOrder = sorting+1; //so it will get drawn on top
        transform.position = new Vector3(hitInfo.point.x, hitInfo.point.y, hitInfo.transform.position.z);
        
        float scale = ((float)sorting) / 20.0f; //make it smaller when it's farther away, sort of


        if ( (hitInfo.collider.gameObject.tag == "Opponent") || (hitInfo.collider.gameObject.tag == "Friend"))
        {
            scale = 2.0f;
            //Debug.Log("Sorting is "+sorting.ToString()+" Scale is "+ scale.ToString());

            hitInfo.collider.gameObject.transform.parent.GetComponent<GalleryTarget>().OnShot();
        } 

        transform.localScale = new Vector3(scale, scale, scale);

        if (hitInfo.collider.gameObject.tag == "Border")
        {
            Destroy(gameObject);
        }
    }
    // Update is called once per frame
    void Update()
    {
        
    }
}
