using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using System;
using System.Text;
using MiniJSON;

public class WebRequestServerInfo : MonoBehaviour
{

    // Start is called before the first frame update
    void Start()
    {
     }

    // Update is called once per frame
    void Update()
    {
    }

    public void StartWebRequest(string httpDest)
    {
        StartCoroutine(GetRequest(httpDest));
    }

    IEnumerator GetRequest(String server)
    {
        WWWForm form = new WWWForm();
        var finalURL = server + "/get_info";

        Debug.Log("Checking server "+ server+ "...");
        
        //Create the request using a static method instead of a constructor
        
        using (var postRequest = UnityWebRequest.Post(finalURL, form))
        {
            //Start the request with a method instead of the object itself
            yield return postRequest.SendWebRequest();

            if (postRequest.result != UnityWebRequest.Result.Success)
            {
                Debug.Log("Error connecting to server " + server + ". ("+ postRequest.error+")  Are you sure it's up and this address/port is right?");
                Debug.Log("Click Configuration, then Save & Apply to try again.");
                GameLogic.Get().ShowConsole(true);
            }
            else
            {
                //converting postRequest.downloadHandler.text to a dictionary
                //and then show all values
           
                var dict = Json.Deserialize(postRequest.downloadHandler.text) as Dictionary<string, object>;

                /*
                if (dict != null)
                { 
                    foreach (KeyValuePair<string, object> kvp in dict)
                    {
                        if (kvp.Value != null)
                        {
                            Debug.Log("Key: " + kvp.Key + " Value: " + kvp.Value.ToString());
                        }
                    }
                }
                */

                /*
                   GPUInfo g;
                   //error checking?  that's for wimps
                   g = new GPUInfo();
                   g.remoteURL = words[1];
                   int.TryParse(words[2], out g.remoteGPUID);
                   m_gpuInfo.Add(g);
                   */

                //log the dict value of context
                String serverName = dict["name"].ToString();
                float version;

                System.Single.TryParse(dict["version"].ToString(), out version);
                List<object> gpus = dict["gpu"] as List<object>;
                Debug.Log("Connected to server " + finalURL + ": " + serverName + " - remote GPUs available: " + gpus.Count);

                for (int i=0; i < gpus.Count; i++)
                {
                    GPUInfo g;
                    //error checking?  that's for wimps
                    g = new GPUInfo();
                    g.remoteURL = server;
                    g.remoteGPUID = i;
                    Config.Get().AddGPU(g);
                }
            }

        }

        //either way, we're done with us
        GameObject.Destroy(gameObject);

    }
    
}
