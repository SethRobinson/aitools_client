using System.Collections;
using UnityEngine;
using TMPro;
using UnityEngine.UI;
using UnityEngine.Networking;
using UnityEngine.EventSystems;

public class ModelModGUIItem : MonoBehaviour, IPointerClickHandler
{
    ModelModItem _item;

    //grab our TMP text object
    public TextMeshProUGUI _text;
    public Image _image;
    Image _backgroundImage;
    public RTToolTip _toolTip;
    Color _originalColor;
    // Start is called before the first frame update
    void Awake()
    {
        _backgroundImage = GetComponent<Image>();
        _originalColor = _backgroundImage.color;

    }

    public void ClearHighlight()
    {
        _backgroundImage.color = _originalColor;
    }

    void SetHighlight()
    {
        _backgroundImage.color = new Color(0.5f, 0, 0, 1);
    }
    public void OnPointerClick(PointerEventData eventData)
    {
        Debug.Log("Clicked on " + _item.name);
 
        string prompt = GameLogic.Get().GetPrompt();
       
        if (_item.type == ModelModItem.ModelType.EMBEDDING)
        {
            if (!RemoveEmbedding(_item.name, ref prompt)) //this should remove any existing Embeddings tags for this item
            {
                AddEmbedding(_item.name, ref prompt); //let's just add it then
                SetHighlight();
            }
            else
            {
                ClearHighlight();
            }

            GameLogic.Get().SetPrompt(prompt);

            return;
        }


        //if Alt is being held down, then we'll also add a random example
        if (Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt))
        {
            //add a random example
            if (_item.exampleList.Count > 0)
            {
                prompt = "";
                int randomIndex = Random.Range(0, _item.exampleList.Count);
                string example = _item.exampleList[randomIndex];
                prompt += example;

                ModelModManager.Get().ClearModGUIHighlights();
            }
        }

        if (!RemoveLora(_item.name, ref prompt)) //this should remove any existing lora tags for this item
        {
            AddLora(_item.name, ref prompt); //let's just add it then
            SetHighlight();
        } else
        {
            //removed lora
            ClearHighlight();
        }

        GameLogic.Get().SetPrompt(prompt);
    }

    void AddEmbedding(string name, ref string prompt)
    {
        // Add an embedding tag to the prompt. Ensure there's a space after if there is following text
        string addition = "(" + name + ":1.0)";
        if (prompt.Length > 0)
        {
            addition += " ";
        }
        prompt = addition + prompt;
    }

    bool RemoveEmbedding(string name, ref string prompt)
    {
        bool bRemoved = false;

        // Remove any existing embedding tags for this item
        string embeddingTag = "(" + name + ":";
        int startIndex = prompt.IndexOf(embeddingTag);
        if (startIndex != -1)
        {
            // Find the end index of the embedding tag
            int endIndex = prompt.IndexOf(")", startIndex);
            if (endIndex != -1)
            {
                endIndex += 1; // Include closing parenthesis in removal

                // If a space exists right after the embedding tag, include it in the removal
                if (endIndex < prompt.Length && prompt[endIndex] == ' ')
                {
                    endIndex += 1;
                }

                // Remove the embedding tag, closing parenthesis, and trailing space (if any)
                prompt = prompt.Remove(startIndex, endIndex - startIndex);
                bRemoved = true;
            }
        }

        return bRemoved;
    }


    void AddLora(string name, ref string prompt)
    {
        //add a lora tag to the prompt.  Make sure there is a space first if didn't exist though
        if (prompt.Length > 0)
        {
            if (prompt[prompt.Length - 1] != ' ')
            {
                prompt += " ";
            }
        }
        prompt += "<lora:" + name + ":1.0>";
    }

    bool RemoveLora(string name, ref string prompt)
    {
        bool bRemoved = false;

        // Remove any existing lora tags for this item
        string loraTag = "<lora:" + name + ":";
        int startIndex = prompt.IndexOf(loraTag);
        if (startIndex != -1)
        {
            // Find the end index of the lora tag
            int endIndex = prompt.IndexOf(">", startIndex);
            if (endIndex != -1)
            {
                // Remove the lora tag and the closing tag
                prompt = prompt.Remove(startIndex, endIndex - startIndex + 1);
                bRemoved = true;
            }
        }

        return bRemoved;
    }


    // Update is called once per frame
    void Update()
    {
        
    }

    public void InitStuff(ModelModItem item, float timeOffsetSecondsToLoadImage)
    {

        _item = item; 
        if (item.alias != null && item.alias.Length > 0)
        {
            _text.text = item.alias;
        } else
        {
            _text.text = item.name;
        }

        //get GPU info
        if (Config.Get().GetGPUCount() == 0)
        {
            return;
        }

        GPUInfo gpuInfo = Config.Get().GetGPUInfo(0);

        if (item.type == ModelModItem.ModelType.EMBEDDING)
        {
            Debug.Log("Found embedding");
            string fileToDownload = gpuInfo.remoteURL + "/sd_extra_networks/thumb?filename=embeddings/" + item.name+".preview.png";
            RTMessageManager.Get().Schedule(timeOffsetSecondsToLoadImage, () => { StartCoroutine(GetTexture(fileToDownload)); });
            _toolTip._text = "Embedding. Keyword: "+item.name;
            _backgroundImage.color = new Color(0, 0.2f, 0, 1);
            _originalColor = _backgroundImage.color;
        }


        if (item.type == ModelModItem.ModelType.LORA)
        {
            _backgroundImage.color = new Color(0, 0, 0.5f, 1);
            _originalColor = _backgroundImage.color;

            string fileToDownload = gpuInfo.remoteURL + "/sd_extra_networks/thumb?filename=" + item.path;

            fileToDownload = fileToDownload.Replace(".safetensors", ".preview.png");
            Debug.Log("Trying to download " + fileToDownload + " to set it as our GUI image");
            //code to download the file in a coroutine

            RTMessageManager.Get().Schedule(timeOffsetSecondsToLoadImage, () => { StartCoroutine(GetTexture(fileToDownload)); });

            _toolTip._text = item.name;

            if (item.modelName != null && item.modelName.Length > 0)
            {
                _toolTip._text += " trained with " + item.modelName;
            }

            if (item.resolution != null && item.resolution.Length > 0)
            {
                _toolTip._text += " at " + item.resolution;
            }
            if (item.exampleList.Count > 0)
            {
                //choose one random example to show
                int randomIndex = Random.Range(0, item.exampleList.Count);
                _toolTip._text += "\nExample: " + item.exampleList[randomIndex] + "\n(click to toggle, alt-click to replace prompt with example)";
            }
        }

    }



    IEnumerator GetTexture(string url)
    {
        //Debug.Log("Downloading " + url);
        using (UnityWebRequest uwr = UnityWebRequestTexture.GetTexture(url))
        {
            yield return uwr.SendWebRequest();

            if (uwr.result == UnityWebRequest.Result.Success)
            {
                //Debug.Log("Downloaded " + url);
                Texture2D texture = DownloadHandlerTexture.GetContent(uwr);
                if (texture != null)
                {
                    _image.sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0, 0));
                }
            }
            else
            {
                //Debug.Log("Failed to download " + url + ". Error: " + uwr.error);
            }
        }
    }
}
