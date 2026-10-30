using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using DG.Tweening;
public class InternetConnectivityCheck : MonoBehaviour
{
    public GameObject NoNetworkDialogue;
    public bool isNoNetwrokDialogueOpen = false;
    void Start()
    {

        StartCoroutine(CheckForConnectivity());
    }

    IEnumerator CheckForConnectivity()
    {
        yield return new WaitForSeconds(3f);
        if (isNoNetwrokDialogueOpen == true && Application.internetReachability == NetworkReachability.NotReachable)
        {
            // Already Dialogue Opened
        }
        else if (isNoNetwrokDialogueOpen == false && Application.internetReachability == NetworkReachability.NotReachable)
        {
            print("OPEN");
            // Open "No Network Dialogue"
           // UiManager.instance.canTakeInput = false;
            isNoNetwrokDialogueOpen = true;
            NoNetworkDialogue.SetActive(true);
            NoNetworkDialogue.transform.GetChild(0).transform.DOKill();
            NoNetworkDialogue.transform.GetChild(0).transform.DOScale(new Vector3(1.2f, 1.2f, 1f), 0.25f).OnComplete(() =>
            {
                NoNetworkDialogue.transform.GetChild(0).transform.DOScale(new Vector3(1f, 1f, 1f), 0.25f);
            });
        }
        else if (Application.internetReachability != NetworkReachability.NotReachable)
        {
            // Close "No Network Dialogue"
          
            isNoNetwrokDialogueOpen = false;
            NoNetworkDialogue.transform.GetChild(0).transform.DOKill();
            NoNetworkDialogue.transform.GetChild(0).transform.DOScale(new Vector3(1.2f, 1.2f, 1f), 0.25f).OnComplete(() =>
            {
                NoNetworkDialogue.transform.GetChild(0).transform.DOScale(new Vector3(0.3f, 0.3f, 1f), 0.25f).OnComplete(() =>
                {
                    if (NoNetworkDialogue.activeSelf)
                    {
                       // UiManager.instance.canTakeInput = true;
                    }
                    NoNetworkDialogue.SetActive(false);
                    
                });
            });
        }

        StartCoroutine(CheckForConnectivity());

    }


    public void OnClickOKtoCheckConnection()
    {
        if (Application.internetReachability == NetworkReachability.NotReachable)
        {
            // No Respose
        }
        else
        {
            // Close "No Network Dialogue"
            isNoNetwrokDialogueOpen = false;
            NoNetworkDialogue.transform.GetChild(0).transform.DOKill();
            NoNetworkDialogue.transform.GetChild(0).transform.DOScale(new Vector3(1.2f, 1.2f, 1f), 0.25f).OnComplete(() =>
            {
                NoNetworkDialogue.transform.GetChild(0).transform.DOScale(new Vector3(0.3f, 0.3f, 1f), 0.25f).OnComplete(() =>
                {
                    if (NoNetworkDialogue.activeSelf)
                    {
                        //UiManager.instance.canTakeInput = true;
                    }
                    NoNetworkDialogue.SetActive(false);
                });
            });
        }
    }


}
