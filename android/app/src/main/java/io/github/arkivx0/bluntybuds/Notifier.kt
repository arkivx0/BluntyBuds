package io.github.arkivx0.bluntybuds

import android.Manifest
import android.app.Notification
import android.app.NotificationChannel
import android.app.NotificationManager
import android.app.PendingIntent
import android.content.BroadcastReceiver
import android.content.Context
import android.content.Intent
import android.content.pm.PackageManager
import android.os.Build
import android.os.Handler
import android.os.Looper
import io.github.arkivx0.bluntybuds.proto.AncMode
import io.github.arkivx0.bluntybuds.proto.BatteryText

/** Local notifications only: optional status notification with noise buttons, and low-battery alerts. */
object Notifier {
    private const val CH_STATUS = "status"
    private const val CH_ALERT = "alerts"
    private const val ID_STATUS = 1
    private const val ID_LOW = 2
    const val ACTION_ANC = "io.github.arkivx0.bluntybuds.ANC"

    fun channels(ctx: Context) {
        val nm = ctx.getSystemService(NotificationManager::class.java) ?: return
        nm.createNotificationChannel(NotificationChannel(CH_STATUS, "Earbud status", NotificationManager.IMPORTANCE_LOW))
        nm.createNotificationChannel(NotificationChannel(CH_ALERT, "Low battery", NotificationManager.IMPORTANCE_DEFAULT))
    }

    fun allowed(ctx: Context) = Build.VERSION.SDK_INT < 33 ||
        ctx.checkSelfPermission(Manifest.permission.POST_NOTIFICATIONS) == PackageManager.PERMISSION_GRANTED

    private fun openApp(ctx: Context) = PendingIntent.getActivity(ctx, 0,
        Intent(ctx, MainActivity::class.java).addFlags(Intent.FLAG_ACTIVITY_SINGLE_TOP), PendingIntent.FLAG_IMMUTABLE)

    private val main = Handler(Looper.getMainLooper())
    private var pending: Context? = null
    private val later = Runnable { pending?.let { refreshNow(it) } }

    /** Coalesce a burst of battery packets into one shade update so Android does not stack a second card. */
    fun refresh(ctx: Context) {
        pending = ctx.applicationContext
        main.removeCallbacks(later)
        main.postDelayed(later, 350)
    }

    fun refreshNow(ctx: Context) {
        main.removeCallbacks(later)
        pending = null
        try {
            val app = ctx.applicationContext
            val nm = app.getSystemService(NotificationManager::class.java) ?: return
            if (!Conn.statusNotification || !Conn.connected || !allowed(app)) {
                nm.cancel(ID_STATUS)
                return
            }
            nm.cancel(ID_LOW)
            val bat = BatteryText.format(Conn.batL, Conn.batR, Conn.chargeL, Conn.chargeR)
            val low = BatteryText.lowest(Conn.batL, Conn.batR)?.let { it <= 20 } == true
            val mode = Conn.ancMode?.let { AncMode.of(it)?.label }
            val detail = when {
                low -> "Low battery"
                Conn.features().anc && mode != null -> "Noise: $mode"
                else -> "Connected"
            }
            val b = Notification.Builder(app, CH_STATUS)
                .setSmallIcon(android.R.drawable.stat_sys_headset)
                .setContentTitle("${Conn.name.ifEmpty { "Earbuds" }} · $bat")
                .setContentText(detail)
                .setOngoing(true).setOnlyAlertOnce(true).setShowWhen(false)
                .setContentIntent(openApp(app))
            if (Conn.features().anc) for ((m, t) in listOf(AncMode.OFF to "Off", AncMode.ANC to "Cancelling", AncMode.TRANSPARENCY to "Ambient")) {
                val pi = PendingIntent.getBroadcast(app, m.code, Intent(app, NotifReceiver::class.java).setAction(ACTION_ANC).putExtra("mode", m.code), PendingIntent.FLAG_IMMUTABLE)
                b.addAction(Notification.Action.Builder(null, t, pi).build())
            }
            nm.notify(ID_STATUS, b.build())
        } catch (_: Exception) {}
    }

    fun lowBattery(ctx: Context, name: String, l: Int?, r: Int?, chargingLeft: Boolean = false, chargingRight: Boolean = false) {
        try {
            if (!allowed(ctx) || Conn.statusNotification) return
            val nm = ctx.getSystemService(NotificationManager::class.java) ?: return
            nm.notify(ID_LOW, Notification.Builder(ctx, CH_ALERT)
                .setSmallIcon(android.R.drawable.stat_sys_warning)
                .setContentTitle("${name.ifEmpty { "Earbuds" }}: low battery")
                .setContentText(BatteryText.format(l, r, chargingLeft, chargingRight))
                .setAutoCancel(true).setContentIntent(openApp(ctx)).build())
        } catch (_: Exception) {}
    }
}

class NotifReceiver : BroadcastReceiver() {
    override fun onReceive(ctx: Context, i: Intent) {
        if (i.action != Notifier.ACTION_ANC) return
        Conn.init(ctx)
        AncMode.of(i.getIntExtra("mode", -1))?.let { Conn.setAnc(it) }
    }
}
