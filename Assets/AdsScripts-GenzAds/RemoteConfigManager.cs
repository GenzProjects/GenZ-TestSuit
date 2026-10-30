using System.Collections.Generic;
using System.Globalization;
using Firebase.RemoteConfig;
using UnityEngine;

/// <summary>
/// This game's Firebase Remote Config: every key, its default and where its value goes.
/// FirebaseManager is the same in every studio game. It sets <see cref="Defaults"/> before the first fetch and calls
/// <see cref="LoadValues"/> after each fetch + activate. For a new game, edit only this file.
///
/// To add a key: add a field (its starting value is the default), add it to Defaults(), and read it in LoadValues().
///
/// Put this component on the ADs object (next to FirebaseManager) to set the Taichi defaults in the Inspector.
/// Without it, the values written below are used.
/// </summary>
public class RemoteConfigManager : MonoBehaviour
{
    public static RemoteConfigManager Instance;

    // ---------------------------------------------------------------- Inspector: Taichi defaults

    [Header("Taichi defaults (until Remote Config loads, and when the key is missing)")]
    [Tooltip("Taichi 1.0: accumulated ad revenue in USD that sends \"custom_ad_impression\". Remote Config key \"custom_rev\".")]
    [SerializeField] double taichi1DefaultRevenue = 0.1;
    [Tooltip("Taichi 2.0: number of ad impressions that sends \"count_ad_impression\". Remote Config key \"custom_imp\". " +
             "Must equal this game's \"Taichi 2.0 Default Values\" in the studio sheet (the Test Suit checks it).")]
    [SerializeField] int taichi2DefaultImpressions = 30;

    static double taichi1Default = 0.1; // the fallback when "custom_rev" can't be read

    // Runs when the SDK scene loads, before FirebaseManager builds the defaults (it waits for Firebase first).
    void Awake()
    {
        if (Instance != null && Instance != this) return;
        Instance = this;
        if (taichi1DefaultRevenue > 0) revenue_maxValue = taichi1Default = taichi1DefaultRevenue;
        if (taichi2DefaultImpressions > 0) impressionValue_ForRevenue = taichi2DefaultImpressions;
    }

    // Runs after every Awake and before FirebaseManager fetches (it waits for Firebase first), so the values
    // loaded here are used until the fetch, and also become the Remote Config defaults (Defaults() below).
    void Start()
    {
        if (Instance != this) return;
        LoadValuesFromSystem();
    }

    /// <summary>
    /// Before Firebase fetches: load the values saved on this device last time (PlayerPrefs or a local JSON file)
    /// into the fields below. Save them in <see cref="LoadValues"/> once Firebase has fetched new ones.
    /// Empty = the Inspector / code values are used until the fetch.
    /// </summary>
    public static void LoadValuesFromSystem()
    {
        // e.g. AdTimer = PlayerPrefs.GetInt("adtimer", (int)AdTimer);
        
    }

    
    // ---------------------------------------------------------------- values
    // Studio keys (every game)
    public static long AdTimer = 20;                   
    public static int useSdkVendorSwitch = 0;           // "useSdkVendorSwitch": 0 = MAX first, 1 = MAX/AdMob one by one, 2 = MAX, MAX, AdMob
    public static double revenue_maxValue = 0.1;        // "custom_rev": Taichi 1.0 revenue threshold (USD); default from the Inspector
    public static int impressionValue_ForRevenue = 30;  // "custom_imp": Taichi 2.0 impression count; default from the Inspector

    // This game's keys
    public static long AdTimer2 = 20;                   
    public static int firstAdTest = 0;                  
    public static int rewPopUpFreq = 5;                
    public static int canShowBigbanner = 0;             
    public static int RewCoinsTest = 0;                
    public static bool canShowBrainBar = true;         
    public static bool canShowRewOnHardLevel = true;   

    // Level order from Remote Config (GameManager.ResetLevelsOrder). Not read at the moment.
    public static LevelData levelList = new LevelData();

    // ---------------------------------------------------------------- defaults

    /// <summary>Remote Config defaults: the starting values above. FirebaseManager passes them to SetDefaultsAsync.</summary>
    public static Dictionary<string, object> Defaults() => new Dictionary<string, object>
    {
        { "adtimer", AdTimer },
        { "useSdkVendorSwitch", useSdkVendorSwitch },
        { "custom_rev", revenue_maxValue },
        { "custom_imp", impressionValue_ForRevenue },

        { "adtimertwo", AdTimer2 },
        { "firstad", firstAdTest },
        { "rewpopupfreq", rewPopUpFreq },
        { "bigbanner", canShowBigbanner },
        { "rewcointest", RewCoinsTest },
        { "showBrainBar", canShowBrainBar },
        { "showRewOnHard", canShowRewOnHardLevel },
    };

    // ---------------------------------------------------------------- loading

    /// <summary>Reads every key after a fetch + activate (called by FirebaseManager on the main thread).</summary>
    public static void LoadValues(FirebaseRemoteConfig rc)
    {
        // Studio keys
        AdTimer = rc.GetValue("adtimer").LongValue;
        if (AdsGapTimer.instance != null) AdsGapTimer.instance.timeLeft = (int)AdTimer;

        useSdkVendorSwitch = (int)rc.GetValue("useSdkVendorSwitch").LongValue;
        PlayerPrefs.SetInt("useSdkVendorSwitch", useSdkVendorSwitch);
        if (AdsManager.instance != null) AdsManager.instance.useSdkVendorSwitch = useSdkVendorSwitch;

        var revenue_maxValue_St = rc.GetValue("custom_rev").StringValue;
        // InvariantCulture: "0.05" stays 0.05 on comma-decimal phones. Missing or bad value: keep the default threshold.
        if (!double.TryParse(revenue_maxValue_St, NumberStyles.Float, CultureInfo.InvariantCulture, out revenue_maxValue) || revenue_maxValue <= 0)
            revenue_maxValue = taichi1Default;

        impressionValue_ForRevenue = (int)rc.GetValue("custom_imp").LongValue;

        // This game's keys
        AdTimer2 = rc.GetValue("adtimertwo").LongValue;
        firstAdTest = (int)rc.GetValue("firstad").LongValue;
        rewPopUpFreq = (int)rc.GetValue("rewpopupfreq").LongValue;
        canShowBigbanner = (int)rc.GetValue("bigbanner").LongValue;
        RewCoinsTest = (int)rc.GetValue("rewcointest").LongValue;
        canShowBrainBar = rc.GetValue("showBrainBar").BooleanValue;
        canShowRewOnHardLevel = rc.GetValue("showRewOnHard").BooleanValue;
        

        // The APK Test Suit reads this line (custom_imp against the sheet): keep its format.
        GenZLog.Info("RemoteConfig", "values", $"adtimer={AdTimer} adtimertwo={AdTimer2} custom_imp={impressionValue_ForRevenue} useSdkVendorSwitch={useSdkVendorSwitch}");
        Debug.Log($"Remote Config: adtimer={AdTimer} adtimertwo={AdTimer2} custom_rev={revenue_maxValue.ToString(CultureInfo.InvariantCulture)} custom_imp={impressionValue_ForRevenue} " +
                  $"useSdkVendorSwitch={useSdkVendorSwitch} firstad={firstAdTest} rewpopupfreq={rewPopUpFreq} bigbanner={canShowBigbanner} " +
                  $"rewcointest={RewCoinsTest} showBrainBar={canShowBrainBar} showRewOnHard={canShowRewOnHardLevel}");
    }
}
