// GenzMonetization-v1.33
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class LowMemory : MonoBehaviour
{
    private void Start()
    {
        Application.lowMemory += OnLowMemory;
    }

    private void OnLowMemory()
    {
        Resources.UnloadUnusedAssets();
        System.GC.Collect();
        GA_Aanalytics.GA_CustomDesingEvent("LowMemory");
        AppmetricaAnalytics.ReportCustomEvent(AnalyticsType.GameData, "LowMemory");
    }
}

