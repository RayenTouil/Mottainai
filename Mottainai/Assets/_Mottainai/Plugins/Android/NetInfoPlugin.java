package com.supcom.mottainai;

import android.Manifest;
import android.content.Context;
import android.content.pm.PackageManager;
import android.net.ConnectivityManager;
import android.net.Network;
import android.net.NetworkCapabilities;
import android.net.NetworkRequest;
import android.os.Build;
import android.os.Handler;
import android.os.Looper;
import android.os.VibrationEffect;
import android.os.Vibrator;
import android.telephony.PhoneStateListener;
import android.telephony.SubscriptionManager;
import android.telephony.TelephonyCallback;
import android.telephony.TelephonyDisplayInfo;
import android.telephony.TelephonyManager;
import java.util.concurrent.Executor;

/** 5G SA / NSA detection + latency slice (NET_CAPABILITY_PRIORITIZE_LATENCY) + haptics. Called from C# via AndroidJavaObject. */
public class NetInfoPlugin {
    private static final int NETWORK_TYPE_NR = 20;
    private static final int NETWORK_TYPE_LTE = 13;
    private static final int OVERRIDE_NR_NSA = 3;
    private static final int OVERRIDE_NR_NSA_MMWAVE = 4;
    private static final int OVERRIDE_NR_ADVANCED = 5;
    private static final int CAP_PRIORITIZE_LATENCY = 34;

    private final Context ctx;
    private volatile TelephonyManager tm;
    private final TelephonyManager tmDefault;
    private boolean sliceRetried = false;
    public volatile int netChanges = 0;
    private final ConnectivityManager cm;
    private volatile int overrideType = 0;
    private volatile String sliceStatus = "unsupported";
    private volatile String lastError = "";
    private boolean listening = false;
    private ConnectivityManager.NetworkCallback sliceCallback;

    public NetInfoPlugin(Context context) {
        ctx = context.getApplicationContext();
        tmDefault = (TelephonyManager) ctx.getSystemService(Context.TELEPHONY_SERVICE);
        tm = dataTm();
        cm = (ConnectivityManager) ctx.getSystemService(Context.CONNECTIVITY_SERVICE);
        // network changes (Wi-Fi <-> 5G, handover): re-read the data SIM, retry the latency slice once
        try {
            cm.registerDefaultNetworkCallback(new ConnectivityManager.NetworkCallback() {
                @Override public void onAvailable(Network n) { onNetChange(); }
                @Override public void onCapabilitiesChanged(Network n, NetworkCapabilities c) { netChanges++; }
            });
        } catch (Throwable t) { lastError = "netcb: " + t; }
    }

    /** Dual-SIM: network type of the SIM that carries data, not the default (voice) SIM. */
    private TelephonyManager dataTm() {
        try {
            int sub = SubscriptionManager.getDefaultDataSubscriptionId();
            if (sub != SubscriptionManager.INVALID_SUBSCRIPTION_ID && tmDefault != null) return tmDefault.createForSubscriptionId(sub);
        } catch (Throwable t) { lastError = "sub: " + t; }
        return tmDefault;
    }

    private void onNetChange() {
        netChanges++;
        TelephonyManager n = dataTm();
        if (n != tm) { tm = n; listening = false; }
        if (!sliceRetried && Build.VERSION.SDK_INT >= 33 && !"ON".equals(sliceStatus)) {
            sliceRetried = true;
            new Handler(Looper.getMainLooper()).post(() -> { sliceCallback = null; requestLatencySlice(); });
        }
    }

    private boolean hasPhonePermission() {
        return ctx.checkSelfPermission(Manifest.permission.READ_PHONE_STATE) == PackageManager.PERMISSION_GRANTED;
    }

    private void ensureDisplayListener() {
        if (listening || tm == null || !hasPhonePermission()) return;
        listening = true;
        new Handler(Looper.getMainLooper()).post(() -> {
            try {
                if (Build.VERSION.SDK_INT >= 31) {
                    Executor ex = ctx.getMainExecutor();
                    tm.registerTelephonyCallback(ex, new DisplayCb());
                } else if (Build.VERSION.SDK_INT >= 30) {
                    tm.listen(new PhoneStateListener() {
                        @Override
                        public void onDisplayInfoChanged(TelephonyDisplayInfo info) { overrideType = info.getOverrideNetworkType(); }
                    }, PhoneStateListener.LISTEN_DISPLAY_INFO_CHANGED);
                }
            } catch (Throwable t) { lastError = t.toString(); }
        });
    }

    private class DisplayCb extends TelephonyCallback implements TelephonyCallback.DisplayInfoListener {
        @Override
        public void onDisplayInfoChanged(TelephonyDisplayInfo info) { overrideType = info.getOverrideNetworkType(); }
    }

    public String getNetworkType() {
        try {
            Network n = cm != null ? cm.getActiveNetwork() : null;
            NetworkCapabilities caps = n != null ? cm.getNetworkCapabilities(n) : null;
            if (caps == null) return "Offline";
            if (caps.hasTransport(NetworkCapabilities.TRANSPORT_WIFI)) return "Wi-Fi";
            if (caps.hasTransport(NetworkCapabilities.TRANSPORT_ETHERNET)) return "Ethernet";
            if (!caps.hasTransport(NetworkCapabilities.TRANSPORT_CELLULAR)) return "Other";
            if (!hasPhonePermission() || tm == null) return "Cellular (no permission)";
            ensureDisplayListener();
            int type = tm.getDataNetworkType();
            if (type == NETWORK_TYPE_NR) return "5G SA";
            if (type == NETWORK_TYPE_LTE) {
                if (overrideType == OVERRIDE_NR_NSA || overrideType == OVERRIDE_NR_ADVANCED || overrideType == OVERRIDE_NR_NSA_MMWAVE) return "5G NSA";
                return "LTE";
            }
            if (type == 0) return "Unknown";
            return "3G/2G";
        } catch (Throwable t) {
            lastError = t.toString();
            return "Unknown";
        }
    }

    public String getSliceStatus() { return sliceStatus; }

    public String getDetail() {
        String d = "API " + Build.VERSION.SDK_INT + " override=" + overrideType;
        if (!lastError.isEmpty()) d += " err=" + lastError;
        return d;
    }

    public void requestLatencySlice() {
        if (Build.VERSION.SDK_INT < 33 || cm == null) { sliceStatus = "unsupported"; return; }
        if (sliceCallback != null) return;
        sliceStatus = "pending";
        try {
            NetworkRequest req = new NetworkRequest.Builder()
                    .addTransportType(NetworkCapabilities.TRANSPORT_CELLULAR)
                    .addCapability(NetworkCapabilities.NET_CAPABILITY_INTERNET)
                    .addCapability(CAP_PRIORITIZE_LATENCY)
                    .build();
            sliceCallback = new ConnectivityManager.NetworkCallback() {
                @Override
                public void onAvailable(Network network) {
                    boolean bound = cm.bindProcessToNetwork(network);
                    sliceStatus = bound ? "ON" : "OFF";
                }
                @Override
                public void onUnavailable() { sliceStatus = "OFF"; sliceCallback = null; }
                @Override
                public void onLost(Network network) { cm.bindProcessToNetwork(null); sliceStatus = "OFF"; }
            };
            cm.requestNetwork(req, sliceCallback, 4000);
        } catch (SecurityException se) {
            sliceStatus = "unsupported";
            lastError = "slice: " + se.getMessage();
            sliceCallback = null;
        } catch (Throwable t) {
            sliceStatus = "unsupported";
            lastError = "slice: " + t;
            sliceCallback = null;
        }
    }

    public void vibrate(int ms) {
        try {
            Vibrator v = (Vibrator) ctx.getSystemService(Context.VIBRATOR_SERVICE);
            if (v == null || !v.hasVibrator()) return;
            v.vibrate(VibrationEffect.createOneShot(Math.max(1, ms), VibrationEffect.DEFAULT_AMPLITUDE));
        } catch (Throwable t) { lastError = t.toString(); }
    }
}
