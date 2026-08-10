using System;
using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;

//Stamps the build date into Assets/Resources/build_date.txt at the start of every player build
//(editor Build menu, Build Profiles window, and batchmode alike). RTBuildInfo reads it at runtime.
class BuildTimestampWriter : IPreprocessBuildWithReport
{
    public int callbackOrder => 0;

    public void OnPreprocessBuild(BuildReport report)
    {
        Directory.CreateDirectory("Assets/Resources"); //folder is gitignored, may not exist on a fresh clone
        File.WriteAllText("Assets/Resources/build_date.txt",
            DateTime.Now.ToString("ddd MM/dd/yyyy HH:mm", CultureInfo.InvariantCulture));
        AssetDatabase.Refresh(); //synchronous import so the data build picks up the new asset
    }
}
