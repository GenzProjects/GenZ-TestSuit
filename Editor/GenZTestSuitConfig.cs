// GenZ-TestSuit-v1.0.1
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace GenZ.TestSuit
{
    /// <summary>The tool's release name, shown in the window and the copied report. Raise it with each release.</summary>
    public static class GenZTestSuitInfo
    {
        public const string Version = "1.0.1";
        public const string Name = "GenZ-TestSuit-v" + Version;

        /// <summary>
        /// The GenZ ad scripts (AdsScripts-GenzAds) version this suite checks for: every script there starts with
        /// "// GenzMonetization-v&lt;version&gt;". Raise it with each GenZ Ads release (CodeCheckSettings.adsScriptsVersion overrides it per project).
        /// </summary>
        public const string AdsScriptsVersion = "1.33";
        public const string AdsScriptsHeader = "GenzMonetization-v";
    }

    /// <summary>How a field's value is validated. Every ID kind also fails on any whitespace.</summary>
    public enum FieldKind
    {
        AdMobUnitId,     // ca-app-pub-XXXXXXXXXXXXXXXX/YYYYYYYYYY, Google sample IDs rejected
        MaxSdkKey,       // AppLovin MAX SDK key (86 chars)
        MaxUnitId,       // AppLovin MAX ad unit ID (16 hex chars)
        InAppList,       // array of elements holding a product Id; at least one required
        MustBeFalse,     // bool switch that must be off in a release (e.g. AdMob.isTestIdOn)
        Text,            // any non-empty string
        AdMobAppId,      // ca-app-pub-XXXXXXXXXXXXXXXX~YYYYYYYYYY, Google sample app ID rejected
        AdjustToken,     // 12 lowercase letters/digits
        GameAnalyticsGameKey,   // 32 hex chars
        GameAnalyticsSecretKey, // 40 hex chars
        MustBeTrue,      // bool switch that must be on
        Expected,        // bool / enum / string / int must equal FieldRule.expectedValue (enums by name)
        NumberAboveZero, // int / float field must be > 0 (e.g. AdsGapTimer.timeLeft default)
        AppMetricaApiKey // GUID: 8-4-4-4-12 hex chars
    }

    /// <summary>Names the code checks anchor on. SDK API calls (MaxSdk.*, InterstitialAd.Load…) are built in.</summary>
    [Serializable]
    public class CodeCheckSettings
    {
        public bool enabled = true;
        [Tooltip("Ad/analytics script classes. Ad logic is looked for in these; calls from other scripts count as game call sites.")]
        public string[] adScripts =
        {
            "AdMob", "Admob", "AdsIds", "AdsManager", "Analytics", "AppmetricaAnalytics", "AppOpen_Code", "BigBanner",
            "FirebaseManager", "GA_Aanalytics", "InAppCalling", "InAppReviewManager", "UmpManager", "unityInApp"
        };
        [Tooltip("Scripts that must exist in the project (class names).")]
        public string[] requiredScripts =
        {
            "AdMob|Admob", "AdsIds", "AdsManager", "Analytics", "AppmetricaAnalytics", "AppOpen_Code",
            "FirebaseManager", "GA_Aanalytics", "InAppCalling", "InAppReviewManager", "UmpManager", "unityInApp", "AdsGapTimer"
        };
        [Tooltip("Scripts a GenZ Ads v1.33 (or newer) project must also have. A v1.32 project doesn't need them.")]
        public string[] requiredScriptsV133 = { "RemoteConfigManager", "GenZLog", "ConsentManager" };
        [Tooltip("Interstitial cooldown timer class.")]
        public string gapTimerClass = "AdsGapTimer";
        [Tooltip("Methods that restart the cooldown.")]
        public string[] resetMethods = { "ResetTimer" };
        [Tooltip("Timer field that Remote Config must set.")]
        public string[] timerFields = { "timeLeft" };
        [Tooltip("Flag that says the cooldown is over.")]
        public string[] gateFields = { "canShowAd" };
        [Tooltip("Classes that fetch Remote Config.")]
        public string[] remoteConfigClasses = { "FirebaseManager", "RemoteConfigManager" }; // any class calling GetValue("…") is also found
        [Tooltip("Int field that picks the Interstitial/Rewarded network order (0 = MAX first, 1 = MAX/AdMob one by one, 2 = two MAX then one AdMob). " +
                 "Not found in the ad scripts = the project has no switch and only MAX first is checked.")]
        public string[] vendorSwitchFields = { "useSdkVendorSwitch" };
        [Tooltip("Where ad revenue (paid) events must be reported: 'Name|regex'. Each revenue handler is checked for every entry.")]
        public string[] revenueSinks =
        {
            @"Adjust|\bAdjust\s*\.\s*TrackAdRevenue\s*\(",
            @"Firebase|""ad_impression""|\bEventAdImpression\b",
            @"AppMetrica|\bReportRevenue_\w*\s*\(|\bReportAdRevenue\s*\("
        };
        [Tooltip("Folders (under Assets) not scanned: third-party SDKs and kits. Editor folders are always skipped.")]
        public string[] excludeFolders =
        {
            "Plugins", "GoogleMobileAds", "MaxSdk", "Firebase", "GameAnalytics", "Adjust", "AppMetrica",
            "ExternalDependencyManager", "Dreamteck", "Demigiant", "NiceVibrations", "TextMesh Pro", "Kits"
        };
        [Tooltip("GenZ ad scripts version every script must state on its first line ('// GenzMonetization-v<version>'). "
                 + "Empty = the version this suite was built for (GenZTestSuitInfo.AdsScriptsVersion).")]
        public string adsScriptsVersion = "";
        [Tooltip("Flag that makes the App Open ad skip one return to the game. Purchases (PurchaseProduct / InitiatePurchase) and the "
                 + "in-app review (LaunchReviewFlow) must set it, and clear it again after their flow ends. Empty = isPausedDuetoAd.")]
        public string[] appOpenSkipFlags = { "isPausedDuetoAd" };
        [Tooltip("Code only one platform can compile: '<iOS|Android>|<regex>'. A match outside #if UNITY_IOS (or #if UNITY_ANDROID) breaks "
                 + "the other platform's build. Scripts in a folder with an .asmdef are skipped: their asmdef picks the platforms.")]
        public string[] platformOnlyApis =
        {
            @"iOS|\bUnity\.Advertisement\.IosSupport\b|\bATTrackingStatusBinding\b|\bSkAdNetworkBinding\b",
            @"Android|\bGoogle\.Play\.(Review|Core|Common|AppUpdate)\b|\bReviewManager\b|\bPlayReviewInfo\b|\bReviewErrorCode\b",
        };
    }

    /// <summary>Step 3: Taichi 1.0 (revenue threshold → custom_ad_impression) and 2.0 (impression count → count_ad_impression).</summary>
    [Serializable]
    public class TaichiSettings
    {
        public bool enabled = true;
        [Tooltip("Classes holding the Taichi switches and code (on the Ads object in the SDK scene).")]
        public string[] managerClasses = { "FirebaseManager" };
        [Tooltip("On = Taichi 1.0 switched off is a failure. Off = it's only reported.")]
        public bool requireTaichi1 = false;
        [Tooltip("On = Taichi 2.0 switched off is a failure. Off = it's only reported.")]
        public bool requireTaichi2 = true;
        [Tooltip("Serialized bool that switches Taichi 1.0 on.")]
        public string[] taichi1Flags = { "Taiche1_0", "Taichi1_0", "taichi1", "Taichi1" };
        [Tooltip("Serialized bool that switches Taichi 2.0 on.")]
        public string[] taichi2Flags = { "Taiche2_0", "Taichi2_0", "taichi2", "Taichi2" };
        [Tooltip("Firebase event Taichi 1.0 sends when the accumulated revenue reaches the threshold.")]
        public string taichi1Event = "custom_ad_impression";
        [Tooltip("Firebase event Taichi 2.0 sends every N impressions.")]
        public string taichi2Event = "count_ad_impression";
        [Tooltip("Remote Config key of the Taichi 1.0 revenue threshold (USD).")]
        public string taichi1Key = "custom_rev";
        [Tooltip("Remote Config key of the Taichi 2.0 impression count.")]
        public string taichi2Key = "custom_imp";
        [Tooltip("Component holding the Taichi defaults set in the Inspector (on the Ads object).")]
        public string[] defaultClasses = { "RemoteConfigManager" };
        [Tooltip("Serialized field with the Taichi 1.0 default revenue threshold (USD).")]
        public string[] taichi1DefaultFields = { "taichi1DefaultRevenue" };
        [Tooltip("Serialized field with the Taichi 2.0 default impression count (must equal the sheet).")]
        public string[] taichi2DefaultFields = { "taichi2DefaultImpressions" };
        [Tooltip("Sheet column with this game's Taichi 2.0 impression count.")]
        public string[] taichi2SheetColumns = { "Taichi 2.0 Default Values", "Taichi 2.0 Default", "Taichi Default Values", "Taichi 2.0" };
    }

    /// <summary>Step 2 file checks: SDK files, Android dependencies and Gradle setup.</summary>
    [Serializable]
    public class IntegrationSettings
    {
        [Tooltip("SDKs that must be installed: '<Label>|<folder under Assets>'. Their Android dependencies must be resolved.")]
        public string[] requiredSdks =
        {
            "Firebase|Firebase", "GameAnalytics|GameAnalytics", "AppMetrica|AppMetrica", "Adjust|Adjust",
            "AppLovin MAX|MaxSdk", "Google Mobile Ads|GoogleMobileAds"
        };
        [Tooltip("Firebase modules the game uses (DLL names without .dll).")]
        public string[] firebaseModules = { "Firebase.App", "Firebase.Analytics", "Firebase.RemoteConfig" };
        [Tooltip("GameAnalytics ad revenue format strings '<Format>|<string>', used only when GAMaxIntegration.cs can't be read. "
                 + "Normally GAAdMobIntegration.cs is compared with the strings GAMaxIntegration.cs sends.")]
        public string[] gaAdFormatFallback = { "Interstitial|INTER", "Rewarded|REWARDED", "Banner|BANNER", "App Open|APP_OPEN" };
        [Tooltip("AppLovin MAX adapters every game must have: '<Network>|<Android group:artifact>|<iOS pod>' (version ignored). "
                 + "Android builds check the Maven package, iOS builds the CocoaPods pod (empty = not checked on iOS). A missing one fails. "
                 + "Custom networks (AdSurge, Helix) come from their own *Dependencies.xml, not the Integration Manager.")]
        public string[] maxAdapters =
        {
            "BidMachine|com.applovin.mediation:bidmachine-adapter|AppLovinMediationBidMachineAdapter",
            "Bigo Ads|com.applovin.mediation:bigoads-adapter|AppLovinMediationBigoAdsAdapter",
            "Pangle (ByteDance)|com.applovin.mediation:bytedance-adapter|AppLovinMediationByteDanceAdapter",
            "Meta Audience Network (Facebook)|com.applovin.mediation:facebook-adapter|AppLovinMediationFacebookAdapter",
            "DT Exchange (Fyber)|com.applovin.mediation:fyber-adapter|AppLovinMediationFyberAdapter",
            "Google AdMob|com.applovin.mediation:google-adapter|AppLovinMediationGoogleAdapter",
            "Google Ad Manager|com.applovin.mediation:google-ad-manager-adapter|AppLovinMediationGoogleAdManagerAdapter",
            "InMobi|com.applovin.mediation:inmobi-adapter|AppLovinMediationInMobiAdapter",
            "Mintegral|com.applovin.mediation:mintegral-adapter|AppLovinMediationMintegralAdapter",
            "Smaato|com.applovin.mediation:smaato-adapter|AppLovinMediationSmaatoAdapter",
            "Unity Ads|com.applovin.mediation:unityads-adapter|AppLovinMediationUnityAdsAdapter",
            "Liftoff Monetize (Vungle)|com.applovin.mediation:vungle-adapter|AppLovinMediationVungleAdapter",
            "Yandex|com.applovin.mediation:yandex-adapter|AppLovinMediationYandexAdapter",
            "YSO Network|com.applovin.mediation:yso-network-adapter|AppLovinMediationYSONetworkAdapter",
            "AdSurge (custom network)|com.adsurge.sdk:adapter-for-max|",
            "Helix / Voodoo ADN (custom network)|io.adn:adn-applovin-adapter|",
        };
        [Tooltip("Networks that must not be in the build: '<Network>|<Android group:artifact>|<iOS pod>'. An Android entry ending in ':' "
                 + "matches any package of that group. Also fails when a MaxSdk/Mediation/<Network> folder is still there.")]
        public string[] forbiddenAdapters =
        {
            "IronSource|com.applovin.mediation:ironsource-adapter|AppLovinMediationIronSourceAdapter",
            "IronSource SDK|com.ironsource.sdk:|IronSourceSDK",
            "IronSource / LevelPlay SDK|com.unity3d.ads-mediation:|",
        };
        [Tooltip("Regex on '<group>:<artifact>' for what counts as an Android ad network adapter. One that isn't in Max Adapters or Forbidden Adapters is a warning.")]
        public string adapterPattern = @"^com\.applovin\.mediation:[\w.-]+-adapter$|adapter-for-max|applovin-adapter|max-adapter";
        [Tooltip("Regex on the iOS pod name for what counts as an iOS ad network adapter. One that isn't in Max Adapters or Forbidden Adapters is a warning.")]
        public string iosAdapterPattern = @"^AppLovinMediation\w+Adapter$|ApplovinAdapter|MaxAdapter";
    }

    /// <summary>Step 2: the Android Player Settings the studio's release builds must have.</summary>
    [Serializable]
    public class AndroidPlayerRules
    {
        public bool enabled = true;
        [Tooltip("Player Settings → Other → Auto Graphics API (Android).")]
        public bool autoGraphicsApi = false;
        [Tooltip("Graphics APIs that must not be in the Android list.")]
        public string[] forbiddenGraphicsApis = { "Vulkan" };
        public bool requireES31 = false;
        public bool requireES31AEP = true;
        public bool requireES32 = true;
        [Tooltip("ScriptingImplementation name: IL2CPP or Mono2x.")]
        public string scriptingBackend = "IL2CPP";
        [Tooltip("AndroidArchitecture names; exactly these must be ticked.")]
        public string[] targetArchitectures = { "ARMv7", "ARM64" };
        [Tooltip("Minimum API level: 24 = Android 7.0.")]
        public int minSdk = 24;
        [Tooltip("Unity 6 and newer: the minimum API levels that pass (24 = Android 7.0, 25 = Android 7.1). Empty = use Min Sdk.")]
        public int[] minSdkUnity6 = { 24, 25 };
        [Tooltip("Target API level: 36 = Android 16.0.")]
        public int targetSdk = 36;
        [Tooltip("Unity 6 and newer: Player Settings → Splash Image → Show Splash Screen must be this. Not checked on older Unity.")]
        public bool showSplashScreenUnity6 = false;
    }

    /// <summary>Android signing: the release keystore is used, and it's the one the studio sheet names for this game.</summary>
    [Serializable]
    public class SigningSettings
    {
        public bool enabled = true;
        [Tooltip("Sheet columns naming this game's keystore (its file name or key alias, e.g. 'gamestoplay').")]
        public string[] sheetColumns = { "KeyStore", "Keystore Name", "Keystore Alias", "Signing Key" };
        [Tooltip("File types searched next to the project's keystore when another game's keystore is needed (APK suite).")]
        public string[] keystoreExtensions = { ".keystore", ".jks" };
    }

    /// <summary>GenZ-ApkTestSuit: a release APK installed on a phone, checked from logcat.</summary>
    [Serializable]
    public class ApkSettings
    {
        [Tooltip("SDKs that must report '[GenZ] <sdk>|init|OK' (GenZLog in the ad scripts).")]
        public string[] requiredSdks = { "MAX", "AdMob", "Firebase", "RemoteConfig", "GameAnalytics", "Adjust", "AppMetrica", "UMP" };
        [Tooltip("SDKs whose start is reported, but only warned about when it fails.")]
        public string[] optionalSdks = { "IAP", "FCM" };
        [Tooltip("Firebase events that must be logged in a session.")]
        public string[] firebaseEvents = { "ad_impression" };
        [Tooltip("Ad formats a full session should show ('[GenZ] Ads|impression' detail ends with it).")]
        public string[] adFormats = { "Banner", "Interstitial", "Rewarded", "AppOpen" };
        [Tooltip("Permissions the APK's merged manifest must have.")]
        public string[] requiredPermissions = { "android.permission.INTERNET", "android.permission.ACCESS_NETWORK_STATE", "com.google.android.gms.permission.AD_ID" };
        [Tooltip("Lowest target SDK the build may have: the studio's target API level (36 = Android 16).")]
        public int minTargetSdk = 36;
        [Tooltip("Log buffer size asked of the phone (MB), so a long session isn't cut.")]
        public int logcatBufferMb = 16;

        [Header("Log patterns (regex)")]
        [Tooltip("Firebase debug log of an event; group 1 = event name.")]
        public string firebaseEventPattern = @"Logging event(?: \(FE\))?:?\s*(?:origin=\w+,\s*name=)?([A-Za-z0-9_]+)";
        public string firebaseUploadOkPattern = @"Successful upload|Upload successful|network response\. code, size: 20\d";
        public string firebaseUploadFailPattern = @"Network upload failed|Upload failed|network response\. code, size: [45]\d\d";
        public string gaTag = "GameAnalytics";
        [Tooltip("A GameAnalytics log line that means an event was added or sent.")]
        public string gaEventPattern = @"(?i)\b(add(ed|ing)?|sen[dt](ing)?)\b.*\bevents?\b";
        public string gaErrorPattern = @"(?i)\b(error|fail(ed|ure)?|invalid)\b";
        [Tooltip("Build value inside a GameAnalytics event (verbose log); group 1 = build.")]
        public string gaBuildPattern = "\"build\"\\s*:\\s*\"([^\"]+)\"";
        public string adjustTag = "Adjust";
    }

    /// <summary>
    /// GenZ-DeepTestSuit: one SDK setting the Test Environment changes and the Release Environment puts back.
    /// The object or asset is found with the config rule named <see cref="rule"/>, so per-game names stay in the rules.
    /// </summary>
    [Serializable]
    public class EnvSetting
    {
        [Tooltip("Off = left as it is by both buttons.")]
        public bool enabled = true;
        public string label;
        [Tooltip("Label of the config rule that finds the object or asset, e.g. 'Adjust' or 'GameAnalytics Settings'.")]
        public string rule;
        [Tooltip("Serialized field names to try. Matching ignores case, '_' and '-'.")]
        public string[] fieldNames;
        [Tooltip("Value in the Test Environment: bool 'true'/'false', enum name, string or number.")]
        public string testValue;
        [Tooltip("Value in the Release Environment.")]
        public string releaseValue;
        [Tooltip("Why the test value helps; shown in the window.")]
        public string note = "";

        public EnvSetting(string label, string rule, string[] fieldNames, string testValue, string releaseValue, string note, bool enabled = true)
        {
            this.label = label;
            this.rule = rule;
            this.fieldNames = fieldNames;
            this.testValue = testValue;
            this.releaseValue = releaseValue;
            this.note = note;
            this.enabled = enabled;
        }
    }

    /// <summary>
    /// One row of the phone's test panel. Its buttons call the game's own ad scripts by name ("Class.Method"),
    /// through reflection, so the game's ad code is what gets tested. The instance is the class's static
    /// instance / _instance / Instance, or the object in the scene. A method that takes the reward delegate gets the
    /// panel's, which logs that the game's reward callback was reached.
    /// </summary>
    [Serializable]
    public class DeepAction
    {
        public string name;
        [Tooltip("'Class.Method' that loads (requests) the ad. Empty = no Load button.")]
        public string load = "";
        [Tooltip("'Class.Method' or 'Class.field' returning bool: is the ad ready. Asked only when Show is pressed (some readiness methods also request an ad).")]
        public string ready = "";
        [Tooltip("'Class.Method' that shows the ad.")]
        public string show = "";
        [Tooltip("'Class.Method' that hides it (banners). Empty = no Hide button.")]
        public string hide = "";
        [Tooltip("On = Show does nothing when 'ready' says false (so a waterfall method can't fall through to the other network).")]
        public bool requireReady;
        [Tooltip("'Class.Method' calls made just before Show, separated by ';' (e.g. hide the other network's banner).")]
        public string before = "";
        [Tooltip("'Class.field=value' set only while Show runs, then put back; separated by ';' (e.g. force the vendor switch to MAX first).")]
        public string showFields = "";
        [Tooltip("'Class.field' delegate set to the panel's reward callback before Show, for a show method that calls it without taking it as a parameter.")]
        public string rewardField = "";

        public DeepAction(string name) { this.name = name; }
        public DeepAction Load(string m) { load = m; return this; }
        public DeepAction Ready(string m, bool require = true) { ready = m; requireReady = require; return this; }
        public DeepAction Show(string m) { show = m; return this; }
        public DeepAction Hide(string m) { hide = m; return this; }
        public DeepAction Before(string calls) { before = calls; return this; }
        public DeepAction During(string fields) { showFields = fields; return this; }
        public DeepAction Reward(string field) { rewardField = field; return this; }
    }

    /// <summary>GenZ-DeepTestSuit: the Test / Release Environment switch and the phone's shake panel.</summary>
    [Serializable]
    public class DeepTestSettings
    {
        [Tooltip("Scripting define that compiles the phone's test panel in. Only set in the Test Environment.")]
        public string define = "GENZ_DEEP_TEST";
        [Tooltip("MaxSdk.SetVerboseLogging(true) before MAX starts.")]
        public bool maxVerboseLogging = true;
        [Tooltip("MAX Creative Debugger: a two-finger double tap on a showing MAX ad tells which network served it.")]
        public bool maxCreativeDebugger = true;
        [Tooltip("Register the phone as an AdMob test device: the real ad unit IDs are requested, but Google serves test ads, so clicks on your own ads are safe.")]
        public bool admobTestDevice = true;
        [Tooltip("Shake strength (in g) that opens or closes the panel.")]
        public float shakeThreshold = 2.0f;

        public List<EnvSetting> settings = DefaultSettings();
        [Tooltip("The panel's rows: each calls the game's own ad methods.")]
        public List<DeepAction> actions = DefaultActions();
        [Tooltip("'Class.field' values shown live at the top of the panel, e.g. the interstitial cooldown.")]
        public string[] watch = { "AdsGapTimer.canShowAd", "AdsGapTimer.time", "AdsGapTimer.timeLeft", "AdsManager.useSdkVendorSwitch", "AdsManager.isBannerShowing", "AdMob.isPausedDuetoAd" };

        public static List<EnvSetting> DefaultSettings() => new List<EnvSetting>
        {
            new EnvSetting("Adjust environment", "Adjust", new[] { "environment" }, "Sandbox", "Production",
                "Sandbox: test installs and events show in Adjust's Testing Console, not in the live numbers."),
            new EnvSetting("Adjust log level", "Adjust", new[] { "logLevel" }, "Verbose", "Info",
                "Every Adjust request and response in logcat (tag Adjust)."),
            new EnvSetting("AppMetrica logs", "AppMetrica", new[] { "Logs" }, "true", "false",
                "AppMetrica's own log in logcat (tag AppMetrica)."),
            new EnvSetting("GameAnalytics verbose log", "GameAnalytics Settings", new[] { "VerboseLogBuild" }, "true", "false",
                "Every GameAnalytics event and its JSON in logcat."),
            new EnvSetting("GameAnalytics info log", "GameAnalytics Settings", new[] { "InfoLogBuild" }, "true", "true",
                "Kept on in release too: the APK Test Suit reads it."),
            new EnvSetting("UMP consent test mode", "UmpManager (AdMob consent)", new[] { "isTestMode", "testMode", "isTestModeOn", "debugMode" }, "true", "false",
                "Shows the EEA consent form on the test devices listed in UmpManager. Off by default: it only works on those devices.", enabled: false),
        };

        // The studio's AdsScripts-GenzAds method names. A game with other names only needs these edited in the config.
        public static List<DeepAction> DefaultActions() => new List<DeepAction>
        {
            // The game's real flow: MAX first, AdMob fallback, vendor switch.
            new DeepAction("Interstitial (game waterfall)").Show("AdsManager.ShowInterstitial"),
            new DeepAction("Rewarded (game waterfall)").Show("AdsManager.ShowRewardedAd"),

            // One network at a time.
            new DeepAction("MAX Interstitial").Load("AdsManager.RequestInter").Ready("AdsManager.IsMaxInterstitialReady").Show("AdsManager.ShowMaxInterstitial"),
            new DeepAction("MAX Rewarded").Load("AdsManager.RequestVideo").Ready("AdsManager.IsMaxRewardedReady")
                .Show("AdsManager.ShowRewardedAd").During("AdsManager.useSdkVendorSwitch=0"), // switch 0 + MAX ready = MAX
            new DeepAction("MAX Banner").Show("AdsManager.ShowBanner").Hide("AdsManager.HideBanner").Before("AdMob.bannerHide"),
            new DeepAction("MAX MREC").Show("AdsManager.ShowMRECBanner").Hide("AdsManager.HideMRECBanner"),
            new DeepAction("AdMob Interstitial").Load("AdMob.RequestInterstitial").Ready("AdMob.HasAdmobInterstialAvaible").Show("AdMob.ShowInterstialAd"),
            new DeepAction("AdMob Rewarded").Load("AdMob.RequesRewardAd").Ready("AdMob.HasRewardedAvaiable").Show("AdMob.ShowRewardAd")
                .Reward("AdsManager._rewarded"), // AdMob.ShowRewardAd calls AdsManager.instance._rewarded()
            new DeepAction("AdMob Banner").Load("AdMob.RequestBanner").Show("AdMob.bannerShow").Hide("AdMob.bannerHide")
                .Before("AdsManager.HideBanner").During("AdsManager.isBannerShowing=false"), // bannerShow hides itself while MAX's banner is up
            new DeepAction("AdMob App Open").Load("AppOpen_Code.LoadOpenApp").Show("AppOpen_Code.ShowAppOpenAdIfAvailable")
                .During("AdMob.isPausedDuetoAd=false"), // otherwise it skips the show and only clears the flag
        };
    }

    /// <summary>
    /// Step 1: the privacy policy and More Games links the game opens, against its store account in the sheet
    /// (games tab "Store Name" → tab "Stors": "Privicy Policy" and "Link").
    /// </summary>
    [Serializable]
    public class StoreLinkSettings
    {
        public bool enabled = true;
        [Tooltip("Games-tab columns naming the game's live store account (Play Console developer). Also the store column of the stores table.")]
        public string[] storeColumns = { "Store Name", "Store" };
        [Tooltip("The games tab has two \"Store Name\" columns: C (older) and U (the current live store, added 2026-11-20). "
                 + "On = the last non-empty one is used, and an empty last one falls back to the earlier with a warning. Off = the first.")]
        public bool useLastStoreColumn = true;
        [Tooltip("Stores-table columns holding the privacy policy URL; the first one the table has is used. \"Privicy Policy New\" is left out "
                 + "on purpose: the studio sheet marks it as not confirmed yet.")]
        public string[] privacyColumns = { "Privicy Policy", "Privacy Policy", "Privacy Policy URL" };
        [Tooltip("Stores-table columns holding the store's developer page (the More Games link).")]
        public string[] moreGamesColumns = { "Link", "Store Link", "More Games", "More Games Link" };
        [Tooltip("Regex (case ignored): a button / UnityEvent calling a method with this in its name and a text argument is a privacy policy link.")]
        public string privacyMethodPattern = "privacy|policy";
        [Tooltip("Regex (case ignored): a web address with this in it (button text argument or a string in the game's scripts) is a privacy policy link.")]
        public string privacyUrlPattern = "privacy|privicy|policy";
        [Tooltip("Regex (case ignored): a button / UnityEvent calling a method with this in its name and a text argument is a More Games link.")]
        public string moreGamesMethodPattern = @"more_?games?|other_?games|developer|publisher";
        [Tooltip("Regex (case ignored): an address like this (button text argument or a string in the game's scripts) is a More Games link: "
                 + "a Play or App Store developer page.")]
        public string moreGamesUrlPattern = @"play\.google\.com/store/apps/dev|market://(dev\?|search\?q=pub:)|apps\.apple\.com/.*developer/|itms-apps://.*developer";
        [Tooltip("Games-tab columns holding the iOS app's App Store ID (the number in apps.apple.com/app/id<number>). The iOS Rate Us link must open it.")]
        public string[] appStoreIdColumns = { "App ID", "Apple ID", "App Store ID" };
        [Tooltip("Regex (case ignored): a button / UnityEvent calling a method with this in its name and a text argument is a Rate Us link.")]
        public string rateUsMethodPattern = @"rate_?(us|app|game|now|it)|^rate|review|store_?page";
        [Tooltip("Regex (case ignored): an address like this is a Rate Us link: the game's own store page (Play details?id= / market://details, "
                 + "App Store app/id…). Checked after the More Games pattern, so a developer page never counts as Rate Us.")]
        public string rateUsUrlPattern = @"play\.google\.com/store/apps/details|market://details|apps\.apple\.com/.*\bid\d|itunes\.apple\.com/.*\bid\d|itms-apps?s?://.*\bid\d";
        [Tooltip("Regex (case ignored): a text field (a script's Inspector field, e.g. privacyPolicyUrl / moreGamesLink) whose name matches the "
                 + "privacy or More Games method pattern AND this one holds such a link, even when its text is empty or not recognised.")]
        public string linkFieldPattern = "url|link";
        [Tooltip("Folders skipped by the link scan (SDKs, plugins' demos, this suite). Everything else under Assets is read: "
                 + "scenes, prefabs, ScriptableObject assets and scripts.")]
        public string[] excludeFolders =
        {
            "GoogleMobileAds", "MaxSdk", "Firebase", "GameAnalytics", "Adjust", "AppMetrica", "ExternalDependencyManager",
            "Dreamteck", "Demigiant", "NiceVibrations", "TextMesh Pro", "GenZ-TestSuit", "GenZ-DeepTest"
        };
    }

    /// <summary>A tab of the IDs spreadsheet to read. Matched by name; the gid is only used when the tab list can't be read.</summary>
    [Serializable]
    public class SheetTab
    {
        public string name;
        [Tooltip("The tab's gid (the number after #gid= in its link). Optional: used only when the tab names can't be read.")]
        public string gid;
        [Tooltip("Read only for a value found by pattern (the shared MAX SDK key, cell B1 of the Android MAX tab): its rows are never "
                 + "used as the game's IDs. iOS games use the same AppLovin account, and the iOS tabs have no SDK key.")]
        public bool keyOnly;

        public SheetTab(string name, string gid, bool keyOnly = false) { this.name = name; this.gid = gid; this.keyOnly = keyOnly; }
    }

    /// <summary>Where a rule's component or asset is looked up.</summary>
    public enum RuleTarget
    {
        AdsObject,   // a component on the Ads object
        SceneObject, // a GameObject found by name in the SDK scene (e.g. Adjust, UmpManager)
        Asset        // a ScriptableObject settings asset (e.g. GoogleMobileAdsSettings)
    }

    [Serializable]
    public class FieldRule
    {
        public string label;
        [Tooltip("Serialized field names to try. Matching ignores case, '_' and '-'.")]
        public string[] fieldNames;
        public FieldKind kind;
        [Tooltip("Off = a missing field or empty value is a warning instead of a failure.")]
        public bool required = true;
        [Tooltip("Google Sheet column headers to try for this value. Exact match, ignoring case, spaces and symbols.")]
        public string[] sheetColumns;
        [Tooltip("Kind = Expected: the required value (bool 'true'/'false', enum name, string or number).")]
        public string expectedValue = "";
        [Tooltip("For per-platform lists (GameAnalytics keys): the list naming each index's platform, e.g. 'Platforms'. Empty = plain field.")]
        public string platformListField = "";
        [Tooltip("Platform to pick from platformListField, e.g. 'Android'.")]
        public string platformName = "";
        [Tooltip("'<Rule label>/<Field label>' of another field that must hold the same value, e.g. 'Google Mobile Ads Settings/Android App ID'.")]
        public string sameAs = "";
        [Tooltip("iOS build target: field names to read instead of fieldNames (e.g. adMobIOSAppId). Empty = fieldNames on both platforms.")]
        public string[] iosFieldNames = new string[0];
        [Tooltip("iOS build target: the platform to pick from platformListField instead of platformName ('|' between accepted names, e.g. 'IPhonePlayer|iOS').")]
        public string iosPlatformName = "";

        public FieldRule(string label, FieldKind kind, string[] fieldNames, string[] sheetColumns = null, bool required = true)
        {
            this.label = label;
            this.kind = kind;
            this.fieldNames = fieldNames;
            this.sheetColumns = sheetColumns ?? new string[0];
            this.required = required;
        }

        public FieldRule Expect(string value) { expectedValue = value; return this; }
        public FieldRule ForPlatform(string listField, string platform) { platformListField = listField; platformName = platform; return this; }
        public FieldRule SameAs(string otherKey) { sameAs = otherKey; return this; }
        public FieldRule OnIos(string[] fields = null, string platform = null)
        {
            if (fields != null) iosFieldNames = fields;
            if (platform != null) iosPlatformName = platform;
            return this;
        }

        /// <summary>This rule as checked for the build target: on iOS, the iOS field names / platform, and "Android" → "iOS" in labels.</summary>
        public FieldRule For(bool ios)
        {
            if (!ios) return this;
            var c = (FieldRule)MemberwiseClone();
            if (iosFieldNames != null && iosFieldNames.Length > 0) c.fieldNames = iosFieldNames;
            if (!string.IsNullOrEmpty(iosPlatformName)) c.platformName = iosPlatformName;
            c.label = (label ?? "").Replace("Android", "iOS");
            c.sameAs = (sameAs ?? "").Replace("Android", "iOS");
            return c;
        }
    }

    [Serializable]
    public class ScriptRule
    {
        public string label;
        public RuleTarget target;
        [Tooltip("Component class names (AdsObject / SceneObject) or ScriptableObject class names (Asset) to accept.")]
        public string[] scriptNames;
        public bool required = true;

        [Tooltip("SceneObject: GameObject names to look for (case-insensitive).")]
        public string[] objectNames = new string[0];
        [Tooltip("SceneObject: the GameObject must be active (SetActive true).")]
        public bool mustBeActive;
        [Tooltip("SceneObject: warn when the object is not a prefab instance.")]
        public bool expectPrefab;

        [Tooltip("Asset: asset file names without extension.")]
        public string[] assetNames = new string[0];
        [Tooltip("Asset: the asset path must contain this folder, e.g. 'Resources/GameAnalytics'.")]
        public string assetFolder = "";

        public List<FieldRule> fields = new List<FieldRule>();

        public ScriptRule(string label, string[] scriptNames, params FieldRule[] fields)
        {
            this.label = label;
            this.target = RuleTarget.AdsObject;
            this.scriptNames = scriptNames;
            this.fields = new List<FieldRule>(fields);
        }

        public static ScriptRule SceneObject(string label, string[] objectNames, string[] scriptNames, params FieldRule[] fields) =>
            new ScriptRule(label, scriptNames, fields) { target = RuleTarget.SceneObject, objectNames = objectNames };

        public static ScriptRule Asset(string label, string[] assetNames, string[] typeNames, string assetFolder, params FieldRule[] fields) =>
            new ScriptRule(label, typeNames, fields) { target = RuleTarget.Asset, assetNames = assetNames, assetFolder = assetFolder };

        public ScriptRule Active() { mustBeActive = true; return this; }

        /// <summary>This rule as checked for the build target (see <see cref="FieldRule.For"/>).</summary>
        public ScriptRule For(bool ios)
        {
            if (!ios) return this;
            var c = (ScriptRule)MemberwiseClone();
            c.label = (label ?? "").Replace("Android", "iOS");
            c.fields = (fields ?? new List<FieldRule>()).Select(f => f.For(true)).ToList();
            return c;
        }
        public ScriptRule Prefab() { expectPrefab = true; return this; }
    }

    /// <summary>
    /// Per-project settings and rules for the GenZ Test Suit. Created automatically next to this script.
    /// Edit the rules here when a project's ad scripts use slightly different names.
    /// </summary>
    public class GenZTestSuitConfig : ScriptableObject
    {
        /// <summary>
        /// Studio IDs spreadsheet. Android: tab "Android Games ID's" (AdMob, Adjust, GameAnalytics, AppMetrica, Taichi 2.0;
        /// one row per game) and tab "Android Max ID's" (MAX unit IDs per game; the shared MAX SDK key in B1), plus tab
        /// "Stors" (each store account's developer page and privacy policy URL). The iOS tabs aren't read.
        /// </summary>
        public const string DefaultSheetUrl = "https://docs.google.com/spreadsheets/d/1nd87c3FFAHqOhtNIN2ZqVyXlNlSfsQJo2YjL1wh9UMk/edit?gid=0#gid=0";
        /// <summary>The studio sheet before 2026-10-23 (both tables on one tab). A config still pointing at it is moved to the new one.</summary>
        const string OldDefaultSheetUrl = "https://docs.google.com/spreadsheets/d/1kr1zIgQJ0fujexkbj_xgfrUb_hJv-RbllTtqTIQ7O4Y/edit?usp=sharing";

        public static List<SheetTab> DefaultSheetTabs() => new List<SheetTab>
        {
            new SheetTab("Android Games ID's", "0"),
            new SheetTab("Android Max ID's", "1078150226"),
            StoresTab(),
        };
        /// <summary>iOS build target: the iOS tabs, the stores tab, and the Android MAX tab for the shared MAX SDK key only.</summary>
        public static List<SheetTab> DefaultIosSheetTabs() => new List<SheetTab>
        {
            new SheetTab("IOS Games ID's", "223612715"),
            new SheetTab("IOS MAX Ids", "1122691852"),
            StoresTab(),
            new SheetTab("Android Max ID's", "1078150226", keyOnly: true),
        };
        /// <summary>Store account → developer page and privacy policy URL ("Store Name | Link | Privicy Policy | …").</summary>
        static SheetTab StoresTab() => new SheetTab("Stors", "405200962");
        public const string DefaultAdsObjectName = "ADs";
        /// <summary>Bump when defaults change; older config assets are upgraded on load (rules reset, scene kept).</summary>
        const int CurrentVersion = 12;

        // Must default to 0: assets saved before this field existed deserialize with the initializer value.
        [HideInInspector] public int configVersion;

        [Header("Where the SDK objects live")]
        [Tooltip("Scene holding the Ads object. Empty = auto-detect from the first build scenes (Consent / Splash).")]
        public SceneAsset sdkScene;
        [Tooltip("Name of the Ads GameObject (case-insensitive). If not found, the object is auto-detected.")]
        public string adsObjectName = DefaultAdsObjectName;

        [Header("Google Sheet verification")]
        [Tooltip("Link to the IDs sheet (normal share link, published link, or a CSV export link).")]
        public string googleSheetUrl = DefaultSheetUrl;
        [Tooltip("Tabs to read, by name (case, spaces and symbols ignored). Their tables are combined. A tab the spreadsheet doesn't have is skipped; none found (or empty list) = the tab in the link.")]
        public List<SheetTab> sheetTabs = DefaultSheetTabs();
        [Tooltip("The tabs read when the build target is iOS (the iOS apps' IDs). Same rules as Sheet Tabs.")]
        public List<SheetTab> iosSheetTabs = DefaultIosSheetTabs();
        public bool verifyAgainstSheet = true;
        [Tooltip("Sheet headers that identify the package name column.")]
        public string[] packageColumnNames = { "Package Name", "Package", "Bundle ID", "Bundle Identifier", "Application ID" };

        [Header("What to run")]
        public bool runIdChecks = true;
        public bool runIntegrationChecks = true;
        public IntegrationSettings integration = new IntegrationSettings();
        public AndroidPlayerRules playerSettings = new AndroidPlayerRules();
        public SigningSettings signing = new SigningSettings();
        public TaichiSettings taichi = new TaichiSettings();
        public StoreLinkSettings storeLinks = new StoreLinkSettings();
        public CodeCheckSettings codeChecks = new CodeCheckSettings();
        public ApkSettings apk = new ApkSettings();
        public DeepTestSettings deepTest = new DeepTestSettings();

        [Header("Rules")]
        public List<ScriptRule> rules = DefaultRules();

        /// <summary>The sheet tabs for the build target: iOS games have their own tabs.</summary>
        public List<SheetTab> SheetTabsFor(bool ios) => ios ? iosSheetTabs : sheetTabs;
        public static bool IosTarget => EditorUserBuildSettings.activeBuildTarget == BuildTarget.iOS;

        public string EffectiveSheetUrl => string.IsNullOrWhiteSpace(googleSheetUrl) ? DefaultSheetUrl : googleSheetUrl.Trim();

        /// <summary>Package name of the active build target (Build Settings platform → Player Settings).</summary>
        public static string PackageName =>
            PlayerSettings.GetApplicationIdentifier(BuildPipeline.GetBuildTargetGroup(EditorUserBuildSettings.activeBuildTarget));

        public static List<ScriptRule> DefaultRules()
        {
            return new List<ScriptRule>
            {
                // ---- Scripts on the Ads object
                new ScriptRule("AdsIds (In-App IDs)", new[] { "AdsIds", "AdIds", "AdsID" },
                    new FieldRule("In-App product IDs", FieldKind.InAppList,
                        new[] { "InAppIds", "InAppKeys", "InAppProducts", "Products", "ProductIds" })), // the studio sheet has no product-ID column ("InApp Key" is the Play licensing key)

                new ScriptRule("AdMob", new[] { "AdMob", "Admob", "AdmobManager", "AdMobManager" },
                    new FieldRule("Banner ID", FieldKind.AdMobUnitId,
                        new[] { "bannerID", "bannerAdUnitId", "admobBannerId" },
                        new[] { "admob banner", "AdMob Banner ID" }),
                    new FieldRule("Interstitial ID", FieldKind.AdMobUnitId,
                        new[] { "interstitialID", "interID", "interstitialAdUnitId", "admobInterstitialId" },
                        new[] { "admob int", "AdMob Interstitial", "AdMob Inter" }),
                    new FieldRule("Rewarded ID", FieldKind.AdMobUnitId,
                        new[] { "rewardID", "rewardedID", "rewardedAdUnitId", "admobRewardedId" },
                        new[] { "admob rewarded", "AdMob Reward" }),
                    new FieldRule("Test IDs switch is OFF", FieldKind.MustBeFalse,
                        new[] { "isTestIdOn", "useTestIds", "testMode" }, required: false)),

                new ScriptRule("AdsManager (AppLovin MAX)", new[] { "AdsManager", "AdManager", "MaxAdsManager" },
                    new FieldRule("MAX SDK Key", FieldKind.MaxSdkKey,
                        new[] { "maxSdkKey", "sdkKey", "MaxSdkKey" },
                        new[] { "MAX SDK Key", "Applovin SDK Key", "SDK Key" }), // studio sheet: one unlabeled 86-char cell, found by pattern
                    new FieldRule("MAX Interstitial ID", FieldKind.MaxUnitId,
                        new[] { "InterstitialAdUnitId_Android", "InterstitialAdUnitId", "InterstitialAdUnitId_IOS", "interAdUnitId" },
                        new[] { "Int", "MAX Int", "MAX Interstitial" }),
                    new FieldRule("MAX Banner ID", FieldKind.MaxUnitId,
                        new[] { "BannerAdUnitId", "BannerAdUnitId_Android", "BannerAdUnitId_IOS" },
                        new[] { "Banner", "MAX Banner" }),
                    new FieldRule("MAX Rewarded ID", FieldKind.MaxUnitId,
                        new[] { "RewardedAdUnitId_Android", "RewardedAdUnitId", "RewardedAdUnitId_IOS", "rewardAdUnitId" },
                        new[] { "Rewarded", "MAX Rewarded" }),
                    new FieldRule("MAX MREC ID", FieldKind.MaxUnitId,
                        new[] { "MRecAdUnitId", "MrecAdUnitId_Android", "MRecAdUnitId_IOS" },
                        new[] { "MRec", "MAX MREC" })),

                new ScriptRule("AppOpen_Code", new[] { "AppOpen_Code", "AppOpenCode", "AppOpen", "AppOpenAdManager" },
                    new FieldRule("App Open ID", FieldKind.AdMobUnitId,
                        new[] { "AppOpenId", "appOpenAdUnitId", "AdUnitId" },
                        new[] { "App Open", "AdMob App Open", "App Open ID" })),

                new ScriptRule("FirebaseManager", new[] { "FirebaseManager" }),
                new ScriptRule("RemoteConfigManager (Taichi defaults)", new[] { "RemoteConfigManager" },
                    new FieldRule("Taichi 1.0 default revenue > 0", FieldKind.NumberAboveZero, new[] { "taichi1DefaultRevenue" }, required: false),
                    new FieldRule("Taichi 2.0 default impressions > 0", FieldKind.NumberAboveZero, new[] { "taichi2DefaultImpressions" }, required: false))
                    { required = false }, // optional: without it, the values in RemoteConfigManager's code are used
                new ScriptRule("Analytics", new[] { "Analytics" }),
                new ScriptRule("unityInApp", new[] { "unityInApp", "UnityInApp", "UnityIAP", "InAppManager" }),

                // ---- Other SDK objects in the same scene
                ScriptRule.SceneObject("Adjust", new[] { "Adjust" }, new[] { "Adjust" },
                    new FieldRule("App Token", FieldKind.AdjustToken,
                        new[] { "appToken", "token" },
                        new[] { "Adjust Token", "Adjust App Token" }),
                    new FieldRule("Environment is Production", FieldKind.Expected,
                        new[] { "environment" }).Expect("Production")).Prefab(),

                ScriptRule.SceneObject("AppMetrica", new[] { "AppMetrica" }, new[] { "AppMetrica" },
                    new FieldRule("API Key", FieldKind.AppMetricaApiKey,
                        new[] { "ApiKey", "apiKey", "key" },
                        new[] { "AppMatrica", "AppMetrica", "AppMetrica Key", "AppMetrica API Key" })).Prefab(),

                ScriptRule.SceneObject("GameAnalytics (object)", new[] { "GameAnalytics" }, new[] { "GameAnalytics" }).Prefab(),

                ScriptRule.SceneObject("AdsGapTimer (interstitial cooldown)", new[] { "AdsGapTimer", "AdGapTimer", "AdsTimer" }, new[] { "AdsGapTimer", "AdGapTimer" },
                    new FieldRule("Default cooldown > 0", FieldKind.NumberAboveZero,
                        new[] { "timeLeft", "gapTime", "cooldown" })),

                ScriptRule.SceneObject("UmpManager (AdMob consent)", new[] { "UmpManager", "UMPManager", "UMP" }, new[] { "UmpManager", "UMPManager" },
                    new FieldRule("Test Mode is OFF", FieldKind.MustBeFalse,
                        new[] { "isTestMode", "testMode", "isTestModeOn", "debugMode" })).Active(),

                // ---- Settings assets
                ScriptRule.Asset("GameAnalytics Settings", new[] { "Settings" }, new[] { "Settings" }, "Resources/GameAnalytics",
                    new FieldRule("Game Key (Android)", FieldKind.GameAnalyticsGameKey,
                        new[] { "gameKey" }, new[] { "GA Game Key", "GameAnalytics Game Key" }).ForPlatform("Platforms", "Android").OnIos(platform: "IPhonePlayer|iOS"),
                    new FieldRule("Secret Key (Android)", FieldKind.GameAnalyticsSecretKey,
                        new[] { "secretKey" }, new[] { "GA Secret Key", "GameAnalytics Secret Key" }).ForPlatform("Platforms", "Android").OnIos(platform: "IPhonePlayer|iOS"),
                    new FieldRule("Send Version is ON", FieldKind.MustBeTrue,
                        new[] { "UsePlayerSettingsBuildNumber" })),

                ScriptRule.Asset("Google Mobile Ads Settings", new[] { "GoogleMobileAdsSettings" }, new[] { "GoogleMobileAdsSettings" }, "Resources",
                    new FieldRule("Android App ID", FieldKind.AdMobAppId,
                        new[] { "adMobAndroidAppId" }, new[] { "Admob AppID", "AdMob App ID" }).OnIos(new[] { "adMobIOSAppId" }),
                    new FieldRule("Optimize Initialization is ON", FieldKind.MustBeTrue,
                        new[] { "optimizeInitialization" }),
                    new FieldRule("Optimize Ad Loading is ON", FieldKind.MustBeTrue,
                        new[] { "optimizeAdLoading" })),

                ScriptRule.Asset("AppLovin Settings (Integration Manager)", new[] { "AppLovinSettings" }, new[] { "AppLovinSettings" }, "Resources",
                    new FieldRule("SDK Key", FieldKind.MaxSdkKey,
                        new[] { "sdkKey" }, new[] { "MAX SDK Key", "Applovin SDK Key", "SDK Key" })
                        .SameAs("AdsManager (AppLovin MAX)/MAX SDK Key"),
                    new FieldRule("AdMob Android App ID", FieldKind.AdMobAppId,
                        new[] { "adMobAndroidAppId" }, new[] { "Admob AppID", "AdMob App ID" }).OnIos(new[] { "adMobIosAppId", "adMobIOSAppId" })
                        .SameAs("Google Mobile Ads Settings/Android App ID")),
            };
        }

        void UpgradeIfOld()
        {
            if (configVersion >= CurrentVersion) return;
            rules = DefaultRules();
            codeChecks = new CodeCheckSettings();
            integration = new IntegrationSettings();
            taichi = new TaichiSettings();
            deepTest = new DeepTestSettings();
            runIdChecks = true;
            runIntegrationChecks = true;
            if (string.IsNullOrWhiteSpace(adsObjectName)) adsObjectName = DefaultAdsObjectName;
            if (string.IsNullOrWhiteSpace(googleSheetUrl) || googleSheetUrl.Trim() == OldDefaultSheetUrl) googleSheetUrl = DefaultSheetUrl;
            if (sheetTabs == null || sheetTabs.Count == 0) sheetTabs = DefaultSheetTabs();
            if (iosSheetTabs == null || iosSheetTabs.Count == 0) iosSheetTabs = DefaultIosSheetTabs(); // v12
            else if (!sheetTabs.Any(t => t != null && GenZSheetReader.Normalize(t.name) == GenZSheetReader.Normalize(StoresTab().name)))
                sheetTabs.Add(StoresTab()); // v11: privacy policy and More Games links per store
            storeLinks = new StoreLinkSettings();
            configVersion = CurrentVersion;
            EditorUtility.SetDirty(this);
            AssetDatabase.SaveAssets();
            Debug.Log("[GenZ Test Suit] Config upgraded to v" + CurrentVersion + " (rules reset to the new defaults).");
        }

        public static GenZTestSuitConfig LoadOrCreate()
        {
            foreach (var guid in AssetDatabase.FindAssets("t:" + nameof(GenZTestSuitConfig)))
            {
                var found = AssetDatabase.LoadAssetAtPath<GenZTestSuitConfig>(AssetDatabase.GUIDToAssetPath(guid));
                if (found == null) continue;
                found.UpgradeIfOld();
                return found;
            }

            var cfg = CreateInstance<GenZTestSuitConfig>();
            cfg.configVersion = CurrentVersion;
            // The package folder is read-only, so each game keeps its own config under Assets.
            if (!AssetDatabase.IsValidFolder(ProjectFolder)) AssetDatabase.CreateFolder("Assets", ProjectFolder.Substring("Assets/".Length));
            AssetDatabase.CreateAsset(cfg, ProjectFolder + "/GenZTestSuitConfig.asset");
            AssetDatabase.SaveAssets();
            return cfg;
        }

        /// <summary>The game's own GenZ Test Suit folder, where a new config asset is created.</summary>
        public const string ProjectFolder = "Assets/GenZ-TestSuit";
    }
}
