// GenzMonetization-v1.33
using AdjustSdk;
using GameAnalyticsSDK;
using GoogleMobileAds.Api;
using GoogleMobileAds.Common;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using static AppmetricaAnalytics;
using static UnityEngine.UI.CanvasScaler;

public class AppOpen_Code : MonoBehaviour
{
    bool APPopendelay = false;

    private void Awake()
    {
        AppStateEventNotifier.AppStateChanged += OnAppStateChanged;
    }
    private void OnDestroy()
    {
        // Always unlisten to events when complete.
        AppStateEventNotifier.AppStateChanged -= OnAppStateChanged;
    }

    public void CallingAppOpen()
    {
        StartCoroutine(LoadAndCallFirstAd());
    }

    IEnumerator LoadAndCallFirstAd()
    {
        yield return new WaitForSeconds(0.5f);
        LoadOpenApp();

    }


    private AppOpenAd _ad;
    private bool isShowingAd = false;
    public string AppOpenId;

    [Header("Screen Orientation")]
    public ScreenOrientation screenOrientation;
    private readonly TimeSpan TIMEOUT = TimeSpan.FromHours(4);
    private DateTime _expireTime;
    public void LoadOpenApp()
    {
        if (PlayerPrefs.GetInt("AdsStop") == 1) return;

        // Clean up the old ad before loading a new one.
        if (_ad != null)
        {
            _ad.Destroy();
            _ad = null;
        }


        // Create our request used to load the ad.
        var adRequest = new AdRequest();

        // send the request to load the ad.
        AppOpenAd.Load(AppOpenId, adRequest,
            (AppOpenAd ad, LoadAdError error) =>
            {
                // if error is not null, the load request failed.
                if (error != null)
                {
                    Invoke(nameof(LoadOpenApp), 5f);
                    return;
                }

                _ad = ad;
                _expireTime = DateTime.Now + TIMEOUT;
                RegisterEventHandlers(ad);

                GameAnalyticsILRD.SubscribeAdMobImpressions(AppOpenId, _ad);
            });
    }

    public void ShowAppOpenAdIfAvailable()
    {
        if (!AdMob._instance.isPausedDuetoAd)
        {
            if (_ad == null || isShowingAd || !IsAppOpenAdTimedOut)
            {
                if (_ad == null || !IsAppOpenAdTimedOut)
                {
                    Invoke(nameof(LoadOpenApp), 5f);
                }

                return;
            }
            _ad.Show();

        }
        else
        {
            AdMob._instance.isPausedDuetoAd = false;
        }
    }

    private void OnAppStateChanged(AppState state)
    {
        Debug.Log("App State changed to : " + state);

        // if the app is Foregrounded and the ad is available, show it.
        if (state == AppState.Foreground)
        {
            if (PlayerPrefs.GetInt("AdsStop") == 0 && PlayerPrefs.GetInt("RemoveAds") == 0)
                ShowAppOpenAdIfAvailable();
        }
    }


    private void RegisterEventHandlers(AppOpenAd ad)
    {
        // Raised when an ad opened full screen content.
        ad.OnAdFullScreenContentOpened += () =>
        {
            isShowingAd = true;
        };
        // Raised when the ad closed full screen content.
        ad.OnAdFullScreenContentClosed += () =>
        {
            // Set the ad to null to indicate that AppOpenAdManager no longer has another ad to show.
            _ad = null;
            isShowingAd = false;
            LoadOpenApp();
        };
        // Raised when the ad failed to open full screen content.
        ad.OnAdFullScreenContentFailed += (AdError error) =>
        {

            // Set the ad to null to indicate that AppOpenAdManager no longer has another ad to show.
            _ad = null;
            LoadOpenApp();
        };

        ad.OnAdPaid += (AdValue adValue) =>
        {
            print(String.Format("AppOpen view paid {0} {1}.",
            adValue.Value,
            adValue.CurrencyCode));
            paidEvent(adValue, AppmetricaAnalytics.AdFormat.AppOpen, AppOpenId);
        };
    }
    //public void OnApplicationPause(bool paused)
    //{
    //    // Display the app open ad when the app is foregrounded
    //    if (!paused && !APPopendelay)
    //    {
    //        if (PlayerPrefs.GetInt("AdsStop") == 0 && PlayerPrefs.GetInt("RemoveAds") == 0)
    //            ShowAppOpenAdIfAvailable();
    //    }
    //}

    private bool IsAppOpenAdTimedOut
    {
        get
        {
            return _ad != null
                   && DateTime.Now < _expireTime;
        }
    }

    public void paidEvent(AdValue adValue, AppmetricaAnalytics.AdFormat adFormat, string idUnit)
    {
        //ADJUST
        double revenue = adValue.Value / 1000000f;
        AdjustAdRevenue adjustAdRevenue = new AdjustAdRevenue("admob_sdk");
        adjustAdRevenue.SetRevenue(revenue, "USD");
        Adjust.TrackAdRevenue(adjustAdRevenue);
        GenZLog.Ok("Ads", "impression", "AdMob " + adFormat);
        GenZLog.Call("Adjust", "ad_revenue", "AdMob " + adFormat);

        //FIREBASE
        var impressionParameters = new[] {
      new Firebase.Analytics.Parameter("ad_platform", "AdMob"),
      new Firebase.Analytics.Parameter("value", revenue),
      new Firebase.Analytics.Parameter("currency", "USD"), // All AppLovin revenue is sent in USD
      };
        Firebase.Analytics.FirebaseAnalytics.LogEvent("ad_impression", impressionParameters);

        FirebaseManager.Instance.CheckTheRevenue(revenue);
        FirebaseManager.Instance.CheckTheImpressionForRevenue(revenue);


        // Appmetrica

        AppmetricaAnalytics.ReportRevenue_Admob(adValue, adFormat, idUnit);
    }
}

