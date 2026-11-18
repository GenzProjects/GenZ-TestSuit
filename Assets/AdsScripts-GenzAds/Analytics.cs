// GenzMonetization-v1.33
using UnityEngine;
using GameAnalyticsSDK;
using System;
//using Facebook.Unity;

public class Analytics : MonoBehaviour
{
    public static Analytics inst = null;
    
    private int rulesRecallTime = 0;

    public static bool isNewUser = false;

    public string valSt;
    private void Awake()
    {
        if (inst != null && inst != this)
        {
            Destroy(gameObject);
            return;
        }
        else
        {
            inst = this;
            DontDestroyOnLoad(gameObject);
        }
        
        //GameAnalytics
        GameAnalytics.Initialize();
        LogGameAnalyticsInit();
    }
    private void Start()
    {
        StartCoroutine(GenZLog.ProbeServers()); // Adjust + AppMetrica server answer, for the APK Test Suit
        //
        // if (isNewUser)
        //     AbTest();
    }
    

    #region GameAnalytics
    // The build GA tags its events with. With "Send Version" on, GA reads the app's versionName itself.
    private void LogGameAnalyticsInit()
    {
        var settings = GameAnalytics.SettingsGA;
        int i = settings != null ? settings.Platforms.IndexOf(Application.platform) : -1;
        string build = settings == null ? "" : settings.UsePlayerSettingsBuildNumber ? Application.version
                     : i >= 0 && i < settings.Build.Count ? settings.Build[i] : "";
        GenZLog.Result("GameAnalytics", "init", GameAnalytics.Initialized,
            "build=" + build + (settings != null && settings.UsePlayerSettingsBuildNumber ? " (Send Version on)" : " (from GA Settings)"));
    }

    // private void AbTest()
    // {
    //     GameAnalytics.RemoteConfigsUpdated();
    //     if (GameAnalytics.IsRemoteConfigsReady())
    //     {
    //         try
    //         {
    //             //=== Combine Ui Num
    //             string val =
    //             GameAnalytics.GetRemoteConfigsValueAsString("SelV", "a");
    //             if (!string.IsNullOrEmpty(val))
    //             {
    //                 valSt = val;
    //                 if (val == "b")         // Level b ==> Level 1 & 2 Part B
    //                     PlayerPrefs.SetInt("SelArrangPref", 1);
    //                 else if (val == "a")                   // Level a ==> Level 1 & 2 Part A
    //                     PlayerPrefs.SetInt("SelArrangPref", 0);
    //             }
    //             else
    //             {
    //                 PlayerPrefs.SetInt("SelArrangPref", 0);
    //             }
    //         }
    //         catch (Exception ex)
    //         {
    //             Debug.Log("Remoteconfig exception=>" + ex.ToString());
    //             PlayerPrefs.SetInt("SelArrangPref", 0);
    //         }
    //         finally
    //         {
    //             //   GameManager.inst.AbTestResult();
    //             PlayerPrefs.SetInt("SelArrangPref", 0);
    //
    //         }
    //     }
    //     else
    //     {
    //         if (rulesRecallTime < 3)
    //         {
    //             rulesRecallTime += 1;
    //             Invoke(nameof(AbTest), 2f);
    //         }
    //         else
    //         {
    //             PlayerPrefs.SetInt("SelArrangPref", 0);
    //         }
    //     }
       
    // }

    
    #endregion
}