// GenzMonetization-v1.33
using UnityEngine;
using UnityEngine.UI;

public class AdsGapTimer : MonoBehaviour
{
    public float timeLeft = 20;
    public float time;
    public bool canShowAd = false;

    public static AdsGapTimer instance;
    private void Awake()
    {
        if (instance)
        {
            DestroyImmediate(gameObject);
        }
        else
        {
            instance = this;
            DontDestroyOnLoad(this.gameObject);
        }
    }


    void Update()
    {
        if (canShowAd == false)
        {
            time += Time.deltaTime;
            if (time >= timeLeft)
            {
                canShowAd = true;
            }
        }
    }

    public void ResetTimer()
    {
        time = 0;
        canShowAd = false;
    }


}
