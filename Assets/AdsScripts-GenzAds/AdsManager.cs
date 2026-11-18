// GenzMonetization-v1.33
using AdjustSdk;
using GameAnalyticsSDK;
using GoogleMobileAds.Api;
using System;
using System.Collections.Generic;
using UnityEngine;

public class AdsManager : MonoBehaviour
{
    public bool isPausedDuetoAd = false;
    public static AdsManager instance = null;

    [SerializeField] private string maxSdkKey = "";
    
    [SerializeField] string InterstitialAdUnitId = "";
    [SerializeField] string RewardedAdUnitId = "";
    [SerializeField] string BannerAdUnitId = "";
    [SerializeField] string MRecAdUnitId = "";
    
    [SerializeField] MaxSdkBase.BannerPosition bannerPosition;

    private int interRequestTime = 0;
    private int rewardRequestTime = 0;

    public bool isBannerShowing;

    public bool videWatchedFromMaxSdk = true;

    // Network order for Interstitial and Rewarded:
    // 0 = MAX first on every show, AdMob as fallback
    // 1 = one by one: MAX, AdMob, MAX, AdMob...
    // 2 = MAX, MAX, AdMob, MAX, MAX, AdMob...
    // On a MAX turn AdMob is the fallback, on an AdMob turn MAX is the fallback.
    public int useSdkVendorSwitch = 0;
    private int interShowCount = 0;
    private int rewardedShowCount = 0;

    private Action<bool> Callback
    {
        get;
        set;
    }
    public static bool bannerShowing = false;

    public delegate void Rewarded();
    public Rewarded _rewarded;
    public bool IsVideoLoaded
    {
        get
        {
            if (!isMaxInitialized || !MaxSdk.IsRewardedAdReady(RewardedAdUnitId))
                return false;
            else
                return true;
        }
    }
    private bool isMaxInitialized = false;

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }
        else
        {
            instance = this;
            DontDestroyOnLoad(this.gameObject);
        }
        Invoke(nameof(LoadAds), 0.01f);
    }

    private void LoadAds()
    {
        if (PlayerPrefs.GetInt("MaxAdStop") == 1) return;

        //AdSettings.SetDataProcessingOptions(new string[] { "LDU" }, 1, 1000);
        
        MaxSdkCallbacks.OnSdkInitializedEvent += (MaxSdkBase.SdkConfiguration sdkConfiguration) =>
        {
            GenZLog.Result("MAX", "init", sdkConfiguration.IsSuccessfullyInitialized);
            isMaxInitialized = true;
            if (PlayerPrefs.GetInt("RemoveAds") == 0)
            {
                InitializeInterstitialAds();
                InitializeBannerAds();
                InitializeMRecAds();
                //Invoke(nameof(ShowBanner), 1);
            }
            InitializeRewardedAds();
            GameAnalyticsILRD.SubscribeMaxImpressions();
        };
        MaxSdk.SetSdkKey(maxSdkKey);
        MaxSdk.InitializeSdk();
    }

    public void SelfDestroy()
    {
        instance = null;
        Destroy(this.gameObject);
    }
    public void RequestInter()
    {
        if (PlayerPrefs.GetInt("MaxAdStop") == 1) return;
        
            if (!isMaxInitialized)
                return;

            if (PlayerPrefs.GetInt("RemoveAds") == 0)
            {
                if (!MaxSdk.IsInterstitialReady(InterstitialAdUnitId))
                    LoadInterstitial();
            }
    }
    public void RequestVideo()
    {
        if (PlayerPrefs.GetInt("MaxAdStop") == 1) return;

            if (!isMaxInitialized)
                return;
            if (!MaxSdk.IsRewardedAdReady(RewardedAdUnitId))
                LoadRewardedAd();
    }
    public void MuteAudio()
    {
        if (PlayerPrefs.GetInt("MaxAdStop") == 1) return;
        
        MaxSdk.SetMuted(PlayerPrefs.GetInt("Audio", 1) == 0);
      
    }

    #region Interstitial Ad Methods
    public void InitializeInterstitialAds()
    {
        if (PlayerPrefs.GetInt("MaxAdStop") == 1) return;
        
            if (!isMaxInitialized)
                return;

            MaxSdkCallbacks.Interstitial.OnAdLoadedEvent += OnInterstitialLoadedEvent;
            MaxSdkCallbacks.Interstitial.OnAdLoadFailedEvent += OnInterstitialLoadFailedEvent;
            MaxSdkCallbacks.Interstitial.OnAdHiddenEvent += OnInterstitialHiddenEvent;
            MaxSdkCallbacks.Interstitial.OnAdDisplayFailedEvent += OnInterstitialAdFailedToDisplayEvent;
            MaxSdkCallbacks.Interstitial.OnAdRevenuePaidEvent += AdRevenuePaidEvent_Inter;
            LoadInterstitial();
      
    }
    private void LoadInterstitial()
    {
        if (PlayerPrefs.GetInt("MaxAdStop") == 1) return;
        
            if (!isMaxInitialized)
                return;
            MuteAudio();
            MaxSdk.LoadInterstitial(InterstitialAdUnitId);
    }
    private void OnInterstitialLoadedEvent(string adUnitId, MaxSdkBase.AdInfo adInfo)
    {
        Debug.Log("Max Interstitial ad loaded");
        interRequestTime = 0;
        
        // #if UNITY_IOS
        // AudioSettings.Reset(AudioSettings.GetConfiguration());
        // #endif
    }
    private void OnInterstitialLoadFailedEvent(string adUnitId, MaxSdkBase.ErrorInfo errorInfo)
    {
        // if (interRequestTime >= 3)
        //     return;
        // interRequestTime += 1;
        Invoke(nameof(LoadInterstitial), 5f);
        Time.timeScale = 1;
        
        #if UNITY_IOS
        AudioSettings.Reset(AudioSettings.GetConfiguration());
        #endif
    }
    private void OnInterstitialAdFailedToDisplayEvent(string adUnitId, MaxSdkBase.ErrorInfo errorInfo, MaxSdkBase.AdInfo adInfo)
    {
        // if (interRequestTime >= 3)
        //     return;
        // interRequestTime += 1;
        Invoke(nameof(LoadInterstitial), 5f);
        Time.timeScale = 1;
        
        #if UNITY_IOS
        AudioSettings.Reset(AudioSettings.GetConfiguration());
        #endif
    }
    private void OnInterstitialHiddenEvent(string adUnitId, MaxSdkBase.AdInfo adInfo)
    {
        Invoke(nameof(LoadInterstitial), 0.5f);
        Time.timeScale = 1;
        AdsGapTimer.instance.ResetTimer();
        
        #if UNITY_IOS
        AudioSettings.Reset(AudioSettings.GetConfiguration());
        #endif
    }

    int currentCount = 0;

    private bool IsAdmobTurn(int showCount)
    {
        switch (useSdkVendorSwitch)
        {
            case 1: return showCount % 2 == 1;
            case 2: return showCount % 3 == 2;
            default: return false;
        }
    }

    private bool IsMaxInterstitialReady()
    {
        return isMaxInitialized && MaxSdk.IsInterstitialReady(InterstitialAdUnitId) && PlayerPrefs.GetInt("MaxAdStop") == 0;
    }

    private bool IsMaxRewardedReady()
    {
        return isMaxInitialized && MaxSdk.IsRewardedAdReady(RewardedAdUnitId) && PlayerPrefs.GetInt("MaxAdStop") == 0;
    }

    public void ShowInterstitial( string placement=null)
    {
        currentCount = PlayerPrefs.GetInt("AdmobCount", 0);

        if (PlayerPrefs.GetInt("RemoveAds", 0) == 1)
            return;

            if (IsAdmobTurn(interShowCount))
            {
                ShowInterstitialAdmobFirst();
                return;
            }

            if (IsMaxInterstitialReady())
            {
                AdsManager.instance.isPausedDuetoAd = true;
                ShowMaxInterstitial();
                interShowCount++;

            }
            else
            {
                GameAnalytics.NewAdEvent(GAAdAction.FailedShow, GAAdType.Interstitial, "MAX", "Interstitial");
                LoadInterstitial();
                if (AdMob._instance.HasAdmobInterstialAvaible())
                {
                    AdsManager.instance.isPausedDuetoAd = true;
                    AdMob._instance.ShowInterstialAd();
                    interShowCount++;

                }
                else
                {
                    GameAnalytics.NewAdEvent(GAAdAction.FailedShow, GAAdType.Interstitial, "ADMOB", "Interstitial");
                    GameAnalytics.NewAdEvent(GAAdAction.FailedShow, GAAdType.Interstitial, "BOTH", "Interstitial");
                    AdMob._instance.RequestInterstitial();
                }
            }

    }

    // AdMob turn of useSdkVendorSwitch 1/2: AdMob first, MAX as fallback.
    private void ShowInterstitialAdmobFirst()
    {
        if (AdMob._instance.HasAdmobInterstialAvaible())
        {
            AdsManager.instance.isPausedDuetoAd = true;
            AdMob._instance.ShowInterstialAd();
            interShowCount++;
        }
        else
        {
            GameAnalytics.NewAdEvent(GAAdAction.FailedShow, GAAdType.Interstitial, "ADMOB", "Interstitial");
            if (IsMaxInterstitialReady())
            {
                AdsManager.instance.isPausedDuetoAd = true;
                ShowMaxInterstitial();
                interShowCount++;
            }
            else
            {
                GameAnalytics.NewAdEvent(GAAdAction.FailedShow, GAAdType.Interstitial, "MAX", "Interstitial");
                GameAnalytics.NewAdEvent(GAAdAction.FailedShow, GAAdType.Interstitial, "BOTH", "Interstitial");
                LoadInterstitial();
                AdMob._instance.RequestInterstitial();
            }
        }
    }

    public void ShowMaxInterstitial()
    {
        if (PlayerPrefs.GetInt("MaxAdStop") == 1) return;
        
        MaxSdk.ShowInterstitial(InterstitialAdUnitId);
       
    }

    public bool IsInterstatialAvailable()
    {
        if (IsMaxInterstitialReady() || AdMob._instance.HasAdmobInterstialAvaible())
            return true;
        return false;
    }

    #endregion

    #region Rewarded Ad Methods
    private void InitializeRewardedAds()
    {
        if (PlayerPrefs.GetInt("MaxAdStop") == 1) return;
        
            if (!isMaxInitialized)
                return;

            MaxSdkCallbacks.Rewarded.OnAdLoadedEvent += OnRewardedAdLoadedEvent;
            MaxSdkCallbacks.Rewarded.OnAdLoadFailedEvent += OnRewardedAdLoadFailedEvent;
            MaxSdkCallbacks.Rewarded.OnAdHiddenEvent += OnRewardedAdHiddenEvent;
            MaxSdkCallbacks.Rewarded.OnAdDisplayFailedEvent += OnRewardedAdFailedToDisplayEvent;
            MaxSdkCallbacks.Rewarded.OnAdReceivedRewardEvent += OnRewardedAdReceivedRewardEvent;
            MaxSdkCallbacks.Rewarded.OnAdRevenuePaidEvent += AdRevenuePaidEvent_Rewarded;
            LoadRewardedAd();
    }
    private void LoadRewardedAd()
    {
        if (PlayerPrefs.GetInt("MaxAdStop") == 1) return;

            Debug.Log("Rewarded loading start=>");
            MuteAudio();
            MaxSdk.LoadRewardedAd(RewardedAdUnitId);
    }

    private void OnRewardedAdLoadedEvent(string adUnitId, MaxSdkBase.AdInfo adInfo)
    {
        print("RewardedAd Loaded event");
        rewardRequestTime = 0;
        
        #if UNITY_IOS
        AudioSettings.Reset(AudioSettings.GetConfiguration());
        #endif
    }

    private void OnRewardedAdLoadFailedEvent(string adUnitId, MaxSdkBase.ErrorInfo errorInfo)
    {
        Callback?.Invoke(false);
        Callback = null;
        // if (rewardRequestTime >= 3)
        //     return;
        // rewardRequestTime += 1;
        Invoke(nameof(LoadRewardedAd), 5f);
        
        #if UNITY_IOS
        AudioSettings.Reset(AudioSettings.GetConfiguration());
        #endif
    }

    private void OnRewardedAdFailedToDisplayEvent(string adUnitId, MaxSdkBase.ErrorInfo errorInfo, MaxSdkBase.AdInfo adInfo)
    {
        Callback?.Invoke(false);
        Callback = null;
        
        Invoke(nameof(LoadRewardedAd), 3f);
        
        #if UNITY_IOS
        AudioSettings.Reset(AudioSettings.GetConfiguration());
        #endif
    }

    private void OnRewardedAdHiddenEvent(string adUnitId, MaxSdkBase.AdInfo adInfo)
    {
        Callback?.Invoke(false);
        Callback = null;
        LoadRewardedAd();
        
        #if UNITY_IOS
        AudioSettings.Reset(AudioSettings.GetConfiguration());
        #endif
    }
    private void OnRewardedAdDismissedEvent(string adUnitId)
    {
        LoadRewardedAd();
        
        
        #if UNITY_IOS
        AudioSettings.Reset(AudioSettings.GetConfiguration());
        #endif
    }
    private void OnRewardedAdReceivedRewardEvent(string adUnitId, MaxSdk.Reward reward, MaxSdkBase.AdInfo adInfo)
    {
        rewardRequestTime = 0;
        LoadRewardedAd();
        _rewarded?.Invoke(); // a null callback would throw and skip the timer reset below

        AdsGapTimer.instance.ResetTimer();
        
        #if UNITY_IOS
        AudioSettings.Reset(AudioSettings.GetConfiguration());
        #endif
    }
    public void ShowRewardedAd(Rewarded rewarded)
    {
            _rewarded = rewarded;

            if (IsAdmobTurn(rewardedShowCount))
            {
                ShowRewardedAdmobFirst();
                return;
            }

            if (IsMaxRewardedReady())
            {
                AdsManager.instance.isPausedDuetoAd = true;
                MaxSdk.ShowRewardedAd(RewardedAdUnitId);
                rewardedShowCount++;

            }
            else
            {
                GameAnalytics.NewAdEvent(GAAdAction.FailedShow, GAAdType.RewardedVideo, "MAX", "Rewarded");
                LoadRewardedAd();
                if (useSdkVendorSwitch == 0)
                {
                    AdMob._instance.ShowRewardAd();
                    AdsManager.instance.isPausedDuetoAd = true;
                }
                else if (AdMob._instance.HasRewardedAvaiable())
                {
                    AdMob._instance.ShowRewardAd();
                    AdsManager.instance.isPausedDuetoAd = true;
                    rewardedShowCount++;
                }
                else
                {
                    GameAnalytics.NewAdEvent(GAAdAction.FailedShow, GAAdType.RewardedVideo, "ADMOB", "Rewarded");
                    GameAnalytics.NewAdEvent(GAAdAction.FailedShow, GAAdType.RewardedVideo, "BOTH", "Rewarded");
                }
            }

    }

    // AdMob turn of useSdkVendorSwitch 1/2: AdMob first, MAX as fallback.
    private void ShowRewardedAdmobFirst()
    {
        if (AdMob._instance.HasRewardedAvaiable())
        {
            AdMob._instance.ShowRewardAd();
            AdsManager.instance.isPausedDuetoAd = true;
            rewardedShowCount++;
        }
        else
        {
            GameAnalytics.NewAdEvent(GAAdAction.FailedShow, GAAdType.RewardedVideo, "ADMOB", "Rewarded");
            if (IsMaxRewardedReady())
            {
                AdsManager.instance.isPausedDuetoAd = true;
                MaxSdk.ShowRewardedAd(RewardedAdUnitId);
                rewardedShowCount++;
            }
            else
            {
                GameAnalytics.NewAdEvent(GAAdAction.FailedShow, GAAdType.RewardedVideo, "MAX", "Rewarded");
                GameAnalytics.NewAdEvent(GAAdAction.FailedShow, GAAdType.RewardedVideo, "BOTH", "Rewarded");
                LoadRewardedAd();
            }
        }
    }

    #endregion

    #region Banner Ad Methods

    // Retrieve the ID from your account

    public void InitializeBannerAds()
    {
        if (PlayerPrefs.GetInt("MaxAdStop") == 1) return;

            MaxSdk.CreateBanner(BannerAdUnitId, bannerPosition);
            MaxSdk.SetBannerBackgroundColor(BannerAdUnitId, Color.clear);
            MaxSdkCallbacks.Banner.OnAdLoadedEvent += OnBannerAdLoadedEvent;
            MaxSdkCallbacks.Banner.OnAdLoadFailedEvent += OnBannerAdLoadFailedEvent;
            MaxSdkCallbacks.Banner.OnAdRevenuePaidEvent += AdRevenuePaidEvent_Banner;
            MaxSdkCallbacks.Banner.OnAdCollapsedEvent += OnBannerAdCollapsedEvent;
        
    }

    private void OnBannerAdLoadedEvent(string adUnitId, MaxSdkBase.AdInfo adInfo)
    {
        isBannerShowing = true;
        AdMob._instance.bannerHide();
        ShowBanner();
    }

    private void OnBannerAdLoadFailedEvent(string adUnitId, MaxSdkBase.ErrorInfo errorInfo)
    {
        isBannerShowing = false;
        AdMob._instance.bannerShow();
        AdsManager.instance.HideBanner();
    }

    private void OnBannerAdClickedEvent(string adUnitId, MaxSdkBase.AdInfo adInfo) { }

    private void OnBannerAdRevenuePaidEvent(string adUnitId, MaxSdkBase.AdInfo adInfo)
    {

    }

    private void OnBannerAdExpandedEvent(string adUnitId, MaxSdkBase.AdInfo adInfo) { }

    private void OnBannerAdCollapsedEvent(string adUnitId, MaxSdkBase.AdInfo adInfo) { }

    public void ShowBanner()
    {
        if (PlayerPrefs.GetInt("MaxAdStop") == 1) return;
        if (PlayerPrefs.GetInt("RemoveAds") == 1) return;

        MaxSdk.ShowBanner(BannerAdUnitId);
    }

    public void HideBanner()
    {
        MaxSdk.HideBanner(BannerAdUnitId);
    }

    #endregion

    #region MREC Ad Methods

    private void InitializeMRecAds()
    {
        if (PlayerPrefs.GetInt("MaxAdStop") == 1) return;
        
            MaxSdk.CreateMRec(MRecAdUnitId, MaxSdkBase.AdViewPosition.TopCenter);
            MaxSdkCallbacks.MRec.OnAdLoadedEvent += OnMRecAdLoadedEvent;
            MaxSdkCallbacks.MRec.OnAdLoadFailedEvent += OnMRecAdFailedEvent;
            MaxSdkCallbacks.MRec.OnAdClickedEvent += OnMRecAdClickedEvent;
            MaxSdkCallbacks.MRec.OnAdRevenuePaidEvent += AdRevenuePaidEvent_MREC;
    }


    public void ShowMRECBanner()
    {
        if (PlayerPrefs.GetInt("AdsStop") == 1) return;
        if (PlayerPrefs.GetInt("RemoveAds") == 1) return;
    
            MaxSdk.ShowMRec(MRecAdUnitId);
    }

    public void ShowMRECBannerPosition(MaxSdkBase.AdViewPosition AdPos)
    {
        if (PlayerPrefs.GetInt("AdsStop") == 1) return;
        if (PlayerPrefs.GetInt("RemoveAds") == 1) return;
        
        MaxSdk.CreateMRec(MRecAdUnitId, AdPos);
        MaxSdk.ShowMRec(MRecAdUnitId);
    }
    public void HideMRECBanner()
    {
        if (PlayerPrefs.GetInt("MaxAdStop") == 1) return;
        
        MaxSdk.HideMRec(MRecAdUnitId);
    }

    private void OnMRecAdLoadedEvent(string adUnitId, MaxSdkBase.AdInfo adInfo)
    {
    }

    private void OnMRecAdFailedEvent(string adUnitId, MaxSdkBase.ErrorInfo errorInfo)
    {
    }

    private void OnMRecAdClickedEvent(string adUnitId, MaxSdkBase.AdInfo adInfo)
    {
    }



    #endregion


    string _placementName = "";

    // ****************** Revenue Paid Events ***************************
    
    private void AdRevenuePaidEvent_Common(string adUnitId, MaxSdkBase.AdInfo impressionData)
    {
       
        double revenue = impressionData.Revenue;
        var impressionParameters = new[] {
            new Firebase.Analytics.Parameter("ad_platform", "AppLovin"),
            new Firebase.Analytics.Parameter("ad_source", impressionData.NetworkName),
            new Firebase.Analytics.Parameter("ad_unit_name", impressionData.AdUnitIdentifier),
            new Firebase.Analytics.Parameter("ad_format", impressionData.AdFormat),
            new Firebase.Analytics.Parameter("value", revenue),
            new Firebase.Analytics.Parameter("currency", "USD"), // All AppLovin revenue is sent in USD
        };
        Firebase.Analytics.FirebaseAnalytics.LogEvent("ad_impression", impressionParameters);

        FirebaseManager.Instance.CheckTheRevenue(revenue);
        FirebaseManager.Instance.CheckTheImpressionForRevenue(revenue);

        //Rev Event For Adjust
        AdjustAdRevenue adjustAdRevenue = new AdjustAdRevenue("applovin_max_sdk");
        adjustAdRevenue.SetRevenue(revenue, "USD");
        adjustAdRevenue.AdRevenueNetwork = impressionData.NetworkName;
        adjustAdRevenue.AdRevenueUnit = $"{impressionData.AdFormat}_{impressionData.AdUnitIdentifier}";
        Adjust.TrackAdRevenue(adjustAdRevenue);

        GenZLog.Ok("Ads", "impression", "MAX Interstitial");
        GenZLog.Call("Adjust", "ad_revenue", "MAX Interstitial");
        AppmetricaAnalytics.ReportRevenue_Applovin(impressionData, AppmetricaAnalytics.AdFormat.Interstitial, _placementName);

        Debug.Log("MAX Rev: " + revenue);

        GA_Aanalytics.GA_IntEvent_MAX();
    }
    private void AdRevenuePaidEvent_Inter(string adUnitId, MaxSdkBase.AdInfo impressionData)
    {
       
            double revenue = impressionData.Revenue;
            var impressionParameters = new[] {
        new Firebase.Analytics.Parameter("ad_platform", "AppLovin"),
        new Firebase.Analytics.Parameter("ad_source", impressionData.NetworkName),
        new Firebase.Analytics.Parameter("ad_unit_name", impressionData.AdUnitIdentifier),
        new Firebase.Analytics.Parameter("ad_format", impressionData.AdFormat),
        new Firebase.Analytics.Parameter("value", revenue),
        new Firebase.Analytics.Parameter("currency", "USD"), // All AppLovin revenue is sent in USD
        };
            Firebase.Analytics.FirebaseAnalytics.LogEvent("ad_impression", impressionParameters);

            FirebaseManager.Instance.CheckTheRevenue(revenue);
            FirebaseManager.Instance.CheckTheImpressionForRevenue(revenue);

            //Rev Event For Adjust
            AdjustAdRevenue adjustAdRevenue = new AdjustAdRevenue("applovin_max_sdk");
            adjustAdRevenue.SetRevenue(revenue, "USD");
            adjustAdRevenue.AdRevenueNetwork = impressionData.NetworkName;
            adjustAdRevenue.AdRevenueUnit = $"{impressionData.AdFormat}_{impressionData.AdUnitIdentifier}";
            Adjust.TrackAdRevenue(adjustAdRevenue);

            GenZLog.Ok("Ads", "impression", "MAX Interstitial");
            GenZLog.Call("Adjust", "ad_revenue", "MAX Interstitial");
            AppmetricaAnalytics.ReportRevenue_Applovin(impressionData, AppmetricaAnalytics.AdFormat.Interstitial, _placementName);

            Debug.Log("MAX Rev: " + revenue);

            GA_Aanalytics.GA_IntEvent_MAX();
    }

    private void AdRevenuePaidEvent_Rewarded(string adUnitId, MaxSdkBase.AdInfo impressionData)
    {
       
            double revenue = impressionData.Revenue;
            var impressionParameters = new[] {
        new Firebase.Analytics.Parameter("ad_platform", "AppLovin"),
        new Firebase.Analytics.Parameter("ad_source", impressionData.NetworkName),
        new Firebase.Analytics.Parameter("ad_unit_name", impressionData.AdUnitIdentifier),
        new Firebase.Analytics.Parameter("ad_format", impressionData.AdFormat),
        new Firebase.Analytics.Parameter("value", revenue),
        new Firebase.Analytics.Parameter("currency", "USD"), // All AppLovin revenue is sent in USD
        };
            Firebase.Analytics.FirebaseAnalytics.LogEvent("ad_impression", impressionParameters);
            FirebaseManager.Instance.CheckTheRevenue(revenue);
            FirebaseManager.Instance.CheckTheImpressionForRevenue(revenue);



            //Rev Event For Adjust
            AdjustAdRevenue adjustAdRevenue = new AdjustAdRevenue("applovin_max_sdk");
            adjustAdRevenue.SetRevenue(revenue, "USD");
            adjustAdRevenue.AdRevenueNetwork = impressionData.NetworkName;
            adjustAdRevenue.AdRevenueUnit = $"{impressionData.AdFormat}_{impressionData.AdUnitIdentifier}";
            Adjust.TrackAdRevenue(adjustAdRevenue);

            GenZLog.Ok("Ads", "impression", "MAX Rewarded");
            GenZLog.Call("Adjust", "ad_revenue", "MAX Rewarded");
            AppmetricaAnalytics.ReportRevenue_Applovin(impressionData, AppmetricaAnalytics.AdFormat.Rewarded, _placementName);

            Debug.Log("MAX Rev: " + revenue);

            GA_Aanalytics.GA_RewardedEventMAX();
       
    }
    private void AdRevenuePaidEvent_Banner(string adUnitId, MaxSdkBase.AdInfo impressionData)
    {
        
            double revenue = impressionData.Revenue;
            var impressionParameters = new[] {
        new Firebase.Analytics.Parameter("ad_platform", "AppLovin"),
        new Firebase.Analytics.Parameter("ad_source", impressionData.NetworkName),
        new Firebase.Analytics.Parameter("ad_unit_name", impressionData.AdUnitIdentifier),
        new Firebase.Analytics.Parameter("ad_format", impressionData.AdFormat),
        new Firebase.Analytics.Parameter("value", revenue),
        new Firebase.Analytics.Parameter("currency", "USD"), // All AppLovin revenue is sent in USD
        };
            Firebase.Analytics.FirebaseAnalytics.LogEvent("ad_impression", impressionParameters);

            FirebaseManager.Instance.CheckTheRevenue(revenue);
            FirebaseManager.Instance.CheckTheImpressionForRevenue(revenue);



            //Rev Event For Adjust
            AdjustAdRevenue adjustAdRevenue = new AdjustAdRevenue("applovin_max_sdk");
            adjustAdRevenue.SetRevenue(revenue, "USD");
            adjustAdRevenue.AdRevenueNetwork = impressionData.NetworkName;
            adjustAdRevenue.AdRevenueUnit = $"{impressionData.AdFormat}_{impressionData.AdUnitIdentifier}";
            Adjust.TrackAdRevenue(adjustAdRevenue);

            GenZLog.Ok("Ads", "impression", "MAX Banner");
            GenZLog.Call("Adjust", "ad_revenue", "MAX Banner");
            AppmetricaAnalytics.ReportRevenue_Applovin(impressionData, AppmetricaAnalytics.AdFormat.Banner, _placementName);

            Debug.Log("MAX Rev: " + revenue);
      
    }

    private void AdRevenuePaidEvent_MREC(string adUnitId, MaxSdkBase.AdInfo impressionData)
    {
        
            double revenue = impressionData.Revenue;
            var impressionParameters = new[] {
        new Firebase.Analytics.Parameter("ad_platform", "AppLovin"),
        new Firebase.Analytics.Parameter("ad_source", impressionData.NetworkName),
        new Firebase.Analytics.Parameter("ad_unit_name", impressionData.AdUnitIdentifier),
        new Firebase.Analytics.Parameter("ad_format", impressionData.AdFormat),
        new Firebase.Analytics.Parameter("value", revenue),
        new Firebase.Analytics.Parameter("currency", "USD"), // All AppLovin revenue is sent in USD
        };
            Firebase.Analytics.FirebaseAnalytics.LogEvent("ad_impression", impressionParameters);

            FirebaseManager.Instance.CheckTheRevenue(revenue);
            FirebaseManager.Instance.CheckTheImpressionForRevenue(revenue);


            //Rev Event For Adjust
            AdjustAdRevenue adjustAdRevenue = new AdjustAdRevenue("applovin_max_sdk");
            adjustAdRevenue.SetRevenue(revenue, "USD");
            adjustAdRevenue.AdRevenueNetwork = impressionData.NetworkName;
            adjustAdRevenue.AdRevenueUnit = $"{impressionData.AdFormat}_{impressionData.AdUnitIdentifier}";
            Adjust.TrackAdRevenue(adjustAdRevenue);


            GenZLog.Ok("Ads", "impression", "MAX MREC");
            GenZLog.Call("Adjust", "ad_revenue", "MAX MREC");
            AppmetricaAnalytics.ReportRevenue_Applovin(impressionData, AppmetricaAnalytics.AdFormat.MREC, _placementName);

            Debug.Log("MAX Rev: " + revenue);
       
    }
}