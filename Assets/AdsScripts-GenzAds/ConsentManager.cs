// GenzMonetization-v1.33
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Serialization;
#if UNITY_IOS
using Unity.Advertisement.IosSupport; // com.unity.ads.ios-support: only compiled for Editor and iOS
#endif

/// <summary>
/// First scene: shows the consent screen in the first session, starts the analytics and monetization objects in the
/// order the two switches set, and loads the next scene once Firebase Remote Config is fetched or Remote Fetch Timeout
/// passes (whichever comes first). Monetization never starts before consent.
///
/// waitForConsentToLoadFirebase  On: analytics start once consent is given (first session: on Accept; later sessions: at launch).
///                               Off: analytics start at launch, before consent.
/// waitForFirebaseForAdLoading   On: after consent, monetization starts once Remote Config is fetched or the timeout passes.
///                               Off: monetization starts as soon as consent is given.
///
/// iOS only, before all of the above: Apple's App Tracking Transparency prompt (first session; later sessions read the
/// saved answer from the system). PlayerPrefs "UMP" = 1 when tracking is allowed, 0 when not; UmpManager shows Google's
/// consent form only when it's 1, and waits for this answer (AttPending) before it starts.
/// </summary>
public class ConsentManager : MonoBehaviour
{
    const string ConsentKey = "isConsentGiven";

    public GameObject _consentScreen;
    public GameObject _loadingScreen;

    [Tooltip("Firebase + RemoteConfigManager, Adjust, AppMetrica, GameAnalytics, FCM… Keep them inactive in the scene: this script starts them.")]
    public GameObject[] analyticalObjects;
    [Tooltip("Ads, UnityIAP… Keep them inactive in the scene: this script starts them, never before consent.")]
    public GameObject[] monetizationObjects;

    [Tooltip("On: analytics objects start only once consent is given. Off: they start at launch, before consent.")]
    public bool waitForConsentToLoadFirebase = true;
    [Tooltip("On: after consent, monetization objects start only once Remote Config is fetched or Remote Fetch Timeout passes. "
             + "Off: they start as soon as consent is given.")]
    public bool waitForFirebaseForAdLoading = false;
    [Tooltip("Longest wait (seconds, real time) for Firebase Remote Config before loading the next scene "
             + "(and before starting monetization when Wait For Firebase For Ad Loading is on).")]
    public float remoteFetchTimeout = 5f;

    // Values match the old bool loadTargetSceneByBuildIndex (false = 0, true = 1), so scenes saved with it keep their choice.
    public enum SceneLoadMode { SceneName = 0, BuildIndex = 1 }

    [Tooltip("Load the next scene by its Build Settings index or by its name.")]
    [FormerlySerializedAs("loadTargetSceneByBuildIndex")]
    public SceneLoadMode loadTargetSceneBy = SceneLoadMode.BuildIndex;
    [Tooltip("Build Settings index of the next scene (used when Load Target Scene By = Build Index).")]
    public int targetSceneBuildIndex = 0;
    [Tooltip("Name of the next scene, as in Build Settings (used when Load Target Scene By = Scene Name).")]
    public string targetSceneName = "";

    /// <summary>PlayerPrefs key UmpManager reads on iOS: 1 = tracking allowed (show Google's consent form), 0 = not allowed.</summary>
    public const string AttKey = "UMP";

    /// <summary>iOS: true while the App Tracking Transparency answer is awaited. UmpManager waits until it's false.</summary>
    public static bool AttPending { get; private set; }

    [Tooltip("iOS: longest wait (seconds, real time) for Apple's tracking prompt to be answered. Only matters if the prompt "
             + "never appears; time with the prompt open doesn't count, because the game is paused then.")]
    public float attPromptTimeout = 30f;

    bool analyticsStarted, monetizationStarted, proceeding;

    static bool ConsentGiven => PlayerPrefs.GetInt(ConsentKey, 0) == 1;

    void Awake()
    {
        Application.targetFrameRate = 60;
#if UNITY_IOS
        // iOS: Apple's tracking prompt comes first; the consent screen, analytics and ads come after it.
        AttPending = true;
        ShowConsentScreen(false);
        StartCoroutine(RequestTrackingThenStart());
#else
        StartFlow();
#endif
    }

#if UNITY_IOS
    IEnumerator RequestTrackingThenStart()
    {
        var status = ATTrackingStatusBinding.GetAuthorizationTrackingStatus();
        bool device = Application.platform == RuntimePlatform.IPhonePlayer;
        if (device && status == ATTrackingStatusBinding.AuthorizationTrackingStatus.NOT_DETERMINED)
        {
            ATTrackingStatusBinding.RequestAuthorizationTracking();
            float end = Time.realtimeSinceStartup + Mathf.Max(0f, attPromptTimeout);
            do
            {
                yield return null; // the game is paused while the prompt is open; this resumes after the answer
                status = ATTrackingStatusBinding.GetAuthorizationTrackingStatus();
            }
            while (status == ATTrackingStatusBinding.AuthorizationTrackingStatus.NOT_DETERMINED && Time.realtimeSinceStartup < end);
        }

        // Editor with the iOS target has no prompt: treated as allowed so UMP can be tested.
        bool allowed = !device || status == ATTrackingStatusBinding.AuthorizationTrackingStatus.AUTHORIZED;
        PlayerPrefs.SetInt(AttKey, allowed ? 1 : 0);
        PlayerPrefs.Save();
        GenZLog.Info("ATT", "status", (device ? status.ToString() : "Editor") + (allowed ? ", UMP may show" : ", UMP skipped"));
        AttPending = false;
        StartFlow();
    }
#endif

    void StartFlow()
    {
        if (!waitForConsentToLoadFirebase)
            ActivateAnalytics(); // starts at launch, whether or not consent is given

        if (ConsentGiven)
            StartCoroutine(AfterConsent()); // later sessions: no consent screen
        else
            ShowConsentScreen(true);
    }

    public void OnClickAcceptConsent()
    {
        if (proceeding) return; // double tap
        PlayerPrefs.SetInt(ConsentKey, 1);
        PlayerPrefs.Save();
        StartCoroutine(AfterConsent());
    }

    IEnumerator AfterConsent()
    {
        proceeding = true;
        ShowConsentScreen(false);

        ActivateAnalytics(); // no-op if it already started at launch
        if (!waitForFirebaseForAdLoading)
            ActivateMonetization();

        // Remote Config: wait until FirebaseManager reports the fetch (success or failure), at most remoteFetchTimeout.
        float end = Time.realtimeSinceStartup + Mathf.Max(0f, remoteFetchTimeout);
        while (!FirebaseManager.isFetchCompleted && Time.realtimeSinceStartup < end)
            yield return null;

        ActivateMonetization(); // no-op if it already started
        LoadNextScene();
    }

    void ShowConsentScreen(bool show)
    {
        if (_consentScreen != null) _consentScreen.SetActive(show);
        if (_loadingScreen != null) _loadingScreen.SetActive(!show);
    }

    public void ActivateAnalytics()
    {
        if (analyticsStarted) return;
        analyticsStarted = true;
        Activate(analyticalObjects);
    }

    public void ActivateMonetization()
    {
        if (monetizationStarted || !ConsentGiven) return; // never before consent
        monetizationStarted = true;
        Activate(monetizationObjects);
    }

    static void Activate(GameObject[] objects)
    {
        if (objects == null) return;
        foreach (var obj in objects)
            if (obj != null) obj.SetActive(true);
    }

    void LoadNextScene()
    {
        if (loadTargetSceneBy == SceneLoadMode.BuildIndex)
            SceneManager.LoadScene(targetSceneBuildIndex);
        else
            SceneManager.LoadScene(targetSceneName);
    }

    public void OpenPrivacyLink(string privacyUrlLink)
    {
        Application.OpenURL(privacyUrlLink);
    }
}
