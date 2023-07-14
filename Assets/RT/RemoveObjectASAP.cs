using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class RemoveObjectASAP : MonoBehaviour
{
    // Start is called before the first frame update
    
    void Start()
    {
       //remove this object as soon as it is created
       Destroy(gameObject);
        
    }

    // Update is called once per frame
    void Update()
    {
        
    }
}
