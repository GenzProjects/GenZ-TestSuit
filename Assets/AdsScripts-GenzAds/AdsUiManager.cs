// GenzMonetization-v1.33
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class AdsUiManager : MonoBehaviour
{

    public GameObject[] RewardedObjects;
    public string FirstNameOfRew;


    private void Awake()
    {
        for (int i = 0; i < RewardedObjects.Length; i++)
        {
            RewardedObjects[i].name = FirstNameOfRew + i;
        }
    }

}
