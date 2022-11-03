using DG.Tweening;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class OverlayImagePopup : MonoBehaviour
{

    
    // Start is called before the first frame update
    void Start()
    {

        SpriteRenderer spriteRenderer = GetComponent<SpriteRenderer>();


        Sequence mySequence = DOTween.Sequence();

        mySequence.Append(transform.DOPunchScale(new Vector3(0.1f, 0.1f, 0), 3, 3, 1));
        mySequence.Insert(1, spriteRenderer.DOFade(0, 2));
        mySequence.OnComplete(() => GameObject.Destroy(gameObject));

    }

    // Update is called once per frame
    void Update()
    {
        
    }
}
