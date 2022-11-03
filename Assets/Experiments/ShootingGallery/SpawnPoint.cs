using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class SpawnPoint : MonoBehaviour
{
    public string m_directionText = "up";
    GameObject m_attachedTarget = null;
    public SpriteRenderer m_spriteRenderer;

    public bool IsBusy() { return m_attachedTarget != null; }
    // Start is called before the first frame update
    
    public void SpawnTarget(GameObject target, RTDB db)
    {
        //Debug.Log("Spawning target " + target.name);
        Debug.Assert(m_attachedTarget == null);

        m_attachedTarget = target;
        m_attachedTarget.transform.position = transform.position;
        m_attachedTarget.transform.localScale = transform.localScale;
        m_attachedTarget.tag = db.GetString("tag");

        GalleryTarget targetScript = m_attachedTarget.GetComponent<GalleryTarget>();
        targetScript.m_spriteRenderer.sortingOrder = m_spriteRenderer.sortingOrder;
        targetScript.StartAI(this, m_directionText);

    }
    public void OnTargetDeInitted()
    {
        m_attachedTarget = null;


    }
    void Start()
    {
        m_spriteRenderer.enabled = false;
    }

    // Update is called once per frame
    void Update()
    {
        
    }
}
