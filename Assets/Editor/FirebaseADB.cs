using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEngine;

    static class FirebaseADB
    {
        #region Debug

#if !UNITY_IOS
        [MenuItem("GenZ/Firebase/StartADB", false, 0)]
        public static void Start()
        {
            string DebugCommand = $"/c adb shell setprop debug.firebase.analytics.app {Application.identifier}";

            Process process = new Process();
            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = @DebugCommand,
                WorkingDirectory = @"C:\Users\" + Environment.UserName
            };
            process.StartInfo = startInfo;
            process.Start();

            UnityEngine.Debug.Log("Please Wait... The prompt will close automatically");
        }

        [MenuItem("GenZ/Firebase/StopADB", false, 0)]
        public static void Stop()
        {
            string DebugCommand = $"/c adb shell setprop debug.firebase.analytics.app .none.";

            Process process = new Process();
            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = @DebugCommand,
                WorkingDirectory = @"C:\Users\" + Environment.UserName
            };
            process.StartInfo = startInfo;
            process.Start();

            UnityEngine.Debug.Log("Please Wait... The prompt will close automatically");
        }

     
#endif

        #endregion

      
    }