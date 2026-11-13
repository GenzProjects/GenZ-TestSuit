// GenzMonetization-v1.33
using Firebase.Messaging;
using Firebase.Extensions;
using UnityEngine;
using System;
using Firebase.Analytics;
#if UNITY_ANDROID
using UnityEngine.Android;
#endif

public class FirebaseMessagingManager : MonoBehaviour
{
    public static FirebaseMessagingManager Instance;

    // Fired when a notification arrives while the app is in the foreground
    public static event Action<FirebaseMessage> OnNotificationReceived;

    // Exposes the current FCM token to other systems
    public static string FCMToken { get; private set; }

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
        }
    }

    /// <summary>
    /// Call this from FirebaseManager.InitializeFirebase() after Firebase is ready.
    /// </summary>
    public void InitializeFCM()
    {
        // Subscribe to incoming messages (foreground)
        FirebaseMessaging.MessageReceived += OnMessageReceived;

        // Subscribe to token refresh events
        FirebaseMessaging.TokenReceived += OnTokenReceived;

        RequestNotificationPermission();

        // Fetch the current token immediately
        FirebaseMessaging.GetTokenAsync().ContinueWithOnMainThread(task =>
        {
            if (task.IsCompletedSuccessfully)
            {
                FCMToken = task.Result;
                Debug.Log("[FCM] Token: " + FCMToken);
                GenZLog.Ok("FCM", "init", "token received");
                SendTokenToServer(FCMToken);
            }
            else
            {
                Debug.LogWarning("[FCM] Failed to get token: " + task.Exception);
                GenZLog.Fail("FCM", "init", "no token: " + task.Exception?.GetBaseException().Message);
            }
        });

        // Optional: subscribe to a topic so you can broadcast to all players
        FirebaseMessaging.SubscribeAsync("all_players").ContinueWithOnMainThread(task =>
        {
            Debug.Log("[FCM] Subscribed to all_players topic: " + task.IsCompletedSuccessfully);
        });

        Debug.Log("[FCM] Firebase Messaging initialized.");
    }
    
    private void RequestNotificationPermission()
    {
#if UNITY_ANDROID
        if (!Permission.HasUserAuthorizedPermission("android.permission.POST_NOTIFICATIONS"))
        {
            Permission.RequestUserPermission("android.permission.POST_NOTIFICATIONS");
        }
#endif
    }

    /// <summary>
    /// Called when a message arrives while the app is in the FOREGROUND.
    /// Background / quit messages are handled by the OS notification tray automatically.
    /// </summary>
    private void OnMessageReceived(object sender, MessageReceivedEventArgs e)
    {
        FirebaseMessage msg = e.Message;

        Debug.Log("[FCM] Message received!");
        Debug.Log("[FCM] Title: " + msg.Notification?.Title);
        Debug.Log("[FCM] Body:  " + msg.Notification?.Body);

        // Fire the event — let UI systems listen without tight coupling
        OnNotificationReceived?.Invoke(msg);

        // Example: read custom data key-value pairs from the message
        if (msg.Data != null)
        {
            foreach (var kv in msg.Data)
            {
                Debug.Log($"[FCM] Data key={kv.Key}  value={kv.Value}");
            }

            // Example: trigger a remote config refresh if the notification says so
            if (msg.Data.ContainsKey("refresh_config") && msg.Data["refresh_config"] == "true")
            {
                FirebaseManager.Instance?.RefreshRemoteConfig();
            }
        }
    }

    /// <summary>
    /// Called when the FCM token changes (device re-registration, app reinstall, etc.)
    /// Always re-send the new token to your backend.
    /// </summary>
    private void OnTokenReceived(object sender, TokenReceivedEventArgs e)
    {
        FCMToken = e.Token;
        Debug.Log("[FCM] Token refreshed: " + FCMToken);
        SendTokenToServer(FCMToken);
    }

    /// <summary>
    /// Replace this with your actual backend call (REST, PlayFab, GameSparks, etc.)
    /// </summary>
    private void SendTokenToServer(string token)
    {
        // Save locally so you can send it later if the network isn't ready
        PlayerPrefs.SetString("FCMToken", token);
        PlayerPrefs.Save();

        // TODO: POST the token to your server
        // e.g. StartCoroutine(PostTokenCoroutine(token));
        Debug.Log("[FCM] Token saved to PlayerPrefs. Send to your server here.");
    }

    private void OnDestroy()
    {
        // Always unsubscribe to avoid memory leaks
        FirebaseMessaging.MessageReceived -= OnMessageReceived;
        FirebaseMessaging.TokenReceived  -= OnTokenReceived;
    }
}