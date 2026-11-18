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

    IEnumerator RequestReview()
    {

        AdsManager.instance.isPausedDuetoAd = true;
        _reviewManager = new ReviewManager();

        var requestFlowOperation = _reviewManager.RequestReviewFlow();
        yield return requestFlowOperation;
        if (requestFlowOperation.Error != ReviewErrorCode.NoError)
        {
            // Log error. For example, using requestFlowOperation.Error.ToString().
            yield break;
        }
        _playReviewInfo = requestFlowOperation.GetResult();


        var launchFlowOperation = _reviewManager.LaunchReviewFlow(_playReviewInfo);
        yield return launchFlowOperation;
        _playReviewInfo = null; // Reset the object
        if (launchFlowOperation.Error != ReviewErrorCode.NoError)
        {
            // Log error. For example, using requestFlowOperation.Error.ToString().
            yield break;
        }

    }

}
