// GenzMonetization-v1.33
using AdjustSdk;
using GameAnalyticsSDK;
using GoogleMobileAds;
using GoogleMobileAds.Api;
using GoogleMobileAds.Common;
using System;
using UnityEngine;

public class AdMob : MonoBehaviour
{
    static BannerView bannerView;
    static InterstitialAd interstitial;
    static RewardedAd rewardedAd;
    public bool isTestIdOn;
    string test_bannerID = "ca-app-pub-3940256099942544/6300978111";
    string test_interstitialID = "ca-app-pub-3940256099942544/1033173712";
    string test_rewardID = "ca-app-pub-3940256099942544/5224354917";

    public string bannerID = "ca-app-pub-3940256099942544/6300978111";
    public string interstitialID = "ca-app-pub-3940256099942544/1033173712";
    public string rewardID = "ca-app-pub-3940256099942544/5224354917";

    public AdSize adSize;
    public AdPosition adPosition;

    public static AdMob _instance;
    public GameObject callBackObject;

    //Action _callback;
    public AppOpen_Code _appOpen;
    private void Awake()
    {
        if (_instance == null)
            _instance = this;

        _appOpen = GetComponent<AppOpen_Code>();
    }
    void Start()
    {
        if (PlayerPrefs.GetInt("AdsStop") == 1) return;

        MobileAds.SetiOSAppPauseOnBackground(true);
        MobileAds.RaiseAdEventsOnUnityMainThread = true;
        
        MobileAds.Initialize(initStatus =>
        {
            GenZLog.Result("AdMob", "init", initStatus != null);
            if (initStatus != null)
                foreach (var adapter in initStatus.getAdapterStatusMap())
                    GenZLog.Result("AdMob", "adapter", adapter.Value.InitializationState == AdapterState.Ready, adapter.Key + " " + adapter.Value.Description);

            if (PlayerPrefs.GetInt("RemoveAds") == 0)
            {
                RequestBanner();
                RequestInterstitial();
                _appOpen.CallingAppOpen();
            }
            RequesRewardAd();


        });
    }

    public void RequestBanner()
    {
            if (bannerView != null)
            {
                bannerView.Destroy();
            }
            bannerView = new BannerView(isTestIdOn == false ? bannerID : test_bannerID, AdSize.Banner, adPosition);
            ListenToAdEvents();
            var adRequest = new AdRequest();
            bannerView.LoadAd(adRequest);

            GameAnalyticsILRD.SubscribeAdMobImpressions(bannerID, bannerView);
    }

    // public void BannerTop()
    // {
    //     try
    //     {
    //         if (bannerView != null)
    //         {
    //             bannerView.SetPosition(AdPosition.Top);
    //         }
    //     }
    //     catch { }
    //
    //
    // }
    // public void BannerBottom()
    // {
    //     try
    //     {
    //         if (bannerView != null)
    //         {
    //             bannerView.SetPosition(AdPosition.Bottom);
    //         }
    //     }
    //     catch { }
    //
    //
    // }
    private void ListenToAdEvents()
    {
        bannerView.OnBannerAdLoadFailed += (LoadAdError error) =>
        {
            bannerHide();
            bannerRetryPending = true; // Update requests a new banner, so there's a fallback when MAX's banner fails
        };
        bannerView.OnAdPaid += (AdValue adValue) =>
        {
            paidEvent(adValue, AppmetricaAnalytics.AdFormat.Banner, bannerID, "");
        };
        bannerView.OnBannerAdLoaded += () =>                                     
        {                                                                        
            if (AdsManager.instance != null &&                                   
                AdsManager.instance.isBannerShowing)                                     
                bannerView.Hide();                                               
        }; 
    }

    public void bannerShow()
    {
        if (PlayerPrefs.GetInt("RemoveAds") == 1) return;
        if (PlayerPrefs.GetInt("AdsStop") == 1) return;

        try
        {
            if (AdsManager.instance.isBannerShowing)
            {
                if (bannerView != null)
                {
                    bannerView.Hide();
                }
                return;
            }

            if (bannerView != null)
            {
                bannerView.Show();
            }
            else
            {
                if (PlayerPrefs.GetInt("RemoveAds") == 0)
                    RequestBanner();
            }

        }
        catch { }


    }
    public void bannerHide()
    {
        if (PlayerPrefs.GetInt("AdsStop") == 1) return;

        try
        {
            if (bannerView != null)
                bannerView.Hide();
        }
        catch { }


    }

    AdRequest interstitialrequest;
    private volatile bool interRetryPending = false;
    private volatile bool rewardRetryPending = false;
    private volatile bool bannerRetryPending = false;

    private void Update()
    {
        // Load callback may arrive off the main thread, so schedule the retry here
        if (interRetryPending)
        {
            interRetryPending = false;
            Invoke(nameof(RequestInterstitial), 5f);
        }
        if (rewardRetryPending)
        {
            rewardRetryPending = false;
            Invoke(nameof(RequesRewardAd), 5f);
        }
        if (bannerRetryPending)
        {
            bannerRetryPending = false;
            // RequestBanner itself doesn't check these, so a bought "Remove Ads" doesn't bring the banner back
            if (PlayerPrefs.GetInt("RemoveAds") == 0 && PlayerPrefs.GetInt("AdsStop") == 0)
                Invoke(nameof(RequestBanner), 10f); // banners: a longer gap between retries
        }
    }

    public void RequestInterstitial()
    {
        if (PlayerPrefs.GetInt("AdsStop") == 1) return;
        
            if (interstitial != null)
            {
                interstitial.Destroy();
                interstitial = null;
            }
            var adRequest = new AdRequest();
            InterstitialAd.Load(isTestIdOn == false ? interstitialID : test_interstitialID, adRequest,
                (InterstitialAd ad, LoadAdError error) =>
                {
                    if (error != null || ad == null)
                    {
                        interRetryPending = true;
                        return;
                    }
                    interstitial = ad;
                    InterstitialRegisterEventHandlers(interstitial);

                    GameAnalyticsILRD.SubscribeAdMobImpressions(interstitialID, interstitial);
                });

    }

    private void InterstitialRegisterEventHandlers(InterstitialAd interstitialAd)
    {
        interstitialAd.OnAdPaid += (AdValue adValue) =>
        {
            paidEvent(adValue, AppmetricaAnalytics.AdFormat.Interstitial, interstitialID, "");
        };
        interstitialAd.OnAdFullScreenContentClosed += () =>
        {
            RequestInterstitial();
            AdsGapTimer.instance.ResetTimer();
        };
        // Show failed (e.g. the ad expired): Closed never comes, so reload the same way as a failed load
        interstitialAd.OnAdFullScreenContentFailed += (AdError error) =>
        {
            AdsManager.instance.isPausedDuetoAd = false; // nothing was shown
            interRetryPending = true;
        };
    }

    public void ShowInterstialAd()
    {
        if (PlayerPrefs.GetInt("RemoveAds") == 1) return;
        if (PlayerPrefs.GetInt("AdsStop") == 1) return;

      
            if (interstitial != null)
            {
                interstitial.Show();
                AdsManager.instance.isPausedDuetoAd = true;
            }

    }

    public bool HasAdmobInterstialAvaible()
    {
        if (PlayerPrefs.GetInt("AdsStop") == 1) return false;

        if (interstitial != null && interstitial.CanShowAd())
        {
            return true;
        }
        RequestInterstitial();

        return false;
    }

    static AdRequest requestReward;
    public void RequesRewardAd()
    {
        if (PlayerPrefs.GetInt("AdsStop") == 1) return;

        try
        {
            if (rewardedAd != null)
            {
                rewardedAd.Destroy();
                rewardedAd = null;
            }
            var adRequest = new AdRequest();
            RewardedAd.Load(isTestIdOn == false ? rewardID : test_rewardID, adRequest,
                (RewardedAd ad, LoadAdError error) =>
                {
                    if (error != null || ad == null)
                    {
                        rewardRetryPending = true;
                        return;
                    }
                    rewardedAd = ad;
                    RewardedRegisterEventHandlers(rewardedAd);

                    GameAnalyticsILRD.SubscribeAdMobImpressions(rewardID, rewardedAd);
                });
        }
        catch { }

    }

    private void RewardedRegisterEventHandlers(RewardedAd ad)
    {
        ad.OnAdPaid += (AdValue adValue) =>
        {
            paidEvent(adValue, AppmetricaAnalytics.AdFormat.Rewarded, rewardID, "");
        };
        ad.OnAdFullScreenContentClosed += () =>
        {
            DelayRequest();
        };
        // Show failed (e.g. the ad expired): Closed never comes, so reload the same way as a failed load
        ad.OnAdFullScreenContentFailed += (AdError error) =>
        {
            AdsManager.instance.isPausedDuetoAd = false; // nothing was shown
            rewardRetryPending = true;
        };
    }

    public void ShowRewardAd()
    {
        if (PlayerPrefs.GetInt("AdsStop") == 1) return;

        if (rewardedAd == null)
        {
            return;
        }

        if (rewardedAd.CanShowAd())
            {
                rewardedAd.Show((Reward reward) =>
                {
                    isreward = true;
                    AdsManager.instance._rewarded?.Invoke();
                    AdsGapTimer.instance.ResetTimer();
                    Invoke(nameof(DelayRequest), 0.3f);
                });
                AdsManager.instance.isPausedDuetoAd = true;

                GA_Aanalytics.GA_RewardedEventADMOB();
            }
            else
            {

                GameAnalytics.NewAdEvent(GAAdAction.FailedShow, GAAdType.RewardedVideo, "ADMOB", "Rewarded");
                GameAnalytics.NewAdEvent(GAAdAction.FailedShow, GAAdType.RewardedVideo, "BOTH", "Rewarded");
                RequesRewardAd();
            }
      
    }

    public bool HasRewardedAvaiable()
    {
        if (PlayerPrefs.GetInt("AdsStop") == 1) return false;

        if (rewardedAd == null)
        {
            return false;
        }

        if (!rewardedAd.CanShowAd())
        {
                RequesRewardAd();
        }
        return rewardedAd.CanShowAd();
    }

    #region RewardedAd callback handlers
    bool isreward;
    void DelayRequest()
    {
        if (PlayerPrefs.GetInt("AdsStop") == 1) return;

        try
        {
            RequesRewardAd();
        }
        catch { }

    }
    #endregion
    public void DestroyBanner()
    {
        try
        {
            if (bannerView != null)
                bannerView.Destroy();
        }
        catch { }
    }

    public void paidEvent(AdValue adValue, AppmetricaAnalytics.AdFormat _adFormat, string _adId, string _placementName)
    {
        //ADJUST
        double revenue = adValue.Value / 1000000f;
        AdjustAdRevenue adjustAdRevenue = new AdjustAdRevenue("admob_sdk");
        adjustAdRevenue.SetRevenue(revenue, "USD");
        Adjust.TrackAdRevenue(adjustAdRevenue);
        GenZLog.Ok("Ads", "impression", "AdMob " + _adFormat);
        GenZLog.Call("Adjust", "ad_revenue", "AdMob " + _adFormat);

        //FIREBASE
        var impressionParameters = new[] {
         new Firebase.Analytics.Parameter("ad_platform", "AdMob"),
         new Firebase.Analytics.Parameter("value", revenue),
         new Firebase.Analytics.Parameter("currency", "USD"), // All AppLovin revenue is sent in USD
         };
        Firebase.Analytics.FirebaseAnalytics.LogEvent("ad_impression", impressionParameters);


        FirebaseManager.Instance.CheckTheRevenue(revenue);
        FirebaseManager.Instance.CheckTheImpressionForRevenue(revenue);


        //APP METRICA
        AppmetricaAnalytics.ReportRevenue_Admob(adValue, _adFormat, _adId, _placementName);

        Debug.Log("Admob Rev: " + revenue);

        if (_adFormat == AppmetricaAnalytics.AdFormat.Interstitial)
        {
            GA_Aanalytics.GA_IntEvent_ADMOB();
        }
        else if (_adFormat == AppmetricaAnalytics.AdFormat.Rewarded)
        {
            GA_Aanalytics.GA_RewardedEventADMOB();
        }
    }
}
