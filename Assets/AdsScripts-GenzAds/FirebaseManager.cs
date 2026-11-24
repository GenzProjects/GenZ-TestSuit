// GenzMonetization-v1.33
using Firebase;
using Firebase.Analytics;
using Firebase.Extensions;
using Firebase.RemoteConfig;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using UnityEngine;

public class FirebaseManager : MonoBehaviour
{


    public static FirebaseManager Instance;
    
    
    public static bool isDataFetch = false;
    public static bool isFetchCompleted = false;
    public static event Action<bool> OnRemoteDataFetch;
    public static void NotifyRemoteDataFetch(bool success)
    {
        isDataFetch = success;
        isFetchCompleted = true;
        OnRemoteDataFetch?.Invoke(success);
    }
    
    
    // Remote Config keys, defaults and values are in RemoteConfigManager (per game); this script is the same in every game.

    private void Awake()
    {
        if (Instance != null)
            return;
        DontDestroyOnLoad(gameObject);
        Instance = this;
        
        OnFireBase();
    }

    #region Firebase

    private DependencyStatus _dependencyStatus = DependencyStatus.UnavailableOther;
    private bool _firebaseInitialized = false;
    private void OnFireBase()
    {
            FirebaseApp.CheckAndFixDependenciesAsync().ContinueWithOnMainThread(task =>
            {
                _dependencyStatus = task.Result;
                GenZLog.Result("Firebase", "init", _dependencyStatus == DependencyStatus.Available, _dependencyStatus.ToString());
                if (_dependencyStatus == DependencyStatus.Available)
                {
                    InitializeFirebase();
                }
                else
                {
                    NotifyRemoteDataFetch(false);
                    //Debug.LogError("Could not resolve all Firebase dependencies: " + _dependencyStatus);
                }
            });
    }
    

    private void InitializeFirebase()
    {
       
            //Debug.Log("Enabling data collection.");
            FirebaseAnalytics.SetAnalyticsCollectionEnabled(true);

            //Debug.Log("Set user properties.");
            // Set the user's sign up method.
            FirebaseAnalytics.SetUserProperty(
                FirebaseAnalytics.UserPropertySignUpMethod,
                "Google");
            // Set the user ID.
            //  FirebaseAnalytics.SetUserId("uber_user_510");
            // Set default session duration values.
            //  FirebaseAnalytics.SetSessionTimeoutDuration(new TimeSpan(0, 30, 0));
            _firebaseInitialized = true;
            var app = FirebaseApp.DefaultInstance;
            // Used before the first fetch and for keys missing from the console (RemoteConfigManager.Defaults).
            var defaults = RemoteConfigManager.Defaults();

            Firebase.RemoteConfig.FirebaseRemoteConfig.DefaultInstance.SetDefaultsAsync(defaults)
                .ContinueWithOnMainThread(task =>
                {
                    FetchDataAsync();
                });
            
            #if UNITY_ANDROID
            if (FirebaseMessagingManager.Instance != null)
                FirebaseMessagingManager.Instance.InitializeFCM();
            #endif

       
    }
    
    public void RefreshRemoteConfig()
    {
        if (_firebaseInitialized)
            FetchDataAsync();
    }

    
    public void ReportEvent(string eventName)
    {
        //Debug.Log(_firebaseInitialized + " " + eventName);
        if (_firebaseInitialized)
        {
            FirebaseAnalytics.LogEvent(eventName);
        }
    }
    public void ReportEvent(string eventName, string parameterName, string parameterValue)
    {
        if (_firebaseInitialized)
        {
            FirebaseAnalytics.LogEvent(eventName, parameterName, parameterValue);
        }
    }

    private Task FetchDataAsync()
    {
        //Debug.Log("Fetching data...");
        var fetchTask =
            Firebase.RemoteConfig.FirebaseRemoteConfig.DefaultInstance.FetchAsync(
                TimeSpan.Zero);
        return fetchTask.ContinueWithOnMainThread(FetchComplete);
    }

    private void FetchComplete(Task fetchTask)
    {
       
            if (fetchTask.IsCanceled)
            {
                NotifyRemoteDataFetch(false);
            }
            else if (fetchTask.IsFaulted)
            {
                NotifyRemoteDataFetch(false);
            }
            else if (fetchTask.IsCompleted)
            {
                Debug.Log("Fetch completed successfully!");
            }

            var info = Firebase.RemoteConfig.FirebaseRemoteConfig.DefaultInstance.Info;
            switch (info.LastFetchStatus)
            {
                case Firebase.RemoteConfig.LastFetchStatus.Success:

                    Firebase.RemoteConfig.FirebaseRemoteConfig.DefaultInstance.ActivateAsync()
                        .ContinueWithOnMainThread(task =>
                        {
                            //Debug.Log($"Remote data loaded and ready (last fetch time {info.FetchTime}).");
                            GenZLog.Ok("RemoteConfig", "init", "fetched and activated");
                            RemoteConfigManager.LoadValues(Firebase.RemoteConfig.FirebaseRemoteConfig.DefaultInstance);
                        });

                    break;
                case Firebase.RemoteConfig.LastFetchStatus.Failure:
                    GenZLog.Fail("RemoteConfig", "init", "fetch failed: " + info.LastFetchFailureReason);
                    switch (info.LastFetchFailureReason)
                    {
                        case Firebase.RemoteConfig.FetchFailureReason.Error:
                            //Debug.Log("Fetch failed for unknown reason");
                            //defaultRemoteDate();
                            break;
                        case Firebase.RemoteConfig.FetchFailureReason.Throttled:
                            //Debug.Log("Fetch throttled until " + info.ThrottledEndTime);
                            break;
                    }
                    NotifyRemoteDataFetch(false);
                    break;
                case Firebase.RemoteConfig.LastFetchStatus.Pending:
                    //Debug.Log("Latest Fetch call still pending.");
                    GenZLog.Info("RemoteConfig", "init", "fetch still pending");
                    // Not finished: don't report it. A later fetch reports the result, or the waiting screen's timeout moves on.
                    break;
            }
      
    }

    #endregion


    private ConsentStatus _consentStatus;
    Dictionary<ConsentType, ConsentStatus> consentdata = new Dictionary<ConsentType, ConsentStatus>();
    public void SendFirebaseConsentDetail(char ad_Personalization)
    {
        _consentStatus = (ad_Personalization == '1') ? (ConsentStatus.Granted) : (ConsentStatus.Denied);
        consentdata.Add(ConsentType.AnalyticsStorage, _consentStatus);

        _consentStatus = (ad_Personalization == '1') ? (ConsentStatus.Granted) : (ConsentStatus.Denied);
        consentdata.Add(ConsentType.AdStorage, _consentStatus);

        _consentStatus = (ad_Personalization == '1') ? (ConsentStatus.Granted) : (ConsentStatus.Denied);
        consentdata.Add(ConsentType.AdUserData, _consentStatus);

        _consentStatus = (ad_Personalization == '1') ? (ConsentStatus.Granted) : (ConsentStatus.Denied);
        consentdata.Add(ConsentType.AdPersonalization, _consentStatus);

        FirebaseAnalytics.SetConsent(consentdata);
    }

    #region TaicheCodes
    public bool Taiche1_0 = false;
    public bool Taiche2_0 = true;

    double custom_revenue;
    double currentAccumulatedRevenue;

    public void CheckTheRevenue(double _rev)
    {

        if (Taiche1_0 == false)
        {
            return;
        }

        Debug.Log("RevenueAds:" + _rev);
        if (!double.TryParse(PlayerPrefs.GetString("revenue_add", "0"), NumberStyles.Float, CultureInfo.InvariantCulture, out custom_revenue))
        {
            custom_revenue = 0.0;
        }
        currentAccumulatedRevenue = (custom_revenue + _rev);
        Debug.Log("RevenueAds!:" + currentAccumulatedRevenue);
        Debug.Log("RevenueAds!:" + currentAccumulatedRevenue + ":" + RemoteConfigManager.revenue_maxValue);

        if (currentAccumulatedRevenue >= RemoteConfigManager.revenue_maxValue)
        {
            var impressionParameters = new[] {
                new Parameter(FirebaseAnalytics.ParameterValue, currentAccumulatedRevenue),
                new Parameter(FirebaseAnalytics.ParameterCurrency, "USD"),
            };
            FirebaseAnalytics.LogEvent("custom_ad_impression", impressionParameters);
            //if (AMAnalytics.inst != null)
            //{
            //    AMAnalytics.inst.ReportEvent("custom_ad_impression", currentAccumulatedRevenue.ToString());
            //}
            Debug.Log("RevenueAds!:" + currentAccumulatedRevenue + ":" + RemoteConfigManager.revenue_maxValue);

            PlayerPrefs.SetString("revenue_add", "0");
        }
        else
        {
            PlayerPrefs.SetString("revenue_add", currentAccumulatedRevenue.ToString(CultureInfo.InvariantCulture));
        }

    }


    [HideInInspector] public int impressionCount = 0;
    [HideInInspector] public double custom_revenue_forImpressrion = 0.0;
    [HideInInspector] public double currentAccumulatedRevenue_OnImpression = 0.0;

    public void CheckTheImpressionForRevenue(double _rev)
    {
        if (Taiche2_0 == false)
        {
            return;
        }

        Debug.Log("RevenueAds:" + _rev);
        impressionCount = PlayerPrefs.GetInt("ImpressionCount");
        impressionCount++;
        if (!double.TryParse(PlayerPrefs.GetString("revenue_add_OnImpression", "0"), NumberStyles.Float, CultureInfo.InvariantCulture, out custom_revenue_forImpressrion))
        {
            custom_revenue_forImpressrion = 0.0;
        }
        currentAccumulatedRevenue_OnImpression = (custom_revenue_forImpressrion + _rev);
        if (impressionCount >= RemoteConfigManager.impressionValue_ForRevenue)
        {
            var impressionParameters = new[] {
           new Parameter(FirebaseAnalytics.ParameterValue, currentAccumulatedRevenue_OnImpression),
           new Parameter(FirebaseAnalytics.ParameterCurrency, "USD"),
       };
            string st = "count_ad_impression";
            FirebaseAnalytics.LogEvent(st, impressionParameters);
            // if (AMAnalytics.inst != null)
            // {
            //     AMAnalytics.inst.ReportEvent(st, currentAccumulatedRevenue.ToString());
            // }
            Debug.Log("RevenueAds!:" + custom_revenue_forImpressrion.ToString() + ":" + RemoteConfigManager.impressionValue_ForRevenue);

            PlayerPrefs.SetString("revenue_add_OnImpression", "0");
            impressionCount = 0;
            PlayerPrefs.SetInt("ImpressionCount", impressionCount);
        }
        else
        {
            PlayerPrefs.SetString("revenue_add_OnImpression", currentAccumulatedRevenue_OnImpression.ToString(CultureInfo.InvariantCulture));
            PlayerPrefs.SetInt("ImpressionCount", impressionCount);
        }

    }

    #endregion

}

[Serializable]
public class LevelData
{
    public List<int> Levels;
}