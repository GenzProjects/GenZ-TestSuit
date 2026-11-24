// GenzMonetization-v1.33
#if UNITY_IOS
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.iOS.Xcode;

// Adds the Helix (Voodoo ADN) SKAdNetwork ID to the iOS Info.plist after every build.
// Guide: https://docs.helix.voodoo.io/monetization/integrate-the-sdk/applovin-max-adapter/unity-max-adapter
// Helix is a MAX custom network, so AppLovin's build step doesn't add its ID.
// Keeps the IDs other SDKs (MAX, AdMob) add and never adds a duplicate.
public static class HelixSKAdNetwork
{
    static readonly string[] Ids = { "tskbem2b5g.skadnetwork" };

    [PostProcessBuild(100)]
    public static void OnPostProcessBuild(BuildTarget target, string buildPath)
    {
        if (target != BuildTarget.iOS) return;

        string plistPath = Path.Combine(buildPath, "Info.plist");
        var plist = new PlistDocument();
        plist.ReadFromFile(plistPath);

        PlistElementArray items;
        if (plist.root.values.TryGetValue("SKAdNetworkItems", out var element) && element is PlistElementArray array)
            items = array;
        else
            items = plist.root.CreateArray("SKAdNetworkItems");

        // IDs already in the list (added by MAX, AdMob or an earlier build).
        var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in items.values)
            if (item is PlistElementDict dict
                && dict.values.TryGetValue("SKAdNetworkIdentifier", out var id)
                && id is PlistElementString idString)
                existing.Add(idString.value);

        foreach (var id in Ids)
            if (existing.Add(id))
                items.AddDict().SetString("SKAdNetworkIdentifier", id);

        plist.WriteToFile(plistPath);
    }
}
#endif
