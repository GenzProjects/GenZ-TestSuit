// GenzMonetization-v1.33
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Purchasing;
using UnityEngine.Purchasing.Extension;

public class unityInApp : MonoBehaviour
{
    public static unityInApp instance;

    private StoreController _store;
    private CatalogProvider _catalog;

    private readonly Dictionary<string, int> _pendingValuesByProductId = new();

    [Header("Subscription IDs (override per store if needed)")]
    private string crossStoreSubscriptionId = "subscription";
    private string appleSubscriptionId = "com.unity3d.subscription.new";
    private string googleSubscriptionId = "com.unity3d.subscription.original";

    [Header("Behavior")]
    [SerializeField] private bool fetchPurchasesAfterProducts = true;

    [Header("Screens (assign in Inspector, optional)")]
    [Tooltip("Shown while a purchase is in progress, hidden when it completes or fails.")]
    public GameObject loadingScreen;
    [Tooltip("Shown when a purchase can't start or fails, hidden after Fail Screen Duration.")]
    public GameObject failIAPScreen;
    [SerializeField] private float failScreenDuration = 1f;
    [Tooltip("Shown when a purchase succeeds and its reward is granted, hidden after Success Screen Duration.")]
    public GameObject successIAPScreen;
    [SerializeField] private float successScreenDuration = 1.5f;

    private void Awake()
    {
        instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private async void Start()
    {
        await InitializePurchasing();
    }

    public async Task InitializePurchasing()
    {
        if (_store != null) return;

        _store = UnityIAPServices.StoreController();

        _store.OnPurchasePending += OnPurchasePending;
        _store.OnPurchaseFailed += OnPurchaseFailed;
        _store.OnStoreDisconnected += OnStoreDisconnected;

        _store.OnProductsFetched += OnProductsFetched;
        _store.OnProductsFetchFailed += OnProductsFetchFailed;
        _store.OnPurchasesFetched += OnPurchasesFetched;
        _store.OnPurchasesFetchFailed += OnPurchasesFetchFailed;

        try
        {
            await _store.Connect();
            Debug.Log("[IAP] Connected to store.");
            GenZLog.Ok("IAP", "init", "connected to store");
            BuildCatalog();
            _catalog.FetchProducts(UnityIAPServices.DefaultProduct().FetchProductsWithNoRetries);
        }
        catch (Exception e)
        {
            Debug.LogError($"[IAP] Connect() failed: {e}");
            GenZLog.Fail("IAP", "init", "store connect failed: " + e.Message);
        }
    }

    private void BuildCatalog()
    {
        _catalog = new CatalogProvider();

        if (AdsIds.instance != null && AdsIds.instance.InAppIds != null)
        {
            foreach (var item in AdsIds.instance.InAppIds)
            {
                if (string.IsNullOrEmpty(item.Id)) continue;
                _catalog.AddProduct(item.Id.ToLowerInvariant(), item.Type, null);
            }
        }

        var storeSpecific = new StoreSpecificIds
        {
            { AppleAppStore.Name, appleSubscriptionId },
            { GooglePlay.Name,    googleSubscriptionId }
        };
        _catalog.AddProduct(crossStoreSubscriptionId.ToLowerInvariant(), ProductType.Subscription, storeSpecific);
    }

    public void BuyProductID(string productId, int value)
    {
        if (_store == null)
        {
            Debug.LogWarning("[IAP] Buy failed: Store not connected yet.");
            ShowFailScreen();
            return;
        }
        if (string.IsNullOrEmpty(productId))
        {
            Debug.LogWarning("[IAP] Buy failed: empty productId.");
            ShowFailScreen();
            return;
        }

        productId = productId.ToLowerInvariant();
        _pendingValuesByProductId[productId] = value;

        try
        {
            SetLoadingScreen(true);
            // The store dialog sends the app to background; skip the App Open ad on return
            // (AppOpen_Code.ShowAppOpenAdIfAvailable clears the flag).
            if (AdsManager.instance != null) AdsManager.instance.isPausedDuetoAd = true;
            _store.PurchaseProduct(productId);
            Debug.Log($"[IAP] Purchasing: {productId}");
        }
        catch (Exception e)
        {
            Debug.LogError($"[IAP] PurchaseProduct threw: {e}");
            _pendingValuesByProductId.Remove(productId);
            if (AdsManager.instance != null) AdsManager.instance.isPausedDuetoAd = false; // store never opened
            SetLoadingScreen(false);
            ShowFailScreen();
        }
    }

    private void SetLoadingScreen(bool show)
    {
        if (loadingScreen != null) loadingScreen.SetActive(show);
    }

    private void ShowFailScreen()
    {
        if (failIAPScreen == null) return;
        CancelInvoke(nameof(HideFailScreen));
        failIAPScreen.transform.localScale = Vector3.one;
        failIAPScreen.SetActive(true);
        Invoke(nameof(HideFailScreen), failScreenDuration);
    }

    private void HideFailScreen()
    {
        if (failIAPScreen != null) failIAPScreen.SetActive(false);
    }

    private void ShowSuccessScreen()
    {
        if (successIAPScreen == null) return;
        CancelInvoke(nameof(HideSuccessScreen));
        HideFailScreen();
        successIAPScreen.transform.localScale = Vector3.one;
        successIAPScreen.SetActive(true);
        Invoke(nameof(HideSuccessScreen), successScreenDuration);
    }

    private void HideSuccessScreen()
    {
        if (successIAPScreen != null) successIAPScreen.SetActive(false);
    }

    public void RestorePurchases()
    {
#if UNITY_IOS || UNITY_STANDALONE_OSX || UNITY_TVOS || UNITY_VISIONOS
        if (_store == null)
        {
            Debug.LogWarning("[IAP] Restore failed: Store not connected.");
            return;
        }

        Debug.Log("[IAP] Restoring transactions (Apple)...");
        _store.RestoreTransactions((success, msg) =>
        {
            Debug.Log($"[IAP] Restore finished. Success: {success}, msg: {msg}");
        });
#else
        Debug.Log("[IAP] RestorePurchases: Not required on this platform (Google auto-restores via FetchPurchases).");
#endif
    }

    private void OnProductsFetched(List<Product> products)
    {
        Debug.Log($"[IAP] Products fetched: {products?.Count ?? 0}");
        GenZLog.Ok("IAP", "products", $"{products?.Count ?? 0} fetched");
        if (fetchPurchasesAfterProducts)
        {
            _store.FetchPurchases();
        }
    }

    private void OnProductsFetchFailed(ProductFetchFailed failure)
    {
        var reason = failure?.FailureReason ?? "(unknown)";
        int failedCount = failure?.FailedFetchProducts?.Count ?? 0;
        Debug.LogError($"[IAP] Products fetch failed. Reason: {reason}. Failed count: {failedCount}");
        GenZLog.Fail("IAP", "products", $"fetch failed: {reason} ({failedCount} products)");
    }

    private void OnPurchasesFetched(Orders orders)
    {
        Debug.Log($"[IAP] Purchases fetched. Confirmed: {orders?.ConfirmedOrders?.Count}, Pending: {orders?.PendingOrders?.Count}, Deferred: {orders?.DeferredOrders?.Count}");
        // If you want, iterate orders.ConfirmedOrders etc. to unlock entitlements.
    }

    private void OnPurchasePending(PendingOrder order)
    {
        // Extract cross-store product id from the first (and only) cart item in v5:
        string productId = null;
        try
        {
            var items = order?.CartOrdered?.Items(); // IReadOnlyList<CartItem>
            if (items != null && items.Count > 0 && items[0]?.Product != null)
            {
                productId = items[0].Product.definition?.id; // <-- v5: ProductDefinition.id
            }
        }
        catch { /* ignore parse errors */ }

        Debug.Log($"[IAP] OnPurchasePending for product: {productId ?? "(unknown)"}");

        // Grant your reward BEFORE confirming
        bool granted = false;
        if (!string.IsNullOrEmpty(productId) && _pendingValuesByProductId.TryGetValue(productId, out var value))
        {
            AdsIds.instance.AfterPurchased(value);
            _pendingValuesByProductId.Remove(productId);
            granted = true;
        }

        // Acknowledge so the store completes the purchase
        _store.ConfirmPurchase(order);
        SetLoadingScreen(false);
        if (granted) ShowSuccessScreen(); // only for a purchase made in this session, not a restored order
    }

    private void OnPurchaseFailed(FailedOrder failed)
    {
        string pid = "(unknown)";
        try
        {
            var items = failed?.CartOrdered?.Items();
            if (items != null && items.Count > 0 && items[0]?.Product != null)
                pid = items[0].Product.definition?.id ?? "(unknown)";
        }
        catch { /* ignore */ }

        var reason = failed?.FailureReason.ToString() ?? "(unknown)";
        var details = failed?.Details ?? "";

        Debug.LogWarning($"[IAP] Purchase FAILED for '{pid}'. Reason: {reason}. Details: {details}");
        if (!string.IsNullOrEmpty(pid)) _pendingValuesByProductId.Remove(pid);

        SetLoadingScreen(false);
        ShowFailScreen();
    }

    private void OnStoreDisconnected(StoreConnectionFailureDescription desc)
    {
        // Debug.LogError($"[IAP] Store disconnected. Reason: {desc?.Reason} | msg: {desc?.Message}");
    }

    private void OnPurchasesFetchFailed(PurchasesFetchFailureDescription desc)
    {
        //  Debug.LogError($"[IAP] FetchPurchases FAILED. Reason: {desc?.Reason} | msg: {desc?.Message}");
    }
}
