//By Seth A. Robinson

using UnityEngine;
using System.Collections;
using UnityEngine.Networking;
using System;
using System.IO;

/*
 There isn't really a way to easily send a file to ComfyUI when running a workthrough, so instead we can just upload everything we need
 before we run it.  Note that Comfy *might* change the filename (to handle situations where two different files with the same name are uploaded)
 so we need to respect the one it sends back.

 Example:

  void OnUploadFinished(RTDB db)
    {
        RTConsole.Log("TexGen completed callback");
        if (db.GetInt32("success") == 0)
        {
            RTConsole.Log("Error uploading file: " + db.GetString("error"));
        }
        else
        {
            RTConsole.Log("File " + db.GetString("name") + " uploaded successfully");
        }
    }

    public void UploadFileToComfyUI()
    {
        GameObject go = m_AIimageGenerator.CreateNewPic();

        //add a file to the ComfyUI server
        ComfyUIFileUploader uploaderScript = ComfyUIFileUploader.CreateObject();

        int gpuID = Config.Get().GetFreeGPU(RTRendererType.ComfyUI, false);
        if (gpuID != -1)
        {
            uploaderScript.UploadFile(Config.Get().GetServerAddressByGPUID(gpuID), "output/testpic.png", OnUploadFinished); 
        }
    }

*/


public class ComfyUIFileUploader : MonoBehaviour
{

    bool m_bShouldReleaseGPUWhenDone = false;
    int m_lastGPUID = -1;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {

    }

    static public ComfyUIFileUploader CreateObject()
    {
        GameObject go = new GameObject("ComfyServerRequest");
        go.transform.parent = RTUtil.FindObjectOrCreate("ServerRequests").transform;

        ComfyUIFileUploader webScript = (ComfyUIFileUploader)go.AddComponent<ComfyUIFileUploader>();
        return webScript;
    }

    void ReleaseGPUIfNeeded()
    {
        if (m_bShouldReleaseGPUWhenDone)
        {
            m_bShouldReleaseGPUWhenDone = false;
            if (Config.Get().IsValidGPU(m_lastGPUID))
            {

                if (!Config.Get().IsGPUBusy(m_lastGPUID))
                {
                    Debug.LogError("Error: GPU " + m_lastGPUID + " was not busy when it shouldn't have been");
                }
                else
                {
                    Config.Get().SetGPUBusy(m_lastGPUID, false);

                }
            }
        }

    }
    void ReserveGPUIfWeCan(int gpuID)
    {
        m_lastGPUID = gpuID;

        if (!Config.Get().IsGPUBusy(gpuID))
        {
            //even though we don't really need the GPU, let's "reserve" this ComfyUI server because we'll need it for the next action,
            //I mean, why are we uploading a file if we aren't about to process it here?
            m_bShouldReleaseGPUWhenDone = true;
            Config.Get().SetGPUBusy(gpuID, true);

        }
    }
    public void UploadFile(int gpuID, string fileName, string fileNameToSaveAsRemotely,  Action<RTDB> myCallback)
    {

        string httpDest = Config.Get().GetServerAddressByGPUID(gpuID);
        ReserveGPUIfWeCan(gpuID);

        StartCoroutine(GetRequest(httpDest+"/upload/image", fileName, fileNameToSaveAsRemotely,  myCallback, null));
    }
    public void UploadFileInMemory(int gpuID, byte[] fileData, string fileNameToSaveAsRemotely, Action<RTDB> myCallback)
    {
        string httpDest = Config.Get().GetServerAddressByGPUID(gpuID);
        ReserveGPUIfWeCan(gpuID);
        StartCoroutine(GetRequest(httpDest + "/upload/image", "",  fileNameToSaveAsRemotely, myCallback, fileData));
    }

    IEnumerator GetRequest(string httpDest, string filePath, string fileNameToSaveAsRemotely, Action<RTDB> myCallback, byte[] fileInMemOrNull)
    {
        // Read the file data; make sure the file exists at filePath.
        
        byte[] fileData = null;


        if (fileInMemOrNull != null)
        {
            fileData = fileInMemOrNull;
        }
        else
        {
            try
            {
                fileData = File.ReadAllBytes(filePath);
            }
            catch (Exception ex)
            {
                Debug.LogError("Error reading file: " + ex.Message);
                yield break;
            }
        }

        // Create a form and add our file (using AddBinaryData) and extra fields.
        WWWForm form = new WWWForm();
        // The key "image" is used to send the file.
        form.AddBinaryData("image", fileData, fileNameToSaveAsRemotely, "image/png"); //probably a lie, we can actually upload any kind of file
        // Add additional fields.
        form.AddField("type", "temp");
        form.AddField("overwrite", "true"); //I know I'll be using unique filenames so helps me schedule multiple things in advanced
        //without needing to wait for replies

        // Create the UnityWebRequest for a POST.
        using (UnityWebRequest www = UnityWebRequest.Post(httpDest, form))
        {
            // Send the request and wait until it is done.
            yield return www.SendWebRequest();

            // Check for network or HTTP errors.
            if (www.result == UnityWebRequest.Result.ConnectionError || www.result == UnityWebRequest.Result.ProtocolError)
            {
                Debug.LogError("Upload Error: " + www.error);

                RTDB db = new RTDB();
                db.Set("success", 0);
                db.Set("error", www.error);
                db.Set("serverID", m_lastGPUID);
                ReleaseGPUIfNeeded();

                myCallback.Invoke(db);
            }
            else
            {
                // The request succeeded. Log the raw response.
                string jsonResponse = www.downloadHandler.text;
                Debug.Log("Upload Response: " + jsonResponse);

                // Parse the JSON response.
                // Example response: {"name": "test.png", "subfolder": "", "type": "input"}
                var json = SimpleJSON.JSON.Parse(jsonResponse);
                if (json == null)
                {
                    Debug.LogError("Failed to parse JSON response.");
                }
                else
                {
                    // Extract values from the JSON response.
                    string finalName = json["name"];
                    string subfolder = json["subfolder"];
                    string type = json["type"];
                    //Debug.LogFormat("Parsed Response: name = {0}, subfolder = {1}, type = {2}",
                    //               finalName, subfolder, type);


                    ReleaseGPUIfNeeded();

                    //Put the vars in the RTDB and call the callback myCallback
                    RTDB db = new RTDB();
                    db.Set("success", 1);
                    db.Set("name", finalName);
                    db.Set("subfolder", subfolder);
                    db.Set("type", type);
                    myCallback.Invoke(db);


                }
            }
        }
    }


    // Update is called once per frame
    void Update()
    {
        
    }
}
