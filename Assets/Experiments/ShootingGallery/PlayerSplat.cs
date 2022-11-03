using DG.Tweening;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PlayerSplat : MonoBehaviour
{

    public SpriteRenderer m_spriteRenderer;

    // Start is called before the first frame update
    void Start()
    {

        m_spriteRenderer.color = new Color(1, 1, 1, 0.7f);
        Sequence mySequence = DOTween.Sequence();

        mySequence.Append(transform.DOScale(0, 0.5f).From());
        mySequence.InsertCallback(0.4f, () => RTAudioManager.Get().PlayEx("squish", 1.0f, 1.0f));

        mySequence.Insert(1.0f, m_spriteRenderer.DOFade(0, 6));

        mySequence.OnComplete(() => GameObject.Destroy(gameObject));
    }

    // Update is called once per frame
    void Update()
    {
        
    }
}
