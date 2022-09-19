using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using System;
using System.Text;
using MiniJSON;
using SimpleJSON;

public class WebRequestServerInfo : MonoBehaviour
{
    bool _bUseHack = false;

    // Start is called before the first frame update
    void Start()
    {
     }

    // Update is called once per frame
    void Update()
    {
    }

    public void StartWebRequest(string httpDest, string extra)
    {
        if (extra == "1")
        {
            _bUseHack = true;
        }
        StartCoroutine(GetRequest(httpDest));
    }

    IEnumerator GetRequest(String server)
    {
        WWWForm form = new WWWForm();
        var finalURL = server + "/file/aitools/get_info.json";

        Debug.Log("Checking server "+ server+ "...");
        
        //Create the request using a static method instead of a constructor
        
        using (var postRequest = UnityWebRequest.Get(finalURL))
        {
            //Start the request with a method instead of the object itself
            yield return postRequest.SendWebRequest();

            if (postRequest.result != UnityWebRequest.Result.Success)
            {
                Debug.Log("Error connecting to server " + finalURL + ". ("+ postRequest.error+")  Are you sure it's up and this address/port is right?");
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

                //log the dict value of context
                String serverName = dict["name"].ToString();
                float version;

                System.Single.TryParse(dict["version"].ToString(), out version);
                Debug.Log("CONNECTED: " + server + " (" + serverName + ") V"+version+"");

                //retrieve offsets
                List<object> gpus = dict["gpu"] as List<object>;
                List<object> fnDictHolder = dict["fn_index"] as List<object>;
                Dictionary<string, object> fnDict = fnDictHolder[0] as Dictionary<string,object>;
                
                for (int i=0; i < gpus.Count; i++)
               {
                   GPUInfo g;
                   //error checking?  that's for wimps
                   g = new GPUInfo();
                   g.remoteURL = server;
                   g.remoteGPUID = i;
                   g.bUseHack = _bUseHack;

                    //oh, we need the fn_index data to fake the gradio calls
                    if (fnDict != null)
                    {
                        foreach (KeyValuePair<string, object> kvp in fnDict)
                        {
                            if (kvp.Value != null)
                            {
                                //Debug.Log("Key: " + kvp.Key + " Value: " + kvp.Value.ToString());
                                int val;
                                int.TryParse(kvp.Value.ToString(), out val);
                                g.fn_indexDict.Add(kvp.Key, val);
                            }
                        }
                    }
                    

                    Config.Get().AddGPU(g);
               }
              
            }

        }

        //either way, we're done with us
        GameObject.Destroy(gameObject);
    }
}
