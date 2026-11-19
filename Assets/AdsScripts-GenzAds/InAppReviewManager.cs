// GenzMonetization-v1.33
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.iOS;

#if UNITY_ANDROID
using Google.Play.Review;
#elif UNITY_IOS
UnityEngine.iOS;
#endif

public class InAppReviewManager : MonoBehaviour
{
    public static InAppReviewManager instance;
    private ReviewManager _reviewManager;
    private PlayReviewInfo _playReviewInfo;


    void Start()
    {
        instance = this;
    }
    public void Review()
    {
#if UNITY_ANDROID
        StartCoroutine(RequestReview());
#elif UNITY_IOS
UnityEngine.iOS;
         Device.RequestStoreReview();
#endif

       

    }

    // Seconds (real time) after the review flow ends before the App Open skip flag is cleared, if
    // returning to the game didn't already clear it. The review card is an overlay, so the app
    // often never goes to background, and Google's quota often shows no card at all.
    private const float AppOpenSkipClearDelay = 2f;

    IEnumerator RequestReview()
    {
        // Skip the App Open ad when the player returns from the review card
        // (AppOpen_Code.ShowAppOpenAdIfAvailable clears the flag).
        if (AdsManager.instance != null) AdsManager.instance.isPausedDuetoAd = true;
        _reviewManager = new ReviewManager();

        var requestFlowOperation = _reviewManager.RequestReviewFlow();
        yield return requestFlowOperation;
        if (requestFlowOperation.Error != ReviewErrorCode.NoError)
        {
            // Log error. For example, using requestFlowOperation.Error.ToString().
            ClearAppOpenSkip(); // nothing was shown
            yield break;
        }
        _playReviewInfo = requestFlowOperation.GetResult();


        var launchFlowOperation = _reviewManager.LaunchReviewFlow(_playReviewInfo);
        yield return launchFlowOperation;
        _playReviewInfo = null; // Reset the object

        yield return new WaitForSecondsRealtime(AppOpenSkipClearDelay);
        ClearAppOpenSkip();
    }

    private void ClearAppOpenSkip()
    {
        if (AdsManager.instance != null) AdsManager.instance.isPausedDuetoAd = false;
    }

}
