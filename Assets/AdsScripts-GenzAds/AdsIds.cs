
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Advertisements;
using UnityEngine.Purchasing;
using UnityEngine.SceneManagement;

public class AdsIds : MonoBehaviour
{

    [Header("Unity InApp Define")]
    public InAppKeys[] InAppIds;

    public GameObject inAppGameObject;
    public static GameObject inappObj;
    public static AdsIds instance;

    void Awake()
    {
        if (SystemInfo.systemMemorySize <= 1024)
        {
            PlayerPrefs.SetInt("AdsStop", 1);
        }
        if (SystemInfo.systemMemorySize <= 2048)
        {
            PlayerPrefs.SetInt("MaxAdStop", 1);
        }

        if (instance == null)
            instance = this;
        else
            Destroy(gameObject);
        DontDestroyOnLoad(gameObject);
        inappObj = inAppGameObject;
    }


    #region In App Call Back
    public void AfterPurchased(int value)
    {
        if (InAppIds[value].removeads)
        {
            print("remove Ads");
           AdsManager.instance.HideMRECBanner();
            PlayerPrefs.SetInt("RemoveAds", 1);
            AdMob._instance.bannerHide();
            AdsManager.instance.HideBanner();

            SceneManager.LoadScene(SceneManager.GetActiveScene().name);
        }
        //if (InAppIds[value].unLockAll)
        //{
        //    print("UnlockALL Ads");
        //    BigBanner.instance.bannerBigBannerHide();
        //    PlayerPrefs.SetInt("unlockall", 1);
        //    PlayerPrefs.SetInt("RemoveAds", 1);
        //    AdMob._instance.bannerHide();
        //    AdsManager.instance.HideBanner();

        //    SceneManager.LoadScene(SceneManager.GetActiveScene().name);
        //}
    }

    void ReloadScene()
    {
        SceneManager.LoadScene(SceneManager.GetActiveScene().name);
    }

    #endregion

}



[System.Serializable]
public class InAppKeys
{
    public string Id;
    public ProductType Type;
    public bool removeads;
    public bool unLockAll;
    public bool vip;
    public bool solver;
    public bool solver2;
    public bool solver3;
}
