package io.github.arkivx0.bluntybuds

import android.annotation.SuppressLint
import android.bluetooth.*
import android.content.Context
import android.os.Build
import android.os.Handler
import android.os.Looper
import io.github.arkivx0.bluntybuds.proto.*
import java.util.UUID

/** Minimal GATT client. Talks only to the earbuds; no network. */
@SuppressLint("MissingPermission")
class BleClient(private val ctx: Context, private val listener: Listener) {
    interface Listener {
        fun onStatus(text: String)
        fun onReady(protocol: Protocol)
        fun onEvent(e: Event)
        fun onDisconnected()
    }

    private val main = Handler(Looper.getMainLooper())
    private var gatt: BluetoothGatt? = null
    private var writeChar: BluetoothGattCharacteristic? = null
    private var notifyChar: BluetoothGattCharacteristic? = null
    var protocol: Protocol? = null; private set
    private var hint: Protocol? = null
    private var mtu = 23
    private val queue = ArrayDeque<ByteArray>()
    private var busy = false
    private val timeout = Runnable { busy = false; pump() }
    private var writeRejects = 0
    /** True from connect() until the link is ready, fails or drops. */
    var connecting = false; private set
    var connectTimeoutMs = 75_000L
    private val connectTimeout = Runnable {
        if (connecting) { status("Connect timed out after ${connectTimeoutMs / 1000} s."); close(); listener.onDisconnected() }
    }

    fun connect(device: BluetoothDevice, protocolHint: Protocol?) {
        close()
        hint = protocolHint
        status("Connecting to ${device.address}…")
        connecting = true
        main.postDelayed(connectTimeout, connectTimeoutMs)
        gatt = try { device.connectGatt(ctx, false, cb, BluetoothDevice.TRANSPORT_LE) }
               catch (e: Exception) { status("Connect failed: ${e.message ?: e.javaClass.simpleName}"); null }
        if (gatt == null) { status("Connect failed: the system refused to open a GATT connection."); fail() }
    }

    private fun fail() { main.post { close(); listener.onDisconnected() } }

    fun close() {
        connecting = false
        main.removeCallbacks(connectTimeout)
        main.removeCallbacks(timeout)
        queue.clear(); busy = false
        gatt?.let { try { it.disconnect(); it.close() } catch (_: Exception) {} }
        gatt = null; writeChar = null; notifyChar = null; protocol = null
    }

    /** Queue a pre-framed command. Long frames are split to fit the negotiated MTU. */
    fun send(frame: ByteArray) {
        if (writeChar == null) { status("Not connected."); return }
        val chunk = maxOf(20, mtu - 3)
        var i = 0
        while (i < frame.size) { queue.addLast(frame.copyOfRange(i, minOf(frame.size, i + chunk))); i += chunk }
        main.post { pump() }
    }

    @Suppress("DEPRECATION")
    private fun pump() {
        if (busy) return
        val g = gatt ?: return
        val c = writeChar ?: return
        val next = queue.removeFirstOrNull() ?: return
        busy = true
        try {
        val noResp = (c.properties and BluetoothGattCharacteristic.PROPERTY_WRITE) == 0
        val type = if (noResp) BluetoothGattCharacteristic.WRITE_TYPE_NO_RESPONSE else BluetoothGattCharacteristic.WRITE_TYPE_DEFAULT
        val ok = if (Build.VERSION.SDK_INT >= 33) {
            g.writeCharacteristic(c, next, type) == BluetoothStatusCodes.SUCCESS
        } else {
            c.writeType = type; c.value = next; g.writeCharacteristic(c)
        }
        if (!ok) {
            busy = false
            if (++writeRejects >= 10) { writeRejects = 0; queue.clear(); status("Write rejected by the Bluetooth stack 10 times; command dropped."); return }
            queue.addFirst(next); main.postDelayed({ pump() }, 100); return
        }
        writeRejects = 0
        main.postDelayed(timeout, 1500)
        } catch (e: Exception) { busy = false; status("Write failed: ${e.message ?: e.javaClass.simpleName}") }
    }

    private fun status(s: String) = main.post { listener.onStatus(s) }

    private fun detect(g: BluetoothGatt): Boolean {
        val jSvc = g.getService(UUID.fromString(ProtoJ.SERVICE))
        var aW: BluetoothGattCharacteristic? = null
        var aN: BluetoothGattCharacteristic? = null
        for (s in g.services) for (c in s.characteristics) {
            val u = c.uuid.toString().lowercase()
            if (u.startsWith(ProtoA.WRITE_UUID_PREFIX)) aW = c
            if (u.startsWith(ProtoA.NOTIFY_UUID_PREFIX)) aN = c
        }
        if (aW == null || aN == null) { // match on the "b611"/"b612" substring only (UUID base varies between firmwares)
            for (s in g.services) for (c in s.characteristics) {
                val u = c.uuid.toString().lowercase()
                if (aW == null && u.substring(4, 8) == "b611") aW = c
                if (aN == null && u.substring(4, 8) == "b612") aN = c
            }
        }
        val haveA = aW != null && aN != null
        val jw = jSvc?.getCharacteristic(UUID.fromString(ProtoJ.WRITE))
        val jn = jSvc?.getCharacteristic(UUID.fromString(ProtoJ.NOTIFY))
        val haveJ = jw != null && jn != null
        val pick = when {
            haveA && haveJ -> hint ?: Protocol.A
            haveA -> Protocol.A
            haveJ -> Protocol.J
            else -> null
        } ?: return false
        protocol = pick
        if (pick == Protocol.A) { writeChar = aW; notifyChar = aN } else { writeChar = jw; notifyChar = jn }
        return true
    }

    @Suppress("DEPRECATION")
    private fun enableNotify(g: BluetoothGatt, c: BluetoothGattCharacteristic) {
        if (!g.setCharacteristicNotification(c, true)) { status("Enabling notifications failed (rejected locally)."); fail(); return }
        val d = c.getDescriptor(UUID.fromString("00002902-0000-1000-8000-00805f9b34fb"))
        if (d == null) { ready(); return }
        val v = if ((c.properties and BluetoothGattCharacteristic.PROPERTY_NOTIFY) != 0)
            BluetoothGattDescriptor.ENABLE_NOTIFICATION_VALUE else BluetoothGattDescriptor.ENABLE_INDICATION_VALUE
        val ok = if (Build.VERSION.SDK_INT >= 33) g.writeDescriptor(d, v) == BluetoothStatusCodes.SUCCESS else { d.value = v; g.writeDescriptor(d) }
        if (!ok) { status("Enabling notifications failed (descriptor write rejected)."); fail() }
    }

    private fun ready() = main.post {
        connecting = false; main.removeCallbacks(connectTimeout)
        protocol?.let { listener.onReady(it) }
    }

    private fun handle(value: ByteArray) {
        val events = try { when (protocol) {
            Protocol.A -> listOfNotNull(ProtoA.decode(value))
            Protocol.J -> ProtoJ.decode(value)
            null -> emptyList()
        } } catch (e: Exception) { status("Bad notification: ${e.message}"); emptyList() }
        main.post {
            main.removeCallbacks(timeout); busy = false; pump()
            events.forEach { listener.onEvent(it) }
        }
    }

    private inline fun guard(f: () -> Unit) {
        try { f() } catch (e: Exception) { status("Bluetooth error: ${e.message ?: e.javaClass.simpleName}") }
    }

    private val cb = object : BluetoothGattCallback() {
        override fun onConnectionStateChange(g: BluetoothGatt, st: Int, newState: Int) = guard {
            if (st != BluetoothGatt.GATT_SUCCESS && newState != BluetoothProfile.STATE_CONNECTED)
                status("Connection failed (${GattErrors.describe(st)}). Take the earbuds out of the case and close other apps using them.")
            if (newState == BluetoothProfile.STATE_CONNECTED) {
                status("Connected, negotiating MTU…")
                if (!g.requestMtu(247) && !g.discoverServices()) { status("Service discovery could not start."); fail() }
            } else if (newState == BluetoothProfile.STATE_DISCONNECTED) {
                main.post { close(); listener.onDisconnected() }
            }
        }
        override fun onMtuChanged(g: BluetoothGatt, m: Int, st: Int) = guard {
            if (st == BluetoothGatt.GATT_SUCCESS) mtu = m
            status(if (st == BluetoothGatt.GATT_SUCCESS) "MTU $mtu, discovering services…" else "MTU request failed (${GattErrors.describe(st)}); using $mtu, discovering services…")
            if (!g.discoverServices()) { status("Service discovery could not start."); fail() }
        }
        override fun onServicesDiscovered(g: BluetoothGatt, st: Int) = guard {
            if (st != BluetoothGatt.GATT_SUCCESS) { status("Service discovery failed (${GattErrors.describe(st)})."); fail(); return@guard }
            if (!detect(g)) { status("No supported control service (protocol A 4157b6xx or protocol J faa0) found."); fail(); return@guard }
            status("Protocol: ${protocol}; enabling notifications…")
            enableNotify(g, notifyChar!!)
        }
        override fun onDescriptorWrite(g: BluetoothGatt, d: BluetoothGattDescriptor, st: Int) {
            if (st != BluetoothGatt.GATT_SUCCESS) { status("Enabling notifications failed (${GattErrors.describe(st)})."); fail(); return }
            ready()
        }
        override fun onCharacteristicWrite(g: BluetoothGatt, c: BluetoothGattCharacteristic, st: Int) {
            if (st != BluetoothGatt.GATT_SUCCESS) status("Write failed (${GattErrors.describe(st)}).")
            // Reply (if any) arrives as a notification; release the queue shortly after the write completes.
            main.post { main.removeCallbacks(timeout); main.postDelayed(timeout, 250) }
        }
        override fun onCharacteristicChanged(g: BluetoothGatt, c: BluetoothGattCharacteristic, value: ByteArray) {
            if (c.uuid == notifyChar?.uuid) handle(value)
        }
        @Deprecated("pre-33")
        @Suppress("DEPRECATION")
        override fun onCharacteristicChanged(g: BluetoothGatt, c: BluetoothGattCharacteristic) {
            if (Build.VERSION.SDK_INT < 33 && c.uuid == notifyChar?.uuid) handle(c.value ?: return)
        }
    }
}
