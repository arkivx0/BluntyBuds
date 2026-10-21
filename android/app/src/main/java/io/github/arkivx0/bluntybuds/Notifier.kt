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
import io.github.arkivx0.bluntybuds.proto.AncMode

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

    fun refresh(ctx: Context) {
        try {
            val nm = ctx.getSystemService(NotificationManager::class.java) ?: return
            if (!Conn.statusNotification || !Conn.connected || !allowed(ctx)) { nm.cancel(ID_STATUS); return }
            val bat = "L ${Conn.batL?.let { "$it%" } ?: "—"}  R ${Conn.batR?.let { "$it%" } ?: "—"}"
            val mode = Conn.ancMode?.let { AncMode.of(it)?.label }
            val b = Notification.Builder(ctx, CH_STATUS)
                .setSmallIcon(android.R.drawable.stat_sys_headset)
                .setContentTitle("${Conn.name.ifEmpty { "Earbuds" }} · $bat")
                .setContentText(if (Conn.features().anc && mode != null) "Noise: $mode" else "Connected")
                .setOngoing(true).setOnlyAlertOnce(true).setShowWhen(false)
                .setContentIntent(openApp(ctx))
            if (Conn.features().anc) for ((m, t) in listOf(AncMode.OFF to "Off", AncMode.ANC to "Noise cancel", AncMode.TRANSPARENCY to "Ambient")) {
                val pi = PendingIntent.getBroadcast(ctx, m.code, Intent(ctx, NotifReceiver::class.java).setAction(ACTION_ANC).putExtra("mode", m.code), PendingIntent.FLAG_IMMUTABLE)
                b.addAction(Notification.Action.Builder(null, t, pi).build())
            }
            nm.notify(ID_STATUS, b.build())
        } catch (_: Exception) {}
    }

    fun lowBattery(ctx: Context, name: String, l: Int?, r: Int?) {
        try {
            if (!allowed(ctx)) return
            val nm = ctx.getSystemService(NotificationManager::class.java) ?: return
            nm.notify(ID_LOW, Notification.Builder(ctx, CH_ALERT)
                .setSmallIcon(android.R.drawable.stat_sys_warning)
                .setContentTitle("${name.ifEmpty { "Earbuds" }}: low battery")
                .setContentText("L ${l?.let { "$it%" } ?: "—"}  R ${r?.let { "$it%" } ?: "—"}")
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
