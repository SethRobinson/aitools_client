using System;
using System.Collections.Generic;
using System.Linq;
using SimpleJSON;

//Don't use this, AI tried but.. I think it's impossible to do this in theory without hardcoded information
//about different ComfyUI node types.  So let's not use it as that would be a maintenance nightmare.  Hopefully
//ComfyUI will get an option to load full workflows via API in the future

public static class ComfyUIConverter
{
    /// <summary>
    /// Converts a ComfyUI workflow JSON (from a file with a “nodes” array)
    /// into an API‐formatted JSON (a dictionary keyed by node id with only “inputs”, “class_type” and “_meta”).
    /// If the JSON is already in API format, it is returned unchanged.
    /// </summary>
    /// <param name="inputJson">The original workflow JSON.</param>
    /// <returns>The converted API-format JSON.</returns>
    public static JSONNode ConvertToApiFormat(JSONNode inputJson)
    {
        // If there is no "nodes" array then assume this is already API-formatted.
        if (!inputJson.HasKey("nodes"))
        {
            return inputJson;
        }

        // Build a lookup for links.
        // In the original workflow, the global "links" array contains entries like:
        // [ linkId, sourceNodeId, sourceSlot, destinationNodeId, destinationSlot, type ]
        var linkMap = new Dictionary<int, (string srcNodeId, int srcSlot)>();
        if (inputJson.HasKey("links"))
        {
            foreach (JSONNode link in inputJson["links"].AsArray)
            {
                // Ensure the link array has at least 3 elements.
                int linkId = link[0].AsInt;
                string srcNodeId = link[1].Value; // note: stored as string in API version
                int srcSlot = link[2].AsInt;
                if (!linkMap.ContainsKey(linkId))
                {
                    linkMap[linkId] = (srcNodeId, srcSlot);
                }
            }
        }

        // The output API JSON – a dictionary keyed by node id.
        var output = new JSONObject();

        // Iterate over all nodes in the original workflow.
        foreach (JSONNode node in inputJson["nodes"].AsArray)
        {
            // Use the node id as the key (convert to string)
            string nodeId = node["id"].Value;
            string nodeType = node["type"].Value;

            var apiNode = new JSONObject();
            // The node type becomes the class_type.
            apiNode["class_type"] = nodeType;

            // Create the _meta object with a title.
            var meta = new JSONObject();
            string title = string.Empty;
            if (node.HasKey("title") && !string.IsNullOrEmpty(node["title"].Value))
            {
                title = node["title"].Value;
            }
            else
            {
                // If no explicit title, use a friendly version of the node type.
                title = SplitCamelCase(nodeType);
            }
            meta["title"] = title;
            apiNode["_meta"] = meta;

            // Build the inputs object.
            var inputsObj = new JSONObject();

            // (1) Process the connection inputs (defined in node["inputs"] array).
            if (node.HasKey("inputs"))
            {
                foreach (JSONNode input in node["inputs"].AsArray)
                {
                    string inputName = input["name"].Value;
                    // If the input is connected (has a non-null "link")
                    if (input.HasKey("link") && !input["link"].IsNull)
                    {
                        int linkId = input["link"].AsInt;
                        if (linkMap.TryGetValue(linkId, out var connection))
                        {
                            // In the API, connection values are represented as an array: [ source_node_id, source_slot ]
                            var connectionArray = new JSONArray();
                            connectionArray.Add(new JSONString(connection.srcNodeId));
                            connectionArray.Add(new JSONNumber(connection.srcSlot));
                            inputsObj[inputName] = connectionArray;
                        }
                    }
                }
            }

            // (2) Process widget values.
            // They may be provided either as an object or an array.
            if (node.HasKey("widgets_values"))
            {
                JSONNode wv = node["widgets_values"];
                if (wv.IsObject)
                {
                    // If it’s already an object, merge its keys into inputs.
                    foreach (KeyValuePair<string, JSONNode> kvp in wv.AsObject)
                    {
                        // Do not override a key that was set via a connection.
                        if (!inputsObj.HasKey(kvp.Key))
                        {
                            inputsObj[kvp.Key] = kvp.Value;
                        }
                    }
                }
                else if (wv.IsArray)
                {
                    // For array widget values we try to give them friendly names.
                    // For known node types, we use a mapping; otherwise, we fall back to "param0", "param1", etc.
                    string[] mapping = null;
                    if (widgetMapping.ContainsKey(nodeType))
                    {
                        mapping = widgetMapping[nodeType];
                    }

                    for (int i = 0; i < wv.Count; i++)
                    {
                        string key = mapping != null && i < mapping.Length ? mapping[i] : "param" + i;
                        // If the key is not already taken (by a connection), add the widget value.
                        if (!inputsObj.HasKey(key))
                        {
                            inputsObj[key] = wv[i];
                        }
                    }
                }
            }

            apiNode["inputs"] = inputsObj;
            output[nodeId] = apiNode;
        }

        return output;
    }

    /// <summary>
    /// A mapping from known node types to friendly parameter names for widget values.
    /// If a node type isn’t in this dictionary, the converter will fall back to naming them "param0", "param1", etc.
    /// </summary>
    private static readonly Dictionary<string, string[]> widgetMapping = new Dictionary<string, string[]> {
        { "AutoDownloadBiRefNetModel", new [] { "model_name", "device" } },
        { "GetMaskByBiRefNet",         new [] { "width", "height", "upscale_method", "mask_threshold" } },
        { "VHS_LoadImagePath",         new [] { "image", "custom_width", "custom_height" } },
        { "CLIPTextEncode",            new [] { "text" } },
        { "CLIPLoader",                new [] { "clip_name", "type", "device" } },
        { "CheckpointLoaderSimple",    new [] { "ckpt_name" } },
        { "LTXVConditioning",          new [] { "frame_rate" } },
        { "EmptyLTXVLatentVideo",      new [] { "width", "height", "length", "batch_size" } },
        { "LTXVScheduler",             new [] { "steps", "max_shift", "base_shift", "stretch", "terminal" } },
        { "SamplerCustom",             new [] { "add_noise", "noise_seed", "cfg" } },
        { "KSamplerSelect",            new [] { "sampler_name" } }
        // For other node types, the widget values will be added as "param0", "param1", etc.
    };

    /// <summary>
    /// Splits a CamelCase string by inserting spaces (e.g. "VAEDecode" → "VAE Decode").
    /// </summary>
    private static string SplitCamelCase(string input)
    {
        if (string.IsNullOrEmpty(input)) return input;
        var result = new System.Text.StringBuilder();
        result.Append(input[0]);
        for (int i = 1; i < input.Length; i++)
        {
            if (char.IsUpper(input[i]) && !char.IsWhiteSpace(input[i - 1]))
            {
                result.Append(' ');
            }
            result.Append(input[i]);
        }
        return result.ToString();
    }
}
