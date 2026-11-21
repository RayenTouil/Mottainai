using UnityEngine;

namespace Mottainai.Platform
{
    public interface INetworkInfoProvider
    {
        void Init();
        /// "5G SA", "5G NSA", "LTE", "Wi-Fi", "Ethernet", "3G", "Unknown", "Offline"
        string NetworkType { get; }
        /// "ON", "OFF", "unsupported", "pending"
        string SliceStatus { get; }
        string Detail { get; }
        void RequestLatencySlice();
        void Vibrate(int ms);
        void Refresh();
    }

    public static class NetworkInfo
    {
        static INetworkInfoProvider _p;
        public static INetworkInfoProvider Provider
        {
            get
            {
                if (_p == null)
                {
#if UNITY_ANDROID && !UNITY_EDITOR
                    _p = new AndroidNetworkInfoProvider();
#else
                    _p = new DefaultNetworkInfoProvider();
#endif
                    _p.Init();
                }
                return _p;
            }
        }
        public static string DeviceLabel => $"{SystemInfo.deviceModel} ({SystemInfo.operatingSystem})";
        /// PROTOCOL.md netType: 0 unknown, 1 Wi-Fi, 2 LTE, 3 5G NSA, 4 5G SA.
        public static byte TypeCode => Provider.NetworkType switch { "Wi-Fi" => 1, "LAN" => 1, "Ethernet" => 1, "LTE" => 2, "5G NSA" => 3, "5G SA" => 4, _ => 0 };
    }

    public class DefaultNetworkInfoProvider : INetworkInfoProvider
    {
        public string NetworkType { get; private set; } = "Unknown";
        public string SliceStatus => "unsupported";
        public string Detail => Application.platform.ToString();
        public void Init() => Refresh();
        public void RequestLatencySlice() { }
        public void Vibrate(int ms)
        {
#if UNITY_IOS
            Handheld.Vibrate();
#endif
        }
        public void Refresh()
        {
            NetworkType = Application.internetReachability switch
            {
                NetworkReachability.ReachableViaLocalAreaNetwork => Application.isMobilePlatform ? "Wi-Fi" : "LAN",
                NetworkReachability.ReachableViaCarrierDataNetwork => "Cellular",
                _ => "Offline"
            };
        }
    }

#if UNITY_ANDROID
    public class AndroidNetworkInfoProvider : INetworkInfoProvider
    {
        AndroidJavaObject plugin;
        public string NetworkType { get; private set; } = "Unknown";
        public string SliceStatus { get; private set; } = "unsupported";
        public string Detail { get; private set; } = "";
        float nextRefresh;

        public void Init()
        {
            try
            {
                const string perm = "android.permission.READ_PHONE_STATE";
                if (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(perm))
                    UnityEngine.Android.Permission.RequestUserPermission(perm);
                using var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                var activity = player.GetStatic<AndroidJavaObject>("currentActivity");
                plugin = new AndroidJavaObject("com.supcom.mottainai.NetInfoPlugin", activity);
            }
            catch (System.Exception e) { Debug.LogWarning("[NetInfo] plugin unavailable: " + e.Message); }
            Refresh();
            RequestLatencySlice();
        }

        public void Refresh()
        {
            if (Time.realtimeSinceStartup < nextRefresh) return;
            nextRefresh = Time.realtimeSinceStartup + 1f;
            if (plugin == null) { NetworkType = "Unknown"; return; }
            try
            {
                NetworkType = plugin.Call<string>("getNetworkType");
                SliceStatus = plugin.Call<string>("getSliceStatus");
                Detail = plugin.Call<string>("getDetail");
            }
            catch (System.Exception e) { Detail = e.Message; }
        }

        public void RequestLatencySlice()
        {
            try { plugin?.Call("requestLatencySlice"); } catch (System.Exception e) { Debug.LogWarning("[NetInfo] slice: " + e.Message); }
        }

        public void Vibrate(int ms)
        {
            try { plugin?.Call("vibrate", ms); } catch { }
        }
    }
#endif
}
