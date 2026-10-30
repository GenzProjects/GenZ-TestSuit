using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class InAppCalling : MonoBehaviour {

	// Use this for initialization
	void Start () {
		
	}
	
	// Update is called once per frame
	public void BuyInApp (int value) {
        unityInApp.instance.BuyProductID(AdsIds.instance.InAppIds[value].Id, value);
    }
    public void restore()
    {
        unityInApp.instance.RestorePurchases();
    }
}

