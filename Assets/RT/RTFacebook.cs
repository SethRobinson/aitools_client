using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.IO;
using System;

//disabled "'Use UnityWebRequest, a fully featured replacement which is more efficient and has additional features'"
#pragma warning disable 0618

public class RTFacebook : MonoBehaviour
{
   static RTFacebook _this;

   void Awake()
    {
        _this = this;
    }

    static public RTFacebook Get() { return _this; }
    
    public void GetUserInfo(Action<string, Dictionary<string, object>> callBackDelegate, string accessToken)
    {
        StartCoroutine(GetUserInfoCoRoutine(callBackDelegate, accessToken));
    }

    private IEnumerator GetUserInfoCoRoutine(Action<string, Dictionary<string, object>> callBackDelegate, string accessToken)
        {

        string facebookUserID = "me";
        ////metadata=1
        string url = "https://graph.facebook.com/v2.12/" + facebookUserID +
            "?fields=first_name,last_name,middle_name,name,age_range,link,gender,locale,timezone,updated_time,verified&access_token=" + accessToken;

      //  print("FB URL: " + url);

        using (WWW www = new WWW(url))
            {

            yield return www;
            string error = null;

            if (!string.IsNullOrEmpty(www.error))
            {
                callBackDelegate(www.error, null);
                yield break;
            }
             
            var dict = MiniJSON.Json.Deserialize(www.text) as Dictionary<string, object>;
            
            if (dict.ContainsKey("error"))
            {
                //we're assuming facebook doesn't change how they report errors
                error = ((Dictionary<string, object>)dict["error"])["message"].ToString();
            }
            callBackDelegate(error, dict);
            }
        }
    
	
}