using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Serialization;


public class ConsentScreen : MonoBehaviour
{
    public GameObject _consentScreen;

    private void Awake()
    {
        if (PlayerPrefs.GetInt("_consentScreen", 0) == 0)
        {
            _consentScreen.SetActive(true);
        }
      
    }
    

    public float remoteFetchTimeout = 5f;
    private bool isRemoteFetchDone = false;

     void OnEnable()
     {
         // FirebaseManager.OnRemoteDataFetch += OnRemoteDataFetch;
     }

    void OnDisable()
    {
        // if (PlayerPrefs.GetInt("Welcome", 0) == 1)
        // {
        //     welcomeScreen.SetActive(false);
        // }
        FirebaseManager.OnRemoteDataFetch -= OnRemoteDataFetch;
    }

    void OnRemoteDataFetch(bool success)
    {
        isRemoteFetchDone = true;
    }


    private IEnumerator Start()
    {
        Application.targetFrameRate = 60;
        yield return new WaitForEndOfFrame();
        FirebaseManager.OnRemoteDataFetch += OnRemoteDataFetch;
        yield return new WaitForSeconds(0.5f);
        

        // Fetch may have finished before this screen subscribed.
        if (FirebaseManager.isFetchCompleted)
        {
            isRemoteFetchDone = true;
        }

        yield return new WaitForEndOfFrame();
        
        // Wait for the remote config result, but never block the player forever if Firebase is slow or offline.
        float waited = 0f;
        while (!isRemoteFetchDone && waited < remoteFetchTimeout)
        {
            waited += 0.1f;
            yield return new WaitForSeconds(0.1f);
        }

        if (PlayerPrefs.GetInt("_consentScreen", 0) == 1)
        {
            LoadSplash();
        }
    }


    void LoadSplash()
    {
        SceneManager.LoadScene(1);
    }


    public void Play()
    {
        PlayerPrefs.SetInt("_consentScreen", 1);
        _consentScreen.SetActive(false);
        LoadSplash();
    }

    public void PrivacyLink(string privacyUrlLink)
    {
        Application.OpenURL(privacyUrlLink);
    }
}
