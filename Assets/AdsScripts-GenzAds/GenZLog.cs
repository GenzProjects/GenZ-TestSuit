// GenzMonetization-v1.33
using System.Collections;
using AdjustSdk;
using UnityEngine;

/// <summary>
/// One-line logs the GenZ APK Test Suit reads from logcat (tag "Unity") on a release build:
/// "[GenZ] <sdk>|<what>|<OK|FAIL|CALL|INFO>|<detail>".
/// Only log what is safe to expose: SDK names, states, error codes/messages, formats, event names.
/// Never log keys, tokens, revenue, or device / advertising / user IDs.
/// </summary>
public static class GenZLog
{
    public const string Prefix = "[GenZ] ";

    public static void Ok(string sdk, string what, string detail = "") => Write(sdk, what, "OK", detail);
    public static void Fail(string sdk, string what, string detail = "") => Write(sdk, what, "FAIL", detail);
    public static void Call(string sdk, string what, string detail = "") => Write(sdk, what, "CALL", detail);
    public static void Info(string sdk, string what, string detail = "") => Write(sdk, what, "INFO", detail);
    public static void Result(string sdk, string what, bool ok, string detail = "") => Write(sdk, what, ok ? "OK" : "FAIL", detail);

    static void Write(string sdk, string what, string status, string detail)
    {
        Debug.Log(Prefix + Clean(sdk) + "|" + Clean(what) + "|" + status + "|" + Clean(detail));
    }

    static string Clean(string s) => string.IsNullOrEmpty(s) ? "" : s.Replace('|', '/').Replace('\n', ' ').Replace('\r', ' ');

    /// <summary>
    /// Adjust and AppMetrica start without a callback. Each hands out an ID only after its server answered,
    /// so ask for it and log whether it arrived (never the ID itself). Started once from Analytics.
    /// </summary>
    public static IEnumerator ProbeServers()
    {
        if (Application.isEditor) yield break;

        bool adjustOk = false, metricaOk = false, metricaAsking = false;
        string metricaError = null;
        try { Adjust.GetAdid(adid => adjustOk = !string.IsNullOrEmpty(adid)); } // called once the first session is tracked
        catch (System.Exception e) { Fail("Adjust", "init", e.Message); }

        const float Timeout = 60, Step = 3;
        bool adjustLogged = false, metricaLogged = false;
        for (float t = 0; t < Timeout && !(adjustLogged && metricaLogged); t += Step)
        {
            if (!metricaOk && !metricaAsking)
            {
                metricaAsking = true;
                try
                {
                    AppMetrica.Instance.RequestAppMetricaDeviceID((id, error) =>
                    {
                        metricaOk = !string.IsNullOrEmpty(id);
                        if (error != null) metricaError = error.ToString();
                        metricaAsking = false;
                    });
                }
                catch (System.Exception e) { metricaError = e.Message; }
            }
            yield return new WaitForSecondsRealtime(Step);
            if (adjustOk && !adjustLogged) { Ok("Adjust", "init", "server answered (adid received)"); adjustLogged = true; }
            if (metricaOk && !metricaLogged) { Ok("AppMetrica", "init", "server answered (device ID received)"); metricaLogged = true; }
        }
        if (!adjustLogged) Fail("Adjust", "init", $"no adid after {Timeout:0} s: the first session wasn't tracked (app token, environment or network)");
        if (!metricaLogged) Fail("AppMetrica", "init", $"no device ID after {Timeout:0} s" + (metricaError != null ? ": " + metricaError : ""));
    }
}
