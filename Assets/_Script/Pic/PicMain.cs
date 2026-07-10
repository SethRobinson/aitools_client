using System.Collections;
using UnityEngine;
using System.IO;
using System;
using System.Text;
using TMPro;
using B83.Image.BMP;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;
using System.Linq;
using System.Collections.Generic;
using System.Data;
using System.Linq.Expressions;
using UnityEngine.ProBuilder.MeshOperations;
using SimpleJSON;
using AITools.AIChat.Video;

public class PicJobData
{
    public string _name = ""; // can be "replace"
    public string _parm1 = ""; // thing to replace
    public string _parm2 = ""; // thing to replace with

    public PicJobData Clone()
    {
        return new PicJobData()
        {
            _name = this._name,
            _parm1 = this._parm1,
            _parm2 = this._parm2
        };
    }
}
// Tracks upload info for multi-input workflows
public class UploadInfo
{
    public string source;      // "image1", "image2" (future), "temp1", "temp2", "temp3"
    public int inputIndex;     // 0-3 for INPUT_1 through INPUT_4
    public string filename;    // Generated GUID filename
}

public class PicJob
{
    public string _job = "none";
    public string _parm_1_string = "";
    public string _workflow = "";
    public string _requestedAudioPrompt = "";
    public string _requestedAudioNegativePrompt = "";
    public string _requestedPrompt = "";
    public string _requestedNegativePrompt = "";
    public string _requestedSegmentationPrompt = "";
    public string _requestedLLMPrompt = "";
    public string _requestedLLMReply = "";

    public RTRendererType requestedRenderer = RTRendererType.ComfyUI;
    public int _serverID = -1;
    public int _llmInstanceID = -1; // Tracks which LLM instance is handling this job
    public int _llmReplicaIndex = 0; // Which replica of the instance (port offset) is in use
    public List<PicJobData> _data = new List<PicJobData>();
    public string _originalJobString = "";
    public float _timeOfStart = 0;
    public float _timeOfEnd = 0;
    
    // Multi-input upload support: filenames for INPUT_1 through INPUT_4
    public string[] _inputFilenames = new string[5] { "", "", "", "", "" };
    // Pending uploads to process before running workflow
    public List<UploadInfo> _pendingUploads = new List<UploadInfo>();

    // PNG bytes of the input images that were uploaded for this job (slots 0..4 match
    // _inputFilenames). Captured during upload_to_comfy and carried into _jobHistory so
    // the "?" info panel can show the user which images fed an N-input workflow.
    // Null slots mean "no input was uploaded for that index". Bytes are immutable and
    // shared by reference across Clones to avoid re-allocating thumbnails.
    public byte[][] _inputImagePngs = new byte[5][];

    // PNG bytes of the generated result for this job, stamped on once the image comes
    // back so the "?" info panel can show "inputs + output" together. Null until the
    // result arrives. Immutable and shared by reference across Clones (MemberwiseClone
    // copies the reference) just like the input PNGs above.
    public byte[] _outputImagePng = null;
    
    // Multi-prompt support: extended prompts for workflows that need multiple distinct prompts
    // (e.g., multi-segment movie generation with different prompts for each segment)
    public const int MAX_EXTENDED_PROMPTS = 8;
    public string[] _requestedPrompts = new string[MAX_EXTENDED_PROMPTS] { "", "", "", "", "", "", "", "" };
    
    public PicJob Clone()
    {
        PicJob clone = (PicJob)this.MemberwiseClone();
        clone._data = new List<PicJobData>();

        foreach (PicJobData d in _data)
        {
            clone._data.Add(d.Clone());
        }
        
        // Deep copy input filenames array
        clone._inputFilenames = (string[])this._inputFilenames.Clone();
        
        // Deep copy pending uploads
        clone._pendingUploads = new List<UploadInfo>();
        foreach (UploadInfo u in _pendingUploads)
        {
            clone._pendingUploads.Add(new UploadInfo() 
            { 
                source = u.source, 
                inputIndex = u.inputIndex, 
                filename = u.filename 
            });
        }
        
        // Deep copy extended prompts array
        clone._requestedPrompts = (string[])this._requestedPrompts.Clone();

        // Shallow-copy the outer byte[][]; inner byte[] are treated as immutable PNGs
        // and shared by reference so we don't pay for a copy each Clone.
        clone._inputImagePngs = (byte[][])this._inputImagePngs.Clone();

        return clone;
    }
}

public class UndoEvent
{
    public Texture2D m_texture;
    public Sprite m_sprite;
    public bool m_active = false;

    public RTRendererType m_requestedRenderer = RTRendererType.Any_Local;
    public long m_lastSeed = -1;
    public int m_lastSteps = 0;
    public string m_lastModel = "?";
    public string m_lastSampler = "?";
    public string m_lastOperation = "None";
    public float m_lastDenoisingStrength = 0;
    public float m_lastCFGScale = 0;
    public int m_gpu = -1;
    public string m_maskContents = "";
    public float m_maskBlending = 0;
    public bool m_fixFaces = false;
    public bool m_tiling = false;
    public bool m_bUsingPix2Pix = false;
    public float m_pix2pixCFG = 0;

    public bool m_bUsingControlNet = false;
    public string m_lastControlNetModel = "";
    public string m_lastControlNetModelPreprocessor = "";
    public float m_lastControlNetWeight = 0;
    public float m_lastControlNetGuidance = 0;
    public PicJob m_picJob = new PicJob();
}

public class PicMain : MonoBehaviour
{
    public TextMeshPro m_text;
    public Canvas m_canvas;
    public SpriteRenderer m_pic;
    public SpriteRenderer m_mask;
    private Texture2D m_image2; // Secondary input texture for 2-input workflows (uploaded as "image2" via @upload). Not displayed.
    private Texture2D m_image3; // 3rd-input texture for multi-image workflows (uploaded as "image3" via @upload). Not displayed.
    private Texture2D m_image4; // 4th-input texture for multi-image workflows (uploaded as "image4" via @upload). Not displayed.
    private Texture2D m_image5; // 5th-input texture for multi-image workflows (uploaded as "image5" via @upload). Not displayed.

    // Optional callback the AI Chat host installs so workflow-runtime aborts (e.g.
    // "Need image5 image first!" when an @upload step finds no source texture) get
    // surfaced back to the LLM as a system injection. Without this, the abort only
    // shows up as the Pic's status text and the LLM has no idea anything went wrong
    // - so it keeps emitting the same broken action. Cleared after a single invoke
    // per workflow run so cascading internal failures don't re-spam the chat.
    public System.Action<string> m_workflowErrorReporter;

    // Holds the PNG bytes captured by each upload_to_comfy job that runs before a
    // run_workflow. When the run_workflow PicJob is cloned into _jobHistory we move
    // these into the cloned PicJob._inputImagePngs and clear the buffer so the next
    // job starts empty. This is how the "?" info panel learns which images were
    // actually sent for an N-input image-to-image job (e.g. img_to_img_klein_edit_4_input).
    byte[][] m_pendingInputImagePngs = new byte[5][];
    private volatile bool m_editFileHasChanged; //set on the FileSystemWatcher thread, read on the main thread
    private FileSystemWatcher m_editFileWatcher;
    //When a change is detected we don't read immediately (the editor may still be writing/holding
    //the file); we wait until its size is readable and has stopped changing, retrying to a deadline.
    private bool m_editReloadArmed;
    private float m_editReloadAt;
    private float m_editReloadDeadline;
    private long m_editLastReadableSize = -1;
    public PicMask m_picMaskScript;
    public PicTargetRect m_targetRectScript;
    public PicTextToImage m_picTextToImageScript;
    public PicInpaint m_picInpaintScript;
    public PicUpscale m_picUpscaleScript;
    public PicGenerator m_picGeneratorScript;
    public PicInterrogate m_picInterrogateScript;
    public PicGenerateMask m_picGenerateMaskScript;
    public PicInfoPanel m_infoPanelScript;
    public PicMovie m_picMovie;
    // When set, an @upload|video|...| step uploads THIS file instead of the Pic's own loaded
    // movie. Lets chat video_to_video supply the source clip while the Pic itself stays an
    // image (so the rendered result transitions image -> video like image_to_movie does).
    public string m_pendingVideoUploadPath = null;
    List<PicJob> m_picJobs = new List<PicJob>();
    List<PicJob> _jobHistory = new List<PicJob>();
    PicJob m_jobDefaultInfo = null;

    public TexGenWebUITextCompletionManager _texGenWebUICompletionManager;
    public OpenAITextCompletionManager _openAITextCompletionManager;
    public AnthropicAITextCompletionManager _anthropicAITextCompletionManager;
    public GeminiTextCompletionManager _geminiTextCompletionManager;
    public GPTPromptManager _promptManager;

    string m_mediaRemoteFilename = ""; //sometimes media has no name because it was generated, but we need to know the filename for sending/loading remotely on ComfyUI

    // Short visual caption (~15 words) generated asynchronously by a vision LLM
    // when the Pic is registered as an AI Chat image. Surfaced in the system
    // prompt's CHAT IMAGES block so the chat LLM can match descriptive references
    // ("the one with grandma") to chat_image="N" indices. Empty until the job
    // returns, or stays empty if no vision LLM is configured.
    public string Caption { get; set; } = "";
    // Short (~one sentence) form of Caption, used where space is tight (chat
    // image bubble label). Set in lockstep with Caption by the captioning
    // pipeline. Empty until the job returns.
    public string CaptionShort { get; set; } = "";

    bool m_bNeedsToUpdateInfoPanel = false;

    public Action<GameObject> m_onFinishedRenderingCallback;
    public Action<GameObject> m_onFinishedScriptCallback;
    public string m_lastLLMReply = "";
    public Camera m_camera;
    List<string> m_jobList = new List<string>();
    // Local (non-GPU) coroutines queued via AppendLocalOp. The job line in m_jobList
    // is "local_op|<key>"; UpdateJobs pops the line, looks the key up here, and runs
    // the coroutine. Dictionary entry is removed when the coroutine completes.
    // Used by the AI Chat composition skills (draw_text, add_border, paste_image,
    // crop_resize, draw_shape) so chain="true" works with the existing job pipeline.
    Dictionary<string, Func<PicMain, IEnumerator>> m_pendingLocalOps = new Dictionary<string, Func<PicMain, IEnumerator>>();
    int m_localOpCounter = 0;
    public bool m_allowServerJobOverrides = true;
    public bool m_isAutoPicJob = false; // Set to true for AutoPic jobs, enables per-server AutoPic override
    public int m_ownedServerID = -1; // When >= 0, this pic owns this server exclusively for AutoPic override
    public string m_autoPicScriptName = ""; // Tracks which AutoPic script was used for this pic
    public bool m_stopAfterScript = false; // Set by @stopjob command - tells callback not to add more jobs
    public int m_requestedServerID = -1; // When >= 0, this pic will only use this specific server (waits if busy, unless m_requestedServerIsPreference)
    public bool m_requestedServerIsPreference = false; // When true, m_requestedServerID is a SOFT hint: if that server is busy/unavailable, fall back to any free GPU instead of waiting. Used by AI Chat's gpu="N" hint (Adventure leaves this false for a hard per-server pin).
    public bool m_skipIgnoredServers = false; // When true, GetFreeGPU skips servers with _ignoredByExtraGenerators
    public string m_gpuNameMatchFilter = ""; // When non-empty AND m_requestedServerID < 0 AND m_ownedServerID < 0, restrict free-GPU lookup to servers whose _name contains this substring (case-insensitive)
    public bool m_autoPicOverridePreApplied = false; // When true, skip per-server AutoPic override (already applied at creation)
    UndoEvent m_undoevent = new UndoEvent();
    UndoEvent m_curEvent = new UndoEvent(); //useful for just saving the current status, makes it easy to copy to/from a real undo event
    bool m_isDestroyed;
    string m_editFilename = "";
    bool m_bLocked;
    bool m_bDisableUndo;
    bool m_isSelected = false;
    Color m_originalPicColor = Color.white;
    bool m_hasOriginalPicColor = false; // Track if we've captured the original color
    static readonly Color SELECTION_TINT = new Color(0.7f, 0.85f, 1f, 1f); // Light blue tint when selected
    LineRenderer m_selectionFrame; // Visual selection frame
    static bool s_hideStatusTextOverlays = false;
    static int s_lastOverlayToggleFrame = -1;

    // Streaming text display limiting - prevents TMP from choking on huge LLM output
    private StringBuilder _streamedTextBuffer = new StringBuilder();
    private float _streamLastUpdateTime;
    private const float STREAM_UPDATE_INTERVAL = 0.1f;
    private const int STREAM_DISPLAY_TAIL_CHARS = 500;
    private const int STREAM_THINKING_TAIL_CHARS = 300;
    
    // Static cache for server ownership - avoids O(N²) GetComponentsInChildren calls in GetFreeGPU
    static HashSet<int> s_ownedServers = new HashSet<int>();
    bool m_noUndo = false;

    public AIGuideManager.PassedInfo m_aiPassedInfo; //a misc place to store things the AI guide wants to
    bool m_waitingForPicJob = false;
    bool _llmIsActive = false;
    int _activeLLMInstanceID = -1; // Tracks which LLM instance is currently active for this pic
    int _activeLLMReplicaIndex = 0; // Which replica of the active instance is in use
    int _pendingServerID = -1; // Tracks which server this pic is targeting during pre-GPU LLM work
    const string m_default_requirements = "gpu";
    string m_requirements = m_default_requirements;
    string m_tempText1 = ""; // General-purpose text buffer for preset scripts
    string m_tempText2 = ""; // Second general-purpose text buffer for preset scripts
    string m_tempText3 = ""; // Third general-purpose text buffer for preset scripts
    string m_tempText4 = ""; // Fourth general-purpose text buffer for preset scripts

    // Custom variable manager for %variable% syntax in job scripts
    VariableManager m_variableManager = new VariableManager();
    public VariableManager GetVariableManager() { return m_variableManager; }

    public void SetPromptManager(GPTPromptManager promptManager)
    {
        _promptManager = promptManager;
    }   

    public void SetMediaRemoteFilename(string fname)
    {
        m_mediaRemoteFilename = fname;
    }

    // Awake runs before any Start() - ensures sprite exists for other scripts that access it
    void Awake()
    {
        // Create default sprite early so other scripts (like GameLogic) can access it during Start()
        if (m_pic.sprite == null || m_pic.sprite.texture == null)
        {
            int biggestSize = 512;
            Texture2D defaultTex = new Texture2D(biggestSize, biggestSize, TextureFormat.RGBA32, false);
            defaultTex.Fill(Color.black);
            defaultTex.FillAlpha(1.0f);
            defaultTex.Apply();
            m_pic.sprite = Sprite.Create(defaultTex, new Rect(0, 0, defaultTex.width, defaultTex.height), new Vector2(0.5f, 0.5f), biggestSize / 5.12f, 0, SpriteMeshType.FullRect);
        }
    }

    // Start is called before the first frame update
    void Start()
    {
        if (m_text.text == "Sample text") //ugly hack because I'm lazy, but this will break if you change the prefab
        {
            SetStatusMessage("");
        }

        m_camera = Camera.allCameras[0];
        m_canvas.worldCamera = m_camera;

        MakeDraggable();
    }

    public void SetRequirements(string req)
    {
        m_requirements = req;
    }
    public void SetDisableUndo(bool bNew)
    {
        m_bDisableUndo = bNew;
    }
    public Camera GetCamera() { return m_camera; }

    public Canvas GetCanvas() { return m_canvas; }
    public UndoEvent GetCurrentStats() { return m_curEvent; }
    
    public bool IsDestroyed()
    {
        return m_isDestroyed;
    }

    public void ClearRenderingCallbacks()
    {
        m_onFinishedRenderingCallback = null;
    }

    /// <summary>
    /// Claim exclusive ownership of a server. Used for AutoPic jobs with server overrides
    /// to ensure the server remains reserved for the entire LLM + render workflow.
    /// Returns true if claim was successful, false if server is already owned by another pic.
    /// </summary>
    public bool ClaimServerOwnership(int serverID)
    {
        if (serverID < 0) return false;
        
        // Check if another pic already owns this server using cached set (O(1) instead of O(N))
        if (s_ownedServers.Contains(serverID))
        {
            RTConsole.Log($"Cannot claim server {serverID} - already owned by another pic");
            return false;
        }
        
        m_ownedServerID = serverID;
        s_ownedServers.Add(serverID);
        RTConsole.Log($"Claimed ownership of server {serverID}");
        return true;
    }

    /// <summary>
    /// Release server ownership. Called when AutoPic workflow completes or is cancelled.
    /// </summary>
    public void ReleaseServerOwnership()
    {
        if (m_ownedServerID >= 0)
        {
            s_ownedServers.Remove(m_ownedServerID);
            RTConsole.Log($"Released ownership of server {m_ownedServerID}");
        }
        m_ownedServerID = -1;
    }

    /// <summary>
    /// Check if this pic owns a specific server.
    /// </summary>
    public bool OwnsServer(int serverID)
    {
        return m_ownedServerID >= 0 && m_ownedServerID == serverID;
    }

    /// <summary>
    /// Static method to check if any pic in the scene owns a specific server.
    /// Used by Config.GetFreeGPU to skip servers that are reserved for AutoPic workflows.
    /// Uses cached HashSet for O(1) lookup instead of iterating all pics (was causing O(N²) performance issues).
    /// </summary>
    public static bool IsServerOwnedByAnyPic(int serverID)
    {
        if (serverID < 0) return false;
        return s_ownedServers.Contains(serverID);
    }

    public void UnloadToSaveMemoryIfPossible()
    {
        if (IsMovie())
        {
            m_picMovie.UnloadTheMovieToSaveMemory();
        }
    }
    public void MakeDraggable()
    {

        //we want to be able to drag this around, so we'll have to disable the mask drawing stuff
        //m_picMaskScript.SetMaskVisible(false);
        //Add ObjectDrag to us
        

    }
    public string GetInfoText()
    {

        //go through every job in _jobHistory and add it to the string
        string msg = "";
        int jobCounter = 1;
        string c1 = "`4";

        // Show server lock status at the top if this pic owns a server
        if (m_ownedServerID >= 0)
        {
            string serverName = Config.Get().GetServerNameByGPUID(m_ownedServerID);
            msg += $@"{c1}Locked to server {m_ownedServerID}:`` {serverName}";
            if (!string.IsNullOrEmpty(m_autoPicScriptName))
            {
                msg += $@" {c1}AutoPic:`` {m_autoPicScriptName}";
            }
            msg += "\n";
        }
        else if (!string.IsNullOrEmpty(m_autoPicScriptName))
        {
            // Show AutoPic script even when not locked to a server
            msg += $@"{c1}AutoPic:`` {m_autoPicScriptName}
";
        }

        if (m_pic != null && m_pic.sprite != null && m_pic.sprite.texture != null)
        {
            msg += $@"{c1}Image size X:`` {(int)m_pic.sprite.texture.width} {c1}, Y: ``{(int)m_pic.sprite.texture.height}";
        }

        msg += $@" {c1}Mask Rect size X: ``{(int)m_targetRectScript.GetOffsetRect().width}{c1}, Y: ``{(int)m_targetRectScript.GetOffsetRect().height}
";

        foreach (var job in _jobHistory)
        {
            string serverTemp = "none";

            if (job._serverID != -1)
            {
                serverTemp = Config.Get().GetServerNameByGPUID(job._serverID);
            }   

            string finished = "";

            //calculate the seconds taken
            if (job._timeOfEnd > 0)
            {
                float seconds = job._timeOfEnd - job._timeOfStart;
                finished = $@"{c1}Time:`` {seconds.ToString("0.0#")}s";
            }

            if (msg != "") msg += "\n";
            
            // Check if extended prompts are set (for multi-segment workflows)
            bool hasExtendedPrompts = false;
            for (int i = 0; i < PicJob.MAX_EXTENDED_PROMPTS; i++)
            {
                if (!string.IsNullOrEmpty(job._requestedPrompts[i]))
                {
                    hasExtendedPrompts = true;
                    break;
                }
            }
            
            msg += $@"{c1}#{jobCounter}`` {c1}{job._job}:`` {job._workflow} {c1}Server ID:`` {serverTemp} {finished}";
            
            if (hasExtendedPrompts)
            {
                // Show numbered prompts only (skip the generic Prompt: line since Prompt 1 is the same)
                for (int i = 0; i < PicJob.MAX_EXTENDED_PROMPTS; i++)
                {
                    if (!string.IsNullOrEmpty(job._requestedPrompts[i]))
                    {
                        msg += $@"
{c1}Prompt {i + 1}:`` {job._requestedPrompts[i]}";
                    }
                }
            }
            else
            {
                // No extended prompts, show the single prompt
                msg += $@"
{c1}Prompt:`` {job._requestedPrompt}";
            }
            
            msg += $@"
{c1}Neg Prompt:`` {job._requestedNegativePrompt}
{c1}Audio Prompt:`` {job._requestedAudioPrompt}
{c1}Neg Audio Prompt:`` {job._requestedAudioNegativePrompt}
{c1}Segmentation Prompt:`` {job._requestedSegmentationPrompt}
{c1}Parm 1: ``{job._parm_1_string}";

            //go through each _data and display it
            foreach (var data in job._data)
            {
                msg += $@"{c1} Mod:`` {data._name} {c1}Old:`` {data._parm1} {c1}New:`` {data._parm2}\n";
            }


            jobCounter++;
        }

        if (m_jobList.Count > 0)
        {
            msg += $@"
{c1}Queued:``
";

            int joblistCounter = 0;
            foreach (var job in m_jobList)
            {
                if (joblistCounter > 0) msg += "\n";
                msg += $@" {job}";
            }
        }

        /*
        var c = GetCurrentStats();

        string c1 = "`4";

        string rendererRequested = GetCurrentStats().m_requestedRenderer.ToString();

        string msg =
$@"`8{c1}Last Operation:`` {c.m_lastOperation} {c1}Renderer:`` {rendererRequested} {c1}on ServerID: ``{c.m_gpu} {Config.Get().GetServerNameByGPUID(c.m_gpu)} ({Config.Get().GetServerAddressByGPUID(c.m_gpu)})
";

        if (m_pic != null && m_pic.sprite != null && m_pic.sprite.texture != null)
        {
            msg += $@"{c1}Image size X:`` {(int)m_pic.sprite.texture.width} {c1}, Y: ``{(int)m_pic.sprite.texture.height}";
        }

msg += $@" {c1}Mask Rect size X: ``{(int)m_targetRectScript.GetOffsetRect().width}{c1}, Y: ``{(int)m_targetRectScript.GetOffsetRect().height}
{c1}Model:`` {c.m_lastModel}
{c1}Sampler:`` {c.m_lastSampler} {c1}Steps:`` {c.m_lastSteps} {c1}Seed:`` {c.m_lastSeed}
{c1}CFG Scale:`` {c.m_lastCFGScale} {c1}Tiling: ``{c.m_tiling} {c1}Fix Faces:`` {c.m_fixFaces}
{c1}Prompt:`` {c.m_picJob.requestedPrompt}
{c1}Negative prompt:`` {c.m_picJob.requestedNegativePrompt}
{c1}Audio prompt:`` {c.m_picJob.requestedAudioPrompt}
{c1}Audio negative prompt:`` {c.m_picJob.requestedAudioNegativePrompt}
";

        /*
        if (GetCurrentStats().m_lastOperation == "img2img")
        {
            msg += $@"{c1}Denoising Strength:`` " + GetCurrentStats().m_lastDenoisingStrength + "\n";
            msg += $@"{c1}Mask Contents:`` " + GetCurrentStats().m_maskContents + " ";
            msg += $@"{c1}Mask Blending:`` " + GetCurrentStats().m_maskBlending.ToString("0.0#") + "\n";

            if (GetCurrentStats().m_bUsingPix2Pix)
            {
                msg += $@"{c1}Pix2Pix active: ``" + GetCurrentStats().m_bUsingPix2Pix + " ";
                msg += $@"{c1}CFG: ``" + GetCurrentStats().m_pix2pixCFG + "\n";
            }

            if (GetCurrentStats().m_bUsingControlNet)
            {
                msg += $@"{c1}Control Net active:`` ";
                msg += $@"{c1}Preprocessor:`` " + GetCurrentStats().m_lastControlNetModelPreprocessor + " ";
                msg += $@"{c1}Model:`` " + GetCurrentStats().m_lastControlNetModel + " ";
                msg += $@"{c1}Weight:`` " + GetCurrentStats().m_lastControlNetWeight+" Guidance: "+GetCurrentStats().m_lastControlNetGuidance + "\n";

            }
        }
      
        */
        return RTUtil.ConvertSansiToUnityColors(msg);
    }

    public void SafelyKillThisPic()
    {
        KillGPUProcesses();
        m_isDestroyed = true;
        GameObject.Destroy(gameObject);
    }

    public void SafelyKillThisPicAndDeleteHoles()
    {
        KillGPUProcesses();
        m_isDestroyed = true;
        
        GameObject.Destroy(gameObject);
       
        /*
        if (!AdventureLogic.Get().IsActive())
        {
            ImageGenerator.Get().ReorganizePics(false);
        }
        */
    }

    public bool IsBusy()
    {
       
        if (IsBusyBasic()) return true;
        if (m_picGeneratorScript.GetIsGenerating()) return true;
        if (m_picJobs.Count > 0) return true;
        if (m_jobList.Count > 0) return true;
        //if (_llmIsActive) return true;
        return false;
    }


    public int GetJobListLeft() { return m_jobList.Count; }
    public int GetJobsLeft() { return m_picJobs.Count; }  

    public bool StillHasJobActivityToDo() { return GetJobListLeft() > 0 || GetJobsLeft() > 0; }

    public bool IsBusyBasic()
    {
        if (m_picTextToImageScript.IsBusy()) return true;
        if (m_picInpaintScript.IsBusy()) return true;
        if (m_picUpscaleScript.IsBusy()) return true;
        if (m_waitingForPicJob) return true;
        if (_llmIsActive) return true;

        return false;
    }

    public bool IsLLMActive() { return _llmIsActive; }

    public bool IsMovie()
    {
        return m_picMovie.IsMovie();
    }

    /// <summary>The currently displayed image texture, or null if none. Used by the
    /// automation harness to report chat-image dimensions and to save pixels.</summary>
    public Texture2D GetCurrentTexture()
    {
        if (m_pic != null && m_pic.sprite != null)
            return m_pic.sprite.texture;
        return null;
    }

    public void SetVisible(bool bNew)
    {
        RTUtil.FindInChildrenIncludingInactive(gameObject, "Canvas").SetActive(bNew);
        RTUtil.FindInChildrenIncludingInactive(gameObject, "Pic").SetActive(bNew);
        RTUtil.FindInChildrenIncludingInactive(gameObject, "StatusText").SetActive(bNew);

    }
    public void KillGPUProcesses(bool forceImmediate = false)
    {
        if (m_picTextToImageScript.IsBusy())
        {
            if (forceImmediate)
                m_picTextToImageScript.ForceCancelForReconnect();
            else
                m_picTextToImageScript.SetForceFinish(true);
        }
        if (m_picInpaintScript.IsBusy()) m_picInpaintScript.SetForceFinish(true);
        if (m_picUpscaleScript.IsBusy()) m_picUpscaleScript.SetForceFinish(true);
        if (m_picGenerateMaskScript != null && m_picGenerateMaskScript.IsBusy()) m_picGenerateMaskScript.SetForceFinish(true);
        SetLLMActive(false); // Use SetLLMActive to properly release the Adventure mode slot

        ClearJobs();
    }

    void ClearJobs()
    {
        m_picJobs.Clear();
        m_jobList.Clear();
        m_waitingForPicJob = false;
        m_requirements = m_default_requirements;

        // Drop any captured input PNGs that never made it onto a run_workflow job, so
        // they can't leak into the next unrelated job.
        for (int __ii = 0; __ii < m_pendingInputImagePngs.Length; __ii++)
        {
            m_pendingInputImagePngs[__ii] = null;
        }

        // Release server ownership when jobs are cleared (cancellation, error, etc.)
        ReleaseServerOwnership();
    }
    public bool GetLocked() { return m_bLocked; }
    public void SetLocked(bool bNew) { m_bLocked = bNew; }
    
    /// <summary>
    /// Get selection state for marquee selection feature.
    /// </summary>
    public bool GetSelected() { return m_isSelected; }
    
    /// <summary>
    /// Set selection state for marquee selection feature.
    /// Shows a visible selection frame around the Pic.
    /// </summary>
    public void SetSelected(bool bNew)
    {
        if (m_isSelected == bNew)
            return;
            
        m_isSelected = bNew;
        
        // Show/hide selection frame
        UpdateSelectionFrame();
    }
    
    /// <summary>
    /// Creates or updates the selection frame visual indicator.
    /// </summary>
    private void UpdateSelectionFrame()
    {
        if (m_isSelected)
        {
            // Create selection frame if it doesn't exist
            if (m_selectionFrame == null)
            {
                GameObject frameObj = new GameObject("SelectionFrame");
                frameObj.transform.SetParent(transform);
                frameObj.transform.localPosition = Vector3.zero;
                
                m_selectionFrame = frameObj.AddComponent<LineRenderer>();
                m_selectionFrame.useWorldSpace = false;
                m_selectionFrame.loop = true;
                m_selectionFrame.positionCount = 4;
                m_selectionFrame.startWidth = 0.08f;
                m_selectionFrame.endWidth = 0.08f;
                m_selectionFrame.sortingOrder = 100; // Above the pic
                
                // Create a simple unlit material for the line
                m_selectionFrame.material = new Material(Shader.Find("Sprites/Default"));
                m_selectionFrame.startColor = new Color(0.2f, 0.6f, 1f, 1f); // Bright blue
                m_selectionFrame.endColor = new Color(0.2f, 0.6f, 1f, 1f);
            }
            
            // Update frame position based on sprite bounds
            if (m_pic != null && m_pic.sprite != null)
            {
                Bounds bounds = m_pic.bounds;
                float padding = 0.05f; // Small padding outside the sprite
                
                // Convert world bounds to local space
                Vector3 min = transform.InverseTransformPoint(bounds.min) - new Vector3(padding, padding, 0);
                Vector3 max = transform.InverseTransformPoint(bounds.max) + new Vector3(padding, padding, 0);
                
                m_selectionFrame.SetPosition(0, new Vector3(min.x, min.y, -0.1f));
                m_selectionFrame.SetPosition(1, new Vector3(max.x, min.y, -0.1f));
                m_selectionFrame.SetPosition(2, new Vector3(max.x, max.y, -0.1f));
                m_selectionFrame.SetPosition(3, new Vector3(min.x, max.y, -0.1f));
            }
            
            m_selectionFrame.enabled = true;
        }
        else
        {
            // Hide selection frame
            if (m_selectionFrame != null)
            {
                m_selectionFrame.enabled = false;
            }
        }
    }
    
    public void SetStatusMessage(string msg)
    {
        //if (GameLogic.Get().GetTurbo())
        //{
        //    m_text.text = ""; //hack to not show messages when in turbo mode
        //}
        //else
        {
            m_text.text = msg;
        }
    }

   

    void KillUndoImageBuffers()
    {
        if (m_undoevent.m_sprite != null && m_undoevent.m_sprite.texture != null)
        {
            Destroy(m_undoevent.m_sprite.texture);
        }

    }
    public bool AddImageUndo(bool bDoFullCopy = false)
    {
        if (m_bDisableUndo) return false;
        if (m_noUndo) return false;

        m_undoevent = m_curEvent;
        m_undoevent.m_active = true;

        KillUndoImageBuffers();

        if (bDoFullCopy && m_pic.sprite != null && m_pic.sprite.texture != null)
        {
            Texture2D copyTexture = m_pic.sprite.texture.Duplicate();

            float biggestSize = Math.Max(copyTexture.width, copyTexture.height);

            UnityEngine.Sprite newSprite = UnityEngine.Sprite.Create(copyTexture, 
                new Rect(0, 0, copyTexture.width, copyTexture.height), new Vector2(0.5f, 0.5f), (biggestSize / 5.12f), 0, SpriteMeshType.FullRect);
            
            m_undoevent.m_sprite = newSprite;
            m_undoevent.m_texture = m_pic.sprite.texture;
        } else
        {
            m_undoevent.m_sprite = m_pic.sprite;
            m_undoevent.m_texture = null;
        }
        //Debug.Log("Image added to undo");

        return true;
    }

    public void UndoImage()
    {
      
        if (m_undoevent.m_active && m_undoevent.m_sprite != null)
        {
            Debug.Log("Undo");

            m_curEvent = m_undoevent;

            Sprite tempSprite = m_pic.sprite;

            bool bSizeChanged = false;

            if (tempSprite.rect != m_undoevent.m_sprite.rect)
            {
                bSizeChanged = true;
            }

            m_pic.sprite = m_undoevent.m_sprite;
            m_undoevent.m_sprite = tempSprite;
            SetNeedsToUpdateInfoPanelFlag();
            MovePicUpIfNeeded();
            
            if (bSizeChanged)
            {
        //        Debug.Log("Size changed!  Throwing away mask because Seth is lazy");
        //        m_picMaskScript.RecreateMask();
            }

            m_picMaskScript.ResizeMaskIfNeeded();
            m_targetRectScript.UpdatePoints();

        }
        else
        {
            Debug.Log("Nothing to undo in this pic.");
        }
    }
    public void MovePicUpIfNeeded()
    {
        var vPos = m_pic.gameObject.transform.localPosition;
       // vPos.y = 0; //default

       // if (m_pic.sprite.bounds.size.y != 5.12f)
        {
           //um.. this pic is not square so it's letterboxed.  Let's move it up near the tool panel, looks better
            vPos.y = (5.12f - m_pic.sprite.bounds.size.y) / 2;
            //Debug.Log("Move to "+vPos);
        }

        m_pic.gameObject.transform.localPosition = vPos;

    }

   public PicMask GetMaskScript() { return m_picMaskScript; }

    public bool LoadImageFromClipboard()
    {
        string tempDir = Application.dataPath;
        tempDir = tempDir.Replace('/', '\\');
        tempDir = tempDir.Substring(0, tempDir.LastIndexOf('\\'));
        string targetExe = tempDir+"\\utils\\RTClip.exe";
        string tempPngFile = tempDir + "\\winclip_image.png";
        RTUtil.DeleteFileIfItExists(tempPngFile);

        var processInfo = new System.Diagnostics.ProcessStartInfo(targetExe, "");
        processInfo.CreateNoWindow = true;
        processInfo.UseShellExecute = false;

        var process = System.Diagnostics.Process.Start(processInfo);

        process.WaitForExit();
        process.Close();

        if (File.Exists(tempPngFile))
        {
            RTConsole.Log("Importing from clipboard...");
            LoadImageByFilename(tempPngFile, false);
            RTUtil.DeleteFileIfItExists(tempPngFile);

        }
        else
        {
            RTConsole.Log("No image found on clipboard");
        }
       

        return false;
    }

    //crashes when accessing image data on clipboard, no idea why.  Have tried in and out of threads

    /*
    public bool LoadImageFromClipboard()
    {
        Debug.Log("Trying to load image from clipboard...");

        Texture2D texture = null;

        System.Threading.Thread t = new System.Threading.Thread(() =>
        {

            if (System.Windows.Forms.Clipboard.ContainsImage())
            {
                Debug.Log("Found image on clipboard...");

                var dataObject = System.Windows.Forms.Clipboard.GetDataObject();

                var formats = dataObject.GetFormats(true);
                if (formats == null || formats.Length == 0)
                {

                }
                else
                {


                    foreach (var f in formats)
                        Debug.Log(" - " + f.ToString());
                }

            }


        });

        t.Start();
        t.Join();


        return true;
    }

    */


    //Reads a file even if another process (e.g. the external editor) still holds it open for
    //writing.  File.ReadAllBytes uses FileShare.Read, which throws a sharing violation in that
    //case; FileShare.ReadWrite tolerates it.
    static byte[] ReadAllBytesShared(string path)
    {
        using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        {
            byte[] buf = new byte[fs.Length];
            int read = 0;
            while (read < buf.Length)
            {
                int n = fs.Read(buf, read, buf.Length - read);
                if (n <= 0) break;
                read += n;
            }
            return buf;
        }
    }

    //File size if it can be opened for a shared read, else -1.  Used to wait until the external
    //editor has finished writing (size stops changing) before we reload it.
    static long GetReadableFileSize(string path)
    {
        try { using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)) return fs.Length; }
        catch { return -1; }
    }

    public void LoadImageByFilename(string filename, bool bResize = false, bool bRenderAlphaHiddenAreasToo = false, bool bInvertLoadedMask = false)
    {
        try
        {


            string fExt = Path.GetExtension(filename).ToLower();
            bool bNeedToProcessAlpha = false;

            if (fExt == ".mp4" || fExt == ".avi" || fExt == ".mov")
            {
                //special handling for movies
                m_picMovie.SetAutoDeleteFileWhenDone(false);
                m_picMovie.PlayMovie(filename);
                return;
            }

            Debug.Log("Loading "+filename+" from disk");
            var buffer = ReadAllBytesShared(filename); //shared read: the external editor may still have the file open
            Texture2D texture = null;
            
         
            if (fExt == ".bmp")
            {
                //RTQuickMessageManager.Get().ShowMessage("Detected bmp");

                BMPLoader bmp = new BMPLoader();
                BMPImage im = bmp.LoadBMP(buffer); //use the shared-read bytes, not a second File.OpenRead (which can hit a lock)
                texture = im.ToTexture2D();

                if (im.HasAlphaChannel())
                {
                    bNeedToProcessAlpha = true;
                }
            }
            else
            {
                texture = new Texture2D(16, 16, TextureFormat.RGBA32, false);
                if (!texture.LoadImage(buffer))
                {
                    Debug.LogError("Error loading image " + filename);
                    Destroy(texture);
                    return;
                }

                if (fExt == ".jpg" || fExt == ".jpeg")
                {
                    Texture2D orientedTexture = texture.ApplyJpegExifOrientation(buffer);
                    if (!ReferenceEquals(orientedTexture, texture))
                    {
                        Destroy(texture);
                        texture = orientedTexture;
                    }
                }
            }

            if (bResize)
            {
                Debug.Assert(false);
                //resize texture but keep aspect ratio
                if (texture.width != 512 || texture.height != 512)
                {
                    ResizeTool.Resize(texture, 512, 512, false, FilterMode.Bilinear);
                }
            }

            
            AddImageUndo();
            float biggestSize = Math.Max(texture.width, texture.height);
            Sprite newSprite = null;
            Texture2D alphaTex = null;

            if (bNeedToProcessAlpha)
            {
                bool bAlphaWasUsed = false;

                alphaTex = texture.GetAlphaMask(out bAlphaWasUsed);
                if (bAlphaWasUsed)
                {
                    //valid alpha found
                    if (bInvertLoadedMask) alphaTex.InvertAlpha(); //undo the export-time flip from the external editor (Photoshop/Patchy)
                    alphaTex.Apply();
                    m_picMaskScript.SetMaskFromTexture(alphaTex);

                    if (bRenderAlphaHiddenAreasToo)
                    {
                        //yes, we have an alpha channel, but actually, let's drop it (it's in our mask now anyway) so we can see the hidden
                        //areas anyway.  We do this when loading a file from photoshop
                        texture.FillAlpha(1.0f); //clearing out the alpha in the image
                        texture.Apply();
                    }
                   
                } else
                {
                    //No alpha.  Disable it so it will be auto-filled for operations that need it
                    alphaTex = null;
                    texture.FillAlpha(1.0f);
                    texture.Apply();
                }
            }
          
            newSprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f), biggestSize / 5.12f, 0, SpriteMeshType.FullRect);

            if (m_pic.sprite != null && m_pic.sprite.texture != null)
            {
                Destroy(m_pic.sprite.texture);
            }
            m_pic.sprite = newSprite;
            MovePicUpIfNeeded();
          
            SetStatusMessage("");
            m_picMaskScript.ResizeMaskIfNeeded();
            m_targetRectScript.UpdatePoints();

        }
        catch (Exception e)
        {
            Debug.LogError("Failed to load image from "+filename+".  Does the file even exist?");
            RTConsole.Log("Failed to load " + System.IO.Path.GetFileName(filename) + ": " + e.Message); //surface to the in-game console too
            System.Console.WriteLine(e.StackTrace);
        }

        SetNeedsToUpdateInfoPanelFlag();

    }

    public void CropToMaskRect()
    {
        AddImageUndo();

        var croppedTexture = ResizeTool.CropTexture(m_pic.sprite.texture, m_targetRectScript.GetOffsetRect());
        float biggestSize = Math.Max(croppedTexture.width, croppedTexture.height);

        m_pic.sprite = Sprite.Create(croppedTexture, new Rect(0, 0, croppedTexture.width, croppedTexture.height), new Vector2(0.5f, 0.5f), biggestSize / 5.12f, 0, SpriteMeshType.FullRect);
        
        MovePicUpIfNeeded();
        m_picMaskScript.ResizeMaskIfNeeded();
        m_targetRectScript.UpdatePoints();
        m_targetRectScript.OnMoveToPixelLocation(new Vector2(0, 0));
        SetNeedsToUpdateInfoPanelFlag();

    }

    public System.Collections.IEnumerator AddBorder(int left, int right, int top, int bottom, Color borderColor, bool bSetMaskToBorder)
    {
        //swap top and bottom, texture is upside down
        int temp = top;
        top = bottom;
        bottom = temp;

        if (m_pic.sprite == null)
        {
            SetStatusMessage("No image loaded");
           //exit from this couroutines
            yield break;
        }
        
        //AddImageUndo(true);

        // Create a new image that is black, that adds the border (based on the size of m_pic.sprite)
        int newWidth = m_pic.sprite.texture.width + left + right;
        int newHeight = m_pic.sprite.texture.height + top + bottom;

        Texture2D defaultTex = new Texture2D(newWidth, newHeight, TextureFormat.RGBA32, false);
        yield return null;
        defaultTex.Fill(borderColor);
        yield return null;
        //defaultTex.FillAlpha(1.0f);
        defaultTex.Apply();
        yield return null;
        float biggestSize = Math.Max(defaultTex.width, defaultTex.height);

        var newSprite = Sprite.Create(defaultTex, new Rect(0, 0, defaultTex.width, defaultTex.height), new Vector2(0.5f, 0.5f), biggestSize / 5.12f, 0, SpriteMeshType.FullRect);

        // Copy the old image into the new one
        Color[] pixels = m_pic.sprite.texture.GetPixels();
        defaultTex.SetPixels(left, top, m_pic.sprite.texture.width, m_pic.sprite.texture.height, pixels);
        yield return null;
        defaultTex.Apply();
        yield return null;

        m_pic.sprite = newSprite;
        MovePicUpIfNeeded();
        m_picMaskScript.ResizeMaskIfNeeded();
      
        if (bSetMaskToBorder)
        {
            int overlayPixel = 4; // It helps to go one extra when outpainting?
                                  // Get four rects of the border
            Rect topRect = new Rect(0, 0, defaultTex.width, top + overlayPixel);
            Rect bottomRect = new Rect(0, defaultTex.height - (bottom + overlayPixel), defaultTex.width, bottom + overlayPixel);
            Rect leftRect = new Rect(0, 0, left + overlayPixel, m_pic.sprite.texture.height);
            Rect rightRect = new Rect(defaultTex.width - (right + overlayPixel), 0, right + overlayPixel, m_pic.sprite.texture.height);

            Color drawColor = new Color(1, 1, 1, 1);


            // Set all four rects
          
            m_mask.sprite.texture.SetPixels((int)topRect.x, (int)topRect.y, (int)topRect.width, (int)topRect.height, Enumerable.Repeat(drawColor, (int)topRect.width * (int)topRect.height).ToArray());
            yield return null;
            m_mask.sprite.texture.SetPixels((int)bottomRect.x, (int)bottomRect.y, (int)bottomRect.width, (int)bottomRect.height, Enumerable.Repeat(drawColor, (int)bottomRect.width * (int)bottomRect.height).ToArray());
            yield return null;
            m_mask.sprite.texture.SetPixels((int)leftRect.x, (int)leftRect.y, (int)leftRect.width, (int)leftRect.height, Enumerable.Repeat(drawColor, (int)leftRect.width * (int)leftRect.height).ToArray());
            yield return null; 
            m_mask.sprite.texture.SetPixels((int)rightRect.x, (int)rightRect.y, (int)rightRect.width, (int)rightRect.height, Enumerable.Repeat(drawColor, (int)rightRect.width * (int)rightRect.height).ToArray());
            yield return null;
            // Make sure the mask is shown
            m_picMaskScript.SetMaskVisible(true);
            m_targetRectScript.UpdatePoints();
          
        }

        m_mask.sprite.texture.Apply();
        m_pic.sprite.texture.Apply();
        m_picMaskScript.SetMaskModified(true);
    }

    public void Resize(int newWidth, int newHeight, bool bKeepAspect, FilterMode filterMode = FilterMode.Bilinear)
    {
        if (m_pic.sprite == null)
        {
            SetStatusMessage("No image loaded");
            return;
        }


        AddImageUndo(true);

        if (bKeepAspect)
        {

            //crop our texture to correct aspect ratio first
            var croppedTexture = ResizeTool.CropTextureToAspectRatio(m_pic.sprite.texture, newWidth, newHeight);

            UnityEngine.Object.Destroy(m_pic.sprite.texture); //this will also kill the sprite?

            //now do the real resizing
            ResizeTool.Resize(croppedTexture, newWidth, newHeight, false, filterMode);
            float biggestSize = Math.Max(croppedTexture.width, croppedTexture.height);
            m_pic.sprite = Sprite.Create(croppedTexture, new Rect(0, 0, croppedTexture.width, croppedTexture.height), new Vector2(0.5f, 0.5f), biggestSize / 5.12f, 0, SpriteMeshType.FullRect);
        }
        else
        {
            ResizeTool.Resize(m_pic.sprite.texture, newWidth, newHeight, false, filterMode);
            float biggestSize = Math.Max(m_pic.sprite.texture.width, m_pic.sprite.texture.height);
            m_pic.sprite = Sprite.Create(m_pic.sprite.texture, new Rect(0, 0, m_pic.sprite.texture.width, m_pic.sprite.texture.height), new Vector2(0.5f, 0.5f), biggestSize / 5.12f, 0, SpriteMeshType.FullRect);
        }

        MovePicUpIfNeeded();
        m_picMaskScript.ResizeMaskIfNeeded();
        m_targetRectScript.UpdatePoints();

        m_targetRectScript.OnMoveToPixelLocation(new Vector2(0, 0));
        SetNeedsToUpdateInfoPanelFlag();

    }

    public void OnTileButton()
    {

        if (m_pic.sprite == null)
        {
            SetStatusMessage("No image loaded");
            return;
        }

        //AddImageUndo(true); //actually why should we, pushing on tile again will perfectly recreate the original
        
        ResizeTool.SetupAsTile(m_pic.sprite.texture, m_pic.sprite.texture.filterMode);
    }
    public void StopFileWatchingIfNeeded()
    {
        if (m_editFileWatcher != null)
        {
            m_editFileWatcher.Changed -= OnChanged;
            m_editFileWatcher.EnableRaisingEvents = false;
            m_editFileWatcher = null;
        }
    }

    public void SetCurrentEvent(UndoEvent newEvent)
    {
        //instead of assign newEvent to m_curEvent, we'll copy it
        m_curEvent = new UndoEvent();
        
        //it would be slow to copy each thing by hand, so we'll itterate through its properties and copy them
        foreach (var prop in newEvent.GetType().GetFields())
        {
            prop.SetValue(m_curEvent, prop.GetValue(newEvent));
        }

        m_curEvent.m_sprite = null;
        m_curEvent.m_texture = null;

    }
    public GameObject Duplicate()
    {
        //make a copy of this entire object

        GameObject go = ImageGenerator.Get().CreateNewPic();
        PicMain targetPicScript = go.GetComponent<PicMain>();
        
        //if in adventure mode, go ahead and move it to the right of us
        if (AdventureLogic.Get().IsActive())
        {
            go.transform.position = new Vector3(transform.position.x + 5.12f, transform.position.y, transform.position.z);
        }

        PicTargetRect targetRectScript = go.GetComponent<PicTargetRect>();
        PicTextToImage targetTextToImageScript = go.GetComponent<PicTextToImage>();
        
        if (IsMovie() && m_picMovie != null)
        {
            string moviePath = m_picMovie.GetFileName();
            if (!string.IsNullOrWhiteSpace(moviePath) && File.Exists(moviePath) && targetPicScript.m_picMovie != null)
            {
                targetPicScript.m_picMovie.SetAutoDeleteFileWhenDone(false);
                targetPicScript.m_picMovie.PlayMovie(moviePath);
            }
        }
        else
        {
            if (m_pic != null && m_pic.sprite != null && m_pic.sprite.texture != null)
                targetPicScript.SetImage(m_pic.sprite.texture, true);
            if (m_mask != null && m_mask.sprite != null && m_mask.sprite.texture != null)
                targetPicScript.SetMask(m_mask.sprite.texture, true);
        }

        if (targetTextToImageScript != null && m_picTextToImageScript != null)
        {
            targetTextToImageScript.SetSeed(m_picTextToImageScript.GetSeed()); //if we've set it, it will carry to the duplicate as well
            targetTextToImageScript.SetTextStrength(m_picTextToImageScript.GetTextStrength()); //if we've set it, it will carry to the duplicate as well
            targetTextToImageScript.SetPrompt(m_picTextToImageScript.GetPrompt()); //if we've set it, it will carry to the duplicate as well
            targetTextToImageScript.SetNegativePrompt(m_picTextToImageScript.GetNegativePrompt()); //if we've set it, it will carry to the duplicate as well
        }
        if (targetRectScript != null && m_targetRectScript != null)
            targetRectScript.SetOffsetRect(m_targetRectScript.GetOffsetRect());
        targetPicScript.SetCurrentEvent(m_curEvent); //copy the current event
        
        targetPicScript._jobHistory = new List<PicJob>(_jobHistory); //copy the job history
        return go;
    }

    public GameObject DuplicateToExistingPic(GameObject go)
    {
        PicMain targetPicScript = go.GetComponent<PicMain>();

      
        PicTargetRect targetRectScript = go.GetComponent<PicTargetRect>();
        PicTextToImage targetTextToImageScript = go.GetComponent<PicTextToImage>();

        if (IsMovie() && m_picMovie != null)
        {
            string moviePath = m_picMovie.GetFileName();
            if (!string.IsNullOrWhiteSpace(moviePath) && File.Exists(moviePath) && targetPicScript.m_picMovie != null)
            {
                targetPicScript.m_picMovie.SetAutoDeleteFileWhenDone(false);
                targetPicScript.m_picMovie.PlayMovie(moviePath);
            }
        }
        else
        {
            if (m_pic != null && m_pic.sprite != null && m_pic.sprite.texture != null)
                targetPicScript.SetImage(m_pic.sprite.texture, true);
            if (m_mask != null && m_mask.sprite != null && m_mask.sprite.texture != null)
                targetPicScript.SetMask(m_mask.sprite.texture, true);
        }

        if (targetTextToImageScript != null && m_picTextToImageScript != null)
        {
            targetTextToImageScript.SetSeed(m_picTextToImageScript.GetSeed()); //if we've set it, it will carry to the duplicate as well
            targetTextToImageScript.SetTextStrength(m_picTextToImageScript.GetTextStrength()); //if we've set it, it will carry to the duplicate as well
            targetTextToImageScript.SetPrompt(m_picTextToImageScript.GetPrompt()); //if we've set it, it will carry to the duplicate as well
            targetTextToImageScript.SetNegativePrompt(m_picTextToImageScript.GetNegativePrompt()); //if we've set it, it will carry to the duplicate as well
        }
        if (targetRectScript != null && m_targetRectScript != null)
            targetRectScript.SetOffsetRect(m_targetRectScript.GetOffsetRect());
        targetPicScript.SetCurrentEvent(m_curEvent); //copy the current event

        targetPicScript._jobHistory = new List<PicJob>(_jobHistory); //copy the job history
        return go;
    }
    public void SetImage(Texture2D newImage, bool bDoFullCopy)
    {
        if (bDoFullCopy)
        {
            newImage = newImage.Duplicate();
        }

        float biggestSize = Math.Max(newImage.width, newImage.height);

        UnityEngine.Sprite newSprite = UnityEngine.Sprite.Create(newImage,
            new Rect(0, 0, newImage.width, newImage.height), new Vector2(0.5f, 0.5f), (biggestSize / 5.12f), 0, SpriteMeshType.FullRect);
       
        if (m_pic.sprite != null && m_pic.sprite.texture != null)
        {
            UnityEngine.Object.Destroy(m_pic.sprite.texture); 
        }

        m_pic.sprite = newSprite;
        OnImageReplaced();
        MovePicUpIfNeeded();
        SetNeedsToUpdateInfoPanelFlag();
    }

    // Secondary reference image for 2-input workflows. Takes ownership of the passed texture
    // and uploads it as the "image2" source (see @upload|image2|inputN| in the preset).
    public void SetImage2(Texture2D tex)
    {
        if (m_image2 != null)
        {
            UnityEngine.Object.Destroy(m_image2);
        }
        m_image2 = tex;
    }

    public void SetImage3(Texture2D tex)
    {
        if (m_image3 != null)
        {
            UnityEngine.Object.Destroy(m_image3);
        }
        m_image3 = tex;
    }

    public void SetImage4(Texture2D tex)
    {
        if (m_image4 != null)
        {
            UnityEngine.Object.Destroy(m_image4);
        }
        m_image4 = tex;
    }

    public void SetImage5(Texture2D tex)
    {
        if (m_image5 != null)
        {
            UnityEngine.Object.Destroy(m_image5);
        }
        m_image5 = tex;
    }

    public void InvertMask()
    {
        Debug.Log("invert mask");
        m_mask.sprite.texture.Invert();
        m_mask.sprite.texture.Apply();
        //SetMask(m_mask.sprite.texture, false);
    }

    public void SetMask(Texture2D newImage, bool bDoFullCopy)
    {
        if (bDoFullCopy)
        {
            var originalImage = newImage;
            newImage = originalImage.Duplicate();
            
            //newImage.SetPixelsFromTextureWithAlphaMask(newImage, originalImage);
            //newImage.Fill(0.0);
            //newImage.FillAlpha(1.0f);
            //newImage.Apply();
        }

        float biggestSize = Math.Max(newImage.width, newImage.height);

        UnityEngine.Sprite newSprite = UnityEngine.Sprite.Create(newImage,
            new Rect(0, 0, newImage.width, newImage.height), new Vector2(0.5f, 0.5f), (biggestSize / 5.12f), 0, SpriteMeshType.FullRect);

        m_picMaskScript.SetMaskFromSprite(newSprite);
    }

    public void OnDuplicateButton()
    {
        Duplicate();
        
        // Also duplicate other selected pics (skip adventure texts)
        ApplyToOtherSelectedPics((pic) => pic.Duplicate());
    }

    public void OnFileEditButton()
    {
        RTQuickMessageManager.Get().ShowMessage("Opening 32 bit bmp with alpha mask in image editor...");
        RTMessageManager.Get().Schedule(0.1f, OnFileEdit);
    }

    public void OnFileEdit()
    {
     
        //if we previously had a watch going, kill it
        StopFileWatchingIfNeeded();
        
        if (m_editFilename == "")
        {
            string tempDir = Application.dataPath;
            tempDir = tempDir.Replace('/', '\\');
            tempDir = tempDir.Substring(0, tempDir.LastIndexOf('\\'));
            tempDir += "\\tempCache";
            Directory.CreateDirectory(tempDir);
            m_editFilename = tempDir + "\\pic_" + System.Guid.NewGuid() + ".bmp";
        }

       
        SaveFile(m_editFilename, "", null, "", false, true, bInvertMaskAlpha: true); //export the mask with subject=white for the external editor (Photoshop/Patchy); reload inverts it back. If m_editFilename is blank, it will create a random one
      
        RunProcess(Config.Get().GetImageEditorPathAndExe(), false, m_editFilename);

        m_editFileWatcher = new FileSystemWatcher();
        m_editFileWatcher.Path = Path.GetDirectoryName(m_editFilename);
        m_editFileWatcher.Filter = Path.GetFileName(m_editFilename);

        // Watch for changes in LastAccess and LastWrite times, and
        // the renaming of files or directories.
        m_editFileWatcher.NotifyFilter = NotifyFilters.LastWrite;

        // Add event handlers
        m_editFileWatcher.Changed += OnChanged;

        // Begin watching
        m_editFileWatcher.EnableRaisingEvents = true;
    }

    private void OnChanged(object source, FileSystemEventArgs e)
    {
        //This runs on a FileSystemWatcher thread-pool thread, so it must NOT touch Unity API.
        //RTConsole.Log -> Add() reaches gameObject.activeInHierarchy / StartCoroutine, which throw
        //off the main thread; that throw used to happen BEFORE the assignment below, so the flag
        //was never set and the reload never fired.  Just set the flag; Update() logs and reloads.
        m_editFileHasChanged = true;
    }

    public void InvalidateExportedEditFile()
    {
        if (m_editFilename == "") return;

        if (File.Exists(m_editFilename))
        {
            File.Delete(m_editFilename);
        }
        StopFileWatchingIfNeeded();
    }

    static void RunProcess(string command, bool runShell, string args = null)
    {
        //string projectCurrentDir = Directory.GetCurrentDirectory();
        //command = projectCurrentDir + "/" + command;
        try
        {
            UnityEngine.Debug.Log(string.Format("{0} Run command: {1}", DateTime.Now, command));

        System.Diagnostics.ProcessStartInfo ps = new System.Diagnostics.ProcessStartInfo(command);
        using (System.Diagnostics.Process p = new System.Diagnostics.Process())
        {
            ps.UseShellExecute = runShell;
            if (!runShell)
            {
                ps.RedirectStandardOutput = true;
                ps.RedirectStandardError = true;
                ps.StandardOutputEncoding = System.Text.ASCIIEncoding.ASCII;
            }

            if (args != null && args != "")
            {
                ps.Arguments = args;
            }
            p.StartInfo = ps;
            p.Start();
            
            /*
            p.WaitForExit();
            if (!runShell)
            {
                string output = p.StandardOutput.ReadToEnd().Trim();
                if (!string.IsNullOrEmpty(output))
                {
                    UnityEngine.Debug.Log(string.Format("{0} Output: {1}", DateTime.Now, output));
                }

                string errors = p.StandardError.ReadToEnd().Trim();
                if (!string.IsNullOrEmpty(errors))
                {
                    UnityEngine.Debug.Log(string.Format("{0} Output: {1}", DateTime.Now, errors));
                }
            }
            */
        }
        }
        catch (Exception ex)
        {
            UnityEngine.Debug.LogError(string.Format("{0} Failed to run command '{1}'. Error: {2}", DateTime.Now, command, ex.Message));
            RTQuickMessageManager.Get().ShowMessage("Failed to run command: " + command);
        }
    }

    public void OnImageReplaced()
    {
        InvalidateCachedPng();
        MovePicUpIfNeeded();
        m_picMaskScript.ResizeMaskIfNeeded();
        m_targetRectScript.UpdatePoints();
    }

    public void AutoSaveImageIfNeeded()
    {
        if (GameLogic.Get().GetAutoSave())
        {
            SaveFile("", "/"+ Config._saveDirName);
        }

        if (GameLogic.Get().GetAutoSavePNG())
        {
            SaveFile("", "/"+Config._saveDirName, null, "", true);
        }

        
        if (GameLogic.Get().GetAnyAutoSave())
        {
            m_picMovie.SaveMovie("/" + Config._saveDirName);
        }
    }

    //save to a random filename if passed a blank filename
    public string SaveFile(string fname="", string subdir = "", Texture2D texToSave = null, string fNamePostFix = "", bool bSaveAsPNG =false, bool bWriteOutTextFileToo = true, bool bInvertMaskAlpha = false)
    {
  
        string fileName = Config.Get().GetBaseFileDir(subdir) + "\\pic_" + System.Guid.NewGuid() + fNamePostFix ;

        if (bSaveAsPNG)
        {
            fileName += ".png";
        } else
        {
            fileName += ".bmp";
        }

        if (fname != null && fname != "")
        {
            fileName = fname;
        }
        byte[] pngBytes;


        if (bSaveAsPNG)
        {
            if (texToSave == null)
            {
                pngBytes = m_pic.sprite.texture.EncodeToPNG();

            }
            else
            {
                bWriteOutTextFileToo = false;
                pngBytes = texToSave.EncodeToPNG();
            }

        }
        else
        {
            if (texToSave == null)
            {
                //bInvertMaskAlpha flips the mask polarity to subject=white for external editors
                //(Photoshop/Patchy); only the "E" edit export sets it.  See OnFileEdit / the reload.
                pngBytes = m_pic.sprite.texture.EncodeToBMP(m_mask.sprite.texture, bInvertMaskAlpha);

            }
            else
            {
                bWriteOutTextFileToo = false;
                pngBytes = texToSave.EncodeToBMP();
            }
        }

        File.WriteAllBytes(fileName, pngBytes);

        if (bWriteOutTextFileToo)
        {
            //write out a text file with the same name, but with .txt extension
            string txtFileName = fileName.Substring(0, fileName.Length - 4) + ".txt";
            UpdateInfoPanel(); //OPTIMIZE:  We don't really need to do it if it's already been done...
            File.WriteAllText(txtFileName, m_infoPanelScript.GetTextInfoWithoutColors());
        }

        RTQuickMessageManager.Get().ShowMessage("Saved " + fileName);
        return fileName;
    }

    public string SaveFileJPG(string fname = "", string subdir = "", Texture2D texToSave = null, string fNamePostFix = "",int JPGquality = 80)
    {

        string fileName = Config.Get().GetBaseFileDir(subdir) + "\\pic_" + System.Guid.NewGuid() + fNamePostFix;

            fileName += ".jpg";
       
        if (fname != null && fname != "")
        {
            fileName = fname;
        }
        byte[] pngBytes;


        bool bWriteOutTextFileToo = true;


        if (texToSave == null)
        {
            pngBytes = m_pic.sprite.texture.EncodeToJPG(JPGquality);

        }
        else
        {
            bWriteOutTextFileToo = false;
            pngBytes = texToSave.EncodeToJPG(JPGquality);
        }

        File.WriteAllBytes(fileName, pngBytes);

        if (bWriteOutTextFileToo)
        {
            //write out a text file with the same name, but with .txt extension
            string txtFileName = fileName.Substring(0, fileName.Length - 4) + ".txt";
            UpdateInfoPanel(); //OPTIMIZE:  We don't really need to do it if it's already been done...
            File.WriteAllText(txtFileName, m_infoPanelScript.GetTextInfoWithoutColors());
        }

        RTQuickMessageManager.Get().ShowMessage("Saved " + fileName);
        return fileName;
    }

    public void SaveFileNoReturn2()
    {

        if (IsMovie())
        {
            m_picMovie.SaveMovie("/" + Config._saveDirName);
        }
        else
        {
            SaveFile("", "/" + Config._saveDirName);
        }
    }

    public void SaveFilePNG()
    {
        SaveFile("", "/" + Config._saveDirName, null,"",true);
    }

    public void OnExportMovieClipButton()
    {
        if (!IsMovie() || m_picMovie == null)
        {
            RTQuickMessageManager.Get().ShowMessage("Export movie clip only works on a movie pic");
            return;
        }

        string sourcePath = m_picMovie.GetProcessingFileName();
        if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
        {
            RTQuickMessageManager.Get().ShowMessage("Movie source file is missing");
            return;
        }

        float currentPlaybackSeconds = (float)m_picMovie.GetCurrentPlaybackTimeSeconds();
        StartCoroutine(ShowExportMovieClipDialog(sourcePath, currentPlaybackSeconds));
    }

    private IEnumerator ShowExportMovieClipDialog(string sourcePath, float initialStartSeconds)
    {
        FfmpegTool.VideoInfo info = null;
        string error = null;
        RTQuickMessageManager.Get().ShowMessage("Inspecting movie...");
        yield return FfmpegTool.ProbeVideo(sourcePath, (i, e) => { info = i; error = e; });

        if (!string.IsNullOrWhiteSpace(error) || info == null)
        {
            RTQuickMessageManager.Get().ShowMessage("Could not inspect movie: " + (error ?? "unknown error"));
            yield break;
        }

        ChatVideoClipChooser.Show(
            null,
            null,
            sourcePath,
            info,
            selection => StartCoroutine(ExportMovieClipSelection(sourcePath, selection)),
            () => { },
            "Export Movie Clip",
            "Export Clip",
            initialStartSeconds,
            onImportStill: seconds =>
            {
                // Same shared clip chooser as the drag-drop video import: grab the frame
                // at the current scrub position and drop it into AI Chat as an image bubble.
                string dims = info.Width > 0 && info.Height > 0 ? $"{info.Width}x{info.Height}" : null;
                if (!AIChatPanel.AddLocalStillFrameToChat(sourcePath, seconds, dims, out string stillError))
                    RTQuickMessageManager.Get().ShowMessage("Could not import still: " + stillError);
            });
    }

    private IEnumerator ExportMovieClipSelection(string sourcePath, ChatVideoClipChooser.ClipSelection selection)
    {
        if (selection == null)
            yield break;

        string outputPath = FfmpegTool.GetClipOutputPath(sourcePath);
        FfmpegTool.ClipResult result = null;
        RTQuickMessageManager.Get().ShowMessage("Exporting movie clip...");
        yield return FfmpegTool.CreateClip(
            sourcePath,
            selection.StartSeconds,
            selection.DurationSeconds,
            outputPath,
            r => result = r,
            fps: selection.Fps,
            includeAudio: selection.IncludeAudio);

        if (result == null || !result.Success)
        {
            RTQuickMessageManager.Get().ShowMessage("Could not export movie clip: " + (result != null ? result.Error : "unknown error"));
            yield break;
        }

        string dimensions = null;
        FfmpegTool.VideoInfo outputInfo = null;
        string probeError = null;
        yield return FfmpegTool.ProbeVideo(result.OutputPath, (i, e) => { outputInfo = i; probeError = e; });
        if (outputInfo != null)
            dimensions = BuildMovieClipDimensionsText(outputInfo);

        if (AIChatPanel.AddLocalMovieClipToChat(result.OutputPath, dimensions, out string chatError))
            RTQuickMessageManager.Get().ShowMessage("Exported clip and added it to AI Chat");
        else
            RTQuickMessageManager.Get().ShowMessage("Exported clip, but could not add to AI Chat: " + chatError);
    }

    private static string BuildMovieClipDimensionsText(FfmpegTool.VideoInfo info)
    {
        if (info == null || info.Width <= 0 || info.Height <= 0) return null;
        string dims = $"{info.Width}x{info.Height}";
        if (info.Fps > 0)
            dims += $" @{info.Fps:0.##}fps";
        return dims;
    }

    //Encodes the current image to a temp PNG and shells out to utils\RTClip.exe in "set"
    //mode, which places it on the Windows clipboard (standard bitmap + alpha-preserving
    //PNG format).  Lets the user paste a generated pic into AI Chat or any other app.
    //Mirrors the read path in LoadImageFromClipboard / ChatImageAttachmentZone.
    public void CopyImageToClipboard()
    {
        try
        {
            if (IsMovie())
            {
                RTQuickMessageManager.Get().ShowMessage("Can't copy a movie to the clipboard");
                return;
            }

            if (m_pic == null || m_pic.sprite == null || m_pic.sprite.texture == null)
            {
                RTQuickMessageManager.Get().ShowMessage("No image to copy");
                return;
            }

            string root = Application.dataPath.Replace('/', '\\');
            root = root.Substring(0, root.LastIndexOf('\\'));
            string exe = root + "\\utils\\RTClip.exe";
            string tempPngFile = root + "\\winclip_copy_image.png";

            if (!File.Exists(exe))
            {
                RTQuickMessageManager.Get().ShowMessage("RTClip.exe not found");
                return;
            }

            File.WriteAllBytes(tempPngFile, m_pic.sprite.texture.EncodeToPNG());

            var processInfo = new System.Diagnostics.ProcessStartInfo(exe, "set \"" + tempPngFile + "\"");
            processInfo.CreateNoWindow = true;
            processInfo.UseShellExecute = false;

            var process = System.Diagnostics.Process.Start(processInfo);
            process.WaitForExit();
            process.Close();

            //RTClip flushes the clipboard before exit, so the temp file is no longer needed.
            RTUtil.DeleteFileIfItExists(tempPngFile);

            RTQuickMessageManager.Get().ShowMessage("Copied image to clipboard");
        }
        catch (Exception e)
        {
            RTConsole.Log("Copy to clipboard failed: " + e.Message);
            RTQuickMessageManager.Get().ShowMessage("Copy to clipboard failed");
        }
    }

    public void SaveFileNoReturn()
    {
        RTQuickMessageManager.Get().ShowMessage("Saving image...");

        RTMessageManager.Get().Schedule(0.01f, this.SaveFileNoReturn2);
    }

    //a method I tried that crashed all the time, don't know why

    /*
    public void OpenFile()
    {

        var fileContent = string.Empty;
        var filePath = string.Empty;
        Debug.Log("opening file");
        OpenFileDialog openFileDialog = new OpenFileDialog();

        openFileDialog.InitialDirectory = Directory.GetCurrentDirectory();
        openFileDialog.Filter = "images files (*.png)|*.png|All files (*.*)|*.*";
        openFileDialog.FilterIndex = 2;
        openFileDialog.RestoreDirectory = true;
        Debug.Log("opening file dialog");

        var results = openFileDialog.ShowDialog();

        if (results == DialogResult.OK)
        {

            filePath = openFileDialog.FileName;
            Debug.Log("Chose " + filePath);

            //Get the path of specified file
            var png = File.ReadAllBytes(filePath);

            try
            {
                var buffer = File.ReadAllBytes(filePath);
                Texture2D texture = new Texture2D(0, 0, TextureFormat.RGBA32, false);
                texture.LoadImage(buffer);
                AddImageUndo();
                Sprite newSprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f), biggestsize / 5.12f);
                m_pic.sprite = newSprite;

            }
            catch (Exception e)
            {
                System.Console.WriteLine(e.StackTrace);
            }


        }
        else
        {
            Debug.LogError("Error loading file " + filePath);
        }
    }
    */


    public void OnInterrogateButton()
    {
         var e = new ScheduledGPUEvent();
        e.mode = "interrogate";
        e.targetObj = this.gameObject;
        ImageGenerator.Get().ScheduleGPURequest(e);
        SetStatusMessage("Waiting for GPU...");
    }

    public void OnUpscaleButton()
    {
        var e = new ScheduledGPUEvent();
        e.mode = "upscale";
        e.targetObj = this.gameObject;
        ImageGenerator.Get().ScheduleGPURequest(e);
        SetStatusMessage("Waiting for GPU...");
    }



    public void AddTextLabelToImage(string label)
    {
        if (label == null || label.Count() == 0) return;

        float bottom = 64;

        Rect textAreaRect = new Rect(0, 0, m_pic.sprite.texture.width, bottom);

        float maxSize = textAreaRect.width;
        if (textAreaRect.height > maxSize)
        {
            maxSize = textAreaRect.height;
        }

        float minSize = textAreaRect.width;
        if (textAreaRect.height < minSize)
        {
            minSize = textAreaRect.height;
        }

        float fontSize = maxSize / 4;
        float titleHeight = 0;
        titleHeight = textAreaRect.height * 0.34f;

        Texture2D tex;
        Rect rect;

        TMPro.FontStyles fontStyles = TMPro.FontStyles.Bold;
        int fontID = 0;

        tex = RTUtil.RenderTextToTexture2D(label, (int)textAreaRect.width,
           (int)(textAreaRect.height - titleHeight), AIGuideManager.Get().GetFontByID(fontID), fontSize, Color.white,
        false, new Vector2(1, 1), fontStyles);

        //File.WriteAllBytes("crap.png", tex.EncodeToPNG()); //for debugging

        rect = textAreaRect;
        rect.yMax -= titleHeight;
        //add it to our real texture
        m_pic.sprite.texture.BlitWithAlpha((int)rect.xMin, (int)(m_pic.sprite.texture.height - rect.height), tex,
            0, 0, (int)rect.width, (int)rect.height);
        m_pic.sprite.texture.Apply();

    }

    public void CleanupPixelArt()
    {
        AddTextLabelToImage("AI Generated");
        m_pic.sprite.texture.filterMode = FilterMode.Point;
        int originalWidth = m_pic.sprite.texture.width;
        int originalHeight = m_pic.sprite.texture.height;

        Resize(128, 128, true, FilterMode.Point);
        m_pic.sprite.texture.filterMode = FilterMode.Point;
        m_pic.sprite.texture.Apply();


        //now let's resize it back up to the original size it was using our saved width/height
       Resize(originalWidth, originalHeight, true, FilterMode.Point);
       m_pic.sprite.texture.filterMode = FilterMode.Point;
       m_pic.sprite.texture.Apply();
    }
    public void OnToggleSmoothing()
    {
        if (m_pic.sprite.texture.filterMode != FilterMode.Point)
        {
            m_pic.sprite.texture.filterMode = FilterMode.Point;
            if (m_undoevent != null && m_undoevent.m_texture != null)
            {
                m_undoevent.m_texture.filterMode = FilterMode.Point;
            }
        } else
        { 
               
           m_pic.sprite.texture.filterMode = FilterMode.Bilinear;
      
            if (m_undoevent != null && m_undoevent.m_texture != null)
             {
                m_undoevent.m_texture.filterMode = FilterMode.Bilinear;
             }
        }
      
    }

    public void FillAlphaMaskWithImageAlpha()
    {
        m_picMaskScript.SetMaskFromTextureAlpha(m_pic.sprite.texture);
    }


    public void FillAlphaMaskIfBlank()
    {
        m_picMaskScript.FillAlphaMaskIfBlank();
    }
    public void OnInpaintButton()
    {
        var e = new ScheduledGPUEvent();
        e.mode = "inpaint";
        e.targetObj = this.gameObject;
        FillAlphaMaskIfBlank();

        ImageGenerator.Get().ScheduleGPURequest(e);
        //ImageGenerator.Get().SetLastImg2ImgObject(this.gameObject); //if we wanted a non batch img2img to count for the restarting batch img2img button
        SetStatusMessage("Waiting for GPU...");
    }

  

    IEnumerator MutateAndWait()
    {
        //RGB texture test:

        var blurFilter = new ConvFilter.BoxBlurFilter();
        var processor = new ConvFilter.ConvolutionProcessor(m_pic.sprite.texture);

        for (int i = 0; i < 2; i++)
        {
            yield return StartCoroutine(processor.ComputeWith(blurFilter));
            processor = new ConvFilter.ConvolutionProcessor(processor.m_originalMap);
        }

        Debug.Log("Done!");

        var newTex = processor.m_originalMap;
        float biggestSize = Math.Max(newTex.width, newTex.height);

        Sprite newSprite = Sprite.Create(newTex, new Rect(0, 0, newTex.width, newTex.height), new Vector2(0.5f, 0.5f), biggestSize / 5.12f, 0, SpriteMeshType.FullRect);
        m_pic.sprite = newSprite;
        m_pic.sprite.texture.Apply();

    }

    public void OnGenerateMaskButton()
    {
        //GameLogic.Get().ShowCompatibilityWarningIfNeeded();
        Debug.Log("Generating mask...");
        var e = new ScheduledGPUEvent();
        e.mode = "genmask";
        e.targetObj = this.gameObject;
        ImageGenerator.Get().ScheduleGPURequest(e);
        SetStatusMessage("Waiting for GPU...");
    }

    public void OnGenerateMaskButtonSimple()
    {
        //GameLogic.Get().ShowCompatibilityWarningIfNeeded();
        Debug.Log("Generating mask...");
        var e = new ScheduledGPUEvent();
        e.mode = "genmask";
        e.targetObj = this.gameObject;
        e.disableTranslucency = true;
        ImageGenerator.Get().ScheduleGPURequest(e);
        SetStatusMessage("Waiting for GPU...");
    }

    public void OnMutateButton()
    {
        Debug.Log("Blurring..");
        AddImageUndo(true);

        StartCoroutine(MutateAndWait());

       //texture.Apply();

       // byte[] picPng = texture.EncodeToPNG();
        //File.WriteAllBytes(Application.dataPath + "/../SavedScreen.png", picPng);

        //have to rebuild the sprite though
     
       
        //Mask test

        /*
        byte[] picPng = m_mask.sprite.texture.EncodeToPNG();
        File.WriteAllBytes(Application.dataPath + "/../PreSavedScreen.png", picPng);

        //Texture2D texture = m_mask.sprite.texture.GetGaussianBlurredVersion();

        int revolutions = 4;
        Texture2D texture = m_mask.sprite.texture;

        for (int i = 0; i < revolutions; i++)
        {
           texture = texture.GetBlurredVersion();
        }

        picPng = texture.EncodeToPNG();
        File.WriteAllBytes(Application.dataPath + "/../SavedScreen.png", picPng);

        //have to rebuild the sprite though
        Sprite newSprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f), texture.width / 5.12f);
        m_mask.sprite = newSprite;
        m_mask.sprite.texture.Apply();

        */


        /*
        var e = new ScheduledGPUEvent();
        e.mode = "mutate";
        e.targetObj = this.gameObject;
        ImageGenerator.Get().ScheduleGPURequest(e);
        SetStatusMessage("Waiting for GPU...");
        */
    }

   

    List<string> ConvertHistoryBackToJobList()
    {
        List<string> jobList = new List<string>();
        foreach (PicJob job in _jobHistory)
        {
            if (job._originalJobString != null)
                jobList.Add(job._originalJobString);
        }
        return jobList;
    }

    public void OnRenderWithAITOrA1111()
    {
        int gpuID = Config.Get().GetFreeGPU(RTRendererType.AI_Tools_or_A1111, false);
        
        if (gpuID == -1)
        {
            //write message they can see
            RTQuickMessageManager.Get().ShowMessage("No AIT/A1111 server is connected to, check your config");
            return;
        }

        GetCurrentStats().m_requestedRenderer = RTRendererType.AI_Tools_or_A1111;
        GetCurrentStats().m_gpu = Config.Get().GetFreeGPU(GetCurrentStats().m_requestedRenderer, true); //show we don't care

        OnReRenderNewSeedButton();
    }

    public void OnRenderWithComfyUI()
    {
        int gpuID = Config.Get().GetFreeGPU(RTRendererType.ComfyUI, true);

        if (gpuID == -1)
        {
            //write message they can see
            RTQuickMessageManager.Get().ShowMessage("No ComfyUI server is connected to, check your config");
            return;
        }

        GetCurrentStats().m_requestedRenderer = RTRendererType.ComfyUI;
        GetCurrentStats().m_gpu = Config.Get().GetFreeGPU(GetCurrentStats().m_requestedRenderer, true); //show we don't care

        OnReRenderNewSeedButton();
    }
    public void ClearErrorsAndJobs()
    {
        SetLLMActive(false); // Release LLM slot if this pic was using one
        ClearJobs();
        SetStatusMessage("");
        if (IsBusy())
        {
            StartCoroutine(m_picTextToImageScript.CancelRender());
        }
    }

    /// <summary>
    /// Surface a workflow-runtime abort back to whoever installed
    /// <see cref="m_workflowErrorReporter"/> (currently only the AI Chat host) and
    /// then null out the reporter so cascading internal failures from the same
    /// workflow run don't re-spam the chat. Safe to call when no reporter is wired -
    /// it's a no-op in that case.
    /// </summary>
    private void ReportWorkflowAbortOnce(string message)
    {
        var reporter = m_workflowErrorReporter;
        if (reporter == null) return;
        m_workflowErrorReporter = null;
        try { reporter(message); }
        catch (Exception ex) { Debug.LogError("PicMain: workflow error reporter threw: " + ex); }
    }

    /// <summary>
    /// Core implementation that clears history and movie for this pic only.
    /// Does not propagate to other selected pics.
    /// </summary>
    private void ClearHistoryToBasePicCore()
    {
        // Clear jobs and errors first
        SetLLMActive(false);
        ClearJobs();
        SetStatusMessage("");
        if (IsBusy())
        {
            StartCoroutine(m_picTextToImageScript.CancelRender());
        }
        
        // Kill any movie, keeping only the current frame as the image
        m_picMovie.KillMovie();
        
        // Clear all job history
        _jobHistory.Clear();
        m_jobDefaultInfo = null;
    }

    /// <summary>
    /// Clears all history and any movie, leaving just the base image and mask.
    /// Works on all selected images if multi-select is being used.
    /// </summary>
    public void ClearHistoryToBasePic()
    {
        ClearHistoryToBasePicCore();
        
        // Apply to other selected pics (call Core directly to avoid recursion)
        ApplyToOtherSelectedPics((pic) => pic.ClearHistoryToBasePicCore());
    }

    public void OnReRenderButton()
    {
        if (_jobHistory.Count == 0)
        {
            SetStatusMessage("Nothing to re-render.");
            return;
        }

        m_picMovie.KillMovie();
        AddImageUndo(true);

        PicJob job = _jobHistory[0]; //remember the starting state

        List<string> jobList = ConvertHistoryBackToJobList();
        _jobHistory.Clear();

        // Clear existing jobs and release GPU lock before re-rendering
        m_picJobs.Clear();
        m_jobList.Clear();
        m_waitingForPicJob = false;
        ReleaseServerOwnership();

        AddJobListWithStartingJobInfo(job, jobList);
        SetStatusMessage("Waiting for GPU...");
    }

    public void OnReRenderNewSeedButton()
    {

        OnReRenderButton();

        PicTextToImage textToImage = GetComponent<PicTextToImage>();
        textToImage.SetSeed(-1); //causes it to be random again
    }

    //I return it, in case the caller wants to set additional parms
    public ScheduledGPUEvent OnRenderButton(string promptAddition)
    {
        var e = new ScheduledGPUEvent();
        e.mode = "render";
        e.targetObj = this.gameObject;
        //e.promptOverride = promptAddition;

        //let's assert so we can remember to fix this later
        
            Debug.Assert(false, "We need a way to reschedule everything that happened");
        

        if (GetCurrentStats().m_gpu != -1)
        {
            e.requestedRenderer = Config.Get().GetGPUInfo(GetCurrentStats().m_gpu)._requestedRendererType;
        }
        else
        {
            //try to get it from somewhere else
            e.requestedRenderer = GetCurrentStats().m_requestedRenderer;
        }

        ImageGenerator.Get().ScheduleGPURequest(e);
        SetStatusMessage("Waiting for GPU...");
        return e;
    }

    public void ApplyToolsIfNeededMainWorkflowJob(ref PicJob jobMain)
    {
        //don't try to change jobMain, it's probably coming from a copy

        /*
        if (GameLogic.Get().m_applyTool1Toggle.isOn)
        {
            OnTool1();
            //modify the requestedAudioPrompt on the last entry of jobs

            m_picJobs[m_picJobs.Count - 1].requestedAudioPrompt = jobMain.requestedAudioPrompt;
            m_picJobs[m_picJobs.Count - 1].requestedAudioNegativePrompt = jobMain.requestedAudioNegativePrompt;
            m_picJobs[m_picJobs.Count - 1].requestedPrompt = jobMain.requestedPrompt;
            m_picJobs[m_picJobs.Count - 1].requestedNegativePrompt = jobMain.requestedNegativePrompt;
        }
        */
    }

    public void AddJobListWithStartingJobInfo(PicJob job, List<string> jobList)
    {

      if (m_picJobs.Count == 0)
        {
            //instead of adding it here, let's put it in a special starting default job parms
           if (m_jobDefaultInfo != null)
            {
                //Error message
                RTConsole.Log("Error, we're trying to add a job to the job list, but there's already a default job set!");
            }

            m_jobDefaultInfo = job;
        } else
        {
            RTConsole.Log("Jobs already active so ignoring the default starting info we were sent");
            //m_picJobs.Add(job);
        }

        AddJobList(jobList);
    }

    public void RunPresetByName(string presetName)
    {
        RunPresetByName(presetName, null, null);
    }

    // Counter for generating unique variable slots when AI Chat stacks chained presets
    // onto this Pic. Each chained step gets its own slot so prompts don't clobber each
    // other if the queue is mid-flight when the next chain step is appended.
    private int m_chainPresetCounter = 0;

    // One-shot output-dimension overrides for the next RunPresetByName / AppendPresetJobs.
    // Two modes:
    //   1) Explicit: m_workflowWidthOverride > 0 && m_workflowHeightOverride > 0 - force
    //      these exact dimensions into the workflow JSON.
    //   2) Aspect-from-source: m_workflowAspectSrcW > 0 && m_workflowAspectSrcH > 0 -
    //      preserve the preset's pixel budget but rotate it to match this aspect.
    // Cleared after a single preset run so the override doesn't leak into a subsequent
    // unrelated workflow on the same Pic.
    private int m_workflowWidthOverride = 0;
    private int m_workflowHeightOverride = 0;
    private int m_workflowAspectSrcW = 0;
    private int m_workflowAspectSrcH = 0;
    private int m_workflowFrameCountOverride = 0;

    // Dimensions the most recent workflow was queued at, after any override applied.
    // Lets a chained step query this Pic's "size class so far" while the prior step's
    // texture is still rendering and TryGetCurrentTexture would return null.
    public int LastQueuedWorkflowWidth { get; private set; } = 0;
    public int LastQueuedWorkflowHeight { get; private set; } = 0;

    // Regexes used to dig the preset's default width/height out of the joblist.
    // Match "@replace|\"width\": <N>|" - the FIRST half of the @replace pair, which
    // carries the literal default the preset author wrote (and which the workflow
    // JSON actually contains until our override appends a follow-up @replace).
    private static readonly System.Text.RegularExpressions.Regex WorkflowWidthReplaceRx =
        new System.Text.RegularExpressions.Regex(@"@replace\|""width"":\s*(\d+)\|",
            System.Text.RegularExpressions.RegexOptions.Compiled);
    private static readonly System.Text.RegularExpressions.Regex WorkflowHeightReplaceRx =
        new System.Text.RegularExpressions.Regex(@"@replace\|""height"":\s*(\d+)\|",
            System.Text.RegularExpressions.RegexOptions.Compiled);
    private static readonly System.Text.RegularExpressions.Regex WorkflowLengthReplaceRx =
        new System.Text.RegularExpressions.Regex(@"@replace\|""length"":\s*(\d+)\|",
            System.Text.RegularExpressions.RegexOptions.Compiled);
    private static readonly System.Text.RegularExpressions.Regex WorkflowFrameLoadCapReplaceRx =
        new System.Text.RegularExpressions.Regex(@"@replace\|""frame_load_cap"":\s*(\d+)\|",
            System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>
    /// Force the next RunPresetByName / AppendPresetJobs to run the workflow at exact
    /// <paramref name="width"/>x<paramref name="height"/>. Pass 0 for either to clear.
    /// One-shot: cleared automatically after the next preset is queued. Useful when an
    /// LLM action specifies width="N" height="N" attributes.
    /// </summary>
    public void SetWorkflowDimensionOverride(int width, int height)
    {
        m_workflowWidthOverride = Mathf.Max(0, width);
        m_workflowHeightOverride = Mathf.Max(0, height);
    }

    /// <summary>
    /// Force the next RunPresetByName / AppendPresetJobs to run the workflow at the
    /// aspect ratio of <paramref name="srcW"/>x<paramref name="srcH"/>, while preserving
    /// the preset's overall pixel budget (so the model's "size class" stays the same).
    /// Resolved at queue time against the preset's actual default dimensions. Lower
    /// priority than <see cref="SetWorkflowDimensionOverride"/> - that takes precedence
    /// when both are set. Pass 0/0 to clear. One-shot.
    /// </summary>
    public void SetWorkflowAspectSource(int srcW, int srcH)
    {
        m_workflowAspectSrcW = Mathf.Max(0, srcW);
        m_workflowAspectSrcH = Mathf.Max(0, srcH);
    }

    /// <summary>
    /// Force the next RunPresetByName / AppendPresetJobs to use this frame count for
    /// workflows that expose length/frame_load_cap through standard @replace lines.
    /// One-shot and independent from the width/height overrides.
    /// </summary>
    public void SetWorkflowFrameCountOverride(int frameCount)
    {
        m_workflowFrameCountOverride = Mathf.Max(0, frameCount);
    }

    /// <summary>
    /// Mutates <paramref name="lines"/> to inject extra @replace operations for width/
    /// height on the workflow-loading line, honouring the one-shot dimension overrides
    /// set via SetWorkflowDimensionOverride / SetWorkflowAspectSource. Clears the
    /// overrides on exit. No-op when no override is set or the preset doesn't follow
    /// the standard @replace|"width":...| pattern (so non-image/video presets pass
    /// through untouched).
    /// </summary>
    private void ApplyDimensionOverrideToJoblist(List<string> lines)
    {
        bool hasExplicit = m_workflowWidthOverride > 0 && m_workflowHeightOverride > 0;
        bool hasAspect = m_workflowAspectSrcW > 0 && m_workflowAspectSrcH > 0;
        bool hasFrameOverride = m_workflowFrameCountOverride > 0;
        try
        {
            if (lines == null || lines.Count == 0) return;

            for (int i = 0; i < lines.Count; i++)
            {
                string line = lines[i];
                if (string.IsNullOrEmpty(line)) continue;
                if (line.IndexOf(".json", StringComparison.OrdinalIgnoreCase) < 0) continue;

                var wMatch = WorkflowWidthReplaceRx.Match(line);
                var hMatch = WorkflowHeightReplaceRx.Match(line);
                int presetW = 0;
                int presetH = 0;
                bool hasDimensionReplace = wMatch.Success
                    && hMatch.Success
                    && int.TryParse(wMatch.Groups[1].Value, out presetW)
                    && int.TryParse(hMatch.Groups[1].Value, out presetH)
                    && presetW > 0
                    && presetH > 0;
                bool touched = false;

                // No override requested: just record the preset's dims as the
                // "last queued" pair so a follow-up chain step can use them.
                if (hasDimensionReplace && !hasExplicit && !hasAspect)
                {
                    LastQueuedWorkflowWidth = presetW;
                    LastQueuedWorkflowHeight = presetH;
                }
                else if (hasDimensionReplace && (hasExplicit || hasAspect))
                {
                    int newW, newH;
                    if (hasExplicit)
                    {
                        newW = m_workflowWidthOverride;
                        newH = m_workflowHeightOverride;
                    }
                    else
                    {
                        // Preserve the preset's budget; rotate it to the source's aspect.
                        long budget = (long)presetW * presetH;
                        float srcAspect = m_workflowAspectSrcW / (float)m_workflowAspectSrcH;
                        if (srcAspect <= 0f) return; // bad source - skip
                        double h = Math.Sqrt(budget / (double)srcAspect);
                        double w = h * srcAspect;
                        newW = Mathf.RoundToInt((float)(w / 32.0)) * 32;
                        newH = Mathf.RoundToInt((float)(h / 32.0)) * 32;
                    }

                    newW = Mathf.Clamp((newW / 32) * 32, 256, 1280);
                    newH = Mathf.Clamp((newH / 32) * 32, 256, 1280);
                    if (newW <= 0 || newH <= 0) return;

                    // Record what we're queueing so the next chain step can read it.
                    LastQueuedWorkflowWidth = newW;
                    LastQueuedWorkflowHeight = newH;

                    if (newW != presetW || newH != presetH)
                    {
                        line = line
                            + $" @replace|\"width\": {presetW}|\"width\": {newW}|"
                            + $" @replace|\"height\": {presetH}|\"height\": {newH}|";
                        touched = true;
                    }
                }

                if (hasFrameOverride && TryAppendFrameCountOverride(ref line, m_workflowFrameCountOverride))
                    touched = true;

                if (touched)
                    lines[i] = line;

                if (hasDimensionReplace || touched)
                    return;
            }
        }
        finally
        {
            // Always clear - one-shot semantics. If no matching line was found we still
            // clear so the override doesn't bleed into the NEXT preset run.
            m_workflowWidthOverride = 0;
            m_workflowHeightOverride = 0;
            m_workflowAspectSrcW = 0;
            m_workflowAspectSrcH = 0;
            m_workflowFrameCountOverride = 0;
        }
    }

    private static bool TryAppendFrameCountOverride(ref string line, int frameCount)
    {
        if (frameCount <= 0 || string.IsNullOrEmpty(line))
            return false;

        var lengthMatch = WorkflowLengthReplaceRx.Match(line);
        if (!lengthMatch.Success || !int.TryParse(lengthMatch.Groups[1].Value, out int defaultLength) || defaultLength <= 0)
            return false;

        int defaultFrameLoadCap = defaultLength;
        var frameLoadCapMatch = WorkflowFrameLoadCapReplaceRx.Match(line);
        if (frameLoadCapMatch.Success
            && int.TryParse(frameLoadCapMatch.Groups[1].Value, out int parsedFrameLoadCap)
            && parsedFrameLoadCap > 0)
        {
            defaultFrameLoadCap = parsedFrameLoadCap;
        }

        line = line
            + $" @replace|\"length\": {defaultLength}|\"length\": {frameCount}|"
            + $" @replace|\"frame_load_cap\": {defaultFrameLoadCap}|\"frame_load_cap\": {frameCount}|";
        return true;
    }

    /// <summary>
    /// Append a preset's compiled job list to this Pic's queue WITHOUT touching
    /// <c>m_jobDefaultInfo</c> or the in-flight job. Used by the AI Chat skills system
    /// to stack a follow-up workflow (e.g. img2vid) onto a Pic that already has a base
    /// workflow queued or running. The chained workflow inherits the prior step's
    /// output via the preset's own <c>@upload|image1|input1|</c> modifier.
    ///
    /// Prompts are routed via the Pic's variable manager (NOT embedded in the compiled
    /// command string) so any character is safe in the prompt - the joblist parser
    /// splits on '|', which would otherwise corrupt arbitrary LLM-written text.
    /// </summary>
    /// <summary>
    /// Queue a local (non-GPU) coroutine to run after any pending workflow / LLM /
    /// other local-op steps on this Pic finish. Used by the AI Chat composition
    /// skills (draw_text, add_border, paste_image, crop_resize, draw_shape) so a
    /// chain="true" tag emitted right after a generate_image properly waits for
    /// the diffusion step to land before drawing on top of it.
    ///
    /// The op receives this PicMain as a parameter (so it can mutate
    /// m_pic.sprite.texture, call AddBorder, etc.) and returns an IEnumerator that
    /// is run on this MonoBehaviour. Its completion automatically advances the job
    /// queue - same lifecycle as a workflow step.
    /// </summary>
    public void AppendLocalOp(Func<PicMain, IEnumerator> op)
    {
        if (op == null) return;
        string key = "op" + (++m_localOpCounter);
        m_pendingLocalOps[key] = op;
        m_jobList.Add("local_op|" + key);
        UpdateJobs();
    }

    /// <summary>
    /// Run a local coroutine immediately on this Pic without going through the job
    /// queue. Use when nothing else is queued and you want the op to start now (e.g.
    /// the AI Chat composition skills spawn a fresh Pic, seed its texture, then call
    /// this to apply the op without the queue indirection). Equivalent to
    /// StartCoroutine(op(this)) but kept symmetric with AppendLocalOp.
    /// </summary>
    public Coroutine RunLocalOpImmediate(Func<PicMain, IEnumerator> op)
    {
        if (op == null) return null;
        return StartCoroutine(op(this));
    }

    private IEnumerator RunLocalOpCoroutine(Func<PicMain, IEnumerator> op)
    {
        IEnumerator inner = null;
        try { inner = op(this); }
        catch (Exception ex)
        {
            Debug.LogError("PicMain local op threw on creation: " + ex);
        }
        if (inner != null) yield return StartCoroutine(inner);
        m_waitingForPicJob = false;
        UpdateJobs();
    }

    public void AppendPresetJobs(string presetName, string promptOverride, string negPromptOverride)
    {
        var lines = GameLogic.Get().GetTempPicJobListAsListOfStrings(presetName);
        if (lines == null) return;

        // Apply pending one-shot dimension overrides (auto-aspect-from-source or
        // explicit width/height) before queuing.
        ApplyDimensionOverrideToJoblist(lines);

        var prefixed = new List<string>();

        if (!string.IsNullOrEmpty(promptOverride))
        {
            int slot = ++m_chainPresetCounter;
            string varName = "aichat_chain_prompt_" + slot;
            m_variableManager.SetText(varName, promptOverride);
            prefixed.Add("command @copy|%" + varName + "%|prompt|");
        }
        if (!string.IsNullOrEmpty(negPromptOverride))
        {
            int slot = m_chainPresetCounter; // share slot id with prompt above for traceability
            string varName = "aichat_chain_neg_prompt_" + slot;
            m_variableManager.SetText(varName, negPromptOverride);
            prefixed.Add("command @copy|%" + varName + "%|negative_prompt|");
        }

        prefixed.AddRange(lines);
        AddJobList(prefixed);

        // Kick the queue. UpdateJobs is idempotent / re-entrant-safe - if the prior
        // step is mid-flight it's a no-op, and if the queue went idle between the
        // base step finishing and this append landing, this picks the chain back up.
        UpdateJobs();
    }

    /// <summary>
    /// Run a preset using explicit prompt overrides instead of pulling from the GameLogic
    /// global prompt fields. Used by the AI Chat skills system, which builds prompts from
    /// the LLM's reply rather than the on-screen prompt input. Pass null for either
    /// override to fall back to the GameLogic value (i.e. legacy single-arg behavior).
    /// </summary>
    public void RunPresetByName(string presetName, string promptOverride, string negativePromptOverride)
    {
        if (_jobHistory.Count() == 0)
        {
            PicJob jobDefaultInfoToStartWith = new PicJob();

            jobDefaultInfoToStartWith._requestedPrompt = promptOverride ?? GameLogic.Get().GetModifiedGlobalPrompt();
            jobDefaultInfoToStartWith._requestedNegativePrompt = negativePromptOverride ?? GameLogic.Get().GetNegativePrompt();
            jobDefaultInfoToStartWith._requestedAudioPrompt = Config.Get().GetDefaultAudioPrompt();
            jobDefaultInfoToStartWith._requestedAudioNegativePrompt = Config.Get().GetDefaultAudioNegativePrompt();
            jobDefaultInfoToStartWith.requestedRenderer = GameLogic.Get().GetGlobalRenderer();

            var lines = GameLogic.Get().GetTempPicJobListAsListOfStrings(presetName);
            ApplyDimensionOverrideToJoblist(lines);
            AddJobListWithStartingJobInfo(jobDefaultInfoToStartWith, lines);
        }
        else
        {

            //Now, we *DO* have history, but we still want to overwrite things with the latest prompts, right?

            m_curEvent.m_picJob._requestedPrompt = promptOverride ?? GameLogic.Get().GetPrompt();
            m_curEvent.m_picJob._requestedNegativePrompt = negativePromptOverride ?? GameLogic.Get().GetNegativePrompt();
            //m_curEvent.m_picJob._requestedAudioPrompt = Config.Get().GetDefaultAudioPrompt();
            //m_curEvent.m_picJob._requestedAudioNegativePrompt = Config.Get().GetDefaultAudioNegativePrompt();
            // Inline AddEveryTempItemToJobList so we can intercept and apply the
            // dimension override before the lines are merged into m_jobList.
            var lines = GameLogic.Get().GetTempPicJobListAsListOfStrings(presetName);
            ApplyDimensionOverrideToJoblist(lines);
            if (lines != null)
            {
                foreach (var job in lines)
                    m_jobList.Add(job);
            }
        }
    }

    /// <summary>
    /// Best-effort accessor for "whatever this Pic is currently displaying" - returns
    /// the movie's RenderTexture when this Pic is a movie, otherwise the still sprite's
    /// texture. Lets viewers (e.g. ChatPicMirror in AI Chat) bind to the live output
    /// without reaching into private fields. Returns false if nothing is renderable yet.
    /// </summary>
    public bool TryGetCurrentTexture(out Texture tex)
    {
        tex = null;

        // Movie case: PicMovie creates a RenderTexture on first frame; we surface the
        // VideoPlayer's targetTexture directly so the chat mirror updates as it plays.
        if (m_picMovie != null && m_picMovie.IsMovie())
        {
            if (m_picMovie._videoPlayer != null && m_picMovie._videoPlayer.targetTexture != null)
            {
                tex = m_picMovie._videoPlayer.targetTexture;
                return true;
            }
            // Movie loaded but RenderTexture not yet provisioned - no texture yet.
            return false;
        }

        // Still image: SpriteRenderer's sprite.texture.
        if (m_pic != null && m_pic.sprite != null && m_pic.sprite.texture != null)
        {
            tex = m_pic.sprite.texture;
            return true;
        }
        return false;
    }

    /// <summary>
    /// Best-effort preload for AI Chat when it needs to snapshot a chat_image whose
    /// movie RenderTexture was unloaded while the world Pic was off-screen. Still
    /// images are already resident in memory in the current Pic lifecycle, so this
    /// mainly forces PicMovie to prepare its VideoPlayer without requiring the user
    /// to pan the main canvas back to that Pic.
    /// </summary>
    public bool TryEnsureLoadedForChatSnapshot()
    {
        if (TryGetCurrentTexture(out Texture tex) && tex != null)
            return true;

        if (m_picMovie != null && m_picMovie.IsMovie())
            return m_picMovie.TryEnsureLoadedForSnapshot();

        return false;
    }

    /// <summary>
    /// Current human-readable status text from the Pic's world-space label (e.g.
    /// "Waiting for GPU...", "Rendering 12/30", ""). Exposed so chat-side mirrors can
    /// echo the live status without reaching into the TextMeshPro component directly.
    /// </summary>
    public string GetStatusMessage()
    {
        return m_text != null ? m_text.text : "";
    }

    /// <summary>
    /// Snapshot the Pic's current displayed image as PNG bytes (whatever
    /// <see cref="TryGetCurrentTexture"/> returns: still sprite OR movie frame).
    /// Used by AI Chat skills that want to feed a previously-generated chat image
    /// back into a fresh img2img / img2vid spawn ("Modify the image you just made").
    /// Returns false if the Pic has no displayable texture yet (e.g. queued render).
    ///
    /// For Texture2D sources (still images) we encode directly. For RenderTexture
    /// sources (movies) we read the current frame via a one-shot RGBA32 readback so
    /// the LLM/preset gets a stable still snapshot of the playing video.
    /// </summary>
    // EncodeToPNG cache: keyed by the texture reference returned from
    // TryGetCurrentTexture. PicMain replaces the texture wholesale on every
    // workflow result (see SetImage / LoadImageByFilename), so a stale cache
    // is naturally evicted by the reference-equality check. Eliminates the
    // 100-500 ms re-encode hitch when the AI Chat path reads the same
    // chat_image="N" multiple times across follow-up edits.
    private byte[] _cachedPng;
    private Texture _cachedPngSourceTex;

    private void InvalidateCachedPng()
    {
        _cachedPng = null;
        _cachedPngSourceTex = null;
    }

    public bool TryGetImageAsPng(out byte[] pngBytes)
    {
        pngBytes = null;
        if (!TryGetCurrentTexture(out Texture tex) || tex == null) return false;

        // Cache hit: the same texture reference encoded earlier is still live.
        if (_cachedPng != null && ReferenceEquals(_cachedPngSourceTex, tex))
        {
            pngBytes = _cachedPng;
            return pngBytes.Length > 0;
        }

        if (tex is Texture2D tex2d)
        {
            try
            {
                pngBytes = tex2d.EncodeToPNG();
                if (pngBytes != null && pngBytes.Length > 0)
                {
                    _cachedPng = pngBytes;
                    _cachedPngSourceTex = tex;
                    return true;
                }
                return false;
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning("PicMain.TryGetImageAsPng: EncodeToPNG threw: " + ex.Message);
                return false;
            }
        }

        if (tex is RenderTexture rt)
        {
            // Movies render into a RenderTexture; copy current frame into a transient
            // Texture2D so we can EncodeToPNG. Cache by the RT reference too - movies
            // change frames, but consecutive snapshots within the same playback frame
            // (which is what the chat path hits) are identical.
            var prev = RenderTexture.active;
            Texture2D snap = null;
            try
            {
                RenderTexture.active = rt;
                snap = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false);
                snap.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
                snap.Apply();
                pngBytes = snap.EncodeToPNG();
                if (pngBytes != null && pngBytes.Length > 0)
                {
                    _cachedPng = pngBytes;
                    _cachedPngSourceTex = tex;
                    return true;
                }
                return false;
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning("PicMain.TryGetImageAsPng: RT readback threw: " + ex.Message);
                return false;
            }
            finally
            {
                RenderTexture.active = prev;
                if (snap != null) Destroy(snap);
            }
        }
        return false;
    }

    /// <summary>
    /// Draw a centered "play" badge (dark translucent disc + white right-pointing
    /// triangle) onto a decoded frame PNG so the "?" info panel can show a video input
    /// as visibly a CLIP, not a still. Returns null on any failure (caller falls back
    /// to the bare frame).
    /// </summary>
    private static byte[] MakeVideoThumbPng(byte[] framePng)
    {
        if (framePng == null || framePng.Length == 0) return null;
        Texture2D tex = null;
        try
        {
            tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!tex.LoadImage(framePng)) return null;
            int w = tex.width, h = tex.height;
            if (w < 8 || h < 8) return tex.EncodeToPNG();
            float cx = w * 0.5f, cy = h * 0.5f;
            float rad = Mathf.Min(w, h) * 0.16f;

            int discR = Mathf.CeilToInt(rad * 1.8f);
            for (int y = -discR; y <= discR; y++)
                for (int x = -discR; x <= discR; x++)
                {
                    if (x * x + y * y > discR * discR) continue;
                    int px = (int)cx + x, py = (int)cy + y;
                    if (px < 0 || py < 0 || px >= w || py >= h) continue;
                    Color bg = tex.GetPixel(px, py);
                    tex.SetPixel(px, py, Color.Lerp(bg, new Color(0f, 0f, 0f, 1f), 0.55f));
                }

            // White right-pointing triangle: vertical base on the left, apex on the right.
            float ax = cx - rad * 0.55f;            // left base x
            float bx = cx + rad;                    // apex x
            int yTop = Mathf.CeilToInt(cy + rad), yBot = Mathf.FloorToInt(cy - rad);
            for (int y = yBot; y <= yTop; y++)
            {
                float t = Mathf.Clamp01(Mathf.Abs(y - cy) / rad); // 0 center -> 1 tip
                float xRight = Mathf.Lerp(bx, ax, t);
                for (int x = (int)ax; x <= (int)xRight; x++)
                {
                    if (x < 0 || y < 0 || x >= w || y >= h) continue;
                    tex.SetPixel(x, y, Color.white);
                }
            }

            tex.Apply();
            return tex.EncodeToPNG();
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning("PicMain.MakeVideoThumbPng: " + ex.Message);
            return null;
        }
        finally
        {
            if (tex != null) UnityEngine.Object.Destroy(tex);
        }
    }

    public void OnGetPromptFromImageButton()
    {
        RunPresetByName("Image To Prompt (LLM).txt");
    }

    public void OnTool1Button()
    {
       
        OnTool1();

        if (GetJobsLeft() == 1)
        {
            SetStatusMessage("Waiting for GPU...");
        } else
        {
            //show a message that it's been scheduled on the screen
            RTQuickMessageManager.Get().ShowMessage("Running job script...");
        }
        
        // Also run on other selected pics (skip adventure texts)
        ApplyToOtherSelectedPics((pic) => pic.OnTool1());
    }
        public void OnSetTemp1Button()
        {
            RTQuickMessageManager.Get().ShowMessage("Copying to temp pic 1");
            DuplicateToExistingPic(GameLogic.Get().GetTempPic1());
        }
    public void OnSetTemp2Button()
    {
        RTQuickMessageManager.Get().ShowMessage("Copying to temp pic 2");
        DuplicateToExistingPic(GameLogic.Get().GetTempPic2());
    }
    public void OnSetTemp3Button()
    {
        RTQuickMessageManager.Get().ShowMessage("Copying to temp pic 3");
        DuplicateToExistingPic(GameLogic.Get().GetTempPic3());
    }
    public void OnTool2Button()
    {

        OnTool2();

        if (GetJobsLeft() == 1)
        {
            SetStatusMessage("Waiting for GPU...");
        }
        else
        {
            //show a message that it's been scheduled on the screen
            RTQuickMessageManager.Get().ShowMessage("Running job script...");
        }
        
        // Also run on other selected pics (skip adventure texts)
        ApplyToOtherSelectedPics((pic) => pic.OnTool2());
    }

    public void OnTool1() 
    {
       //if we have no history, let's fill in some defaults
      
        if (_jobHistory.Count() == 0)
        {
            PicJob jobDefaultInfoToStartWith = new PicJob();

            jobDefaultInfoToStartWith._requestedPrompt = GameLogic.Get().GetModifiedGlobalPrompt();
            jobDefaultInfoToStartWith._requestedNegativePrompt = GameLogic.Get().GetNegativePrompt();
            jobDefaultInfoToStartWith._requestedAudioPrompt = Config.Get().GetDefaultAudioPrompt();
            jobDefaultInfoToStartWith._requestedAudioNegativePrompt = Config.Get().GetDefaultAudioNegativePrompt();
            jobDefaultInfoToStartWith.requestedRenderer = GameLogic.Get().GetGlobalRenderer();
           
            AddJobListWithStartingJobInfo(jobDefaultInfoToStartWith, GameLogic.Get().GetPicJobListAsListOfStrings());
        }
        else
        {

            //Now, we *DO* have history, but we still want to overwrite things with the latest prompts, right?

            m_curEvent.m_picJob._requestedPrompt = GameLogic.Get().GetPrompt();
            m_curEvent.m_picJob._requestedNegativePrompt = GameLogic.Get().GetNegativePrompt();
            //m_curEvent.m_picJob._requestedAudioPrompt = Config.Get().GetDefaultAudioPrompt();
            //m_curEvent.m_picJob._requestedAudioNegativePrompt = Config.Get().GetDefaultAudioNegativePrompt();
            
            GameLogic.Get().AddEveryItemToJobList(ref m_jobList);
        }
    }

    public void OnTool2()
    {
        RunPresetByName(GameLogic.Get().GetNameOfActiveTempPreset());
    }
    
    /// <summary>
    /// Helper to apply an action to other selected pics when multiselect is active.
    /// Skips adventure texts and the current pic (which already had the action applied).
    /// </summary>
    private void ApplyToOtherSelectedPics(System.Action<PicMain> action)
    {
        var selectionManager = SelectionManager.Get();
        if (selectionManager == null || selectionManager.GetSelectedCount() <= 1)
            return;
            
        // Get all selected items and apply action to other PicMains (not this one, not adventure texts)
        var picsParent = RTUtil.FindObjectOrCreate("Pics").transform;
        var allPics = picsParent.GetComponentsInChildren<PicMain>();
        
        foreach (var pic in allPics)
        {
            if (pic == null || pic.IsDestroyed())
                continue;
                
            // Skip ourselves - we already ran the action
            if (pic == this)
                continue;
                
            // Only apply to pics that are selected
            if (!selectionManager.IsSelected(pic.gameObject))
                continue;
                
            // Run the action on this pic
            action(pic);
        }
    }

    public void OnFinishedJob()
    {
        m_waitingForPicJob = false;
        m_curEvent.m_picJob._timeOfEnd = Time.realtimeSinceStartup;
        SetNeedsToUpdateInfoPanelFlag();
    }

    public void OnFinishedRenderingWorkflow(bool bSuccess)
    {
        OnFinishedJob();
        //rewrite the last job in our history with this updated version, but bounds check everything to make sure it's safe
        if (_jobHistory.Count > 0)
        {
            _jobHistory[_jobHistory.Count - 1] = m_curEvent.m_picJob;
        } else
        {
            //Show error
            RTConsole.Log("Error:  No job history to update with the latest job");
        }

    }
    void OnUploadFinished(RTDB db)
    {
        OnFinishedJob();
        if (db.GetInt32("success") == 0)
        {
            RTConsole.Log("Error uploading file: " + db.GetString("error")+" canceling other jobs");

            SetStatusMessage("Failed to send\nfile to Server "+db.GetInt32("serverID"));
            ClearJobs();
        }
        else
        {
            RTConsole.Log("File " + db.GetString("name") + " uploaded successfully");
            SetStatusMessage(""); // Clear the "Uploading to ComfyUI..." message
            
            // Check if this was the last job
            if (!StillHasJobActivityToDo())
            {
                if (m_onFinishedScriptCallback != null)
                {
                    m_onFinishedScriptCallback.Invoke(gameObject);
                }
            }
            else
            {
                UpdateJobs();
            }
        }
    }

    public void SetJobWithInfoFromCur(ref PicJob job)
    {
        m_curEvent.m_picJob = job;
        /*
        job._requestedPrompt = m_curEvent.m_picJob._requestedPrompt;
        job._requestedNegativePrompt = m_curEvent.m_picJob._requestedNegativePrompt;
        job._requestedAudioPrompt = m_curEvent.m_picJob._requestedAudioPrompt;
        job._requestedAudioNegativePrompt = m_curEvent.m_picJob._requestedAudioNegativePrompt;
        */
    }

    public void ProcessJobIntoFinal(ref PicJob job)
    {
        string mediaLocalFilename = "";

        if (m_picMovie.IsMovie())
        {
            //the VHS movie loader requires the "temp/" part of the path
            mediaLocalFilename = m_picMovie.GetProcessingFileName();
            m_mediaRemoteFilename = m_picMovie.GetProcessingFileNameWithoutPath();
        }

        //Replace the <MOVIE_FILENAME> with the actual filename in job.m_string_parm1
        job._parm_1_string = job._parm_1_string.Replace("<MEDIA_FILENAME>", m_mediaRemoteFilename);
        job._parm_1_string = job._parm_1_string.Replace("<MEDIA_LOCAL_FILENAME>", mediaLocalFilename);
    }
  
    public void AddJobList(List<String> jobList)
    {

        foreach (var item in jobList)
        {
            string itemTrimmed = item.Trim();
            if (itemTrimmed.Length > 0 && itemTrimmed[0] != '-' && itemTrimmed[0] != '#' && itemTrimmed[0] != '@')
                m_jobList.Add(item);
        }
       
    }

    // Helper to parse prompt_N variable names and return the 0-based index, or -1 if not a match
    int TryParsePromptIndex(string varName)
    {
        // Support both "prompt_N" and "promptN" formats
        if (varName.StartsWith("prompt_") && int.TryParse(varName.Substring(7), out int idx1))
        {
            if (idx1 >= 1 && idx1 <= PicJob.MAX_EXTENDED_PROMPTS)
                return idx1 - 1; // Convert to 0-based
        }
        else if (varName.StartsWith("prompt") && varName.Length > 6 && int.TryParse(varName.Substring(6), out int idx2))
        {
            if (idx2 >= 1 && idx2 <= PicJob.MAX_EXTENDED_PROMPTS)
                return idx2 - 1; // Convert to 0-based
        }
        return -1;
    }

    // Static method that parses SET_PROMPTN tags from text and returns array of prompts
    // Supports formats: SET_PROMPT1:, SET_PROMPT_1:, set_prompt1:, etc. (case-insensitive)
    // Falls back: if no SET_PROMPT tags found, uses entire text as prompt[0]
    // Can be called by AIGuideManager and other systems that need to parse multi-prompt text
    public static string[] ParseSetPromptTags(string text)
    {
        string[] result = new string[PicJob.MAX_EXTENDED_PROMPTS];
        for (int i = 0; i < result.Length; i++)
            result[i] = "";

        if (string.IsNullOrEmpty(text))
            return result;

        string textLower = text.ToLower();
        bool foundAny = false;

        // Try to find SET_PROMPTN: or SET_PROMPT_N: patterns for each index
        for (int i = 1; i <= PicJob.MAX_EXTENDED_PROMPTS; i++)
        {
            // Try both formats: SET_PROMPT1: and SET_PROMPT_1:
            string[] patterns = new string[] { $"set_prompt{i}:", $"set_prompt_{i}:" };
            
            int startPos = -1;
            int patternLen = 0;
            
            foreach (string pattern in patterns)
            {
                int pos = textLower.IndexOf(pattern);
                if (pos >= 0)
                {
                    startPos = pos;
                    patternLen = pattern.Length;
                    break;
                }
            }

            if (startPos >= 0)
            {
                foundAny = true;
                
                // Find the start of the content (after the pattern)
                int contentStart = startPos + patternLen;
                
                // Find the end: either the next SET_PROMPT tag or end of string
                int contentEnd = text.Length;
                
                for (int j = 1; j <= PicJob.MAX_EXTENDED_PROMPTS; j++)
                {
                    if (j == i) continue; // Skip self
                    
                    string[] nextPatterns = new string[] { $"set_prompt{j}:", $"set_prompt_{j}:" };
                    foreach (string nextPattern in nextPatterns)
                    {
                        int nextPos = textLower.IndexOf(nextPattern, contentStart);
                        if (nextPos >= 0 && nextPos < contentEnd)
                        {
                            contentEnd = nextPos;
                        }
                    }
                }
                
                // Also stop at AUDIO: or IMAGE: tags if present (for AIGuide integration)
                string[] stopPatterns = new string[] { "audio:", "image:" };
                foreach (string stopPattern in stopPatterns)
                {
                    int stopPos = textLower.IndexOf(stopPattern, contentStart);
                    if (stopPos >= 0 && stopPos < contentEnd)
                    {
                        contentEnd = stopPos;
                    }
                }

                // Extract and trim the content
                result[i - 1] = text.Substring(contentStart, contentEnd - contentStart).Trim();
            }
        }

        // Fallback: if no SET_PROMPT tags found, use entire text as prompt[0]
        if (!foundAny)
        {
            result[0] = text.Trim();
        }

        return result;
    }

    // Parse SET_PROMPTN: patterns from LLM reply and populate _requestedPrompts array
    // Applies prepend/append prompts to each parsed prompt if they are set
    void ParseLLMPrompts(ref PicJob job)
    {
        string reply = job._requestedLLMReply;
        if (string.IsNullOrEmpty(reply))
        {
            RTConsole.Log("parse_llm_prompts: LLM reply is empty, nothing to parse");
            return;
        }

        string[] parsedPrompts = ParseSetPromptTags(reply);
        
        // Get prepend/append prompts
        string prependPrompt = GameLogic.Get().GetComfyPrependPrompt() ?? "";
        string appendPrompt = GameLogic.Get().GetComfyAppendPrompt() ?? "";
        bool applyPrependAppend = prependPrompt.Length > 0 || appendPrompt.Length > 0;
        
        if (applyPrependAppend)
        {
            RTConsole.Log($"parse_llm_prompts: Applying prepend='{prependPrompt}' append='{appendPrompt}'");
        }
        
        // Copy to job, apply prepend/append, and log results
        for (int i = 0; i < PicJob.MAX_EXTENDED_PROMPTS; i++)
        {
            string prompt = parsedPrompts[i];
            
            // Apply prepend/append to non-empty prompts
            if (!string.IsNullOrEmpty(prompt))
            {
                if (prependPrompt.Length > 0)
                    prompt = prependPrompt + " " + prompt;
                if (appendPrompt.Length > 0)
                    prompt = prompt + " " + appendPrompt;
                    
                string preview = prompt.Length > 50 ? prompt.Substring(0, 50) + "..." : prompt;
                RTConsole.Log($"parse_llm_prompts: Found prompt_{i + 1}: {preview}");
            }
            
            job._requestedPrompts[i] = prompt;
        }
        
        // Check if we used fallback
        bool foundTaggedPrompts = false;
        for (int i = 1; i < PicJob.MAX_EXTENDED_PROMPTS; i++)
        {
            if (!string.IsNullOrEmpty(parsedPrompts[i]))
            {
                foundTaggedPrompts = true;
                break;
            }
        }
        if (!foundTaggedPrompts && !string.IsNullOrEmpty(parsedPrompts[0]))
        {
            RTConsole.Log("parse_llm_prompts: No SET_PROMPT tags found, using entire reply as prompt_1");
        }
        
        // Backward compatibility: set main _requestedPrompt to first parsed prompt
        // so workflows using <AITOOLS_PROMPT> (instead of <AITOOLS_PROMPT_1>) will still work
        if (!string.IsNullOrEmpty(job._requestedPrompts[0]))
        {
            job._requestedPrompt = job._requestedPrompts[0];
        }
    }

    /// <summary>
    /// Creates a delegate that resolves built-in variables (prompt, llm_reply, temp_text1, etc.)
    /// This allows %varname% syntax to work for built-in variables in any text context.
    /// </summary>
    public BuiltInVariableResolver CreateBuiltInResolver(PicJob job)
    {
        return (string varName) => ResolveBuiltInVariable(job, varName);
    }

    /// <summary>
    /// Resolves a built-in variable name to its value.
    /// Returns null if the variable name is not a recognized built-in.
    /// </summary>
    public string ResolveBuiltInVariable(PicJob job, string varName)
    {
        if (string.IsNullOrEmpty(varName)) return null;
        
        string varLower = varName.ToLower().Trim();
        
        // Job-based variables
        if (job != null)
        {
            if (varLower == "prompt") return job._requestedPrompt ?? "";
            if (varLower == "audio_prompt") return job._requestedAudioPrompt ?? "";
            if (varLower == "negative_prompt") return job._requestedNegativePrompt ?? "";
            if (varLower == "audio_negative_prompt") return job._requestedAudioNegativePrompt ?? "";
            if (varLower == "segmentation_prompt") return job._requestedSegmentationPrompt ?? "";
            if (varLower == "llm_prompt") return job._requestedLLMPrompt ?? "";
            if (varLower == "llm_reply") return job._requestedLLMReply ?? "";
            
            // Support extended prompts: prompt_1 through prompt_8 (or prompt1 through prompt8)
            int promptIdx = TryParsePromptIndex(varLower);
            if (promptIdx >= 0)
            {
                return job._requestedPrompts[promptIdx] ?? "";
            }
        }
        
        // GameLogic-based variables
        if (varLower == "global_prompt") return GameLogic.Get()?.GetPrompt() ?? "";
        if (varLower == "prepend_prompt") return GameLogic.Get()?.GetComfyPrependPrompt() ?? "";
        if (varLower == "append_prompt") return GameLogic.Get()?.GetComfyAppendPrompt() ?? "";
        
        // PicMain-based variables (temp_text buffers)
        if (varLower == "temp_text1") return m_tempText1 ?? "";
        if (varLower == "temp_text2") return m_tempText2 ?? "";
        if (varLower == "temp_text3") return m_tempText3 ?? "";
        if (varLower == "temp_text4") return m_tempText4 ?? "";
        if (varLower == "requirements") return m_requirements ?? "";
        if (TryResolveVideoFpsBuiltIn(varLower, out string videoFpsValue)) return videoFpsValue;
        
        return null; // Not a built-in variable
    }

    private bool TryResolveVideoFpsBuiltIn(string varLower, out string value)
    {
        value = null;
        if (string.IsNullOrEmpty(varLower)) return false;

        int multiplier = 0;
        if (varLower == "video_fps" || varLower == "source_video_fps")
        {
            multiplier = 1;
        }
        else if (varLower == "video_fps_2x" || varLower == "source_video_fps_2x")
        {
            multiplier = 2;
        }
        else if (varLower == "video_fps_3x" || varLower == "source_video_fps_3x")
        {
            multiplier = 3;
        }
        else if (varLower == "video_fps_4x" || varLower == "source_video_fps_4x")
        {
            multiplier = 4;
        }
        else if (varLower == "rife_output_fps")
        {
            multiplier = 2;
        }

        if (multiplier <= 0)
            return false;

        double sourceFps = ProbeWorkflowVideoSourceFps();
        if (sourceFps <= 0 || double.IsNaN(sourceFps) || double.IsInfinity(sourceFps))
            sourceFps = FfmpegTool.DefaultFps;

        double outFps = Math.Max(1.0, Math.Min(240.0, sourceFps * multiplier));
        value = outFps.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
        return true;
    }

    private double ProbeWorkflowVideoSourceFps()
    {
        string videoPath = !string.IsNullOrEmpty(m_pendingVideoUploadPath)
            ? m_pendingVideoUploadPath
            : ((m_picMovie != null && IsMovie()) ? m_picMovie.GetProcessingFileName() : null);

        if (string.IsNullOrWhiteSpace(videoPath) || !File.Exists(videoPath))
            return 0;

        if (FfmpegTool.TryProbeVideoSync(videoPath, out var info, out string error)
            && info != null
            && info.Fps > 0)
        {
            return info.Fps;
        }

        if (!string.IsNullOrEmpty(error))
            RTConsole.Log("Warning: could not probe source video fps for workflow variable: " + error);
        return 0;
    }

    string ConvertVarToText(ref PicJob job, string source)
    {
        string sourceOriginal = source;

        // First try to resolve as a built-in variable
        string builtInValue = ResolveBuiltInVariable(job, source);
        if (builtInValue != null)
        {
            return builtInValue;
        }

        // Check custom variable manager if no built-in variable was found
        // Try to resolve as a %variable% from local or global manager
        string varName = VariableManager.StripDelimiters(sourceOriginal);
        VariableManager globalVM = GameLogic.Get()?.GetGlobalVariableManager();
        
        string resolved = VariableManager.ResolveVariable(varName, m_variableManager, globalVM);
        if (resolved != null)
        {
            return resolved;
        }

        // Not a variable, return as literal text
        return sourceOriginal;
    }

    void DoVarCopy(ref PicJob job, string source, string dest)
    {
        string temp = ConvertVarToText(ref job, source);

        // Handle image-to-image copies between image1, temp1, temp2, temp3
        if (IsImageSlot(source) && IsImageSlot(dest))
        {
            PicMain sourcePic = GetPicMainForSlot(source);
            PicMain destPic = GetPicMainForSlot(dest);

            if (sourcePic == null)
            {
                RTQuickMessageManager.Get().BroadcastMessage("Error: Unknown source image: " + source);
                return;
            }
            if (destPic == null)
            {
                RTQuickMessageManager.Get().BroadcastMessage("Error: Unknown destination image: " + dest);
                return;
            }

            Texture2D sourceTexture = sourcePic.m_pic.sprite?.texture;
            if (sourceTexture == null)
            {
                RTQuickMessageManager.Get().BroadcastMessage("Error: Source image to copy from is invalid");
                return;
            }

            destPic.SetImage(sourceTexture, true);
            return;
        }

        // Handle text variable copies
        if (dest == "prompt")
        {
            job._requestedPrompt = temp;
            // Also sync to _requestedPrompts[0] for consistency with multi-prompt workflows
            job._requestedPrompts[0] = temp;
        }
        if (dest == "global_prompt") GameLogic.Get().SetPrompt(temp);
        if (dest == "audio_prompt") job._requestedAudioPrompt = temp;
        if (dest == "negative_prompt") job._requestedNegativePrompt = temp;
        if (dest == "audio_negative_prompt") job._requestedAudioNegativePrompt = temp;
        if (dest == "segmentation_prompt") job._requestedSegmentationPrompt = temp;
        if (dest == "llm_prompt") job._requestedLLMPrompt = temp;
        if (dest == "llm_reply") job._requestedLLMReply = temp;
        if (dest == "requirements") m_requirements = temp;
        if (dest == "prepend_prompt") GameLogic.Get().SetComfyPrependPrompt(temp);
        if (dest == "append_prompt") GameLogic.Get().SetComfyAppendPrompt(temp);
        if (dest == "temp_text1") m_tempText1 = temp;
        if (dest == "temp_text2") m_tempText2 = temp;
        if (dest == "temp_text3") m_tempText3 = temp;
        if (dest == "temp_text4") m_tempText4 = temp;
        
        // Support extended prompts: prompt_1 through prompt_8 (or prompt1 through prompt8)
        int promptIdx = TryParsePromptIndex(dest);
        if (promptIdx >= 0)
        {
            job._requestedPrompts[promptIdx] = temp;
        }

        // Support custom %variable% destinations
        if (VariableManager.IsVariableReference(dest))
        {
            VariableManager targetVM = VariableManager.GetManagerForVariable(dest, m_variableManager, GameLogic.Get()?.GetGlobalVariableManager());
            if (targetVM != null)
            {
                targetVM.SetText(dest, temp);
            }
        }
    }

    bool IsImageSlot(string slot)
    {
        return slot == "image" || slot == "image1" || slot == "temp1" || slot == "temp2" || slot == "temp3";
    }

    PicMain GetPicMainForSlot(string slot)
    {
        if (slot == "image" || slot == "image1") return this;

        GameObject go = null;
        if (slot == "temp1") go = GameLogic.Get().GetTempPic1();
        else if (slot == "temp2") go = GameLogic.Get().GetTempPic2();
        else if (slot == "temp3") go = GameLogic.Get().GetTempPic3();

        return go?.GetComponent<PicMain>();
    }
    
    /// <summary>
    /// Get a Texture2D from a source slot name (image, image1, temp1, temp2, temp3).
    /// Used for vision LLM image input.
    /// </summary>
    Texture2D GetTextureFromSource(string source)
    {
        PicMain picMain = GetPicMainForSlot(source);
        if (picMain != null && picMain.m_pic.sprite != null && picMain.m_pic.sprite.texture != null)
        {
            return picMain.m_pic.sprite.texture;
        }
        return null;
    }

    void DoVarAdd(ref PicJob job, string source, string dest)
    {
        string temp = ConvertVarToText(ref job, source);

        if (dest == "prompt")
        {
            job._requestedPrompt += temp;
            // Also sync to _requestedPrompts[0] for consistency with multi-prompt workflows
            job._requestedPrompts[0] += temp;
        }
        if (dest == "global_prompt") GameLogic.Get().SetPrompt(GameLogic.Get().GetPrompt()+ temp);
        if (dest == "audio_prompt") job._requestedAudioPrompt += temp;
        if (dest == "negative_prompt") job._requestedNegativePrompt += temp;
        if (dest == "prepend_prompt") GameLogic.Get().SetComfyPrependPrompt(GameLogic.Get().GetComfyPrependPrompt() + temp);
        if (dest == "append_prompt") GameLogic.Get().SetComfyAppendPrompt(GameLogic.Get().GetComfyAppendPrompt() + temp);
        if (dest == "audio_negative_prompt") job._requestedAudioNegativePrompt += temp;
        if (dest == "segmentation_prompt") job._requestedSegmentationPrompt += temp;
        if (dest == "llm_prompt") job._requestedLLMPrompt += temp;
        if (dest == "llm_reply") job._requestedLLMReply += temp;
        if (dest == "requirements") m_requirements += temp;
        if (dest == "temp_text1") m_tempText1 += temp;
        if (dest == "temp_text2") m_tempText2 += temp;
        if (dest == "temp_text3") m_tempText3 += temp;
        if (dest == "temp_text4") m_tempText4 += temp;
        
        // Support extended prompts: prompt_1 through prompt_8 (or prompt1 through prompt8)
        int promptIdx = TryParsePromptIndex(dest);
        if (promptIdx >= 0)
        {
            job._requestedPrompts[promptIdx] += temp;
        }
    }

    public void SetNoUndo(bool bNoUndo)
    {
        m_noUndo = bNoUndo;
    }

    public bool GetNoUndo()
    {
        return m_noUndo;
    }


    public bool IsWaitingForGPU()
    {
        if (m_waitingForPicJob || IsBusyBasic()) return false;
        if (!StillHasJobActivityToDo()) return false;

        return true;
    }

    private bool ProcessResizeIfLargerOrForcedCommand(string[] commandParts, bool bForceResize)
    {
        // Expected formats:
        // Old: resize_if_larger|x|<newWidth>|y|<newHeight>|aspect_correct|<1 or 0>
        // New: resize_if_larger|<slot>|x|<newWidth>|y|<newHeight>|aspect_correct|<1 or 0>
        // Where <slot> is: image, image1, temp1, temp2, or temp3
        
        // Detect if first parameter is a slot name (new format) or "x" (old format)
        int paramOffset = 0;
        PicMain targetPic = this;
        string firstParam = commandParts.Length > 1 ? commandParts[1].Trim().ToLower() : "";
        
        if (IsImageSlot(firstParam))
        {
            // New format with slot specified
            paramOffset = 1;
            targetPic = GetPicMainForSlot(firstParam);
            if (targetPic == null)
            {
                RTConsole.Log($"Error: Unknown image slot '{firstParam}' in resize command.");
                RTQuickMessageManager.Get().ShowMessage($"Error: Unknown image slot '{firstParam}'");
                return false;
            }
            RTConsole.Log($"resize: targeting slot '{firstParam}'");
        }
        
        int requiredParams = 7 + paramOffset;
        if (commandParts.Length < requiredParams)
        {
            RTConsole.Log("Error: 'resize_if_larger' command missing parameters.");
            RTQuickMessageManager.Get().ShowMessage("Error: 'resize_if_larger' command missing parameters.");
            return false;
        }

        if (!commandParts[1 + paramOffset].Trim().Equals("x", StringComparison.OrdinalIgnoreCase) ||
            !commandParts[3 + paramOffset].Trim().Equals("y", StringComparison.OrdinalIgnoreCase) ||
            !commandParts[5 + paramOffset].Trim().Equals("aspect_correct", StringComparison.OrdinalIgnoreCase))
        {
            RTConsole.Log("Error: 'resize_if_larger' command parameters malformed.");
            RTQuickMessageManager.Get().ShowMessage("Error: 'resize_if_larger' command parameters malformed.");
            return false;
        }

        if (!int.TryParse(commandParts[2 + paramOffset], out int newWidth))
        {
            RTConsole.Log("Error: invalid width in 'resize_if_larger' command.");
            RTQuickMessageManager.Get().ShowMessage("Error: invalid width in 'resize_if_larger' command.");
            return false;
        }

        if (!int.TryParse(commandParts[4 + paramOffset], out int newHeight))
        {
            RTConsole.Log("Error: invalid height in 'resize_if_larger' command.");
            RTQuickMessageManager.Get().ShowMessage("Error: invalid height in 'resize_if_larger' command.");
            return false;
        }

        // Determine whether to keep aspect ratio.
        bool bKeepAspect = false;
        string aspectValue = commandParts[6 + paramOffset].Trim();
        if (aspectValue.Equals("1") || aspectValue.Equals("true", StringComparison.OrdinalIgnoreCase))
        {
            bKeepAspect = true;
        }

        // Get the target texture.
        Texture2D currentTexture = targetPic.m_pic.sprite?.texture;
        if (currentTexture != null && (bForceResize || currentTexture.width > newWidth || currentTexture.height > newHeight))
        {
            targetPic.Resize(newWidth, newHeight, bKeepAspect, FilterMode.Bilinear);
            RTConsole.Log($"resize: Resized {(paramOffset > 0 ? firstParam : "image")} to {newWidth}x{newHeight} (aspect: {bKeepAspect})");
        }
        else
        {
            RTConsole.Log($"resize: {(paramOffset > 0 ? firstParam : "image")} already within {newWidth}x{newHeight}");
        }

        return true;
    }


    void OnTexGenCompletedCallback(RTDB db, JSONObject jsonNode, string streamedText)
    {
        SetLLMActive(false);
        m_waitingForPicJob = false;

        if (jsonNode == null && streamedText.Length == 0)
        {
            //must have been an error
            string error = db.GetStringWithDefault("msg", "Unknown");


            //check to see if "429" is inside the string error
            if (error.Contains("429"))
            {
                RTConsole.Log("LLM reports too many requests, waiting 5 seconds to try again: " + error);
                // SetTryAgainWait(5);
            }
            else
            {
                RTConsole.Log("Error talking to the LLM: " + error);
                RTQuickMessageManager.Get().ShowMessage(error);
                GameLogic.Get().ShowConsole(true);
                //SetAuto(false); //don't let it continue doing crap
            }
            
            // Show error on the pic and clean up properly
            SetStatusMessage("LLM Error");
            ClearJobs();
            
            // Invoke the script callback so any waiting code gets notified of the failure
            if (m_onFinishedScriptCallback != null)
            {
                m_onFinishedScriptCallback.Invoke(gameObject);
            }
            return;
        }

        if (jsonNode != null)
        {
            RTConsole.LogError("Error, we only support streaming text now");
            return;

        }
        
        // Log if thinking content is detected in the response
        bool hasThinkingContent = streamedText.Contains("<think>") && streamedText.Contains("</think>");
        if (hasThinkingContent)
        {
            RTConsole.Log("LLM response contains thinking tags (<think>...</think>)");
        }
        
        // Respect the "Strip <think> tags" toggle setting (same as AdventureText)
        if (GenerateSettingsPanel.GetStripThinkTags())
        {
            if (hasThinkingContent)
            {
                RTConsole.Log("Stripping thinking tags from response (toggle is ON)");
            }
            streamedText = OpenAITextCompletionManager.RemoveThinkTagsFromString(streamedText);
        }

        m_curEvent.m_picJob._requestedLLMReply = streamedText.Trim();
        m_lastLLMReply = m_curEvent.m_picJob._requestedLLMReply;
        
        // Check if all jobs are now complete
        if (!StillHasJobActivityToDo())
        {
            SetNeedsToUpdateInfoPanelFlag();
            SetStatusMessage("LLM reply received");
            
            if (m_onFinishedScriptCallback != null)
            {
                m_onFinishedScriptCallback.Invoke(gameObject);
            }
        }

    }

    public void SetLLMActive(bool bActive, int instanceID = -1, int replicaIndex = 0)
    {
        if (bActive == _llmIsActive && (bActive || _activeLLMInstanceID == instanceID)) return;
        _llmIsActive = bActive;
        
        // Track LLM instance busy state
        var instanceMgr = LLMInstanceManager.Get();
        if (instanceMgr != null)
        {
            if (bActive && instanceID >= 0)
            {
                _activeLLMInstanceID = instanceID;
                _activeLLMReplicaIndex = replicaIndex;
                instanceMgr.SetLLMBusy(instanceID, replicaIndex, true);
            }
            else if (!bActive && _activeLLMInstanceID >= 0)
            {
                instanceMgr.SetLLMBusy(_activeLLMInstanceID, _activeLLMReplicaIndex, false);
                _activeLLMInstanceID = -1;
                _activeLLMReplicaIndex = 0;
            }
        }
        
        // Track pending server LLM work (for server-specific LLM-first presets)
        if (!bActive && _pendingServerID >= 0)
        {
            Config.Get().DecrementPendingLLM(_pendingServerID);
            _pendingServerID = -1;
        }
        
        // Track LLM requests in Adventure mode to honor the "LLMs at once" limit
        if (AdventureLogic.Get().IsActive())
        {
            AdventureLogic.Get().ModLLMRequestCount(bActive ? 1 : -1);
        }
    }



    public void OnStreamingTextCallback(string text)
    {
        if (text == null || text.Length == 0) return;

        _streamedTextBuffer.Append(text);

        if (Time.time - _streamLastUpdateTime < STREAM_UPDATE_INTERVAL) return;
        _streamLastUpdateTime = Time.time;

        m_text.text = BuildStreamingDisplayText(_streamedTextBuffer.ToString());
    }

    private string BuildStreamingDisplayText(string fullText)
    {
        int thinkOpen = fullText.IndexOf("<think>");
        if (thinkOpen >= 0)
        {
            int thinkContentStart = thinkOpen + 7;
            int thinkClose = fullText.IndexOf("</think>", thinkContentStart);
            string preThink = thinkOpen > 0 ? fullText.Substring(0, thinkOpen) : "";

            if (thinkClose < 0)
            {
                string thinkContent = fullText.Substring(thinkContentStart);
                int len = thinkContent.Length;
                string tail = len > STREAM_THINKING_TAIL_CHARS
                    ? "..." + thinkContent.Substring(len - STREAM_THINKING_TAIL_CHARS)
                    : thinkContent;
                return TruncateHead(preThink) + $"<i>[Thinking... {len:N0} chars]\n{tail}</i>";
            }
            else
            {
                string thinkContent = fullText.Substring(thinkContentStart, thinkClose - thinkContentStart);
                string postThink = fullText.Substring(thinkClose + 8);
                int len = thinkContent.Length;
                string tail = len > STREAM_THINKING_TAIL_CHARS
                    ? "..." + thinkContent.Substring(len - STREAM_THINKING_TAIL_CHARS)
                    : thinkContent;
                return TruncateHead(preThink)
                    + $"<i>[Thinking complete - {len:N0} chars]\n{tail}</i>\n"
                    + TruncateTail(postThink);
            }
        }

        if (fullText.Length > STREAM_DISPLAY_TAIL_CHARS)
            return $"[{fullText.Length:N0} chars]\n..." + fullText.Substring(fullText.Length - STREAM_DISPLAY_TAIL_CHARS);

        return fullText;
    }

    private string TruncateHead(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        int cap = STREAM_DISPLAY_TAIL_CHARS / 2;
        if (text.Length <= cap) return text;
        return "..." + text.Substring(text.Length - cap) + "\n";
    }

    private string TruncateTail(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        if (text.Length <= STREAM_DISPLAY_TAIL_CHARS) return text;
        return text.Substring(text.Length - STREAM_DISPLAY_TAIL_CHARS) + "...";
    }

    public void UpdateJobs()
    {
        if (m_waitingForPicJob || IsBusyBasic()) return;

        //now is a good time to start  job I guess

        if (!StillHasJobActivityToDo())
        {
            // Release manual GPU lock if we had one (AutoPic handles its own release via callback)
            if (m_ownedServerID >= 0 && string.IsNullOrEmpty(m_autoPicScriptName))
            {
                ReleaseServerOwnership();
            }
            return;
        }

        // Local (non-GPU) op short-circuit: if the next queued job line is a synthetic
        // local_op marker, run its coroutine instead of going through the workflow /
        // GPU-allocation path. Used by AI Chat composition skills (draw_text, etc.)
        // so chain="true" can mix local image ops in with normal generate / edit
        // workflows on the same Pic.
        if (m_picJobs.Count == 0 && m_jobList.Count > 0 && m_jobList[0] != null && m_jobList[0].StartsWith("local_op|"))
        {
            string line = m_jobList[0];
            m_jobList.RemoveAt(0);
            string key = line.Length > 9 ? line.Substring(9) : "";
            Func<PicMain, IEnumerator> op;
            if (!m_pendingLocalOps.TryGetValue(key, out op) || op == null)
            {
                Debug.LogWarning("PicMain.UpdateJobs: local_op key '" + key + "' had no registered coroutine.");
                UpdateJobs();
                return;
            }
            m_pendingLocalOps.Remove(key);
            m_waitingForPicJob = true;
            // RunLocalOpCoroutine itself is exception-tolerant; if it throws BEFORE its
            // first yield (e.g. font lookup, TMP setup), unset the wait flag so the
            // queue isn't permanently stuck and let UpdateJobs continue.
            try
            {
                StartCoroutine(RunLocalOpCoroutine(op));
            }
            catch (Exception ex)
            {
                Debug.LogError("PicMain.UpdateJobs: local_op coroutine launch threw: " + ex);
                m_waitingForPicJob = false;
                UpdateJobs();
            }
            return;
        }

        // Determine which renderer type is needed for the pending job
        RTRendererType neededRenderer = GameLogic.Get().GetGlobalRenderer();
        if (m_jobDefaultInfo != null)
        {
            neededRenderer = m_jobDefaultInfo.requestedRenderer;
        }
        else if (m_picJobs.Count > 0)
        {
            neededRenderer = m_picJobs[0].requestedRenderer;
        }

        int serverID = -1;
        
        // If we own a server (from AutoPic override), use it exclusively
        if (m_ownedServerID >= 0)
        {
            GPUInfo ownedServer = Config.Get().GetGPUInfo(m_ownedServerID);
            // For owned servers, only check the actual GPU busy flag (ownedServer.IsGPUBusy),
            // NOT Config.IsGPUBusy() which also includes pendingLLMCount.
            // Our own LLM work may have incremented pendingLLMCount, but that shouldn't block us.
            if (ownedServer != null && ownedServer._bIsActive && !ownedServer.IsGPUBusy)
            {
                serverID = m_ownedServerID;
            }
            // If owned server's GPU is actually busy, wait for it (don't use a different server)
        }
        else if (m_requestedServerID >= 0)
        {
            // Per-server adventure render: only use the requested server, wait if busy
            GPUInfo reqServer = Config.Get().GetGPUInfo(m_requestedServerID);
            if (reqServer != null && reqServer._bIsActive && !reqServer.IsGPUBusy
                && !PicMain.IsServerOwnedByAnyPic(m_requestedServerID))
            {
                serverID = m_requestedServerID;
            }
            else if (m_requestedServerIsPreference)
            {
                // Soft GPU hint (e.g. AI Chat's gpu="N"): the preferred server is busy or
                // unavailable, so fall back to ANY free GPU rather than waiting forever for
                // that one. This matches the AI Chat prompt's documented promise ("if you
                // specify a gpu and it's busy, the scheduler will fall back automatically")
                // and prevents the multi-movie deadlock where the LLM pins several pics to
                // the same/busy GPUs while other GPUs sit idle.
                serverID = Config.Get().GetFreeGPU(neededRenderer, false, m_skipIgnoredServers, m_gpuNameMatchFilter);
            }
            // else: hard pin (Adventure per-server render) - leave serverID == -1 and wait.
        }
        else
        {
            // Normal path: find any free GPU (optionally restricted by per-spawn name filter)
            serverID = Config.Get().GetFreeGPU(neededRenderer, false, m_skipIgnoredServers, m_gpuNameMatchFilter);
        }

        if (serverID == -1 && m_requirements == "gpu")
        {
            SetStatusMessage("Waiting for GPU...");
            //nothing available yet
            return;
        } else
        {
            //I guess we don't need a serverID for what we're doing, perhaps it's an LLM or resizing job
        }

        GPUInfo serverInfo = null;
        
        if (serverID >= 0) serverInfo = Config.Get().GetGPUInfo(serverID);

        // Check if the next job is an LLM call and if we can initiate it
        if (m_picJobs.Count > 0 && m_picJobs[0]._job == "call_llm")
        {
            // Check LLM instance capacity for small jobs (autopics)
            // Note: We don't check AreLLMsPaused() here - that only pauses big jobs (Adventure text)
            // Autopics (small jobs) can continue processing even when paused
            var instanceMgr = LLMInstanceManager.Get();
            if (instanceMgr != null && instanceMgr.GetInstanceCount() > 0)
            {
                // Check if this is a vision job (has images pending or attached to any interaction)
                bool isVisionJob = _promptManager.HasAnyImages();
                
                if (!instanceMgr.IsAnyLLMFree(isSmallJob: true, isVisionJob: isVisionJob))
                {
                    if (isVisionJob)
                    {
                        SetStatusMessage("Waiting for Vision LLM...");
                    }
                    else
                    {
                        SetStatusMessage("Waiting for LLM slot...");
                    }
                    return;
                }
            }
        }

        if (m_picJobs.Count > 0)
        {
            m_waitingForPicJob = true;
            PicJob job = m_picJobs[0];
            m_picJobs.RemoveAt(0);
            job._serverID = serverID;
            job._timeOfStart = Time.realtimeSinceStartup;

            ProcessJobIntoFinal(ref job);

            if (job._job == "run_workflow" || job._job == "run_dalle" || job._job == "run_openai_image")
            {
                SetStatusMessage("Waiting for GPU to\nrun workflow...");

                //we'll schedule it as a GPU event
                var e = new ScheduledGPUEvent();
                e.mode = "render";
                e.m_picJob = job;
                e.workflow = job._workflow;
                e.targetObj = gameObject;

                //trim the whitespace from the strings above
                e.requestedRenderer = job.requestedRenderer;
                PassInTempInfoPicJob(job);

                // Move any input PNGs captured by upload_to_comfy onto this job before it
                // becomes the history record so the info panel can show what was sent.
                // We hand them to the live job too (so PassInTempInfoPicJob's reference,
                // which is the same object, has them as well).
                for (int __ii = 0; __ii < m_pendingInputImagePngs.Length; __ii++)
                {
                    job._inputImagePngs[__ii] = m_pendingInputImagePngs[__ii];
                    m_pendingInputImagePngs[__ii] = null;
                }

                _jobHistory.Add(job.Clone());
                SetNeedsToUpdateInfoPanelFlag();

                if (!ImageGenerator.Get().RunGPUEventNow(e, serverID, false))
                {
                    ImageGenerator.Get().ScheduleGPURequest(e);
                }

            }
            else  if (job._job == "upload_to_comfy")
            {
                //add a file to the ComfyUI server
                // New format: "source|inputIndex|filename" where source is image1, temp1, temp2, etc.
                ComfyUIFileUploader uploaderScript = ComfyUIFileUploader.CreateObject();
                SetStatusMessage("Uploading to\nComfyUI...");
                
                // Parse the parm_1_string format: "source|inputIndex|filename"
                string[] uploadParts = job._parm_1_string.Split('|');
                if (uploadParts.Length >= 3)
                {
                    string source = uploadParts[0].ToLower().Trim();
                    // inputIndex is uploadParts[1] - we don't need it here, it was already used to set _inputFilenames
                    string remoteFileName = uploadParts[2].Trim();
                    
                    // Get the texture based on source
                    Texture2D sourceTexture = null;
                    Texture2D sourceMask = null;
                    
                    if (source == "image1" || source == "image")
                    {
                        if (m_pic.sprite != null && m_pic.sprite.texture != null)
                        {
                            sourceTexture = m_pic.sprite.texture;
                            if (m_mask.sprite != null && m_mask.sprite.texture != null)
                            {
                                sourceMask = m_mask.sprite.texture;
                            }
                        }
                    }
                    else if (source == "temp1")
                    {
                        GameObject temp1GO = GameLogic.Get().GetTempPic1();
                        if (temp1GO != null)
                        {
                            PicMain temp1Script = temp1GO.GetComponent<PicMain>();
                            if (temp1Script != null && temp1Script.m_pic.sprite != null && temp1Script.m_pic.sprite.texture != null)
                            {
                                sourceTexture = temp1Script.m_pic.sprite.texture;
                                if (temp1Script.m_mask.sprite != null && temp1Script.m_mask.sprite.texture != null)
                                {
                                    sourceMask = temp1Script.m_mask.sprite.texture;
                                }
                            }
                        }
                    }
                    else if (source == "temp2")
                    {
                        GameObject temp2GO = GameLogic.Get().GetTempPic2();
                        if (temp2GO != null)
                        {
                            PicMain temp2Script = temp2GO.GetComponent<PicMain>();
                            if (temp2Script != null && temp2Script.m_pic.sprite != null && temp2Script.m_pic.sprite.texture != null)
                            {
                                sourceTexture = temp2Script.m_pic.sprite.texture;
                                if (temp2Script.m_mask.sprite != null && temp2Script.m_mask.sprite.texture != null)
                                {
                                    sourceMask = temp2Script.m_mask.sprite.texture;
                                }
                            }
                        }
                    }  
                    else if (source == "temp3")
                    {
                        GameObject temp3GO = GameLogic.Get().GetTempPic3();
                        if (temp3GO != null)
                        {
                            PicMain temp3Script = temp3GO.GetComponent<PicMain>();
                            if (temp3Script != null && temp3Script.m_pic.sprite != null && temp3Script.m_pic.sprite.texture != null)
                            {
                                sourceTexture = temp3Script.m_pic.sprite.texture;
                                if (temp3Script.m_mask.sprite != null && temp3Script.m_mask.sprite.texture != null)
                                {
                                    sourceMask = temp3Script.m_mask.sprite.texture;
                                }
                            }
                        }
                    }
                    else if (source == "video" || source == "video1")
                    {
                        // Video source - upload from file path. Prefer an explicitly-supplied
                        // source clip (m_pendingVideoUploadPath, set by chat video_to_video so the
                        // Pic can stay an image and transition cleanly to the result), else the
                        // Pic's own loaded movie. Upload under remoteFileName (the parse-stage
                        // placeholder name, e.g. pic_<guid>.mp4) so the file we send matches the
                        // <AITOOLS_INPUT_N> token already baked into the workflow - using the
                        // source's own name would desync them and the loader would point at a
                        // file that was never uploaded.
                        string videoPath = !string.IsNullOrEmpty(m_pendingVideoUploadPath)
                            ? m_pendingVideoUploadPath
                            : (IsMovie() ? m_picMovie.GetProcessingFileName() : null);
                        if (!string.IsNullOrEmpty(videoPath))
                        {
                            // Stash a poster frame (with a play badge) for the "?" info panel so the
                            // user can see a VIDEO was fed into this slot - and, on multi-input
                            // presets, a video alongside any still inputs in the same row.
                            if (int.TryParse(uploadParts[1], out int vIdx)
                                && vIdx >= 0 && vIdx < m_pendingInputImagePngs.Length
                                && TryGetImageAsPng(out byte[] framePng) && framePng != null)
                            {
                                m_pendingInputImagePngs[vIdx] = MakeVideoThumbPng(framePng) ?? framePng;
                            }
                            uploaderScript.UploadFile(serverID, videoPath, remoteFileName, OnUploadFinished);
                            return; // Video upload handled, exit early
                        }
                        else
                        {
                            ClearErrorsAndJobs();
                            SetStatusMessage("Need video\nloaded first!");
                            RTConsole.Log("Error: No video loaded for video upload");
                            ReportWorkflowAbortOnce(
                                "Workflow aborted: the preset expected a loaded video, but the Pic had none. " +
                                "If you wanted to operate on an existing chat movie, reference it via chat_image=\"N\" pointing at the Movie #N bubble.");
                            return;
                        }
                    }
                    else if (source == "image2")
                    {
                        if (m_image2 != null)
                        {
                            sourceTexture = m_image2;
                        }
                    }
                    else if (source == "image3")
                    {
                        if (m_image3 != null)
                        {
                            sourceTexture = m_image3;
                        }
                    }
                    else if (source == "image4")
                    {
                        if (m_image4 != null)
                        {
                            sourceTexture = m_image4;
                        }
                    }
                    else if (source == "image5")
                    {
                        if (m_image5 != null)
                        {
                            sourceTexture = m_image5;
                        }
                    }

                    if (sourceTexture == null)
                    {
                        ClearErrorsAndJobs();
                        SetStatusMessage("Need " + source + "\nimage first!");
                        RTConsole.Log("Error: Source '" + source + "' has no valid texture for upload");
                        // Map "imageN" to the chat_imageN / attachmentN slot suffix the
                        // LLM uses (image1 / image == primary slot, suffix-less; image2..5
                        // use the explicit number). Anything else falls back to no suffix
                        // so we don't crash on an unexpected source token.
                        string slotSuffix = "";
                        if (source != null && source.StartsWith("image") && source.Length > "image".Length)
                        {
                            string tail = source.Substring("image".Length);
                            if (tail != "1") slotSuffix = tail;
                        }
                        ReportWorkflowAbortOnce(
                            $"Workflow aborted: the preset's @upload step needed source '{source}' but no image is wired into that slot. " +
                            $"If you used a multi-input preset, pass one chat_image{slotSuffix}=\"N\" (or attachment{slotSuffix}=\"N\") per required input, OR pick a smaller N-Input preset that matches the number of references you have.");
                        return;
                    }
                    
                    byte[] pngBytes;
                    if (sourceMask != null)
                    {
                        Texture2D finalTexture = Tex2DExtension.CombineTexturesForAlpha(sourceTexture, sourceMask);
                        pngBytes = finalTexture.EncodeToPNG();
                    }
                    else
                    {
                        pngBytes = sourceTexture.EncodeToPNG();
                    }

                    // Stash a copy for the "?" info panel so the user can see which images
                    // fed this N-input job. The bytes get transferred onto the run_workflow
                    // PicJob when it's cloned into _jobHistory below.
                    int capturedIdx;
                    if (int.TryParse(uploadParts[1], out capturedIdx) && capturedIdx >= 0 && capturedIdx < m_pendingInputImagePngs.Length)
                    {
                        m_pendingInputImagePngs[capturedIdx] = pngBytes;
                    }

                    uploaderScript.UploadFileInMemory(serverID, pngBytes, remoteFileName, OnUploadFinished);
                }
                else
                {
                    // Legacy fallback or error
                    RTConsole.Log("Error: Invalid upload_to_comfy format. Expected 'source|inputIndex|filename', got: " + job._parm_1_string);
                    ClearErrorsAndJobs();
                    SetStatusMessage("Upload format\nerror!");
                    ReportWorkflowAbortOnce(
                        "Workflow aborted: an @upload step in the preset has a malformed format. Expected 'source|inputIndex|filename'. " +
                        "This is a preset-file bug, not a usage error - tell the user to check the preset's @upload directives.");
                }
            } else  if (job._job == "call_llm")
            {
                // Ensure LLM managers exist (add components dynamically if not assigned via prefab)
                if (_openAITextCompletionManager == null)
                    _openAITextCompletionManager = gameObject.GetComponent<OpenAITextCompletionManager>() ?? gameObject.AddComponent<OpenAITextCompletionManager>();
                if (_anthropicAITextCompletionManager == null)
                    _anthropicAITextCompletionManager = gameObject.GetComponent<AnthropicAITextCompletionManager>() ?? gameObject.AddComponent<AnthropicAITextCompletionManager>();
                if (_texGenWebUICompletionManager == null)
                    _texGenWebUICompletionManager = gameObject.GetComponent<TexGenWebUITextCompletionManager>() ?? gameObject.AddComponent<TexGenWebUITextCompletionManager>();
     
                RTDB db = new RTDB();
                _streamedTextBuffer.Clear();
                _streamLastUpdateTime = 0;
                SetStatusMessage("Running LLM...");
                var mgr = LLMSettingsManager.Get();
                
                // Detect if this is a vision job (has images pending or attached to any interaction)
                bool isVisionJob = _promptManager.HasAnyImages();
                if (isVisionJob)
                {
                    RTConsole.Log("Vision job detected: prompt contains image(s)");
                }
                
                // Try to use multi-instance system first (isSmallJob=true for autopic LLM calls)
                var instanceMgr = LLMInstanceManager.Get();
                int llmReplicaIndex = 0;
                int llmInstanceID = instanceMgr?.GetFreeLLM(isSmallJob: true, isVisionJob: isVisionJob, out llmReplicaIndex) ?? -1;
                
                // If no free instance, try to get the least busy one that can accept the job type
                if (llmInstanceID < 0 && instanceMgr != null && instanceMgr.GetInstanceCount() > 0)
                {
                    llmInstanceID = instanceMgr.GetLeastBusyLLM(isSmallJob: true, isVisionJob: isVisionJob, out llmReplicaIndex);
                    RTConsole.Log($"No free LLM for job type, using least busy: {llmInstanceID} replica {llmReplicaIndex}");
                }
                
                // Check if vision job has no eligible LLM
                if (isVisionJob && llmInstanceID < 0 && instanceMgr != null && instanceMgr.GetInstanceCount() > 0)
                {
                    RTConsole.Log("Error: No vision-capable LLM available. Set an LLM instance to 'Vision Jobs Only' or 'Any' job mode.");
                    RTQuickMessageManager.Get().ShowMessage("No vision-capable LLM available. Check LLM Settings.", 5);
                    SetStatusMessage("No vision LLM!");
                    ClearErrorsAndJobs();
                    return;
                }
                
                LLMInstanceInfo llmInstance = llmInstanceID >= 0 ? instanceMgr?.GetInstance(llmInstanceID) : null;
                
                // Fall back to legacy single-provider only if NO instances are configured at all
                LLMProvider activeProvider;
                LLMProviderSettings activeSettings;
                
                if (llmInstance != null)
                {
                    activeProvider = llmInstance.providerType;
                    activeSettings = llmInstance.settings;
                    job._llmInstanceID = llmInstanceID;
                    job._llmReplicaIndex = llmReplicaIndex;
                    RTConsole.Log($"Using LLM instance {llmInstanceID}: {llmInstance.name} replica {llmReplicaIndex}");
                }
                else
                {
                    // Only fall back to legacy if no instances are configured
                    activeProvider = mgr.GetActiveProvider();
                    activeSettings = mgr.GetProviderSettings(activeProvider);
                    job._llmInstanceID = -1;
                    job._llmReplicaIndex = 0;
                    llmReplicaIndex = 0;
                    RTConsole.Log("No LLM instances configured, using legacy provider");
                }
                
                var lines = _promptManager.BuildPromptChat();
                float temperature = AdventureLogic.Get().GetExtractor().Temperature;

                switch (activeProvider)
                {
                    case LLMProvider.OpenAI:
                        {
                            string apiKey = activeSettings.apiKey;
                            string model = activeSettings.selectedModel;

                            // OpenAI APIs require at least one user message - add placeholder if missing
                            bool hasUserMessage = false;
                            foreach (var line in lines)
                            {
                                if (line._role == "user")
                                {
                                    hasUserMessage = true;
                                    break;
                                }
                            }
                            if (!hasUserMessage)
                            {
                                lines.Enqueue(new GTPChatLine("user", "Please proceed."));
                            }

                            // Single source of truth for "which OpenAI request shape does this model want?".
                            // Edit OpenAIRequestProfileResolver to add new model families.
                            var profile = OpenAIRequestProfileResolver.Resolve(model, activeSettings, llmReplicaIndex);

                            string json = _openAITextCompletionManager.BuildChatCompleteJSON(
                                lines, LLMRequestProfile.NoExplicitOutputTokenCap, temperature, model, true,
                                profile.useResponsesAPI, profile.isReasoningModel, profile.includeTemperature,
                                profile.reasoningEffort, profile.enableThinking);
                            RTConsole.Log("Contacting OpenAI at " + profile.endpoint);
                            _openAITextCompletionManager.SpawnChatCompleteRequest(json, OnTexGenCompletedCallback, db, apiKey, profile.endpoint, OnStreamingTextCallback, true, debugJobSize: LLMDebugLog.JobSize.Small);
                            SetLLMActive(true, llmInstanceID, llmReplicaIndex);
                        }
                        break;

                    case LLMProvider.Anthropic:
                        {
                            string apiKey = activeSettings.apiKey;
                            string model = activeSettings.selectedModel;
                            string endpoint = activeSettings.endpoint;
                            
                            // Fall back to Config if settings are empty
                            if (string.IsNullOrEmpty(apiKey)) apiKey = Config.Get().GetAnthropicAI_APIKey();
                            if (string.IsNullOrEmpty(model)) model = Config.Get().GetAnthropicAI_APIModel();
                            if (string.IsNullOrEmpty(endpoint)) endpoint = Config.Get().GetAnthropicAI_APIEndpoint();
                            
                            RTConsole.Log("Contacting Anthropic at " + endpoint);
                            string json = _anthropicAITextCompletionManager.BuildChatCompleteJSON(lines, LLMRequestProfile.GetAnthropicMaxOutputTokens(model), temperature, model, true);
                            _anthropicAITextCompletionManager.SpawnChatCompletionRequest(json, OnTexGenCompletedCallback, db, apiKey, endpoint, OnStreamingTextCallback, true, debugJobSize: LLMDebugLog.JobSize.Small);
                            SetLLMActive(true, llmInstanceID, llmReplicaIndex);
                        }
                        break;

                    case LLMProvider.LlamaCpp:
                        {
                            string serverAddress = LLMInstanceManager.ApplyReplicaPortOffset(activeSettings.endpoint, llmReplicaIndex);
                            string apiKey = activeSettings.apiKey;
                            
                            RTConsole.Log("Contacting llama.cpp at " + serverAddress);
                            string suggestedEndpoint;
                            // Build LLM params from instance settings if available
                            var llmParms = llmInstance != null ? mgr.GetInstanceLLMParms(llmInstanceID) : mgr.GetLLMParms(LLMProvider.LlamaCpp);
                            string json = _texGenWebUICompletionManager.BuildForInstructJSON(lines, out suggestedEndpoint,
                                LLMRequestProfile.NoExplicitOutputTokenCap, temperature,
                                Config.Get().GetGenericLLMMode(), true, llmParms, false, true);
                            _texGenWebUICompletionManager.SpawnChatCompleteRequest(json, OnTexGenCompletedCallback, db, serverAddress, suggestedEndpoint, OnStreamingTextCallback, true, apiKey, debugJobSize: LLMDebugLog.JobSize.Small);
                            SetLLMActive(true, llmInstanceID, llmReplicaIndex);
                        }
                        break;

                    case LLMProvider.Ollama:
                        {
                            string serverAddress = LLMInstanceManager.ApplyReplicaPortOffset(activeSettings.endpoint, llmReplicaIndex);
                            string apiKey = activeSettings.apiKey;
                            
                            RTConsole.Log("Contacting Ollama at " + serverAddress);
                            string suggestedEndpoint;
                            // Build LLM params from instance settings if available
                            var llmParms = llmInstance != null ? mgr.GetInstanceLLMParms(llmInstanceID) : mgr.GetLLMParms(LLMProvider.Ollama);
                            string json = _texGenWebUICompletionManager.BuildForInstructJSON(lines, out suggestedEndpoint,
                                LLMRequestProfile.NoExplicitOutputTokenCap, temperature,
                                Config.Get().GetGenericLLMMode(), true, llmParms, true, false);
                            _texGenWebUICompletionManager.SpawnChatCompleteRequest(json, OnTexGenCompletedCallback, db, serverAddress, suggestedEndpoint, OnStreamingTextCallback, true, apiKey, debugJobSize: LLMDebugLog.JobSize.Small);
                            SetLLMActive(true, llmInstanceID, llmReplicaIndex);
                        }
                        break;

                    case LLMProvider.Gemini:
                        {
                            string apiKey = activeSettings.apiKey;
                            // No silent fallback model: an unset model surfaces a clear error rather
                            // than masking the misconfiguration by quietly using some default.
                            string model = activeSettings.selectedModel ?? "";
                            string baseEndpoint = activeSettings.endpoint ?? "https://generativelanguage.googleapis.com/v1beta/models";
                            bool enableThinking = activeSettings.enableThinking;

                            // Build full endpoint URL with model name
                            string endpoint = GeminiTextCompletionManager.BuildEndpointUrl(baseEndpoint, model, true);
                            
                            RTConsole.Log($"PicMain: Contacting Gemini at {endpoint} with model {model}, thinking: {enableThinking}");

                            // Gemini requires at least one user message in contents - add placeholder if missing
                            bool hasUserMessage = false;
                            foreach (var line in lines)
                            {
                                if (line._role == "user")
                                {
                                    hasUserMessage = true;
                                    break;
                                }
                            }
                            if (!hasUserMessage)
                            {
                                lines.Enqueue(new GTPChatLine("user", "Please proceed."));
                            }

                            // Ensure we have a GeminiTextCompletionManager
                            if (_geminiTextCompletionManager == null)
                            {
                                _geminiTextCompletionManager = gameObject.GetComponent<GeminiTextCompletionManager>();
                                if (_geminiTextCompletionManager == null)
                                {
                                    _geminiTextCompletionManager = gameObject.AddComponent<GeminiTextCompletionManager>();
                                }
                            }

                            string json = _geminiTextCompletionManager.BuildChatCompleteJSON(lines, LLMRequestProfile.NoExplicitOutputTokenCap, temperature, model, true, enableThinking);
                            _geminiTextCompletionManager.SpawnChatCompleteRequest(json, OnTexGenCompletedCallback, db, apiKey, endpoint, OnStreamingTextCallback, true, debugJobSize: LLMDebugLog.JobSize.Small);
                            SetLLMActive(true, llmInstanceID, llmReplicaIndex);
                        }
                        break;

                    case LLMProvider.OpenAICompatible:
                        {
                            string serverAddress = LLMInstanceManager.ApplyReplicaPortOffset(activeSettings.endpoint, llmReplicaIndex);
                            string apiKey = activeSettings.apiKey;
                            string model = activeSettings.selectedModel ?? "";
                            
                            // Build endpoint URL for OpenAI compatible server
                            string endpoint = serverAddress.TrimEnd('/') + "/v1/chat/completions";
                            
                            RTConsole.Log($"PicMain: Contacting OpenAI Compatible server at {endpoint} with model {model}");
                            
                            // Normalize messages for strict role alternation (required by models like Mistral)
                            var normalizedLines = OpenAITextCompletionManager.NormalizeForStrictAlternation(lines);
                            
                            bool isDeepSeek = LLMRequestProfile.IsDeepSeekModel(model);
                            var compatReasoningEffort = isDeepSeek
                                ? activeSettings.GetReasoningEffort()
                                : (activeSettings.enableThinking ? LLMReasoningEffort.High : LLMReasoningEffort.Off);
                            bool? compatEnableThinking = isDeepSeek
                                ? compatReasoningEffort != LLMReasoningEffort.Off
                                : activeSettings.enableThinking;
                            float compatTemperature = isDeepSeek
                                ? LLMRequestProfile.GetRecommendedTemperature(model, compatReasoningEffort, temperature)
                                : temperature;
                            float? compatTopP = isDeepSeek
                                ? LLMRequestProfile.GetRecommendedTopP(model, compatReasoningEffort, 1.0f)
                                : (float?)null;
                            
                            string compatReasoningEffortParam = isDeepSeek ? LLMReasoningEffortUtil.ToConfigValue(compatReasoningEffort) : null;
                            string json = _openAITextCompletionManager.BuildChatCompleteJSON(normalizedLines, LLMRequestProfile.NoExplicitOutputTokenCap, compatTemperature, model, true,
                                enableThinking: compatEnableThinking,
                                topP: compatTopP,
                                customReasoningEffort: compatReasoningEffortParam);
                            _openAITextCompletionManager.SpawnChatCompleteRequest(json, OnTexGenCompletedCallback, db, apiKey, endpoint, OnStreamingTextCallback, true, debugJobSize: LLMDebugLog.JobSize.Small);
                            SetLLMActive(true, llmInstanceID, llmReplicaIndex);
                        }
                        break;
                }

            }
        }  else
    {
       
            if (m_jobList.Count > 0)
            {
                //convert the joblist into a real Job
                SetNoUndo(false);
                //there is a chance we need to use a different workflow, if the server wants it
                if (serverInfo!= null && m_allowServerJobOverrides && serverInfo._jobListOverride != "")
                {
                    List<string> serverJobList = serverInfo.GetPicJobListAsListOfStrings();
                    if (serverJobList.Count > 0)
                    {
                        m_allowServerJobOverrides = false; //once is enough
                        m_jobList.Clear();
                        AddJobList(serverJobList);
                        
                        // If this server's override has LLM calls before GPU work, mark it as pending
                        // This prevents multiple pics from queueing up on the same server
                        if (serverInfo.HasLLMFirstOverride() && serverID >= 0 && _pendingServerID < 0)
                        {
                            _pendingServerID = serverID;
                            Config.Get().IncrementPendingLLM(serverID);
                        }
                    }
                }

                // Per-server AutoPic override - applies to AutoPic jobs even when m_allowServerJobOverrides is false
                // Skip if the override was already pre-applied at creation (unlocked GPU scenario)
                if (serverInfo != null && m_isAutoPicJob && !m_autoPicOverridePreApplied && !string.IsNullOrEmpty(serverInfo._autoPicOverride))
                {
                    // Load the server-specific AutoPic preset and replace the job list
                    var preset = PresetManager.Get().LoadPreset(serverInfo._autoPicOverride, PresetManager.Get().GetActivePreset());
                    if (preset != null && !string.IsNullOrEmpty(preset.JobList))
                    {
                        List<string> autoPicJobList = GameLogic.Get().GetPicJobListAsListOfStrings(preset.JobList);
                        if (autoPicJobList.Count > 0)
                        {
                            // Try to claim ownership of this server - keeps it reserved for entire AutoPic workflow
                            if (!ClaimServerOwnership(serverID))
                            {
                                // Another pic already claimed this server - return and try again later
                                // (GetFreeGPU will give us a different server next time)
                                return;
                            }
                            m_isAutoPicJob = false; // Once is enough, claim succeeded
                            m_autoPicScriptName = serverInfo._autoPicOverride; // Track which override script was used
                            m_jobList.Clear();
                            AddJobList(autoPicJobList);
                        }
                    }
                }

                // Lock GPU for ALL AutoPic jobs (not just server overrides) to avoid race conditions
                if (m_isAutoPicJob && m_ownedServerID < 0 && serverID >= 0)
                {
                    if (!ClaimServerOwnership(serverID))
                    {
                        // Another pic already claimed this server - return and try again later
                        return;
                    }
                    m_isAutoPicJob = false; // Once is enough, claim succeeded
                }

                string workFlowNameWithoutTest = m_jobList[0];

                //remove "test_" from the front if it's there
                if (workFlowNameWithoutTest.StartsWith("test_"))
                {
                    workFlowNameWithoutTest = workFlowNameWithoutTest.Substring(5);
                }

                PicJob job = new PicJob();
                job = m_curEvent.m_picJob.Clone();

                //kill anything that shouldn't be carrried to the next render

                job._data.Clear();
                job._serverID = -1;

                // Per-render upload/snapshot state must NOT carry over from the previous
                // job. @upload directives below APPEND to these, and the upload-job builder
                // re-uploads everything in _pendingUploads - so without this reset a chained
                // step (e.g. img2img -> img2video) re-uploads the prior step's extra inputs
                // and the "?" info panel shows them (and the prior result) as belonging to
                // this step. Each workflow re-declares its own @upload directives, so these
                // get repopulated fresh for whatever this render actually needs.
                job._inputFilenames = new string[5] { "", "", "", "", "" };
                job._pendingUploads = new List<UploadInfo>();
                job._inputImagePngs = new byte[5][];
                job._outputImagePng = null;


                if (m_jobDefaultInfo != null)
                {
                    job = m_jobDefaultInfo;
                    m_jobDefaultInfo = null;
                }
                job._originalJobString = m_jobList[0];

                job._job = "run_workflow";
                //separate m_jobList[0] into a vector of strings separated by space
                string[] words = m_jobList[0].Split('@');
                job._workflow = words[0];

                //remove whitespace from each word in "words"
                for (int j = 0; j < words.Length; j++)
                {
                    words[j] = words[j].Trim();
                }


                bool bSkipNextParm = false;

                //if job._workflow ends in a '-' or '/' then remove it.  Also trim it after that
                if (job._workflow.EndsWith("-"))
                {
                    job._workflow = job._workflow.Substring(0, job._workflow.Length - 1);
                    bSkipNextParm = true; //A dash?!!  We'll need to ignore the next parm I guess
                }

                job._workflow = job._workflow.Trim();

                for (int i=1; i < words.Length; i++)
                {

                    if (bSkipNextParm)
                    {
                        bSkipNextParm = false;

                        if (words[i].EndsWith("-"))
                        {
                            bSkipNextParm = true;
                        }
                        continue;
                    }

                    if (words[i].EndsWith("-"))
                    {
                        bSkipNextParm = true;
                    }

                    //this must be a command.  Split it up by | character
                    string[] commandParts = words[i].Split('|');
                    if (commandParts.Length >= 1)
                    {
                        PicJobData picJobData = new PicJobData();
                        picJobData._name = commandParts[0].Trim();

                        // Get variable managers for %variable% substitution
                        VariableManager globalVM = GameLogic.Get()?.GetGlobalVariableManager();
                        // Create built-in resolver so %prompt%, %llm_reply%, etc. work in variable substitution
                        BuiltInVariableResolver builtInResolver = CreateBuiltInResolver(job);

                        // Apply variable substitution to ALL command parts (not just parm1/parm2)
                        // so commands like resize_if_larger that use parts beyond [1] and [2] also get substitution
                        for (int p = 1; p < commandParts.Length; p++)
                        {
                            commandParts[p] = VariableManager.ProcessVariables(commandParts[p], m_variableManager, globalVM, builtInResolver);
                        }

                        if (commandParts.Length >= 2)
                        {
                            picJobData._parm1 = commandParts[1];
                        }

                        if (commandParts.Length >= 3)
                        {
                            picJobData._parm2 = commandParts[2];
                        }

                        if (picJobData._name.ToLower() == "copy")
                        {
                            DoVarCopy(ref job, picJobData._parm1, picJobData._parm2);
                        }
                        else if (picJobData._name.ToLower() == "add")
                        {
                            DoVarAdd(ref job, picJobData._parm1, picJobData._parm2);
                        }
                        else if (picJobData._name.ToLower() == "set")
                        {
                            // @set|%variable_name%|value - Set a text variable
                            string varName = picJobData._parm1;
                            string varValue = picJobData._parm2;
                            VariableManager targetVM = VariableManager.GetManagerForVariable(varName, m_variableManager, GameLogic.Get()?.GetGlobalVariableManager());
                            if (targetVM != null)
                            {
                                targetVM.SetText(varName, varValue);
                            }
                        }
                        else if (picJobData._name.ToLower() == "setimage")
                        {
                            // @setimage|%variable_name%|source - Set an image variable from an image slot
                            string varName = picJobData._parm1;
                            string source = picJobData._parm2?.ToLower().Trim() ?? "";
                            
                            Texture2D sourceTexture = null;
                            
                            // First check if source is a %variable% reference
                            if (VariableManager.IsVariableReference(source))
                            {
                                sourceTexture = VariableManager.ResolveImageVariable(source, m_variableManager, GameLogic.Get()?.GetGlobalVariableManager());
                            }
                            
                            // Otherwise try to get from image slot
                            if (sourceTexture == null)
                            {
                                sourceTexture = GetTextureFromSource(source);
                            }
                            
                            if (sourceTexture != null)
                            {
                                VariableManager targetVM = VariableManager.GetManagerForVariable(varName, m_variableManager, GameLogic.Get()?.GetGlobalVariableManager());
                                if (targetVM != null)
                                {
                                    // Make a copy of the texture to avoid reference issues
                                    Texture2D textureCopy = new Texture2D(sourceTexture.width, sourceTexture.height, sourceTexture.format, false);
                                    Graphics.CopyTexture(sourceTexture, textureCopy);
                                    targetVM.SetImage(varName, textureCopy);
                                }
                            }
                            else
                            {
                                RTConsole.Log($"setimage: Error - could not get texture from source '{source}'");
                            }
                        }
                        else if (picJobData._name.ToLower() == "clear")
                        {
                            // @clear|%variable_name% - Clear a variable
                            string varName = picJobData._parm1;
                            VariableManager targetVM = VariableManager.GetManagerForVariable(varName, m_variableManager, GameLogic.Get()?.GetGlobalVariableManager());
                            if (targetVM != null)
                            {
                                targetVM.Clear(varName);
                            }
                        }
                        if (picJobData._name.ToLower() == "llm_prompt_reset")
                        {
                            _promptManager.Reset();
                        }
                        else
                        if (picJobData._name.ToLower() == "llm_prompt_set_base_prompt")
                        {

                         _promptManager.SetBaseSystemPrompt(ConvertVarToText(ref job,  picJobData._parm1));
                        }
                        else
                        if (picJobData._name.ToLower() == "llm_prompt_pop_first")
                        {
                            _promptManager.PopFirstInteraction();
                        }
                        else
                        if (picJobData._name.ToLower() == "llm_prompt_add_from_assistant")
                        {
                            _promptManager.AddInteraction(Config.Get().GetAIAssistantWord(), ConvertVarToText(ref job, picJobData._parm1));
                        } else if (picJobData._name.ToLower() == "llm_prompt_add_from_user")
                        {
                            _promptManager.AddInteraction("user", ConvertVarToText(ref job, picJobData._parm1));
                        }
                        else if(picJobData._name.ToLower() == "llm_prompt_add_to_last_interaction")
                        {
                            _promptManager.AppendToLastInteraction(" " + ConvertVarToText(ref job, picJobData._parm1));
                        }
                        else if (picJobData._name.ToLower() == "llm_add_image")
                        {
                            // Add an image to the next LLM user message (for vision LLM support)
                            // Usage: command @llm_add_image|temp1|  (or image, image1, temp2, temp3)
                            string source = picJobData._parm1.ToLower().Trim();
                            Texture2D sourceTexture = GetTextureFromSource(source);
                            
                            if (sourceTexture != null)
                            {
                                byte[] pngBytes = sourceTexture.EncodeToPNG();
                                string base64Image = System.Convert.ToBase64String(pngBytes);
                                _promptManager.AddPendingImage(base64Image);
                                RTConsole.Log($"llm_add_image: Added image from '{source}' ({sourceTexture.width}x{sourceTexture.height}, {base64Image.Length} base64 chars)");
                            }
                            else
                            {
                                RTConsole.Log($"llm_add_image: Error - could not get texture from source '{source}'");
                            }
                        }
                        else if (picJobData._name.ToLower() == "parse_llm_prompts")
                        {
                            // Parse SET_PROMPT1: through SET_PROMPT8: from LLM reply and populate _requestedPrompts array
                            ParseLLMPrompts(ref job);
                        }
                        else if (picJobData._name.ToLower() == "fill_mask_if_blank")
                        {
                            FillAlphaMaskIfBlank();
                        }
                        else if (picJobData._name.ToLower() == "invert_alpha")
                        {
                            // Format: @invert_alpha| or @invert_alpha|slot|
                            // Where slot is: image, image1, temp1, temp2, temp3
                            PicMain targetPic = this;
                            string slotParam = picJobData._parm1?.Trim().ToLower() ?? "";
                            
                            if (!string.IsNullOrEmpty(slotParam) && IsImageSlot(slotParam))
                            {
                                targetPic = GetPicMainForSlot(slotParam);
                                if (targetPic == null)
                                {
                                    RTConsole.Log($"Error: Unknown image slot '{slotParam}' in invert_alpha command.");
                                    break;
                                }
                            }
                            
                            Texture2D tex = targetPic.m_pic.sprite?.texture;
                            if (tex != null)
                            {
                                tex.InvertAlpha();
                                tex.Apply();
                                targetPic.InvalidateCachedPng();
                                targetPic.SetStatusMessage("");
                                RTConsole.Log($"invert_alpha: Inverted alpha channel for {(string.IsNullOrEmpty(slotParam) ? "image" : slotParam)}");
                            }
                        }
                        else if (picJobData._name.ToLower() == "no_undo")
                        {
                            SetNoUndo(true);
                        }
                        else if (picJobData._name.ToLower() == "stopjob")
                        {
                            // Signal that the script callback should NOT add more jobs after this script completes
                            m_stopAfterScript = true;
                        }
                        else if (picJobData._name.ToLower() == "lock_gpu")
                        {
                            // Lock this server exclusively for the entire preset workflow
                            if (serverID >= 0 && m_ownedServerID < 0)
                            {
                                if (ClaimServerOwnership(serverID))
                                {
                                    RTConsole.Log($"@lock_gpu: Locked server {serverID} for this preset");
                                }
                            }
                        }
                        else if (picJobData._name.ToLower() == "resize_if_larger")
                        {
                            ProcessResizeIfLargerOrForcedCommand(commandParts, false);
                        }
                        else if (picJobData._name.ToLower() == "resize")
                        {
                            ProcessResizeIfLargerOrForcedCommand(commandParts, true);
                        }
                        else if (picJobData._name.ToLower() == "upload")
                        {
                            // Parse: @upload|source|inputN|
                            // source: image1, image2 (future), temp1, temp2, temp3
                            // dest: input1, input2, input3, input4, input5 (or just 1, 2, 3, 4, 5)
                            string source = picJobData._parm1.ToLower().Trim();
                            string dest = picJobData._parm2.ToLower().Trim();

                            // Parse input index from dest (input1 -> 0, input2 -> 1, etc.)
                            int inputIndex = -1;
                            if (dest == "input1" || dest == "1") inputIndex = 0;
                            else if (dest == "input2" || dest == "2") inputIndex = 1;
                            else if (dest == "input3" || dest == "3") inputIndex = 2;
                            else if (dest == "input4" || dest == "4") inputIndex = 3;
                            else if (dest == "input5" || dest == "5") inputIndex = 4;

                            if (inputIndex >= 0 && inputIndex < 5)
                            {
                                // Generate a GUID filename for this upload. Video sources keep
                                // their real container extension (.mp4 etc.) so the uploaded file
                                // and the workflow's video loader agree on the path; image sources
                                // stay .png.
                                string uploadExt = ".png";
                                if (source == "video" || source == "video1")
                                {
                                    string movieFile = !string.IsNullOrEmpty(m_pendingVideoUploadPath)
                                        ? m_pendingVideoUploadPath
                                        : ((m_picMovie != null && IsMovie()) ? m_picMovie.GetProcessingFileName() : null);
                                    string movieExt = string.IsNullOrEmpty(movieFile) ? null : System.IO.Path.GetExtension(movieFile);
                                    uploadExt = string.IsNullOrEmpty(movieExt) ? ".mp4" : movieExt;
                                }
                                string guidFilename = "pic_" + System.Guid.NewGuid() + uploadExt;

                                UploadInfo uploadInfo = new UploadInfo()
                                {
                                    source = source,
                                    inputIndex = inputIndex,
                                    filename = guidFilename
                                };
                                job._pendingUploads.Add(uploadInfo);
                                
                                // Set the input filename that will be used in workflow replacement
                                job._inputFilenames[inputIndex] = "temp/" + guidFilename;
                            }
                            else
                            {
                                RTConsole.Log("Error: Invalid upload destination '" + dest + "'. Use input1-input5 or 1-5.");
                            }
                        }

                        //add it
                        job._data.Add(picJobData);
                    }
                    else
                    {
                        //show error
                        RTConsole.Log("Error parsing command: " + words[i]+" in job script line "+ m_jobList[0]);    
                    }
                }

                //actually, job

                // Create upload jobs from @upload commands (stored in job._pendingUploads)
                foreach (UploadInfo uploadInfo in job._pendingUploads)
                {
                    PicJob uploaderJob = new PicJob();
                    uploaderJob._job = "upload_to_comfy";
                    // Store the source and input index info in the job
                    // Format: "source|inputIndex|filename"
                    uploaderJob._parm_1_string = uploadInfo.source + "|" + uploadInfo.inputIndex + "|" + uploadInfo.filename;
                    m_picJobs.Add(uploaderJob);
                }
                
                //if first first word is "command", we'll ignore it

                if (words[0] != "command")
                {
                    if (words[0] == "call_llm")
                    {
                        SetJobWithInfoFromCur(ref job);
                        job._job = "call_llm"; //we'll call the LLM with this job
                        m_picJobs.Add(job);

                    }
                    else
                    {
                        //default handling
                        SetJobWithInfoFromCur(ref job);
                        m_picJobs.Add(job);
                    }
                    //remove the first string in m_joblist
                }  else
                {
                    m_curEvent.m_picJob = job;
         
                }

                m_jobList.RemoveAt(0);

                if (m_jobList.Count == 0)
                {
                    // Check if all jobs (including pending PicJobs) are done
                    if (m_picJobs.Count == 0)
                    {
                        // All jobs truly complete - invoke callback
                        SetNeedsToUpdateInfoPanelFlag();
                        // Command-only lines should not leave a stale overlay from an earlier LLM/render status.
                        SetStatusMessage("");
                        
                        if (m_onFinishedScriptCallback != null)
                        {
                            m_onFinishedScriptCallback.Invoke(gameObject);
                        }
                    }
                    else
                    {
                        // Still have PicJobs to execute, continue processing
                        UpdateJobs();
                    }
                }
                else
                {
                    UpdateJobs();
                }
                
            }
        }
    }

    private void OnDestroy()
    {
       InvalidateExportedEditFile();

        // Release LLM slot if this pic was using one
        SetLLMActive(false);
        
        // Release server ownership if this pic owned a server
        ReleaseServerOwnership();
        
        // Also ensure pending server LLM count is decremented (defensive cleanup)
        if (_pendingServerID >= 0)
        {
            Config.Get().DecrementPendingLLM(_pendingServerID);
            _pendingServerID = -1;
        }

        if (m_pic.sprite && m_pic.sprite.texture)
        {
            UnityEngine.Object.Destroy(m_pic.sprite.texture); //this will also kill the sprite?
            UnityEngine.Object.Destroy(m_pic.sprite);
        }

        if (m_image2 != null)
        {
            UnityEngine.Object.Destroy(m_image2);
            m_image2 = null;
        }
        if (m_image3 != null)
        {
            UnityEngine.Object.Destroy(m_image3);
            m_image3 = null;
        }
        if (m_image4 != null)
        {
            UnityEngine.Object.Destroy(m_image4);
            m_image4 = null;
        }
        if (m_image5 != null)
        {
            UnityEngine.Object.Destroy(m_image5);
            m_image5 = null;
        }

        KillUndoImageBuffers();
    }

    public void SetNeedsToUpdateInfoPanelFlag()
    {
        m_bNeedsToUpdateInfoPanel = true;
    }

    public void SetControlImage(Texture tex)
    {
        float biggestSize = Math.Max(tex.width, tex.height);

        Sprite newSprite = Sprite.Create(tex as Texture2D, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), biggestSize / 5.12f, 0, SpriteMeshType.FullRect);
        newSprite.texture.Apply();
        m_infoPanelScript.SetSprite(newSprite);
    }

    // Attach the generated result PNG to the most-recently-run job so the "?" panel can
    // show "inputs + output". Jobs run sequentially and only run_* jobs are added to
    // history, so the last history entry is the job that just produced this result.
    //
    // We stamp BOTH the current-stats job and the last history record: OnFinishedRenderingWorkflow
    // overwrites _jobHistory[last] with GetCurrentStats().m_picJob after the render, so if we
    // only stamped the history clone the result would be lost on that rewrite. Stamping the
    // live job covers the workflow path; stamping the history record covers any path that
    // doesn't run that rewrite (e.g. DALL-E / OpenAI image).
    public void SetLastGeneratedResultPng(byte[] pngBytes)
    {
        if (pngBytes == null || pngBytes.Length == 0) return;

        var curStats = GetCurrentStats();
        if (curStats != null && curStats.m_picJob != null)
            curStats.m_picJob._outputImagePng = pngBytes;

        if (_jobHistory != null && _jobHistory.Count > 0)
            _jobHistory[_jobHistory.Count - 1]._outputImagePng = pngBytes;
    }

    public void UpdateInfoPanel()
    {
        m_infoPanelScript.SetInfoText(GetInfoText());

        // Build a chronological timeline of every generation step's input images and its
        // result, so the "?" panel can show how a chained/composited image (or movie) was
        // built up - including the original anchor images used in earlier steps, which the
        // latest step only sees as a single already-composited input. The per-job PNG
        // snapshots are captured at upload time / on result and persist in _jobHistory; we
        // pass the live byte[][] / byte[] references straight through (PicInfoPanel caches
        // by reference to avoid re-compositing every update tick).
        var rows = new List<PicInfoPanel.ImageHistoryRow>();
        bool anyInputs = false;
        foreach (var hj in _jobHistory)
        {
            if (hj == null) continue;

            bool hasInputs = false;
            if (hj._inputImagePngs != null)
            {
                for (int k = 0; k < hj._inputImagePngs.Length; k++)
                {
                    if (hj._inputImagePngs[k] != null && hj._inputImagePngs[k].Length > 0)
                    {
                        hasInputs = true;
                        break;
                    }
                }
            }
            bool hasOutput = hj._outputImagePng != null && hj._outputImagePng.Length > 0;
            if (!hasInputs && !hasOutput) continue;

            if (hasInputs) anyInputs = true;
            rows.Add(new PicInfoPanel.ImageHistoryRow { inputs = hj._inputImagePngs, output = hj._outputImagePng });
        }

        // Preserve the "no strip for plain prompt-to-image" behavior: only show the
        // timeline once at least one step actually fed an input image (i.e. an
        // image-to-image / chain happened). A history of pure generations shows nothing.
        if (!anyInputs) rows.Clear();

        m_infoPanelScript.SetImageHistory(rows);

        m_bNeedsToUpdateInfoPanel = false;
    }

    public bool IsVisible()
    {
        return m_pic.isVisible;
    }
    // Update is called once per frame
    void Update()
    {
        if (m_editFileHasChanged)
        {
            //The watcher (background thread) flagged a change.  Logging happens HERE on the main
            //thread (RTConsole touches Unity API).  Arm a debounced reload rather than reading
            //immediately, since the editor may still be writing/holding the file.
            m_editFileHasChanged = false;
            m_editReloadArmed = true;
            m_editReloadAt = Time.unscaledTime + 0.2f;        //let the editor settle
            m_editReloadDeadline = Time.unscaledTime + 6f;    //stop retrying after this
            m_editLastReadableSize = -1;
            RTConsole.Log("File we're editing changed, reloading " + System.IO.Path.GetFileName(m_editFilename) + " ...");
        }
        if (m_editReloadArmed && Time.unscaledTime >= m_editReloadAt)
        {
            long size = GetReadableFileSize(m_editFilename);
            if (size > 0 && size == m_editLastReadableSize)
            {
                //Readable and its size has stopped changing -> the write finished, safe to reload.
                m_editReloadArmed = false;
                AddImageUndo();
                LoadImageByFilename(m_editFilename, false, true, bInvertLoadedMask: true); //invert the externally-edited mask back to internal polarity
                RTConsole.Log("Reloaded edited file (" + size + " bytes).");
            }
            else if (Time.unscaledTime >= m_editReloadDeadline)
            {
                m_editReloadArmed = false;
                RTConsole.Log("Gave up reloading edited file (still locked or empty?): " + System.IO.Path.GetFileName(m_editFilename));
            }
            else
            {
                m_editLastReadableSize = size;            //remember to detect a stable size next poll
                m_editReloadAt = Time.unscaledTime + 0.15f;
            }
        }

        //mask things faster when zoomed out by not rendering the GUI

        if (m_camera.orthographicSize > 9 || !m_pic.isVisible)
        {
            if (m_canvas.enabled)
            {
                m_canvas.enabled = false;
            }
        }
        else
        {
            m_canvas.enabled = true;
        }

        UpdateJobs();
       
        if (m_bNeedsToUpdateInfoPanel)
        {
            if (m_infoPanelScript.IsPanelOpen())
            {
                UpdateInfoPanel();
            }
        }

        UpdateStatusTextOverlayVisibility();
    }

    void UpdateStatusTextOverlayVisibility()
    {
        // Global toggle. Every PicMain sees the same Input.GetKeyDown frame, so guard
        // by frame count to prevent multiple Pics from flipping the state repeatedly.
        bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
        if (!ctrl
            && Input.GetKeyDown(KeyCode.Z)
            && !IsTypingInInputField()
            && s_lastOverlayToggleFrame != Time.frameCount)
        {
            s_lastOverlayToggleFrame = Time.frameCount;
            s_hideStatusTextOverlays = !s_hideStatusTextOverlays;

            var quickMessage = RTQuickMessageManager.Get();
            if (quickMessage != null)
            {
                quickMessage.ShowMessage(
                    s_hideStatusTextOverlays
                        ? "Image overlays hidden. Press Z again to show them."
                        : "Image overlays visible. Press Z again to hide them.");
            }
        }

        if (m_text != null && m_text.enabled == s_hideStatusTextOverlays)
            m_text.enabled = !s_hideStatusTextOverlays;
    }

    static bool IsTypingInInputField()
    {
        var es = EventSystem.current;
        if (es == null) return false;
        var selected = es.currentSelectedGameObject;
        if (selected == null) return false;
        var tmpInput = selected.GetComponent<TMP_InputField>();
        return tmpInput != null && tmpInput.isFocused;
    }

    public void PassInTempInfoPicJob(PicJob picJob)
    {
        GetCurrentStats().m_picJob = picJob;
        SetNeedsToUpdateInfoPanelFlag();
    }

    public void PassInTempInfo(RTRendererType requestedRenderer, int gpuID)
    {
    
    }

}
