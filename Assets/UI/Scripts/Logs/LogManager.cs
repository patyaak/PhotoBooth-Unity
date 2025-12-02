using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

public enum LogTypeCustom
{
    CustomerClick,
    Payment,
    Crash,
    Connection
}

public class LogManager : MonoBehaviour
{
    public static LogManager Instance;

    private string logFolderPath;
    private Dictionary<LogTypeCustom, string> logFiles = new Dictionary<LogTypeCustom, string>();

    public Dictionary<LogTypeCustom, List<string>> logCache = new Dictionary<LogTypeCustom, List<string>>();

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
            Init();
        }
        else
        {
            Destroy(gameObject);
        }
    }

    void Init()
    {
        logFolderPath = Application.persistentDataPath + "/Logs/";

        if (!Directory.Exists(logFolderPath))
            Directory.CreateDirectory(logFolderPath);

        logFiles[LogTypeCustom.CustomerClick] = logFolderPath + "customer_click_log.txt";
        logFiles[LogTypeCustom.Payment] = logFolderPath + "payment_log.txt";
        logFiles[LogTypeCustom.Crash] = logFolderPath + "crash_log.txt";
        logFiles[LogTypeCustom.Connection] = logFolderPath + "connection_log.txt";

        foreach (var type in Enum.GetValues(typeof(LogTypeCustom)))
        {
            LogTypeCustom t = (LogTypeCustom)type;
            logCache[t] = new List<string>();

            if (!File.Exists(logFiles[t]))
                File.WriteAllText(logFiles[t], "");
            else
                logCache[t].AddRange(File.ReadAllLines(logFiles[t]));
        }

        Application.logMessageReceived += HandleUnityCrashLog;
    }

    private void HandleUnityCrashLog(string condition, string stackTrace, UnityEngine.LogType type)
    {
        if (type == UnityEngine.LogType.Exception)
        {
            WriteLog(LogTypeCustom.Crash, $"CRASH: {condition} --- {stackTrace}");
        }
    }

    public void WriteLog(LogTypeCustom type, string message)
    {
        string logEntry = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} => {message}";

        File.AppendAllText(logFiles[type], logEntry + Environment.NewLine);
        logCache[type].Add(logEntry);
    }

    public string GetLogs(LogTypeCustom type)
    {
        return string.Join("\n", logCache[type]);
    }
}
