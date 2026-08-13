// NetworkQueueForegroundService.java
// Runs as an Android Foreground Service so the OS does not kill the process
// while the Unity app is in the background.  Unity's C# layer calls
// startForegroundService / stopService via the JNI bridge in
// BackgroundTaskManager.cs.

package com.splensoft.webapiservice;

import android.app.Notification;
import android.app.NotificationChannel;
import android.app.NotificationManager;
import android.app.Service;
import android.content.Intent;
import android.os.Build;
import android.os.IBinder;
import android.util.Log;

import androidx.annotation.Nullable;
import androidx.core.app.NotificationCompat;

public class NetworkQueueForegroundService extends Service
{
    private static final String TAG             = "SNQ_ForegroundService";
    private static final String CHANNEL_ID      = "snq_background_channel";
    private static final int    NOTIFICATION_ID = 0x534E5100; // "SNQ\0"

    // ── Service lifecycle ────────────────────────────────────────────────────

    @Override
    public void onCreate()
    {
        super.onCreate();
        Log.d(TAG, "onCreate");
        createNotificationChannel();
    }

    @Override
    public int onStartCommand(Intent intent, int flags, int startId)
    {
        Log.d(TAG, "onStartCommand");

        // Retrieve optional strings passed from C# via Intent extras.
        String title   = "Syncing data";
        String message = "Network queue is processing in the background.";

        if (intent != null)
        {
            if (intent.hasExtra("notificationTitle"))
                title = intent.getStringExtra("notificationTitle");
            if (intent.hasExtra("notificationMessage"))
                message = intent.getStringExtra("notificationMessage");
        }

        Notification notification = buildNotification(title, message);
        startForeground(NOTIFICATION_ID, notification);

        // We do NOT manage threads here – the Unity/Mono runtime continues
        // running while a foreground service is active, so the C# queue
        // processing loop keeps ticking normally.
        //
        // Return START_STICKY so the service is restarted if the system kills
        // it before the app calls stopService().
        return START_STICKY;
    }

    @Override
    public void onDestroy()
    {
        super.onDestroy();
        Log.d(TAG, "onDestroy");
        stopForeground(true);
    }

    @Nullable
    @Override
    public IBinder onBind(Intent intent)
    {
        // Not a bound service.
        return null;
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private void createNotificationChannel()
    {
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O)
        {
            NotificationChannel channel = new NotificationChannel(
                CHANNEL_ID,
                "Background Network Queue",
                NotificationManager.IMPORTANCE_LOW
            );
            channel.setDescription("Keeps the network request queue processing while the app is in the background.");
            channel.setShowBadge(false);

            NotificationManager manager =
                getSystemService(NotificationManager.class);
            if (manager != null)
                manager.createNotificationChannel(channel);
        }
    }

    private Notification buildNotification(String title, String message)
    {
        // Use a minimal, low-importance notification.
        // Apps that ship an icon named "ic_notification" in their drawable
        // resources will see it here; we fall back to the system default.
        int iconRes = getResources().getIdentifier(
            "ic_notification", "drawable", getPackageName());
        if (iconRes == 0)
            iconRes = android.R.drawable.ic_dialog_info;

        return new NotificationCompat.Builder(this, CHANNEL_ID)
            .setContentTitle(title)
            .setContentText(message)
            .setSmallIcon(iconRes)
            .setPriority(NotificationCompat.PRIORITY_LOW)
            .setOngoing(true)
            .build();
    }
}
