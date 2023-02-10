using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.Networking;
using MiniJSON;

public class UpdateChecker : MonoBehaviour
{
    // Start is called before the first frame update
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        
    }

    public void StartInitialWebRequest()
    {
        StartCoroutine(GetRequest("https://raw.githubusercontent.com/SethRobinson/aitools_client/main/latest_version_checker.json"));
    }

    IEnumerator GetRequest(String server)
    {
        string finalURL = server;
        WWWForm form = new WWWForm();
        string serverClickableURL = "<link=\"" + server + "\"><u>" + server + "</u></link>";
        //Debug.Log("Checking github to see if there is an update..." + serverClickableURL + "...");

        using (var postRequest = UnityWebRequest.Get(finalURL))
        {
            //Start the request with a method instead of the object itself
            yield return postRequest.SendWebRequest();

            if (postRequest.result != UnityWebRequest.Result.Success)
            {
                Debug.Log("Result is " + postRequest.result);
                Debug.Log("Error connecting to server " + serverClickableURL + ". (" + postRequest.error + ")");
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
                String message = dict["message"].ToString();
                float version;

                System.Single.TryParse(dict["latest_version"].ToString(), NumberStyles.Any, CultureInfo.CurrentCulture, out version);

               message =  message.Replace("{VERSION_STRING}", "V"+version.ToString());

                if (Config.Get().GetVersion() < version)
                {
                    RTConsole.Log(message);
                    RTUtil.SetActiveByNameIfExists("UpdatePanel", true);
                    var updatePanel = RTUtil.FindObjectOrCreate("UpdatePanel");

                    RTUtil.FindInChildrenIncludingInactive(updatePanel, "MessageText").GetComponent<TMPro.TextMeshProUGUI>().text = message;

                    //Debug.Log("NOTE: V"+version.ToString()+" ERROR: The server version is outdated, we required " + Config.Get().GetRequiredServerVersion() + " or newer. GO UPGRADE!  Trying anyway though.");
                    // GameLogic.Get().ShowConsole(true);
                } else
                {
                    RTConsole.Log("Checking github for update: We're good, " + version.ToString() + " is still the latest reported version.");
                }

            }
        }

        //either way, we're done with us
        GameObject.Destroy(gameObject);
    }

}
