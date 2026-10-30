using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

public class Consent : MonoBehaviour
{
    public string ppUrl;

    private void Awake()
    {
        if (PlayerPrefs.GetInt("HasConsented") == 1)
        {
            SceneManager.LoadScene(1);
        }
        
    }


    public void OnClickPP()
    {
        Application.OpenURL(ppUrl);
    }
    public void OnClickPlay()
    {
        PlayerPrefs.SetInt("HasConsented", 1);
        SceneManager.LoadScene(1);
    }
}
