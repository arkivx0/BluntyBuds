package io.github.arkivx0.bluntybuds

import android.Manifest
import android.annotation.SuppressLint
import android.app.Activity
import android.app.AlertDialog
import android.bluetooth.BluetoothDevice
import android.bluetooth.BluetoothManager
import android.bluetooth.le.ScanCallback
import android.bluetooth.le.ScanResult
import android.bluetooth.le.ScanSettings
import android.content.Intent
import android.content.pm.PackageManager
import android.content.res.ColorStateList
import android.graphics.Typeface
import android.graphics.drawable.GradientDrawable
import android.os.Build
import android.os.Bundle
import android.text.TextUtils
import android.view.WindowInsets
import android.view.WindowInsetsController
import android.os.Handler
import android.os.Looper
import android.view.Gravity
import android.view.View
import android.view.ViewGroup
import android.view.ViewGroup.LayoutParams.MATCH_PARENT
import android.view.ViewGroup.LayoutParams.WRAP_CONTENT
import android.widget.*
import io.github.arkivx0.bluntybuds.IconDrawable.Kind
import io.github.arkivx0.bluntybuds.proto.*

/**
 * Home: one screen, no scrolling. Top strip (device, connection dot, L / Case / R battery pills, Offline badge),
 * noise control, Tune (10-band EQ with Lock EQ) and Sound Profile chips, then a row of icon buttons that open
 * compact sheets (Gestures, L-R Balance, Low-Latency, Ping Buds).
 */
@SuppressLint("MissingPermission", "SetTextI18n")
class MainActivity : Activity(), BleClient.Listener {
    companion object {
        // Background, surfaces, one accent. 8 dp grid, 32 dp rows, 13-14 sp text.
        const val BG = 0xFF0E0F12.toInt(); const val CARD = 0xFF17191E.toInt(); const val CTL = 0xFF23262D.toInt()
        const val LINE = 0xFF353944.toInt()
        const val FG = 0xFFE6E8EE.toInt(); const val DIM = 0xFF8C93A3.toInt(); const val ACCENT = 0xFF3DD6B5.toInt()
        const val ACCENT_FG = 0xFF0B1A17.toInt()
        const val RC_IMPORT = 41; const val RC_EXPORT = 42; const val RC_AUTOEQ = 43
        const val ROW = 32
    }
    private val ble get() = Conn.ble
    private lateinit var tone: Tone
    private lateinit var presets: Presets
    private val main = Handler(Looper.getMainLooper())
    private val ui by lazy { getSharedPreferences("ui", MODE_PRIVATE) }

    private lateinit var status: TextView
    private lateinit var deviceSpinner: Spinner
    private lateinit var showAll: CheckBox
    private lateinit var controls: LinearLayout
    private lateinit var deviceName: TextView
    private lateinit var connDot: View
    private lateinit var chipL: TextView
    private lateinit var chipR: TextView
    private lateinit var soloLabel: TextView
    private lateinit var pillL: Pill
    private lateinit var pillR: Pill
    private lateinit var pillCase: Pill
    private lateinit var firmware: TextView
    private lateinit var game: Switch
    private val eqBars = ArrayList<VerticalSeekBar>()
    private val eqLabels = ArrayList<TextView>()
    private lateinit var eqRow: LinearLayout
    private lateinit var lockBtn: ImageView
    private lateinit var chipRow: LinearLayout
    private var selectedProfile: String? = null
    private val touchSpinners = HashMap<Pair<Boolean, Gesture>, Spinner>()
    private lateinit var balanceBar: SeekBar
    private lateinit var balanceLabel: TextView
    private lateinit var ancBox: LinearLayout
    private val ancButtons = LinkedHashMap<AncMode, TextView>()
    private lateinit var ancOther: TextView
    private lateinit var ancLevelBox: LinearLayout
    private lateinit var ancLevelBar: SeekBar
    private lateinit var ancLevelLabel: TextView
    private lateinit var btnGestures: View
    private lateinit var btnBalance: View
    private lateinit var btnLowLat: View
    private lateinit var btnPing: View
    private lateinit var lowLatIcon: IconDrawable
    private lateinit var deviceSheet: LinearLayout
    private lateinit var gestureSheet: LinearLayout
    private lateinit var balanceSheet: LinearLayout
    private lateinit var lowLatSheet: LinearLayout
    private lateinit var pingSheet: LinearLayout
    private lateinit var soundSheet: LinearLayout
    private lateinit var snack: LinearLayout
    private lateinit var snackText: TextView
    private var snackUndo: (() -> Unit)? = null
    private val hideSnack = Runnable { snack.visibility = View.GONE; snackUndo = null }

    private val model get() = Conn.model
    private var touch = TouchMap.defaults()
    private var suppress = false
    private val seen = LinkedHashMap<String, Pair<ScanResult, AdParser.VendorAd?>>()
    private var scanning = false
    private val bonded = LinkedHashMap<String, BluetoothDevice>()
    private data class DevEntry(val label: String, val device: BluetoothDevice, val ad: AdParser.VendorAd?, val model: DeviceModel, val name: String)
    private var entries = listOf<DevEntry>()
    private val devLabels = ArrayList<String>()
    private var devAdapter: android.widget.ArrayAdapter<String>? = null
    private val scanGen = Generation()
    private val renderCoalesce = Coalescer(500)
    private var renderPosted = false
    private val renderTick = Runnable { renderPosted = false; if (renderCoalesce.shouldFlush(android.os.SystemClock.uptimeMillis())) renderDevices() else scheduleRender() }
    private fun scheduleRender() { if (!renderPosted) { renderPosted = true; main.postDelayed(renderTick, 250) } }

    private var eqLocked: Boolean
        get() = ui.getBoolean("eq_locked", false)
        set(v) { ui.edit().putBoolean("eq_locked", v).apply(); renderLock() }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        Conn.init(this)
        tone = Tone(this); presets = Presets(this)
        edgeToEdge()
        setContentView(buildUi())
        lightBarIcons()
        Conn.listeners.add(this)
        requestPerms()
        if (hasPerms()) afterPerms()
        renderBattery()
        if (Conn.connected) Conn.ble.protocol?.let { onReady(it) } else renderConnection(false)
    }

    override fun onRequestPermissionsResult(rc: Int, perms: Array<out String>, res: IntArray) {
        super.onRequestPermissionsResult(rc, perms, res)
        if (rc == 2) Notifier.refreshNow(this)
        if (hasPerms()) afterPerms()
    }

    private fun afterPerms() {
        listBonded()
        if (!Conn.connected && Conn.autoReconnect && Conn.lastAddress != null) {
            onStatus("Reconnecting to ${Conn.lastName ?: "the last earbuds"}…")
            Conn.connectLast()
        }
    }

    // Connection lives in Conn (shared with the Quick Settings tile / notification), so it is not closed here.
    /** Close button and back: stop scanning and the chirp, drop the earbud link (no reconnect loop left behind), close the task. */
    private fun quit() {
        main.removeCallbacks(renderTick)
        stopScan()
        tone.stop()
        Conn.quit()
        finishAndRemoveTask()
    }
    @Deprecated("Back is the same as Close")
    @Suppress("DEPRECATION")
    override fun onBackPressed() { quit() }

    override fun onDestroy() { Conn.listeners.remove(this); main.removeCallbacks(renderTick); main.removeCallbacks(hideSnack); stopScan(); tone.stop(); super.onDestroy() }

    // ---------- permissions ----------
    private fun neededPerms(): Array<String> {
        val p = ArrayList<String>()
        if (Build.VERSION.SDK_INT >= 31) { p += Manifest.permission.BLUETOOTH_SCAN; p += Manifest.permission.BLUETOOTH_CONNECT }
        else p += Manifest.permission.ACCESS_FINE_LOCATION
        return p.toTypedArray()
    }
    private fun hasPerms() = neededPerms().all { checkSelfPermission(it) == PackageManager.PERMISSION_GRANTED }
    private fun requestPerms() { if (!hasPerms()) requestPermissions(neededPerms(), 1) }
    private fun askNotifPermission() {
        if (Build.VERSION.SDK_INT >= 33 && checkSelfPermission(Manifest.permission.POST_NOTIFICATIONS) != PackageManager.PERMISSION_GRANTED)
            requestPermissions(arrayOf(Manifest.permission.POST_NOTIFICATIONS), 2)
    }
    /** Only permissions that are actually granted right now, for About and Privacy. */
    private fun grantedPermissions(): List<String> {
        fun ok(p: String) = checkSelfPermission(p) == PackageManager.PERMISSION_GRANTED
        val out = ArrayList<String>()
        if (Build.VERSION.SDK_INT >= 31) {
            if (ok(Manifest.permission.BLUETOOTH_SCAN)) out += "Nearby devices: find earbuds (no location)"
            if (ok(Manifest.permission.BLUETOOTH_CONNECT)) out += "Nearby devices: connect to earbuds"
        } else {
            out += "Bluetooth"
            if (ok(Manifest.permission.ACCESS_FINE_LOCATION)) out += "Location (Android 10–11 requires it for Bluetooth scanning; not used for location)"
        }
        if (Build.VERSION.SDK_INT < 33 || ok(Manifest.permission.POST_NOTIFICATIONS)) out += "Notifications (local only)"
        return out
    }

    // ---------- UI helpers (dark, compact) ----------
    private fun dp(v: Int) = (v * resources.displayMetrics.density).toInt()
    private fun round(color: Int, r: Int = 8, stroke: Int? = null) = GradientDrawable().apply {
        setColor(color); cornerRadius = dp(r).toFloat(); stroke?.let { setStroke(dp(1), it) }
    }
    private fun outline(color: Int, r: Int = 8) = GradientDrawable().apply { setColor(0); cornerRadius = dp(r).toFloat(); setStroke(dp(1), color) }
    private fun text(t: String, size: Float = 13f, color: Int = FG, bold: Boolean = false) = TextView(this).apply {
        text = t; textSize = size; setTextColor(color); if (bold) setTypeface(typeface, Typeface.BOLD)
    }
    private fun title(t: String) = text(t, 14f, FG, true)
    private fun btn(t: String, accent: Boolean = false, on: () -> Unit) = TextView(this).apply {
        text = t; textSize = 13f; gravity = Gravity.CENTER
        setTextColor(if (accent) ACCENT_FG else FG); if (accent) setTypeface(typeface, Typeface.BOLD)
        background = round(if (accent) ACCENT else CTL, 8)
        setPadding(dp(12), 0, dp(12), 0); minHeight = dp(ROW)
        isClickable = true; isFocusable = true
        setOnClickListener { try { on() } catch (e: Exception) { onStatus("Error: ${e.message}") } }
        layoutParams = LinearLayout.LayoutParams(WRAP_CONTENT, dp(ROW)).apply { marginEnd = dp(8) }
    }
    private fun icon(kind: Kind, color: Int = FG, size: Int = 20) = IconDrawable(kind, color, dp(size))
    private fun iconBtn(kind: Kind, desc: String, on: () -> Unit) = ImageView(this).apply {
        setImageDrawable(icon(kind, DIM)); contentDescription = desc; scaleType = ImageView.ScaleType.CENTER
        layoutParams = LinearLayout.LayoutParams(dp(ROW), dp(ROW)); isClickable = true; isFocusable = true
        background = round(0, 8); setOnClickListener { on() }
    }
    private fun row(vararg v: View) = LinearLayout(this).apply {
        orientation = LinearLayout.HORIZONTAL; gravity = Gravity.CENTER_VERTICAL; v.forEach { addView(it) }
    }
    private fun col(vararg v: View) = LinearLayout(this).apply { orientation = LinearLayout.VERTICAL; v.forEach { addView(it) } }
    private fun card(vararg v: View) = col(*v).apply {
        background = round(CARD, 12); setPadding(dp(12), dp(8), dp(12), dp(8))
        layoutParams = LinearLayout.LayoutParams(MATCH_PARENT, WRAP_CONTENT).apply { bottomMargin = dp(8) }
    }
    private fun space(w: Int) = View(this).apply { layoutParams = LinearLayout.LayoutParams(dp(w), 1) }
    private fun weight(v: View) = v.apply { layoutParams = LinearLayout.LayoutParams(0, WRAP_CONTENT, 1f) }
    private fun tintSeek(s: SeekBar) = s.apply {
        progressTintList = ColorStateList.valueOf(ACCENT); thumbTintList = ColorStateList.valueOf(ACCENT)
        progressBackgroundTintList = ColorStateList.valueOf(CTL)
    }
    private fun <T> spinner(items: List<T>) = Spinner(this, Spinner.MODE_DROPDOWN).apply {
        adapter = darkAdapter(items); background = round(CTL, 8)
        setPopupBackgroundDrawable(round(CARD, 8, LINE))
        setPadding(dp(8), 0, dp(8), 0); minimumHeight = dp(ROW)
    }
    private fun <T> darkAdapter(items: List<T>) = object : ArrayAdapter<T>(this, android.R.layout.simple_spinner_dropdown_item, items) {
        override fun getView(p: Int, c: View?, parent: ViewGroup) = style(super.getView(p, c, parent), true)
        override fun getDropDownView(p: Int, c: View?, parent: ViewGroup) = style(super.getDropDownView(p, c, parent), false)
        private fun style(v: View, compact: Boolean) = v.apply {
            (this as? TextView)?.apply { setTextColor(FG); textSize = 13f; if (compact) { setPadding(0, dp(4), 0, dp(4)); maxLines = 1 } }
        }
    }
    private fun switch(t: String, on: (Boolean) -> Unit) = Switch(this).apply {
        text = t; setTextColor(FG); textSize = 13f; minHeight = dp(ROW)
        thumbTintList = ColorStateList(arrayOf(intArrayOf(android.R.attr.state_checked), intArrayOf()), intArrayOf(ACCENT, DIM))
        trackTintList = ColorStateList(arrayOf(intArrayOf(android.R.attr.state_checked), intArrayOf()), intArrayOf(0xFF1F6B5B.toInt(), CTL))
        setOnCheckedChangeListener { _, c -> if (!suppress) try { on(c) } catch (e: Exception) { onStatus("Error: ${e.message}") } }
    }
    private fun chip(t: String, on: Boolean) = TextView(this).apply {
        text = t; textSize = 13f; gravity = Gravity.CENTER; maxLines = 1
        setPadding(dp(12), 0, dp(12), 0)
        setTextColor(if (on) ACCENT_FG else FG); background = if (on) round(ACCENT, 16) else round(CTL, 16)
        layoutParams = LinearLayout.LayoutParams(WRAP_CONTENT, dp(ROW)).apply { marginEnd = dp(8) }
    }

    /** Battery pill: number plus a bar. A missing earbud is faded to an outline (never shown as 0%). */
    private inner class Pill(val side: String, small: Boolean) : LinearLayout(this@MainActivity) {
        val label = text("$side —", if (small) 12f else 14f, FG, !small).apply { gravity = Gravity.CENTER; maxLines = 1 }
        val bar = ProgressBar(this@MainActivity, null, android.R.attr.progressBarStyleHorizontal).apply {
            max = 100; progressTintList = ColorStateList.valueOf(ACCENT); progressBackgroundTintList = ColorStateList.valueOf(LINE)
            layoutParams = LayoutParams(MATCH_PARENT, dp(4)).apply { topMargin = dp(4) }
        }
        init {
            orientation = VERTICAL; gravity = Gravity.CENTER; setPadding(dp(8), dp(4), dp(8), dp(6))
            addView(label); addView(bar)
        }
        fun set(pct: Int?, charging: Boolean, missing: Boolean) {
            if (pct == null) {
                label.text = if (missing) side else "$side —"
                bar.visibility = View.INVISIBLE
                background = outline(if (missing) CTL else LINE, 10)
                alpha = if (missing) 0.55f else 0.8f
                contentDescription = if (missing) "$side earbud not in use" else "$side battery unknown"
            } else {
                label.text = "$side $pct%" + if (charging) " ⚡\uFE0E" else ""
                bar.visibility = View.VISIBLE; bar.progress = pct
                background = round(CTL, 10); alpha = 1f
                contentDescription = "$side $pct percent" + if (charging) ", charging" else ""
            }
        }
    }

    private fun buildUi(): View {
        val frame = FrameLayout(this).apply { setBackgroundColor(BG) }
        val root = LinearLayout(this).apply { orientation = LinearLayout.VERTICAL; setPadding(dp(16), dp(8), dp(16), dp(8)) }
        frame.addView(root, FrameLayout.LayoutParams(MATCH_PARENT, MATCH_PARENT))

        // ---- top strip: device name · connection dot · L/R chips · Offline badge, then L / Case / R pills ----
        connDot = View(this).apply { background = GradientDrawable().apply { shape = GradientDrawable.OVAL; setColor(DIM) }; layoutParams = LinearLayout.LayoutParams(dp(8), dp(8)) }
        deviceName = text("Not connected", 14f, FG, true).apply {
            maxLines = 1; ellipsize = TextUtils.TruncateAt.END; setPadding(dp(8), 0, dp(8), 0); minHeight = dp(ROW); gravity = Gravity.CENTER_VERTICAL
            isClickable = true; setOnClickListener { openSheet("Earbuds", deviceSheet) }; contentDescription = "Earbuds and connection"
        }
        fun budChip(t: String) = text(t, 12f, DIM, true).apply { gravity = Gravity.CENTER; layoutParams = LinearLayout.LayoutParams(dp(24), dp(24)).apply { marginStart = dp(4) } }
        chipL = budChip("L"); chipR = budChip("R")
        soloLabel = text("Solo", 12f, ACCENT, true).apply { visibility = View.GONE; setPadding(dp(4), 0, 0, 0) }
        val offline = text("Offline", 12f, ACCENT, true).apply {
            setCompoundDrawablesRelative(icon(Kind.SHIELD, ACCENT, 14).also { it.setBounds(0, 0, dp(14), dp(14)) }, null, null, null)
            compoundDrawablePadding = dp(4); gravity = Gravity.CENTER_VERTICAL
            background = outline(0xFF1F6B5B.toInt(), 12); setPadding(dp(8), dp(2), dp(8), dp(2))
            layoutParams = LinearLayout.LayoutParams(WRAP_CONTENT, dp(24)).apply { marginStart = dp(8) }
            contentDescription = "Offline: no internet permission"; setOnClickListener { showAbout() }
        }
        pillL = Pill("L", false); pillR = Pill("R", false); pillCase = Pill("Case", true)
        val pills = row(weight(pillL), space(8), pillCase.apply { layoutParams = LinearLayout.LayoutParams(dp(88), WRAP_CONTENT) }, space(8), weight(pillR)).apply { setPadding(0, dp(4), 0, 0) }
        root.addView(card(
            row(connDot, weight(deviceName), soloLabel, chipL, chipR, offline,
                iconBtn(Kind.INFO, "About and Privacy") { showAbout() }.apply { (layoutParams as LinearLayout.LayoutParams).marginStart = dp(4) },
                iconBtn(Kind.CLOSE, "Close BluntyBuds") { quit() }),
            pills))
        status = text("Idle", 12f, DIM).apply { maxLines = 1; ellipsize = TextUtils.TruncateAt.END; setPadding(dp(4), 0, dp(4), dp(8)) }
        root.addView(status)

        controls = LinearLayout(this).apply { orientation = LinearLayout.VERTICAL; alpha = 0.45f }
        root.addView(controls, LinearLayout.LayoutParams(MATCH_PARENT, 0, 1f))

        // ---- noise: segmented control, Depth only where the device supports it ----
        val seg = LinearLayout(this).apply { orientation = LinearLayout.HORIZONTAL; background = round(CTL, 8); setPadding(dp(2), dp(2), dp(2), dp(2)) }
        for ((m, t) in listOf(AncMode.ANC to "Quiet", AncMode.TRANSPARENCY to "Aware", AncMode.OFF to "Off", AncMode.CUSTOM to "Custom")) {
            val b = text(t, 13f).apply {
                gravity = Gravity.CENTER; maxLines = 1
                layoutParams = LinearLayout.LayoutParams(0, dp(ROW - 4), 1f)
                setOnClickListener { if (!suppress) Conn.setAnc(m) }
            }
            ancButtons[m] = b; seg.addView(b)
        }
        ancLevelLabel = text("Depth 5", 13f, DIM).apply { width = dp(72) }
        ancLevelBar = tintSeek(SeekBar(this)).apply {
            max = ProtoA.ANC_LEVEL_MAX - ProtoA.ANC_LEVEL_MIN; progress = 4
            setOnSeekBarChangeListener(object : SeekBar.OnSeekBarChangeListener {
                override fun onProgressChanged(s: SeekBar?, p: Int, u: Boolean) { ancLevelLabel.text = "Depth ${p + 1}" }
                override fun onStartTrackingTouch(s: SeekBar?) {}
                override fun onStopTrackingTouch(s: SeekBar?) { if (!suppress) Conn.setAncLevel(progress + 1) }
            })
        }
        ancLevelBox = row(ancLevelLabel, weight(ancLevelBar)).apply { minimumHeight = dp(ROW); setPadding(0, dp(4), 0, 0) }
        ancOther = text("", 12f, DIM)
        ancBox = card(seg, ancLevelBox, ancOther)
        controls.addView(ancBox)

        // ---- Tune: EQ with Lock EQ, Sound Profile chips below ----
        lockBtn = iconBtn(Kind.UNLOCK, "Lock EQ") { eqLocked = !eqLocked; toast(if (eqLocked) "EQ locked" else "EQ unlocked") }
        val tuneHeader = row(title("Tune"), space(4), lockBtn, weight(View(this)),
            btn("Sound") { openSheet("Sound", soundSheet) }.apply { (layoutParams as LinearLayout.LayoutParams).marginEnd = 0 })
        eqRow = LinearLayout(this).apply { orientation = LinearLayout.HORIZONTAL; setPadding(0, dp(4), 0, dp(4)) }
        for (i in 0 until 10) {
            val c = LinearLayout(this).apply { orientation = LinearLayout.VERTICAL; gravity = Gravity.CENTER_HORIZONTAL; layoutParams = LinearLayout.LayoutParams(0, MATCH_PARENT, 1f) }
            val lbl = text("0", 12f, FG).apply { gravity = Gravity.CENTER }
            val bar = VerticalSeekBar(this).apply {
                tintSeek(this)
                max = 48; progress = 24; setPadding(dp(8), 0, dp(8), 0)
                layoutParams = LinearLayout.LayoutParams(dp(32), 0, 1f)
                setOnSeekBarChangeListener(object : SeekBar.OnSeekBarChangeListener {
                    override fun onProgressChanged(s: SeekBar?, p: Int, u: Boolean) { updateEqLabel(i) }
                    override fun onStartTrackingTouch(s: SeekBar?) {}
                    override fun onStopTrackingTouch(s: SeekBar?) {}
                })
                blocked = { eqLocked }
                onBlockedTap = { toast("EQ locked") }
                onRelease = { onSliderReleased() }
            }
            val f = EqGrid.FREQS[i]
            val fl = text(if (f >= 1000) "${f / 1000.0}".removeSuffix(".0") + "k" else "$f", 12f, DIM).apply { gravity = Gravity.CENTER }
            eqLabels += lbl; eqBars += bar
            c.addView(lbl); c.addView(bar); c.addView(fl)
            eqRow.addView(c)
        }
        for (i in 0 until 10) updateEqLabel(i)
        chipRow = LinearLayout(this).apply { orientation = LinearLayout.HORIZONTAL; gravity = Gravity.CENTER_VERTICAL }
        val chipScroll = HorizontalScrollView(this).apply { isHorizontalScrollBarEnabled = false; addView(chipRow) }
        val tune = card(tuneHeader, eqRow.apply { layoutParams = LinearLayout.LayoutParams(MATCH_PARENT, 0, 1f) },
            text("Sound Profiles", 12f, DIM).apply { setPadding(0, 0, 0, dp(4)) }, chipScroll)
        tune.layoutParams = LinearLayout.LayoutParams(MATCH_PARENT, 0, 1f).apply { bottomMargin = dp(8) }
        controls.addView(tune)
        refreshPresets()
        renderLock()

        // ---- bottom row: icon buttons that open compact sheets ----
        fun tool(kind: Kind, label: String, on: () -> Unit): Pair<LinearLayout, IconDrawable> {
            val d = icon(kind, FG, 20)
            val v = col(ImageView(this).apply { setImageDrawable(d); layoutParams = LinearLayout.LayoutParams(MATCH_PARENT, dp(24)) },
                text(label, 12f, DIM).apply { gravity = Gravity.CENTER; maxLines = 1 }).apply {
                gravity = Gravity.CENTER; background = round(CARD, 12); setPadding(0, dp(8), 0, dp(8)); isClickable = true; isFocusable = true
                contentDescription = label; setOnClickListener { try { on() } catch (e: Exception) { onStatus("Error: ${e.message}") } }
                layoutParams = LinearLayout.LayoutParams(0, WRAP_CONTENT, 1f).apply { marginEnd = dp(8) }
            }
            return v to d
        }
        btnGestures = tool(Kind.GESTURE, "Gestures") { openSheet("Gestures", gestureSheet) }.first
        btnBalance = tool(Kind.BALANCE, "L-R Balance") { openSheet("L-R Balance", balanceSheet) }.first
        val ll = tool(Kind.BOLT, "Low-Latency") { openSheet("Low-Latency", lowLatSheet) }
        btnLowLat = ll.first; lowLatIcon = ll.second
        btnPing = tool(Kind.PING, "Ping Buds") { openSheet("Ping Buds", pingSheet) }.first
        (btnPing.layoutParams as LinearLayout.LayoutParams).marginEnd = 0
        controls.addView(row(btnGestures, btnBalance, btnLowLat, btnPing))

        buildSheets()
        setControlsEnabled(false)

        // ---- Undo snackbar (3 s) ----
        snackText = text("", 13f, FG).apply { maxLines = 1; ellipsize = TextUtils.TruncateAt.END }
        snack = row(weight(snackText), text("Undo", 13f, ACCENT, true).apply {
            setPadding(dp(16), 0, dp(8), 0); minHeight = dp(ROW); gravity = Gravity.CENTER_VERTICAL
            setOnClickListener { val u = snackUndo; main.removeCallbacks(hideSnack); hideSnack.run(); u?.invoke() }
        }).apply { background = round(CTL, 8, LINE); setPadding(dp(16), dp(4), dp(8), dp(4)); visibility = View.GONE; elevation = dp(6).toFloat() }
        frame.addView(snack, FrameLayout.LayoutParams(MATCH_PARENT, WRAP_CONTENT, Gravity.BOTTOM).apply { setMargins(dp(16), 0, dp(16), dp(96)) })

        // Edge-to-edge: content stays clear of the status bar, camera cutout and navigation bar; the background fills behind them.
        frame.setOnApplyWindowInsetsListener { v, insets -> val (l, t, r, b) = barInsets(insets); v.setPadding(l, t, r, b); insets }
        frame.requestApplyInsets()
        return frame
    }

    private fun buildSheets() {
        // Earbuds (connection) sheet
        deviceSpinner = spinner(listOf("(no devices yet)"))
        showAll = CheckBox(this).apply {
            text = "All"; setTextColor(DIM); textSize = 13f; buttonTintList = ColorStateList.valueOf(ACCENT)
            setOnCheckedChangeListener { _, _ -> renderDevices() }
        }
        firmware = text("FW —", 12f, DIM).apply { setPadding(0, dp(8), 0, 0) }
        val auto = switch("Auto-reconnect to last device") { Conn.autoReconnect = it }.apply { isChecked = Conn.autoReconnect }
        val notif = switch("Earbud status in the notification shade") { if (it) askNotifPermission(); Conn.statusNotification = it }.apply { isChecked = Conn.statusNotification }
        deviceSheet = col(
            row(weight(deviceSpinner), space(8), btn("Connect", accent = true) { connectSelected() }.apply { (layoutParams as LinearLayout.LayoutParams).marginEnd = 0 }),
            row(btn("Scan") { startScan() }, btn("Paired") { listBonded() }, btn("Disconnect") { Conn.disconnect() }, showAll).apply { setPadding(0, dp(8), 0, 0) },
            firmware, auto, notif,
            text("Quick Settings: add the “Noise” tile to cycle Off → Quiet → Aware.", 12f, DIM).apply { setPadding(0, dp(4), 0, 0) })

        // Gestures
        val grid = GridLayout(this).apply { columnCount = 3 }
        fun cell(v: View, c: Int, w: Float) = v.apply {
            layoutParams = GridLayout.LayoutParams(GridLayout.spec(GridLayout.UNDEFINED), GridLayout.spec(c, w)).apply { width = 0; setMargins(0, dp(2), dp(8), dp(2)) }
        }
        grid.addView(cell(text("", 12f), 0, 0.8f)); grid.addView(cell(text("Left", 12f, DIM), 1, 1f)); grid.addView(cell(text("Right", 12f, DIM), 2, 1f))
        val actions = TouchAction.entries.map { it.label }
        for (g in Gesture.entries) {
            grid.addView(cell(text(g.label, 13f), 0, 0.8f))
            for (left in listOf(true, false)) {
                val sp = spinner(actions); touchSpinners[left to g] = sp
                grid.addView(cell(sp, if (left) 1 else 2, 1f))
            }
        }
        gestureSheet = col(grid, row(btn("Defaults") { touch = TouchMap.defaults(); renderTouch() }, btn("Apply", accent = true) { sendTouch(); toast("Gestures sent") }).apply { setPadding(0, dp(8), 0, 0) })

        // L-R Balance (sent on release)
        balanceLabel = text("0", 13f, FG).apply { width = dp(32); gravity = Gravity.END }
        balanceBar = tintSeek(SeekBar(this)).apply {
            max = 22; progress = 11
            setOnSeekBarChangeListener(object : SeekBar.OnSeekBarChangeListener {
                override fun onProgressChanged(s: SeekBar?, p: Int, u: Boolean) { balanceLabel.text = "%+d".format(p - 11).replace("+0", "0") }
                override fun onStartTrackingTouch(s: SeekBar?) {}
                override fun onStopTrackingTouch(s: SeekBar?) { if (!suppress) sendBalance() }
            })
        }
        balanceSheet = col(row(text("L", 13f, DIM), weight(balanceBar), text("R", 13f, DIM), balanceLabel).apply { minimumHeight = dp(ROW) },
            row(btn("Centre") { balanceBar.progress = 11; sendBalance() }).apply { setPadding(0, dp(8), 0, 0) })

        // Low-Latency
        game = switch("Low-Latency") { sendGame(it); renderLowLat(it) }
        lowLatSheet = col(game, text("Lower audio delay for games and video. May use a little more battery.", 12f, DIM))

        // Ping Buds
        pingSheet = col(text("Plays a loud chirp. Take the earbuds out of your ears first.", 12f, DIM).apply { setPadding(0, 0, 0, dp(8)) },
            row(btn("Left") { tone.play(true, false) }, btn("Right") { tone.play(false, true) }, btn("Both") { tone.play(true, true) }, btn("Stop") { tone.stop() }))

        // Sound: profile files and corrections
        soundSheet = col(
            text("Tap a Sound Profile chip to apply it. Long-press a chip to save or rename.", 12f, DIM).apply { setPadding(0, 0, 0, dp(8)) },
            row(btn("Import Correction") { importAutoEq() }, btn("Save as new…") { savePresetDialog() }),
            row(btn("Import profiles") { importPresets() }, btn("Export profiles") { exportPresets() }).apply { setPadding(0, dp(8), 0, 0) },
            text("Import Correction loads an AutoEQ ParametricEQ.txt or GraphicEQ.txt and fits it to the 10 bands, offline.", 12f, DIM).apply { setPadding(0, dp(8), 0, 0) })
    }

    private var openDialog: AlertDialog? = null
    /** Compact bottom sheet holding one of the prebuilt panels. */
    private fun openSheet(t: String, body: LinearLayout) {
        (body.parent as? ViewGroup)?.removeView(body)
        val wrap = col(text(t, 14f, FG, true).apply { setPadding(0, 0, 0, dp(8)) }, body).apply { setPadding(dp(16), dp(16), dp(16), dp(16)) }
        openDialog?.dismiss()
        openDialog = AlertDialog.Builder(this).setView(wrap).create().apply {
            setOnShowListener { window?.setBackgroundDrawable(round(CARD, 16)) }
            window?.setGravity(Gravity.BOTTOM)
            show()
        }
    }

    @Suppress("DEPRECATION")
    private fun edgeToEdge() {
        window.statusBarColor = BG; window.navigationBarColor = BG
        if (Build.VERSION.SDK_INT >= 30) window.setDecorFitsSystemWindows(false)
        else window.decorView.systemUiVisibility = window.decorView.systemUiVisibility or View.SYSTEM_UI_FLAG_LAYOUT_STABLE or
            View.SYSTEM_UI_FLAG_LAYOUT_FULLSCREEN or View.SYSTEM_UI_FLAG_LAYOUT_HIDE_NAVIGATION
    }

    @Suppress("DEPRECATION")
    private fun barInsets(insets: WindowInsets): IntArray =
        if (Build.VERSION.SDK_INT >= 30) {
            val i = insets.getInsets(WindowInsets.Type.systemBars() or WindowInsets.Type.displayCutout())
            intArrayOf(i.left, i.top, i.right, i.bottom)
        } else {
            val c = insets.displayCutout
            intArrayOf(maxOf(insets.systemWindowInsetLeft, c?.safeInsetLeft ?: 0), maxOf(insets.systemWindowInsetTop, c?.safeInsetTop ?: 0),
                maxOf(insets.systemWindowInsetRight, c?.safeInsetRight ?: 0), maxOf(insets.systemWindowInsetBottom, c?.safeInsetBottom ?: 0))
        }

    /** Light (white) status and navigation bar icons on the dark background. */
    @Suppress("DEPRECATION")
    private fun lightBarIcons() {
        if (Build.VERSION.SDK_INT >= 30) {
            window.insetsController?.setSystemBarsAppearance(0,
                WindowInsetsController.APPEARANCE_LIGHT_STATUS_BARS or WindowInsetsController.APPEARANCE_LIGHT_NAVIGATION_BARS)
        } else {
            window.decorView.systemUiVisibility = window.decorView.systemUiVisibility and
                (View.SYSTEM_UI_FLAG_LIGHT_STATUS_BAR or View.SYSTEM_UI_FLAG_LIGHT_NAVIGATION_BAR).inv()
        }
    }

    private fun setControlsEnabled(on: Boolean) {
        controls.alpha = if (on) 1f else 0.45f
        fun walk(v: View) { v.isEnabled = on; (v as? ViewGroup)?.let { g -> for (i in 0 until g.childCount) walk(g.getChildAt(i)) } }
        walk(controls)
        for (s in listOf(gestureSheet, balanceSheet, lowLatSheet)) walk(s)
        renderLock()
    }

    // ---------- battery strip (Solo Mode) ----------
    private fun renderBattery() {
        val l = Conn.batL; val r = Conn.batR
        val solo = if (Conn.connected) BatteryText.solo(l, r) else null
        pillL.set(l, Conn.chargeL, missing = solo == "R")
        pillR.set(r, Conn.chargeR, missing = solo == "L")
        pillCase.set(Conn.caseBat, Conn.caseCharge, missing = false)
        pillCase.visibility = if (Conn.caseBat != null) View.VISIBLE else View.INVISIBLE
        fun budChip(c: TextView, present: Boolean) {
            c.setTextColor(if (present) ACCENT_FG else DIM)
            c.background = if (present) round(ACCENT, 12) else outline(LINE, 12)
            c.contentDescription = "${c.text} earbud " + if (present) "in use" else "not in use"
        }
        val show = Conn.connected && (l != null || r != null)
        for (c in listOf(chipL, chipR)) c.visibility = if (show) View.VISIBLE else View.GONE
        budChip(chipL, l != null); budChip(chipR, r != null)
        soloLabel.visibility = if (solo != null) View.VISIBLE else View.GONE
    }
    private fun renderConnection(on: Boolean) {
        (connDot.background as GradientDrawable).setColor(if (on) ACCENT else DIM)
        connDot.contentDescription = if (on) "Connected" else "Not connected"
        deviceName.text = if (on) Conn.name.ifEmpty { if (model.isUnknown) "Earbuds" else model.name } else (Conn.lastName?.let { "$it · tap to connect" } ?: "Tap to connect")
        renderBattery()
    }
    private fun renderLowLat(on: Boolean) { lowLatIcon.color = if (on) ACCENT else FG; lowLatIcon.invalidateSelf() }

    // ---------- Tune / Lock EQ / Sound Profiles ----------
    private fun renderLock() {
        if (!::lockBtn.isInitialized) return
        val locked = eqLocked
        lockBtn.setImageDrawable(icon(if (locked) Kind.LOCK else Kind.UNLOCK, if (locked) ACCENT else DIM))
        lockBtn.contentDescription = if (locked) "EQ locked. Tap to unlock" else "Lock EQ"
        eqRow.alpha = if (locked) 0.4f else 1f
    }
    private fun gainOf(i: Int) = (eqBars[i].progress - 24) / 2f
    private fun updateEqLabel(i: Int) { eqLabels[i].text = "%.1f".format(gainOf(i)).removeSuffix(".0").replace("-0", "0").let { if (gainOf(i) > 0) "+$it" else it } }
    private fun setEqUi(g: FloatArray) { for (i in 0 until 10) eqBars[i].progress = Math.round(g[i].coerceIn(-12f, 12f) * 2) + 24 }
    private fun eqUi() = FloatArray(10) { gainOf(it) }
    /** No Apply step: a slider change is sent when the finger lifts. */
    private fun onSliderReleased() {
        if (ble.protocol == null) return
        sendEq()
        if (selectedProfile != null && presetMap()[selectedProfile]?.contentEquals(eqUi()) != true) { selectedProfile = null; renderChips() }
    }

    // ----- AutoEQ (built-in profiles from assets, fitted offline at dev time) -----
    private val autoEqProfiles by lazy {
        try { AutoEq.parseProfiles(assets.open("autoeq_profiles.tsv").use { it.readBytes().toString(Charsets.UTF_8) }) } catch (e: Exception) { emptyList() }
    }
    private fun autoEqName(m: String) = "AutoEQ · $m"
    /** Sound Profiles plus AutoEQ profiles; the connected model's profile is listed first. */
    private fun presetMap(): LinkedHashMap<String, FloatArray> {
        val (mine, sure) = AutoEq.suggest(autoEqProfiles, model)
        val m = LinkedHashMap<String, FloatArray>()
        mine?.let { m[AutoEq.suggestLabel(it, sure)] = it.gains }
        m.putAll(presets.all())
        for (p in autoEqProfiles) if (p !== mine) m[autoEqName(p.model)] = p.gains
        return m
    }
    private fun isFixed(n: String) = presets.isBuiltIn(n) || n.startsWith("AutoEQ")
    private fun importAutoEq() {
        // Many file managers report AutoEQ .txt files as octet-stream or nothing at all: allow any type, validate the content.
        startActivityForResult(Intent(Intent.ACTION_OPEN_DOCUMENT).addCategory(Intent.CATEGORY_OPENABLE).setType("*/*")
            .putExtra(Intent.EXTRA_MIME_TYPES, arrayOf("text/plain", "text/*", "application/octet-stream", "*/*")), RC_AUTOEQ)
    }
    private fun showAbout() {
        val v = packageManager.getPackageInfo(packageName, 0).versionName
        val perms = grantedPermissions().joinToString("\n") { "• $it" }.ifEmpty { "• None granted yet" }
        AlertDialog.Builder(this).setTitle("About and Privacy")
            .setMessage("BluntyBuds $v\nUnofficial companion\n\nNo internet. No account. No tracking.\n\n" +
                "Permissions granted:\n$perms\n\nSettings and Sound Profiles stay on this phone. MIT License.\n\n" +
                AutoEq.ATTRIBUTION + "\n\nAutoEq is MIT licensed: permission is granted, free of charge, to use, copy, modify and distribute it, provided the copyright and permission notice are included. It is provided \"as is\", without warranty of any kind.")
            .setPositiveButton("OK", null).show()
    }

    private fun refreshPresets(select: String? = null) {
        val names = presetMap().keys
        val want = select ?: presets.lastUsed()
        selectedProfile = want?.takeIf { it in names }
        renderChips()
    }
    private fun renderChips() {
        chipRow.removeAllViews()
        val on = controls.alpha >= 1f
        for (n in presetMap().keys) {
            chipRow.addView(chip(n, n == selectedProfile).apply {
                isEnabled = on
                setOnClickListener { applyProfile(n) }
                setOnLongClickListener { profileMenu(n); true }
            })
        }
        chipRow.addView(chip("+ Save", false).apply { isEnabled = on; setTextColor(ACCENT); setOnClickListener { savePresetDialog() }; contentDescription = "Save current Tune as a Sound Profile" })
    }
    /** Tap a chip: applied to the earbuds immediately, with a 3-second Undo. */
    private fun applyProfile(n: String) {
        if (eqLocked) { toast("EQ locked"); return }
        val g = presetMap()[n] ?: return
        val prevGains = eqUi(); val prevSel = selectedProfile
        setEqUi(g); selectedProfile = n; presets.setLastUsed(n); renderChips()
        if (ble.protocol != null) sendEq()
        showUndo("“$n” applied") {
            setEqUi(prevGains); selectedProfile = prevSel; prevSel?.let { presets.setLastUsed(it) }; renderChips()
            if (ble.protocol != null) sendEq()
        }
    }
    private fun showUndo(msg: String, undo: () -> Unit) {
        snackText.text = msg; snackUndo = undo; snack.visibility = View.VISIBLE
        main.removeCallbacks(hideSnack); main.postDelayed(hideSnack, 3000)
    }
    /** Long press: save the current Tune into a profile, or rename / delete your own. */
    private fun profileMenu(n: String) {
        if (isFixed(n)) {
            AlertDialog.Builder(this).setTitle(n).setItems(arrayOf("Save current Tune as new profile…")) { _, _ -> savePresetDialog() }.show()
            return
        }
        AlertDialog.Builder(this).setTitle(n).setItems(arrayOf("Save current Tune to “$n”", "Rename…", "Delete")) { _, w ->
            when (w) {
                0 -> { presets.save(n, eqUi()); refreshPresets(n); toast("Saved “$n”") }
                1 -> nameDialog("Rename Sound Profile", n) { nn ->
                    val g = presets.all()[n] ?: return@nameDialog
                    presets.save(nn, g); presets.delete(n)
                    if (presets.lastUsed() == n) presets.setLastUsed(nn)
                    refreshPresets(if (selectedProfile == n) nn else selectedProfile)
                }
                2 -> { presets.delete(n); refreshPresets(if (selectedProfile == n) null else selectedProfile) }
            }
        }.show()
    }
    private fun nameDialog(t: String, initial: String, done: (String) -> Unit) {
        val et = EditText(this).apply { hint = "Profile name"; setText(initial); setSelection(initial.length) }
        AlertDialog.Builder(this).setTitle(t).setView(et)
            .setPositiveButton("Save") { _, _ ->
                val n = et.text.toString().trim()
                if (n.isEmpty() || isFixed(n) || (n != initial && n in presetMap())) toast("Pick a new name") else done(n)
            }.setNegativeButton("Cancel", null).show()
    }
    private fun savePresetDialog() = nameDialog("Save Sound Profile", "") { n -> presets.save(n, eqUi()); presets.setLastUsed(n); refreshPresets(n) }
    private fun importPresets() {
        startActivityForResult(Intent(Intent.ACTION_OPEN_DOCUMENT).addCategory(Intent.CATEGORY_OPENABLE).setType("*/*"), RC_IMPORT)
    }
    private fun exportPresets() {
        startActivityForResult(Intent(Intent.ACTION_CREATE_DOCUMENT).addCategory(Intent.CATEGORY_OPENABLE)
            .setType("application/json").putExtra(Intent.EXTRA_TITLE, "bluntybuds-profiles.json"), RC_EXPORT)
    }
    @Deprecated("platform API") override fun onActivityResult(rc: Int, res: Int, data: Intent?) {
        super.onActivityResult(rc, res, data)
        val uri = data?.data ?: return
        if (res != RESULT_OK) return
        try {
            when (rc) {
                RC_IMPORT -> {
                    val json = contentResolver.openInputStream(uri)?.use { it.readBytes().toString(Charsets.UTF_8) } ?: return
                    val n = presets.importJson(json); refreshPresets(selectedProfile); toast("Imported $n Sound Profile(s)")
                }
                RC_AUTOEQ -> {
                    if (eqLocked) { toast("EQ locked"); return }
                    // Any file type can be picked, so check the content: small, text, and a real AutoEQ file (fitText throws otherwise).
                    val bytes = contentResolver.openInputStream(uri)?.use { it.readNBytesCompat((1 shl 20) + 1) } ?: return
                    if (bytes.size > (1 shl 20) || bytes.any { it == 0.toByte() }) throw IllegalArgumentException("not an AutoEQ text file")
                    val prev = eqUi(); val prevSel = selectedProfile
                    setEqUi(AutoEq.fitText(bytes.toString(Charsets.UTF_8))); selectedProfile = null; renderChips()
                    if (ble.protocol != null) {
                        sendEq()
                        showUndo("Correction applied") { setEqUi(prev); selectedProfile = prevSel; renderChips(); sendEq() }
                    } else toast("Correction fitted to Tune. It is sent when the earbuds connect; long-press + Save to keep it")
                }
                RC_EXPORT -> {
                    contentResolver.openOutputStream(uri, "wt")?.use { it.write(presets.exportJson().toByteArray()) }
                    toast("Exported ${presets.user().size} Sound Profile(s)")
                }
            }
        } catch (e: Exception) { onStatus("Profile file error: ${e.message}") }
    }

    private fun renderTouch() {
        for ((k, sp) in touchSpinners) {
            val v = if (k.first) touch.left[k.second.slot] else touch.right[k.second.slot]
            val a = TouchAction.fromA(v) ?: TouchAction.NONE
            sp.setSelection(TouchAction.entries.indexOf(a))
        }
    }
    private fun touchUi(): TouchMap {
        val m = touch.copyMap()
        for ((k, sp) in touchSpinners) {
            val a = TouchAction.entries[sp.selectedItemPosition]
            if (k.first) m.left[k.second.slot] = a.a else m.right[k.second.slot] = a.a
        }
        return m
    }

    // ---------- devices ----------
    private val scanCb = object : ScanCallback() {
        override fun onScanResult(type: Int, r: ScanResult) {
            try { seen[r.device.address] = r to AdParser.parse(r.scanRecord?.bytes); renderCoalesce.mark(); scheduleRender() } catch (_: Exception) {} // list refreshes at most ~2x/s
        }
        override fun onScanFailed(errorCode: Int) {
            scanning = false
            val why = when (errorCode) { 1 -> "already started"; 2 -> "app registration failed (toggle Bluetooth)"; 3 -> "internal error"; 4 -> "LE scan not supported"; 5 -> "out of hardware resources"; 6 -> "scanning too frequently, wait 30 s"; else -> "code $errorCode" }
            onStatus("Scan failed: $why")
        }
    }
    private fun startScan() {
        try {
            if (!hasPerms()) { requestPerms(); return }
            val ad = (getSystemService(BLUETOOTH_SERVICE) as BluetoothManager).adapter
            if (ad == null || !ad.isEnabled) { onStatus("Turn Bluetooth on first."); return }
            stopScan(); seen.clear(); renderDevices()
            ad.bluetoothLeScanner?.startScan(null, ScanSettings.Builder().setScanMode(ScanSettings.SCAN_MODE_LOW_LATENCY).build(), scanCb)
            scanning = true
            onStatus("Scanning for 12 s… (take the earbuds out of the case)")
            val gen = scanGen.begin() // a newer scan invalidates this timer so it can't stop the new scan early
            main.postDelayed({ if (scanGen.isCurrent(gen) && scanning) { stopScan(); onStatus("Scan finished. Pick a device and Connect.") } }, 12000)
        } catch (e: Exception) { onStatus("Scan failed: ${e.message}") }
    }
    private fun stopScan() {
        if (!scanning) return
        scanning = false
        try { (getSystemService(BLUETOOTH_SERVICE) as BluetoothManager).adapter?.bluetoothLeScanner?.stopScan(scanCb) } catch (_: Exception) {}
    }
    private fun listBonded() {
        try {
            val ad = (getSystemService(BLUETOOTH_SERVICE) as BluetoothManager).adapter ?: return
            bonded.clear(); ad.bondedDevices?.forEach { bonded[it.address] = it }
            renderDevices()
            if (!Conn.connected) onStatus("${bonded.values.count { isKnownEarbud(it.name) }} paired earbud(s). Take the earbuds out of the case, pick one and Connect.")
        } catch (e: Exception) { onStatus("Can't list paired devices: ${e.message}") }
    }
    private fun isKnownEarbud(n: String?) = n?.contains("TOZO", ignoreCase = true) == true
    private fun renderDevices() {
        val list = ArrayList<DevEntry>()
        val covered = HashSet<String>()
        for ((addr, p) in seen) {
            val (r, tz) = p
            val name = r.device.name ?: r.scanRecord?.deviceName ?: tz?.let { bonded[it.classicMac]?.name } ?: "(no name)"
            if (tz == null && !isKnownEarbud(name) && !showAll.isChecked) continue
            tz?.let { covered.add(it.classicMac) }; covered.add(addr)
            val m = Models.detect(name, tz?.mark)
            list += DevEntry("$name · ${if (!m.isUnknown) "$m · " else ""}${r.rssi} dBm", r.device, tz, m, name)
        }
        for ((addr, d) in bonded) {
            if (addr in covered) continue
            val name = d.name ?: "(no name)"
            if (!isKnownEarbud(name) && !showAll.isChecked) continue
            val m = Models.detect(name)
            list += DevEntry("$name · ${if (!m.isUnknown) "$m · " else ""}paired", d, null, m, name)
        }
        val prev = (deviceSpinner.selectedItemPosition.takeIf { it in entries.indices })?.let { entries[it].device.address }
        entries = list
        val labels = if (list.isEmpty()) listOf("(no earbuds found — Scan or pair first)") else list.map { it.label }
        // update in place: reuse one adapter, only notify when something changed
        val a = devAdapter ?: darkAdapter(devLabels).also { devAdapter = it; deviceSpinner.adapter = it }
        if (labels != devLabels) { devLabels.clear(); devLabels.addAll(labels); a.notifyDataSetChanged() }
        val want = prev ?: Conn.lastAddress
        list.indexOfFirst { it.device.address == want }.takeIf { it >= 0 && it != deviceSpinner.selectedItemPosition }?.let { deviceSpinner.setSelection(it) }
    }
    private fun connectSelected() {
        val e = entries.getOrNull(deviceSpinner.selectedItemPosition) ?: run { onStatus("Select a device first."); return }
        try {
            stopScan(); setControlsEnabled(false)
            Conn.connect(e.device, e.ad?.let { AdParser.protocolHint(it.mark) }, e.model, e.name)
        } catch (ex: Exception) { onStatus("Connect failed: ${ex.message}") }
    }

    // ---------- commands ----------
    private fun send(f: ByteArray) = Conn.send(f)
    private fun refreshAll() {
        when (ble.protocol) {
            Protocol.A -> ProtoA.statusQueries(Models.features(model).anc, Models.features(model).ancLevel)
                .forEach { send(ProtoA.get(it)) }
            Protocol.J -> {
                Conn.beginBatteryPoll()
                listOf(ProtoJ.BATTERY_LEFT, ProtoJ.BATTERY_RIGHT, ProtoJ.VERSION, ProtoJ.GAME_GET,
                    ProtoJ.BUTTON_GET, ProtoJ.EQ_GET).forEach { send(ProtoJ.get(it)) }
            }
            null -> {}
        }
    }
    private fun sendGame(on: Boolean) = when (ble.protocol) {
        Protocol.A -> send(ProtoA.setGame(on)); Protocol.J -> send(ProtoJ.setGame(on)); null -> {}
    }
    private fun sendEq() {
        val g = eqUi()
        when (ble.protocol) { Protocol.A -> send(ProtoA.setEq(g)); Protocol.J -> send(ProtoJ.setEq(g)); null -> {} }
    }
    private fun sendTouch() {
        val m = touchUi(); touch = m
        when (ble.protocol) { Protocol.A -> send(ProtoA.setTouch(m)); Protocol.J -> send(ProtoJ.setTouch(m)); null -> {} }
    }
    private fun sendBalance() { if (ble.protocol == Protocol.A) send(ProtoA.setBalance(balanceBar.progress - 11)) }

    private fun showAnc(mode: Int?) {
        for ((m, b) in ancButtons) {
            val on = mode == m.code
            b.background = if (on) round(ACCENT, 6) else null
            b.setTextColor(if (on) ACCENT_FG else FG)
        }
        val f = Models.features(model)
        ancOther.text = if (mode != null && (mode > 3 || (mode == 5 && !f.ancLevel)) && mode != AncMode.CUSTOM.code) "Current mode: ${AncMode.of(mode)?.label ?: mode} (set from the earbuds)" else ""
        ancOther.visibility = if (ancOther.text.isEmpty()) View.GONE else View.VISIBLE
    }

    // ---------- BleClient.Listener (via Conn) ----------
    override fun onStatus(text: String) { status.text = text }
    override fun onReady(protocol: Protocol) {
        onStatus("Connected" + if (model.isUnknown || model.tested) "" else " · untested model")
        val f = Models.features(model)
        // Hide what the device does not support.
        ancBox.visibility = if (f.anc && protocol == Protocol.A) View.VISIBLE else View.GONE
        ancLevelBox.visibility = if (f.ancLevel && protocol == Protocol.A) View.VISIBLE else View.GONE
        ancButtons[AncMode.CUSTOM]?.visibility = ancLevelBox.visibility
        btnBalance.visibility = if (f.balance && protocol == Protocol.A) View.VISIBLE else View.GONE
        btnLowLat.visibility = if (f.game) View.VISIBLE else View.GONE
        btnGestures.visibility = if (f.touch) View.VISIBLE else View.GONE
        setControlsEnabled(true)
        renderConnection(true)
        showAnc(Conn.ancMode)
        Conn.game?.let { suppress = true; game.isChecked = it; suppress = false; renderLowLat(it) }
        // Last-used profile wins; otherwise a confident AutoEQ match (never an "may differ by hardware revision" one).
        val (ae, sure) = AutoEq.suggest(autoEqProfiles, model)
        refreshPresets(presets.lastUsed()?.takeIf { it in presetMap() } ?: ae?.takeIf { sure }?.let { AutoEq.suggestLabel(it, true) })
        refreshAll()
        // Options that depend on the link (status notification) are read now, not on the next battery packet.
        Notifier.refreshNow(this)
    }
    override fun onDisconnected() {
        setControlsEnabled(false)
        renderConnection(false)
        renderChips()
        onStatus(if (Conn.autoReconnect) "Disconnected — will reconnect when the earbuds are back." else "Disconnected")
    }
    override fun onEvent(e: Event) {
        when (e) {
            is Event.Battery, is Event.CaseBattery -> renderBattery()
            is Event.Firmware -> firmware.text = "FW ${e.left}" + (e.right?.takeIf { it != e.left }?.let { " / $it" } ?: "")
            is Event.Game -> { suppress = true; game.isChecked = e.on; suppress = false; renderLowLat(e.on) }
            is Event.Touch -> {
                for (i in 0 until 5) { if (e.map.left[i] >= 0) touch.left[i] = e.map.left[i]; if (e.map.right[i] >= 0) touch.right[i] = e.map.right[i] }
                renderTouch()
            }
            is Event.Eq -> if (e.gains.size == 10) {
                setEqUi(e.gains.toFloatArray())
                val match = presetMap().entries.firstOrNull { it.value.contentEquals(eqUi()) }?.key
                if (match != selectedProfile && (selectedProfile == null || presetMap()[selectedProfile]?.contentEquals(eqUi()) != true)) { selectedProfile = match; renderChips() }
            }
            is Event.Balance -> { suppress = true; balanceBar.progress = (e.value + 11).coerceIn(0, 22); suppress = false }
            is Event.Ack -> if (!e.ok) toast("Earbuds rejected the change (0x%04X)".format(e.cmd))
            is Event.Anc -> showAnc(e.mode)
            is Event.AncLevel -> { suppress = true; ancLevelBar.progress = e.level - 1; suppress = false }
            is Event.Raw -> {}
        }
    }

    private fun toast(s: String) = Toast.makeText(this, s, Toast.LENGTH_SHORT).show()
}

/** Reads at most [max] bytes (InputStream.readNBytes needs API 33). */
private fun java.io.InputStream.readNBytesCompat(max: Int): ByteArray {
    val out = java.io.ByteArrayOutputStream(); val buf = ByteArray(8192)
    while (out.size() < max) { val n = read(buf, 0, minOf(buf.size, max - out.size())); if (n < 0) break; out.write(buf, 0, n) }
    return out.toByteArray()
}
