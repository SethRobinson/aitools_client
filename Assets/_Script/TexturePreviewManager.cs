using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

public class TexturePreviewManager : MonoBehaviour
{
    bool m_bActive;
    Vector3 m_oldCamPos;
    float m_oldCamSize;
    public GameObject m_tilePreviewObj;
    Camera m_camera;

    // Start is called before the first frame update
    void Start()
    {
       m_camera = Camera.allCameras[0];
    }

    void InitTexturePreview()
    {
        //what texture are they hovering the mouse over?

        GameObject go = GameLogic.Get().GetPicWereHoveringOver();

        if (!go)
        {
            Debug.Log("No picture is currently under the mouse cursor, can't preview.");
            return;
        }

        //ok, we know where to steal the texture from
        SpriteRenderer spriteRenderer =  RTUtil.FindInChildrenIncludingInactive(go, "Pic").GetComponent<SpriteRenderer>();  

        if (!spriteRenderer)
        {
            Debug.LogError("Can't find sprite attached to pic");
            return;
        }

        m_tilePreviewObj.SetActive(true);
        var targetMesh = m_tilePreviewObj.GetComponent<MeshRenderer>();
        targetMesh.material.mainTexture = spriteRenderer.sprite.texture;

        //oh, let's remember the original camera position so we can return to it
        m_oldCamPos = m_camera.transform.position;
        m_oldCamSize = m_camera.orthographicSize;

        //also, lets move the preview texture to where they actually are
        var vTemp = m_tilePreviewObj.transform.position;
        vTemp.x = go.transform.position.x;
        vTemp.y = go.transform.position.y;

        m_tilePreviewObj.transform.position = vTemp;
    }

    void SetPreviewActive(bool bNew)
    {
        if (bNew == m_bActive) return; //no change

        m_bActive = bNew;

        if (m_bActive)
        {
            Debug.Log("Showing tiled texture preview");
            InitTexturePreview();
        } else
        {
            Debug.Log("Ending tiled texture preview");
            m_tilePreviewObj.SetActive(false);
            m_camera.transform.position = m_oldCamPos;
            m_camera.orthographicSize = m_oldCamSize;
        }
    }

    // Update is called once per frame
    void Update()
    {
        if (Input.GetKeyUp(KeyCode.T))
        { 
            SetPreviewActive(false);
        }


        if (!GameLogic.Get().GUIIsBeingUsed())
        {
            if (Input.GetKeyDown(KeyCode.T))
            {
                SetPreviewActive(true);
            }
        }
    }
}
