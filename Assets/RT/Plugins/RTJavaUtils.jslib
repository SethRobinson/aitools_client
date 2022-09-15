//RTJavaUtils.jslib by Seth A. Robinson ( codedojo.com )

mergeInto(LibraryManager.library, 
{

  //Here do some fancy javascript magic to hook addEventListener('click'), run our Unity function, then stop listening.  It stops the browser from
  //blocking new browser windows.
  //Note: This will only work if it's called from a button down, not a normal GUI onclick()!  Use RTButton instead of Button so there's a OnPointerDown() callback
  //To avoid having to do the [DllImport("__Internal")] crap, you can call this with RTUtil.PopupUnblockSendMessage(callbackObjectStr,callbackFunctionStr,callbackParmStr) instead
  
	JLIB_PopupUnblockSendMessage: function (callbackObjectStr,callbackFunctionStr,callbackParmStr) 
  {
    var objectStr = Pointer_stringify(callbackObjectStr);
    var functionStr = Pointer_stringify(callbackFunctionStr);
    var parmStr = Pointer_stringify(callbackParmStr);
  
    //console.log("JLIB_PopupUnblockSendMessage: Got object named "+objectStr+" to run function "+functionStr+" with optional parm of '"+parmStr+"'");
  
    var OpenPopup = function() 
    {
      //run the thing we're supposed to
      gameInstance.SendMessage(objectStr, functionStr,parmStr);
      //unhook this, we're done
	    document.getElementById('#canvas').removeEventListener('click', OpenPopup);
    };
    
    var el = document.getElementById('#canvas');
    if (el)
    {
	    document.getElementById('#canvas').addEventListener('click', OpenPopup, false);
	  } else
	  {
	     console.log("Can't find a #canvas element. Is it just called canvas instead or something?  Check RTFacebook.jslib.  Calling without popup safety.");
	     gameInstance.SendMessage(objectStr, functionStr,parmStr);
	  }
   
  },
  
  
  JLIB_PopupUnblockOpenURL: function (URLStr) 
  {
    var url = Pointer_stringify(URLStr);
  
    var OpenPopup = function() 
    {
      //run the thing we're supposed to
      window.open(url);
      //unhook this, we're done
	    document.getElementById('#canvas').removeEventListener('click', OpenPopup);
    };
    
    var el = document.getElementById('#canvas');
    if (el)
    {
	    document.getElementById('#canvas').addEventListener('click', OpenPopup, false);
	  } else
	  {
	     console.log("Can't find a #canvas element. Is it just called canvas instead or something?  Check RTJavaUtils.jslib.  Calling without popup safety.");
	      window.open(url);
	  }
   
  },
  
   JLIB_SyncFiles : function()
   {
   		 console.log("Syncing java filesystem");
    
       FS.syncfs(false,function (err) 
       {
            // handle callback
       });
   },


//Use RTUtil.IsOnMobile() to use this

   JLIB_IsOnMobile : function()
   {
   	if (/iPhone|iPad|iPod|Android/i.test(navigator.userAgent)) 
	{
		 console.log("On Mobile");
 		return true;
	} 
	
	console.log("Not on mobile");
	return false;
   },



  
});