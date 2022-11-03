/*
 * Written by Seth A. Robinson (rtsoft.com)
 * 
*/
using UnityEngine;
using System.Collections;
using System.Collections.Generic;

/*
	
Created by Seth A. Robinson - rtsoft.com 2013
	
 TO SETUP:
 
 Create a GameObject in unity called RTAudioManager and attach this script 
 
To call from any script:

//to play a sfx.. must be less then 8 seconds for no good reason
//Note: Don't add the .wav part of the filename (this will scan any arrays of audioclips added first as well)

//You can also pass in an Audioclip.  

RTAudioManager.Get().Play("sfxFilename");

//2018 note:  Use RTMessageManager instead of RTEventManager, its easier to use:

 RTMessageManager.Get().Schedule(2, RTAudioManager.Get().Play, "crap.wav"); //plays crap.wav from Resources dir in 2 seconds
 
 //Note that you have to include ALL optional parms, and clearly define int vs float (via the f at the end)
 RTMessageManager.Get().Schedule(1, RTAudioManager.Get().PlayEx, "blip_lose2", 1.0f, 1.0f, false, 0.0f);
 

//The old way with RTEventManager to play in X seconds:
			
RTEventManager.Get().Schedule(RTAudioManager.GetName(), "Play", 1, "sfxFilename");

//to play a mp3 song (song or its folders must be placed off of Assets/Resources)
//this will replace any existing music
RTAudioManager.Get().PlayMusic("audio/wiz", 1.0f, 1.0f, true);

//To use with RTEventManager to play the same song 2 seconds later   
RTEventManager.Get().Schedule(RTAudioManager.GetName(), "PlayMusic", 2, "musicFileName");

//Play the song 2 seconds later, but with settings for vol, pitch, and looping
RTEventManager.Get().Schedule(RTAudioManager.GetName(), "PlayMusicEx", 2, new RTDB("fileName", "chalk",
			"volume", 1.0f, "pitch", 2.0f,  "loop", false));

 
Note: If a parm is missing with PlayEx the default is used instead

Note: .wav or .mp3/ogg etc must be located in the Assets/Resources so unity includes them automatically.
They must NOT be marked as 3d sounds, if you hear nothing but don't see an error, this is what's wrong.
 

To add a list<AudioClip> of sounds do this:

add this somewhere up top, add the sounds by using the Unity property window, can drag all at once:
public List<AudioClip> clips;

Add this to your startup somewhere:
RTAudioManager.Get().AddClipsToLibrary(clips);

 //Play back sounds like:

 RTAudioManager.Get().Play("sfxFilename"); //note, it's not case sensitive

Or like this to schedule them to play after a delay:
 
RTMessageManager.Get().Schedule(0, RTAudioManager.Get().Play, "jump");

*/


public class RTAudioManager : MonoBehaviour 
{
	static RTAudioManager _this = null;
	public AudioSource _activeMusic = null;
	
    float _defaultMusicVol = 1.0f;

    Dictionary<string, GameObject> _objects = null; //we track certain kinds of sounds by putting them in objects
    Dictionary<string, float> _lastPlaybackDict = null; //track the last time we played this type of sound

    Dictionary<AudioClip, GameObject> _clipObjects = null; //we track certain kinds of sounds by putting them in objects
    Dictionary<AudioClip, float> _clipLastPlaybackDict = null; //track the last time we played this type of sound

    Dictionary<string, AudioClip> _clipLibrary; //our own storage of clips, optional to use

    RTAudioManager()
	{
			_this = this;
	}

    public static RTAudioManager Get()
    {
        if (!_this)
        {
            GameObject gameObject = new GameObject();
            _this = gameObject.AddComponent<RTAudioManager>();
            _this.Start();
        }
        return _this;
    }


    public void AddClipsToLibrary(List<AudioClip> clips)
    {
        for (int i=0; i < clips.Count; i++)
        {
            _clipLibrary.Add(clips[i].name.ToLower(), clips[i]);
        }

    }

    public void Start()
	{
        if (_objects != null) return; //we already ran this

        _objects = new Dictionary<string, GameObject>();
        _lastPlaybackDict = new Dictionary<string, float>();

        _clipObjects = new Dictionary<AudioClip, GameObject>();
        _clipLastPlaybackDict = new Dictionary<AudioClip, float>();
        _clipLibrary = new Dictionary<string, AudioClip>();
        gameObject.name = "RTAudioManager";
        DontDestroyOnLoad(gameObject);

        //Debug.Log("RTAudioManager initted, gameobject we're in renamed to RTAudioManager");
    }

    public void SetDefaultMusicVol(float vol)
    {
        _defaultMusicVol = vol;
    }

	public void PlayEx(RTDB db)
	{
		PlayEx(db.GetString("fileName"),
			db.GetFloatWithDefault("volume", 1.0f),
			db.GetFloatWithDefault("pitch", 1.0f),
              db.GetBoolWithDefault("killExisting", false), //if true, only allows one of these kinds of sounds
              db.GetFloatWithDefault("ignoreIfRecentlyPlayedSeconds", 0.0f)
              );
	}

	public void PlayMusicEx(RTDB db)
	{
		PlayMusic(db.GetString("fileName"),
		       db.GetFloatWithDefault("volume", _defaultMusicVol),
		       db.GetFloatWithDefault("pitch", 1.0f),
		       db.GetBoolWithDefault("loop", true)
		       );
	}
	
	public void PlayMusic(string fileName)
	{
        PlayMusic(fileName, _defaultMusicVol, 1, true);
	}

	public void StopMusic()
	{
		if (_activeMusic != null)
		{
			//Debug.Log ("Stopping music");
            _activeMusic.Stop();
			//Destroy(_activeMusic);
			//_activeMusic = null;
        }
        
	}

	public void PlayMusic(string fileName, float vol, float pitch, bool loop)
	{

		if (Camera.main != null)
		{
			AudioListener al = (AudioListener) Camera.main.GetComponent("AudioListener");

            if (al)
            {
                transform.parent = al.transform;
                transform.position = Camera.main.transform.position;
            }
		}

        AudioClip clip;

        if (_clipLibrary.TryGetValue(fileName.ToLower(), out clip))
        {
            //oh, we have a clip with this name added with AddClipsToLibrary.  Use it instead of actually looking for a file in the resources dir
         
        } else
        {
            clip = Resources.Load(fileName) as AudioClip;

            if (clip == null)
            {
                Debug.LogWarning("Couldn't find " + fileName);
                return;
            }
        }



        //Debug.Log ("Playing music "+fileName);
     

		if (_activeMusic != null)
		{
			//Debug.Log ("Stopping music");
			_activeMusic.Stop();
			//Destroy(m_activeMusic);
		//	_activeMusic = null;
		} else
        {
            _activeMusic = gameObject.AddComponent<AudioSource>() as AudioSource;
        }


        GetComponent<AudioSource>().volume = vol;
		GetComponent<AudioSource>().pitch = pitch;
		GetComponent<AudioSource>().loop = loop;
		GetComponent<AudioSource>().clip = clip;
		GetComponent<AudioSource>().Play();
	}
	
	public AudioSource GetMusicComponent()
	{
		return _activeMusic;
    }

    public void Play3DEx(string fileName, Vector3 vPos, float vol = 1.0f, float pitch = 1.0f, bool killExisting = false, float ignoreIfRecentlyPlayedSeconds = 0)
    {

        AudioClip clipTemp;
        if (_clipLibrary.TryGetValue(fileName.ToLower(), out clipTemp))
        {
            //oh, we have a clip with this name added with AddClipsToLibrary.  Use it instead of actually looking for a file in the resources dir
            Play3DEx(clipTemp, vPos, vol, pitch, killExisting, ignoreIfRecentlyPlayedSeconds);
            return;
        }

        Debug.LogError("Clip by filename of "+fileName+" not found - Seth never added support for playing 3D sounds from a raw /Resources dir file.  Copy stuff from PlayEx I guess, or use a clip or filename of a clip added with AddClipsToLibrary()");
    }

        public void PlayEx(string fileName, float vol = 1.0f, float pitch = 1.0f, bool killExisting = false, float ignoreIfRecentlyPlayedSeconds = 0)
	{

        AudioClip clipTemp;
        if (_clipLibrary.TryGetValue(fileName.ToLower(), out clipTemp))
        {
            //oh, we have a clip with this name added with AddClipsToLibrary.  Use it instead of actually looking for a file in the resources dir
           // Debug.Log("Playing clip from lib: " + fileName);
            PlayEx(clipTemp, vol, pitch, killExisting, ignoreIfRecentlyPlayedSeconds);
            return;
        }

        GameObject audioObj = null;

        if (killExisting)
        {
            //special handling, we're going to keep our own clip
            if (_objects.ContainsKey(fileName))
            {
               // print("Using existing audioobj to play "+fileName);
                audioObj = _objects[fileName];
                audioObj.GetComponent<AudioSource>().volume = vol;
                audioObj.GetComponent<AudioSource>().pitch = pitch;
                audioObj.GetComponent<AudioSource>().Play();
                return;
            }
            else
            {
                //print("Can't use existing, first one..");
            }

        }

        if (ignoreIfRecentlyPlayedSeconds > 0)
        {
            if (_lastPlaybackDict.ContainsKey(fileName))
            {
                if (_lastPlaybackDict[fileName] + ignoreIfRecentlyPlayedSeconds > Time.time)
                {
                   // print("ignoring sfx, we played it too recently..");
                    return;
                } else
                {
                    //print("well, we last played it at "+ _lastPlaybackDict[fileName]+" and it's "+ Time.time+" so cool I guess");
                }

            }
              
            //either way, set it to now
            _lastPlaybackDict[fileName] = Time.time;
        }

		//Debug.Log ("Playing sfx "+fileName);
		AudioClip clip = Resources.Load(fileName) as AudioClip;
 	 	
		if (clip == null)
		{
			Debug.LogWarning ("Couldn't find "+fileName);
			return;
		}

        audioObj = new GameObject("sfx");

        if (killExisting)
        {
            _objects[fileName] = audioObj; //remember this for later
        }

        audioObj.transform.parent = gameObject.transform; //parent to us
        
        audioObj.AddComponent<AudioSource>();
		audioObj.GetComponent<AudioSource>().volume = vol;
		audioObj.GetComponent<AudioSource>().pitch = pitch;
        audioObj.GetComponent<AudioSource>().spatialBlend = 0.0f; //2d

        if (killExisting)
        {
            audioObj.GetComponent<AudioSource>().clip = clip;
            audioObj.GetComponent<AudioSource>().Play();
        }
        else
        {
            audioObj.GetComponent<AudioSource>().PlayOneShot(clip);
        }

        if (!killExisting)
        {
            GameObject.Destroy(audioObj, clip.length); //kill it in a bit.. 
        }
	}

    public void Play3DEx(AudioClip clip, Vector3 vPos, float vol = 1.0f, float pitch = 1.0f, bool killExisting = false, float ignoreIfRecentlyPlayedSeconds = 0)
    {

        if (clip == null)
        {
            Debug.LogError("RTAudioManager:  Clip supplied is null, can't play SFX");
            return;
        }
        GameObject audioObj = null;

        if (killExisting)
        {
            //special handling, we're going to keep our own clip
            if (_clipObjects.ContainsKey(clip))
            {
                // print("Using existing..");
                audioObj = _clipObjects[clip];
                audioObj.transform.position = vPos;
                audioObj.GetComponent<AudioSource>().volume = vol;
                audioObj.GetComponent<AudioSource>().pitch = pitch;
                audioObj.GetComponent<AudioSource>().Play();
                return;
            }
            else
            {
                //print("Can't use existing, first one..");
            }

        }

        if (ignoreIfRecentlyPlayedSeconds > 0)
        {
            if (_clipLastPlaybackDict.ContainsKey(clip))
            {
                if (_clipLastPlaybackDict[clip] + ignoreIfRecentlyPlayedSeconds > Time.time)
                {
                    // print("ignoring sfx, we played it too recently..");
                    return;
                }
                else
                {
                    //print("well, we last played it at "+ _clipLastPlaybackDict[fileName]+" and it's "+ Time.time+" so cool I guess");
                }

            }

            //either way, set it to now
            _clipLastPlaybackDict[clip] = Time.time;

        }

        audioObj = new GameObject("sfx");

        if (killExisting)
        {
            _clipObjects[clip] = audioObj; //remember this for later
        }

        audioObj.transform.parent = gameObject.transform; //parent to us

        audioObj.AddComponent<AudioSource>();
        audioObj.GetComponent<AudioSource>().volume = vol;
        audioObj.GetComponent<AudioSource>().spatialBlend = 1.0f;
        audioObj.GetComponent<AudioSource>().pitch = pitch;
        audioObj.GetComponent<AudioSource>().spatialBlend = 0.0f; //2d
        audioObj.transform.position = vPos;

        if (killExisting)
        {
            audioObj.GetComponent<AudioSource>().clip = clip;
            audioObj.GetComponent<AudioSource>().Play();
        }
        else
        {
            audioObj.GetComponent<AudioSource>().PlayOneShot(clip);
        }

        if (!killExisting)
        {
            GameObject.Destroy(audioObj, clip.length); //kill it in a bit.. 
        }
    }

    public void PlayEx(AudioClip clip, float vol = 1.0f, float pitch = 1.0f, bool killExisting = false, float ignoreIfRecentlyPlayedSeconds = 0)
    {

        if (clip == null)
        {
            Debug.LogError("RTAudioManager:  Clip supplied is null, can't play SFX");
            return;
        }
        GameObject audioObj = null;

        if (killExisting)
        {
            //special handling, we're going to keep our own clip
            if (_clipObjects.ContainsKey(clip))
            {
                // print("Using existing..");
                audioObj = _clipObjects[clip];
                audioObj.GetComponent<AudioSource>().volume = vol;
                audioObj.GetComponent<AudioSource>().pitch = pitch;
                // audioObj.GetComponent<AudioSource>().PlayOneShot(audioObj.GetComponent<AudioSource>().clip);
                audioObj.GetComponent<AudioSource>().Play();
                return;
            }
            else
            {
                //print("Can't use existing, first one..");
            }
       }

        if (ignoreIfRecentlyPlayedSeconds > 0)
        {
            if (_clipLastPlaybackDict.ContainsKey(clip))
            {
                if (_clipLastPlaybackDict[clip] + ignoreIfRecentlyPlayedSeconds > Time.time)
                {
                    // print("ignoring sfx, we played it too recently..");
                    return;
                }
                else
                {
                    //print("well, we last played it at "+ _clipLastPlaybackDict[fileName]+" and it's "+ Time.time+" so cool I guess");
                }

            }


            //either way, set it to now
            _clipLastPlaybackDict[clip] = Time.time;

        }

        audioObj = new GameObject("sfx");

        if (killExisting)
        {
            _clipObjects[clip] = audioObj; //remember this for later
        }

      
        audioObj.transform.parent = gameObject.transform; //parent to us
      
        audioObj.AddComponent<AudioSource>();
        audioObj.GetComponent<AudioSource>().volume = vol;
        audioObj.GetComponent<AudioSource>().pitch = pitch;
        audioObj.GetComponent<AudioSource>().spatialBlend = 0.0f; //2d

        if (killExisting)
        {
            audioObj.GetComponent<AudioSource>().clip = clip;
            audioObj.GetComponent<AudioSource>().Play();
        }
        else
        {
            audioObj.GetComponent<AudioSource>().PlayOneShot(clip);
        }


        if (!killExisting)
        {
                GameObject.Destroy(audioObj, clip.length); //kill it in a bit.. 
        }
    }


    public void Play(string fileName)
	{
		PlayEx(fileName, 1, 1, false);
	}

    public void Play3D(string fileName, Vector3 vPos)
    {
        Play3DEx(fileName, vPos, 1, 1, false, 0);
    }

    public void Play(AudioClip clip)
    {
        PlayEx(clip, 1, 1, false);
    }

    // Update is called once per frame
//     void Update () 
// 	{
// 	
// 	}
// 	
	
	public static string GetName()
	{
		return Get ().name;
	}
	
	
}
