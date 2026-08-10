using UnityEngine;

public static class RTBuildInfo
{
    static string _timestamp;

    public static string Timestamp
    {
        get
        {
            if (_timestamp == null)
            {
#if UNITY_EDITOR
                //scripts just recompiled, so "now" is the honest build time in the editor
                _timestamp = System.DateTime.Now.ToString("ddd MM/dd/yyyy HH:mm",
                    System.Globalization.CultureInfo.InvariantCulture);
#else
                var ta = Resources.Load<TextAsset>("build_date");
                _timestamp = ta != null ? ta.text.Trim() : "unknown";
#endif
            }
            return _timestamp;
        }
    }
}
