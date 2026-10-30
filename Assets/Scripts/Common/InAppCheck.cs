using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class InAppCheck : MonoBehaviour
{
    void Start()
    {
        if (PlayerPrefs.GetInt("unlockall") == 1)
        {
            gameObject.SetActive(false);
        }
    }

}
