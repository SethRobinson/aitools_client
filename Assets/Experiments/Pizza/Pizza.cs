using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Pizza : MonoBehaviour
{
    float m_speed;

    public MeshRenderer m_mesh;
    // Start is called before the first frame update
    void Start()
    {

    }

    public void InitPizza(Material mat, Texture2D tex, Texture2D alphaTex)
    {
        m_mesh.material = mat;

        m_mesh.material.SetTexture("_MainTex", tex);

        if (alphaTex)
        {
            m_mesh.material.SetTexture("_AlphaTex", alphaTex);
        }

        m_speed = Random.Range(1.0f, 5.0f);

        if (Camera.allCameras[0] != null)
        {
            Rect r = Camera.allCameras[0].GetScreenWorldRect();

            var pos = transform.position;
            pos.y = Random.Range(r.yMin, r.yMax);
           
            float scale = Random.Range(0.2f, 0.6f);
            transform.localScale = new Vector3(scale, scale, scale);
            float pizzaSize = 5* scale;

            pos.x = r.xMin- pizzaSize;

            transform.position = pos;

            transform.Rotate(0, 0, Random.Range(0.0f, 360.0f));

        }
        else
        {
            Debug.LogError("Can't find camera");
        }

        Destroy(gameObject, 60);
    }

    void Update()
    {
        Vector3 vPos = transform.position;
        vPos += new Vector3(Time.deltaTime * m_speed, 0, 0);
        transform.position = vPos;
    }

}
