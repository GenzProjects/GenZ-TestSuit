// GenzMonetization-v1.33
using GameAnalyticsSDK;
using UnityEngine;

public static class GA_Aanalytics
{

    #region GameAnalytics Events
    public static void GA_IntEvent_MAX()
    {
        GameAnalytics.NewAdEvent(GAAdAction.Show, GAAdType.Interstitial, "MAX", "Interstitial");
    }
    public static void GA_IntEvent_ADMOB()
    {
        GameAnalytics.NewAdEvent(GAAdAction.Show, GAAdType.Interstitial, "ADMOB", "Interstitial");
    }

    public static void GA_RewardedEventMAX()
    {
        GameAnalytics.NewAdEvent(GAAdAction.Show, GAAdType.RewardedVideo, "MAX", "Rewarded");
    }
    public static void GA_RewardedEventADMOB()
    {
        GameAnalytics.NewAdEvent(GAAdAction.Show, GAAdType.RewardedVideo, "ADMOB", "Rewarded");
    }

    public static void GA_RewardOnWatchVideoMAX(string rew)
    {
        GameAnalytics.NewAdEvent(GAAdAction.RewardReceived, GAAdType.RewardedVideo, "MAX", rew);
    }
    public static void GA_RewardOnWatchVideoADMOB(string rew)
    {
        GameAnalytics.NewAdEvent(GAAdAction.RewardReceived, GAAdType.RewardedVideo, "ADMOB", rew);
    }


    public static void GA_LevelStartEvent(string level)
    {
        GameAnalytics.NewProgressionEvent(GAProgressionStatus.Start, level);
    }
    public static void GA_LevelCompleteEvent(string level)
    {
        GameAnalytics.NewProgressionEvent(GAProgressionStatus.Complete, level);
    }
    public static void GA_CustomDesingEvent(string str)
    {
        GameAnalytics.NewDesignEvent(str);
    }

    #endregion

}
