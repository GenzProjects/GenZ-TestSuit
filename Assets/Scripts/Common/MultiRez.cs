using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class MultiRez : MonoBehaviour
{
    public CanvasScaler canvasObj;
    private void Awake()
    {
        // landscape
        //float aspect = (float)Screen.height / (float)Screen.width;
        //aspect = (float)Math.Round(aspect, 3);
        //aspect = (aspect - 0.439f) / 0.311f;
        //aspect = Mathf.Abs(1f - aspect);
        //canvasObj.matchWidthOrHeight = aspect;


        // portrait
        float aspect = (float)Screen.width / (float)Screen.height;
        aspect = (float)Math.Round(aspect, 3);
        aspect = (aspect - 0.439f) / 0.311f;
        canvasObj.matchWidthOrHeight = aspect;
    }

}

/*
 * Resolution of device having maximum height is considered to be:
 * 720 x 1640 (wxh) where w / h = 0.439
 * Canvas Scale should be 0 on this value
 * 
 * 
 * Resolution of device having maximum width is considered to be:
 * 1536 x 2048 (wxh) where w / h = 0.75
 * Canvas Scale should be 1 on this value
 * 
 * in order to move the canvas value from 0 to 1 as canvas ratio varies from 0.439 to 0.75
 * formula used:
 * aspect = (aspect - min value)
 * 
 */
