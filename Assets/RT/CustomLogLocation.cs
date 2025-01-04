using UnityEngine;
using System.IO;

public class CustomLogLocation : MonoBehaviour
{
    private string logFilePath;
    private StreamWriter logWriter;

    void Awake()
    {
        // Get the directory where the executable is located
        //string exeDirectory = Path.GetDirectoryName(Application.dataPath);
        string exeDirectory = "";
        logFilePath = Path.Combine(exeDirectory, "log.txt");

        // Create or clear the log file
        logWriter = new StreamWriter(logFilePath, false);
        logWriter.AutoFlush = true;

        // Add custom log handler
        Application.logMessageReceived += HandleLog;

        Debug.Log($"Log file initialized at: {logFilePath}");
    }

    void HandleLog(string logString, string stackTrace, LogType type)
    {
        // Format the log message with timestamp
        string timeStamp = System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        string formattedMessage = $"[{timeStamp}] [{type}] {logString}";

        // Add stack trace for errors and exceptions
        if (type == LogType.Error || type == LogType.Exception)
        {
            formattedMessage += $"\nStack Trace:\n{stackTrace}";
        }

        // Write to the log file
        logWriter.WriteLine(formattedMessage);
    }

    void OnDisable()
    {
        // Clean up
        Application.logMessageReceived -= HandleLog;
        logWriter?.Close();
        logWriter?.Dispose();
    }
}