package io.github.arkivx0.bluntybuds

import android.annotation.SuppressLint
import android.bluetooth.BluetoothDevice
import android.bluetooth.BluetoothManager
import android.content.ComponentName
import android.content.Context
import android.os.Handler
import android.os.Looper
import android.service.quicksettings.TileService
import io.github.arkivx0.bluntybuds.proto.*
import java.util.concurrent.CopyOnWriteArrayList

/**
 * Process-wide earbud connection shared by the activity, the Quick Settings tile and the notification.
 * Remembers the last device (local SharedPreferences only) and reconnects when it comes back.
 */
@SuppressLint("MissingPermission")
object Conn : BleClient.Listener {
    private lateinit var app: Context
    lateinit var ble: BleClient; private set
    private val main = Handler(Looper.getMainLooper())
    val listeners = CopyOnWriteArrayList<BleClient.Listener>()

    var model = DeviceModel.UNKNOWN; private set
    var name = ""; private set
    var connected = false; private set
    var batL: Int? = null; private set
    var batR: Int? = null; private set
    var chargeL = false; private set
    var chargeR = false; private set
    var caseBat: Int? = null; private set
    var caseCharge = false; private set
    private var pollToken = 0
    private var sawL = false
    private var sawR = false
    var ancMode: Int? = null; private set
    var ancLevel: Int? = null; private set
    var game: Boolean? = null; private set
    var status = "Idle"; private set
    private var userDisconnected = false
    private var pendingCycle = false
    private var lowWarned = false
    private val backoff = Backoff()

    private val prefs by lazy { app.getSharedPreferences("conn", Context.MODE_PRIVATE) }
    var autoReconnect: Boolean
        get() = prefs.getBoolean("auto", true)
        set(v) { prefs.edit().putBoolean("auto", v).apply(); if (!v) main.removeCallbacks(retry) }
    var statusNotification: Boolean
        get() = prefs.getBoolean("notif", false)
        set(v) { prefs.edit().putBoolean("notif", v).apply(); Notifier.refreshNow(app) }
    val lastAddress: String? get() = prefs.getString("addr", null)
    val lastName: String? get() = prefs.getString("name", null)

    fun init(ctx: Context) {
        if (::app.isInitialized) return
        app = ctx.applicationContext
        ble = BleClient(app, this).also { it.connectTimeoutMs = backoff.connectTimeoutMs }
        Notifier.channels(app)
    }

    fun features() = Models.features(model)

    fun connect(d: BluetoothDevice, hint: Protocol?, m: DeviceModel, n: String) {
        try {
            main.removeCallbacks(retry)
            userDisconnected = false
            model = m; name = n
            batL = null; batR = null; chargeL = false; chargeR = false; caseBat = null; caseCharge = false; lowWarned = false
            prefs.edit().putString("addr", d.address).putString("name", n).apply()
            ble.connect(d, hint)
        } catch (e: Exception) { onStatus("Connect failed: ${e.message}") }
    }

    /** Connect to the remembered device. Returns false if there is none / Bluetooth is off. */
    fun connectLast(): Boolean {
        val a = lastAddress ?: return false
        return try {
            val ad = (app.getSystemService(Context.BLUETOOTH_SERVICE) as BluetoothManager).adapter ?: return false
            if (!ad.isEnabled) { onStatus("Bluetooth is off."); return false }
            val d = ad.getRemoteDevice(a)
            val n = d.name ?: lastName ?: a
            connect(d, null, Models.detect(n), n)
            true
        } catch (e: Exception) { onStatus("Reconnect failed: ${e.message}"); false }
    }

    fun disconnect() {
        userDisconnected = true; pendingCycle = false
        main.removeCallbacks(retry)
        try { ble.close() } catch (_: Exception) {}
        markDisconnected()
        onStatus("Disconnected")
    }

    /**
     * Close button / back: drop the link and stop everything that could reconnect on its own
     * (retry timer, pending tile cycle). The remembered device is kept, so the Quick Settings tile
     * can still connect on demand later.
     */
    fun quit() {
        disconnect()
        backoff.reset()
    }

    fun send(f: ByteArray) { try { ble.send(f) } catch (e: Exception) { onStatus("Send failed: ${e.message}") } }

    fun setAnc(m: AncMode) {
        if (!connected || ble.protocol != Protocol.A || !features().anc) return
        if (m == AncMode.CUSTOM && !features().ancLevel) return
        send(ProtoA.setAnc(m))
        if (m == AncMode.CUSTOM) send(ProtoA.setAncLevel(ancLevel ?: 5))
        update { ancMode = m.code }
    }
    fun setAncLevel(level: Int) {
        if (!connected || ble.protocol != Protocol.A || !features().ancLevel) return
        if (ancMode != AncMode.CUSTOM.code) send(ProtoA.setAnc(AncMode.CUSTOM))
        send(ProtoA.setAncLevel(level))
        update { ancMode = AncMode.CUSTOM.code; ancLevel = level }
    }
    fun nextAnc(): AncMode = when (ancMode) { 0 -> AncMode.ANC; 1 -> AncMode.TRANSPARENCY; else -> AncMode.OFF }
    /** Tile / hotkey style cycling: Off -> Quiet -> Aware -> Off. Connects first if needed. */
    fun cycleAnc() {
        if (connected) { setAnc(nextAnc()); return }
        pendingCycle = connectLast()
    }

    private inline fun update(f: () -> Unit) { f(); publish() }
    private fun publish() {
        try { Notifier.refresh(app) } catch (_: Exception) {}
        try { TileService.requestListeningState(app, ComponentName(app, AncTileService::class.java)) } catch (_: Exception) {}
    }

    /** Protocol J reports each bud on its own. A side that does not answer this refresh is not in use. */
    fun beginBatteryPoll() {
        val token = ++pollToken
        sawL = false; sawR = false
        main.postDelayed({
            if (token != pollToken) return@postDelayed
            if (!sawL) { batL = null; chargeL = false }
            if (!sawR) { batR = null; chargeR = false }
            publish()
        }, 1500)
    }

    private fun applyBattery(e: Event.Battery) {
        if (e.snapshot) {
            batL = e.left; batR = e.right; chargeL = e.chargingLeft; chargeR = e.chargingRight
        } else {
            if (e.reportsLeft) { batL = e.left; chargeL = e.chargingLeft; sawL = true }
            if (e.reportsRight) { batR = e.right; chargeR = e.chargingRight; sawR = true }
        }
        val low = BatteryText.lowest(batL, batR)
        if (low != null && low <= 20 && !lowWarned) {
            lowWarned = true
            if (!statusNotification) Notifier.lowBattery(app, name, batL, batR, chargeL, chargeR)
        } else if (low == null || low > 25) lowWarned = false
    }
    private fun markDisconnected() { connected = false; caseBat = null; publish() }

    /** One attempt at a time: the next retry is scheduled only when this attempt fails (onDisconnected). */
    private fun mayRetry() = RetryPolicy.shouldRetry(connected, autoReconnect, userDisconnected, lastAddress != null)
    private val retry = Runnable {
        if (!mayRetry()) return@Runnable
        if (ble.connecting) { scheduleRetry(); return@Runnable }
        if (!connectLast()) scheduleRetry()
    }
    private fun scheduleRetry() {
        main.removeCallbacks(retry)
        if (mayRetry()) main.postDelayed(retry, backoff.next())
    }

    // ---- BleClient.Listener (always called on the main thread) ----
    override fun onStatus(text: String) {
        val t = if (connected && !model.isUnknown && !model.tested && !text.endsWith(UNTESTED)) text + UNTESTED else text
        status = t; listeners.forEach { it.onStatus(t) }
    }
    private const val UNTESTED = " · untested model"
    override fun onReady(protocol: Protocol) {
        connected = true; backoff.reset(); lowWarned = false
        main.removeCallbacks(retry)
        // Read the battery right away on every connect, not only on the next notify,
        // so the strip, the notification and the tray pick it up immediately.
        when (protocol) {
            Protocol.A -> {
                send(ProtoA.get(ProtoA.GET_BATTERY))
                if (listeners.isEmpty() && features().anc) send(ProtoA.get(ProtoA.GET_ANC_MODE))
            }
            Protocol.J -> { beginBatteryPoll(); send(ProtoJ.get(ProtoJ.BATTERY_LEFT)); send(ProtoJ.get(ProtoJ.BATTERY_RIGHT)) }
        }
        if (pendingCycle) { pendingCycle = false; ancMode?.let { setAnc(nextAnc()) } ?: setAnc(AncMode.ANC) }
        publish()
        listeners.forEach { it.onReady(protocol) }
    }
    override fun onEvent(e: Event) {
        when (e) {
            is Event.Battery -> applyBattery(e)
            is Event.CaseBattery -> { caseBat = e.level; caseCharge = e.charging }
            is Event.Anc -> ancMode = e.mode
            is Event.AncLevel -> ancLevel = e.level
            is Event.Game -> game = e.on
            else -> {}
        }
        publish()
        listeners.forEach { it.onEvent(e) }
    }
    override fun onDisconnected() {
        markDisconnected()
        listeners.forEach { it.onDisconnected() }
        if (!userDisconnected) scheduleRetry()
    }
}
