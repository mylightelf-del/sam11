using System;
using System.IO;
using UnityEngine;

namespace Sam11.Unity
{
    /// <summary>오류를 persistentDataPath/logs/에 남긴다 (버그 제보 때 파일을 받는다). 하루 한 파일.</summary>
    public static class ErrorLog
    {
        private static string _path;

        public static void Install()
        {
            if (_path != null) return;
            string dir = Path.Combine(Application.persistentDataPath, "logs");
            Directory.CreateDirectory(dir);
            _path = Path.Combine(dir, $"sam11_{DateTime.Now:yyyyMMdd}.log");
            Application.logMessageReceived += OnLog;
        }

        private static void OnLog(string message, string stack, LogType type)
        {
            if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert) return;
            try { File.AppendAllText(_path, $"[{DateTime.Now:HH:mm:ss}] {type}: {message}\n{stack}\n"); }
            catch (IOException) { }
        }
    }
}
