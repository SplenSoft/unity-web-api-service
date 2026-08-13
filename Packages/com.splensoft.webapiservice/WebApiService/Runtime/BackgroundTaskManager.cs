using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace SplenSoft.Unity
{
    /// <summary>
    /// Manages background execution for the <see cref="SerializedNetworkQueue"/>.
    /// <para>
    /// On iOS a UIApplication background task is started when the app moves to
    /// the background, giving up to ~30 seconds of extra run-time.  A
    /// BGProcessingTask is also scheduled so iOS can resume the queue while the
    /// device is idle / on charge.
    /// </para>
    /// <para>
    /// On Android a foreground service is started, which prevents the OS from
    /// killing the process while the app is in the background.
    /// </para>
    /// Attach this component to the same GameObject as your
    /// <see cref="SerializedNetworkQueue"/>, or use the provided prefab which
    /// already includes it.
    /// </summary>
    [RequireComponent(typeof(SerializedNetworkQueue))]
    public class BackgroundTaskManager : MonoBehaviourR3
    {
        // ── Inspector ────────────────────────────────────────────────────────

        [field: SerializeField, Tooltip(
            "Identifier registered in Info.plist under " +
            "BGTaskSchedulerPermittedIdentifiers.  Leave empty to use the " +
            "default value (com.splensoft.webapiservice.networkqueue).")]
        private string IOSBGTaskIdentifier { get; set; } =
            "com.splensoft.webapiservice.networkqueue";

        [field: SerializeField, Tooltip(
            "Title shown in the Android foreground-service notification.")]
        private string AndroidNotificationTitle { get; set; } =
            "Syncing data";

        [field: SerializeField, Tooltip(
            "Message shown in the Android foreground-service notification.")]
        private string AndroidNotificationMessage { get; set; } =
            "Network queue is processing in the background.";

        // ── iOS native imports ────────────────────────────────────────────────
#if UNITY_IOS && !UNITY_EDITOR
        [DllImport("__Internal")]
        private static extern void SNQ_BeginBackgroundTask();

        [DllImport("__Internal")]
        private static extern void SNQ_EndBackgroundTask();

        [DllImport("__Internal")]
        private static extern void SNQ_SetBGTaskExpiredCallback(
            IntPtr callback);

        [DllImport("__Internal")]
        private static extern void SNQ_RegisterBGProcessingTask(
            string identifier);

        [DllImport("__Internal")]
        private static extern void SNQ_ScheduleBGProcessingTask(
            string identifier);

        [DllImport("__Internal")]
        private static extern void SNQ_CompleteBGProcessingTask(bool success);

        [DllImport("__Internal")]
        private static extern double SNQ_GetRemainingBackgroundTime();

        // Delegate signature must match the typedef in the .mm file.
        private delegate void BackgroundExpiredDelegate();

        // Keep a reference so the GC doesn't collect the delegate while the
        // native side still holds a pointer to it.
        private static BackgroundExpiredDelegate _expiredDelegate;
#endif

        // ── Android helpers ───────────────────────────────────────────────────
#if UNITY_ANDROID && !UNITY_EDITOR
        private AndroidJavaObject _unityActivity;

        private AndroidJavaObject UnityActivity
        {
            get
            {
                if (_unityActivity == null)
                {
                    using var unityPlayer =
                        new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                    _unityActivity =
                        unityPlayer.GetStatic<AndroidJavaObject>(
                            "currentActivity");
                }
                return _unityActivity;
            }
        }

        private const string ServiceClassName =
            "com.splensoft.webapiservice.NetworkQueueForegroundService";
#endif

        // ── Unity lifecycle ───────────────────────────────────────────────────

        protected override void Awake()
        {
            base.Awake();

#if UNITY_IOS && !UNITY_EDITOR
            // Pin the delegate and register its pointer with native code.
            _expiredDelegate = OnBackgroundTimeExpired;
            IntPtr fp = Marshal.GetFunctionPointerForDelegate(_expiredDelegate);
            SNQ_SetBGTaskExpiredCallback(fp);

            // Register the BGProcessingTask handler.  Must be called before
            // the app finishes launching (Awake is early enough in Unity).
            SNQ_RegisterBGProcessingTask(IOSBGTaskIdentifier);
#endif
        }

        private void OnApplicationPause(bool paused)
        {
            if (paused)
                StartBackgroundProcessing();
            else
                StopBackgroundProcessing();
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            StopBackgroundProcessing();
        }

        // ── Public API ────────────────────────────────────────────────────────

        /// <summary>
        /// Starts the platform-specific background execution mechanism.
        /// Called automatically via <c>OnApplicationPause</c>; you may also
        /// call it manually if needed.
        /// </summary>
        public void StartBackgroundProcessing()
        {
#if UNITY_IOS && !UNITY_EDITOR
            SNQ_BeginBackgroundTask();
            SNQ_ScheduleBGProcessingTask(IOSBGTaskIdentifier);
            Log("iOS background task started.", LogLevel.Verbose);
#elif UNITY_ANDROID && !UNITY_EDITOR
            StartAndroidForegroundService();
#endif
        }

        /// <summary>
        /// Stops the platform-specific background execution mechanism.
        /// Called automatically when the app returns to the foreground.
        /// </summary>
        public void StopBackgroundProcessing()
        {
#if UNITY_IOS && !UNITY_EDITOR
            SNQ_EndBackgroundTask();
            // Signal the BGProcessingTask as complete (if one is running).
            SNQ_CompleteBGProcessingTask(true);
            Log("iOS background task ended.", LogLevel.Verbose);
#elif UNITY_ANDROID && !UNITY_EDITOR
            StopAndroidForegroundService();
#endif
        }

#if UNITY_IOS && !UNITY_EDITOR
        /// <summary>
        /// Returns the number of seconds remaining in the current iOS
        /// background task, or <c>double.MaxValue</c> if running in the
        /// foreground (iOS returns a very large number in that case).
        /// </summary>
        public double GetRemainingBackgroundTime()
        {
            return SNQ_GetRemainingBackgroundTime();
        }
#endif

        // ── Private helpers ───────────────────────────────────────────────────

#if UNITY_IOS && !UNITY_EDITOR
        [AOT.MonoPInvokeCallback(typeof(BackgroundExpiredDelegate))]
        private static void OnBackgroundTimeExpired()
        {
            // This runs on the main thread (dispatch_get_main_queue in .mm).
            Debug.LogWarning(
                "[BackgroundTaskManager] iOS background time is about to " +
                "expire.  The network queue will resume when the app is next " +
                "in the foreground.");
        }
#endif

#if UNITY_ANDROID && !UNITY_EDITOR
        private void StartAndroidForegroundService()
        {
            try
            {
                using var intent =
                    new AndroidJavaObject("android.content.Intent");
                using var serviceClass =
                    new AndroidJavaClass(ServiceClassName);

                intent.Call<AndroidJavaObject>(
                    "setClass",
                    UnityActivity,
                    serviceClass.GetStatic<AndroidJavaObject>("class"));

                intent.Call<AndroidJavaObject>(
                    "putExtra", "notificationTitle",
                    AndroidNotificationTitle);
                intent.Call<AndroidJavaObject>(
                    "putExtra", "notificationMessage",
                    AndroidNotificationMessage);

                // startForegroundService is required on API 26+.
                if (AndroidVersion() >= 26)
                {
                    UnityActivity.Call<AndroidJavaObject>(
                        "startForegroundService", intent);
                }
                else
                {
                    UnityActivity.Call<AndroidJavaObject>(
                        "startService", intent);
                }

                Log("Android foreground service started.", LogLevel.Verbose);
            }
            catch (Exception ex)
            {
                Debug.LogError(
                    $"[BackgroundTaskManager] Failed to start Android " +
                    $"foreground service: {ex.Message}");
            }
        }

        private void StopAndroidForegroundService()
        {
            try
            {
                using var intent =
                    new AndroidJavaObject("android.content.Intent");
                using var serviceClass =
                    new AndroidJavaClass(ServiceClassName);

                intent.Call<AndroidJavaObject>(
                    "setClass",
                    UnityActivity,
                    serviceClass.GetStatic<AndroidJavaObject>("class"));

                UnityActivity.Call<bool>("stopService", intent);
                Log("Android foreground service stopped.", LogLevel.Verbose);
            }
            catch (Exception ex)
            {
                Debug.LogError(
                    $"[BackgroundTaskManager] Failed to stop Android " +
                    $"foreground service: {ex.Message}");
            }
        }

        private static int AndroidVersion()
        {
            using var version =
                new AndroidJavaClass("android.os.Build$VERSION");
            return version.GetStatic<int>("SDK_INT");
        }
#endif
    }
}
