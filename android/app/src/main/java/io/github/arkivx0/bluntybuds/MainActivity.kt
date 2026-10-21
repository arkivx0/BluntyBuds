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
import android.graphics.Color
import android.graphics.Typeface
import android.graphics.drawable.GradientDrawable
import android.os.Build
import android.os.Bundle
import android.os.Handler
import android.os.Looper
import android.view.Gravity
import android.view.View
import android.view.ViewGroup
import android.view.ViewGroup.LayoutParams.MATCH_PARENT
import android.view.ViewGroup.LayoutParams.WRAP_CONTENT
import android.widget.*
import io.github.arkivx0.bluntybuds.proto.*

@SuppressLint("MissingPermission", "SetTextI18n")
class MainActivity : Activity(), BleClient.Listener {
    companion object {
        const val BG = 0xFF0E0F12.toInt(); const val CARD = 0xFF1A1C21.toInt(); const val CTL = 0xFF262931.toInt()
        const val FG = 0xFFE6E8EE.toInt(); const val DIM = 0xFF8C93A3.toInt(); const val ACCENT = 0xFF3DD6B5.toInt()
        const val ACCENT_FG = 0xFF0B1A17.toInt()
        const val RC_IMPORT = 41; const val RC_EXPORT = 42
    }
    private val ble get() = Conn.ble
    private lateinit var tone: Tone
    private lateinit var presets: Presets
    private val main = Handler(Looper.getMainLooper())

    private lateinit var status: TextView
    private lateinit var deviceSpinner: Spinner
    private lateinit var showAll: CheckBox
    private lateinit var controls: LinearLayout
    private lateinit var modelLabel: TextView
    private lateinit var battery: TextView
    private lateinit var firmware: TextView
    private lateinit var game: Switch
    private val eqBars = ArrayList<SeekBar>()
    private val eqLabels = ArrayList<TextView>()
    private lateinit var presetSpinner: Spinner
    private val touchSpinners = HashMap<Pair<Boolean, Gesture>, Spinner>()
    private lateinit var balanceBar: SeekBar
    private lateinit var balanceLabel: TextView
    private lateinit var balanceBox: LinearLayout
    private lateinit var ancBox: LinearLayout
    private val ancButtons = LinkedHashMap<AncMode, TextView>()
    private lateinit var ancOther: TextView
    private lateinit var ancLevelBox: LinearLayout
    private lateinit var ancLevelBar: SeekBar
    private lateinit var ancLevelLabel: TextView

    private val model get() = Conn.model
    private var batL: Int? = null
    private var batR: Int? = null
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

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        Conn.init(this)
        tone = Tone(this); presets = Presets(this)
        window.statusBarColor = BG; window.navigationBarColor = BG
        setContentView(buildUi())
        Conn.listeners.add(this)
        requestPerms()
        if (hasPerms()) afterPerms()
        if (Conn.connected) Conn.ble.protocol?.let { onReady(it) }
    }

    override fun onRequestPermissionsResult(rc: Int, perms: Array<out String>, res: IntArray) {
        super.onRequestPermissionsResult(rc, perms, res)
        if (hasPerms()) afterPerms()
    }

    private fun afterPerms() {
        listBonded()
        if (!Conn.connected && Conn.autoReconnect && Conn.lastAddress != null) {
            onStatus("Reconnecting to ${Conn.lastName ?: Conn.lastAddress}…")
            Conn.connectLast()
        }
    }

    // Connection lives in Conn (shared with the Quick Settings tile / notification), so it is not closed here.
    override fun onDestroy() { Conn.listeners.remove(this); main.removeCallbacks(renderTick); stopScan(); tone.stop(); super.onDestroy() }

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

    // ---------- UI helpers (dark theme) ----------
    private fun dp(v: Int) = (v * resources.displayMetrics.density).toInt()
    private fun round(color: Int, r: Int = 10, stroke: Int? = null) = GradientDrawable().apply {
        setColor(color); cornerRadius = dp(r).toFloat(); stroke?.let { setStroke(dp(1), it) }
    }
    private fun text(t: String, size: Float = 14f, color: Int = FG, bold: Boolean = false) = TextView(this).apply {
        text = t; textSize = size; setTextColor(color); if (bold) setTypeface(typeface, Typeface.BOLD)
    }
    private fun title(t: String) = text(t.uppercase(), 11f, DIM, true).apply { letterSpacing = 0.06f }
    private fun btn(t: String, small: Boolean = true, accent: Boolean = false, on: () -> Unit) = TextView(this).apply {
        text = t; textSize = if (small) 12f else 14f; gravity = Gravity.CENTER
        setTextColor(if (accent) ACCENT_FG else FG); if (accent) setTypeface(typeface, Typeface.BOLD)
        background = round(if (accent) ACCENT else CTL, 7)
        setPadding(dp(if (small) 9 else 14), dp(if (small) 5 else 8), dp(if (small) 9 else 14), dp(if (small) 5 else 8))
        isClickable = true; isFocusable = true
        setOnClickListener { try { on() } catch (e: Exception) { onStatus("Error: ${e.message}") } }
        layoutParams = LinearLayout.LayoutParams(WRAP_CONTENT, WRAP_CONTENT).apply { marginEnd = dp(6) }
    }
    private fun row(vararg v: View) = LinearLayout(this).apply {
        orientation = LinearLayout.HORIZONTAL; gravity = Gravity.CENTER_VERTICAL; v.forEach { addView(it) }
    }
    private fun card(vararg v: View) = LinearLayout(this).apply {
        orientation = LinearLayout.VERTICAL; background = round(CARD, 12)
        setPadding(dp(12), dp(10), dp(12), dp(12))
        layoutParams = LinearLayout.LayoutParams(MATCH_PARENT, WRAP_CONTENT).apply { bottomMargin = dp(10) }
        v.forEach { addView(it) }
    }
    private fun space(w: Int) = View(this).apply { layoutParams = LinearLayout.LayoutParams(dp(w), 1) }
    private fun weight(v: View) = v.apply { layoutParams = LinearLayout.LayoutParams(0, WRAP_CONTENT, 1f) }
    private fun tintSeek(s: SeekBar) = s.apply {
        progressTintList = ColorStateList.valueOf(ACCENT); thumbTintList = ColorStateList.valueOf(ACCENT)
        progressBackgroundTintList = ColorStateList.valueOf(CTL)
    }
    private fun <T> spinner(items: List<T>) = Spinner(this, Spinner.MODE_DROPDOWN).apply {
        adapter = darkAdapter(items); background = round(CTL, 7)
        setPopupBackgroundDrawable(round(CARD, 8, 0xFF353944.toInt()))
        setPadding(dp(8), dp(2), dp(8), dp(2))
    }
    private fun <T> darkAdapter(items: List<T>) = object : ArrayAdapter<T>(this, android.R.layout.simple_spinner_dropdown_item, items) {
        override fun getView(p: Int, c: View?, parent: ViewGroup) = style(super.getView(p, c, parent), true)
        override fun getDropDownView(p: Int, c: View?, parent: ViewGroup) = style(super.getDropDownView(p, c, parent), false)
        private fun style(v: View, compact: Boolean) = v.apply {
            (this as? TextView)?.apply { setTextColor(FG); textSize = if (compact) 13f else 14f; if (compact) { setPadding(0, dp(6), 0, dp(6)); maxLines = 1 } }
        }
    }
    private fun switch(t: String, on: (Boolean) -> Unit) = Switch(this).apply {
        text = t; setTextColor(FG); textSize = 13f
        thumbTintList = ColorStateList(arrayOf(intArrayOf(android.R.attr.state_checked), intArrayOf()), intArrayOf(ACCENT, DIM))
        trackTintList = ColorStateList(arrayOf(intArrayOf(android.R.attr.state_checked), intArrayOf()), intArrayOf(0xFF1F6B5B.toInt(), CTL))
        setOnCheckedChangeListener { _, c -> if (!suppress) try { on(c) } catch (e: Exception) { onStatus("Error: ${e.message}") } }
    }

    private fun buildUi(): View {
        val root = LinearLayout(this).apply { orientation = LinearLayout.VERTICAL; setPadding(dp(12), dp(12), dp(12), dp(28)); setBackgroundColor(BG) }

        // ---- header ----
        root.addView(row(text("BluntyBuds", 20f, ACCENT, true), space(10), weight(text("offline · no account · no analytics", 11f, DIM))))
        deviceSpinner = spinner(listOf("(no devices yet)"))
        showAll = CheckBox(this).apply {
            text = "All"; setTextColor(DIM); textSize = 12f; buttonTintList = ColorStateList.valueOf(ACCENT)
            setOnCheckedChangeListener { _, _ -> renderDevices() }
        }
        status = text("Idle", 12f, DIM).apply { setPadding(0, dp(6), 0, 0); maxLines = 3 }
        root.addView(card(
            row(weight(deviceSpinner), space(6), btn("Connect", accent = true) { connectSelected() }),
            row(btn("Scan") { startScan() }, btn("Paired") { listBonded() }, btn("Disconnect") { Conn.disconnect() }, showAll).apply { setPadding(0, dp(8), 0, 0) },
            status))

        controls = LinearLayout(this).apply { orientation = LinearLayout.VERTICAL; alpha = 0.45f }
        root.addView(controls)

        // ---- strip: model · battery · fw ----
        modelLabel = text("—", 14f, FG, true)
        battery = text("L —  R —", 15f, FG, true)
        firmware = text("FW —", 12f, DIM)
        controls.addView(card(row(modelLabel, space(12), battery, space(12), weight(firmware), btn("⟳") { refreshAll() })))

        // ---- noise + game ----
        val seg = LinearLayout(this).apply { orientation = LinearLayout.HORIZONTAL; background = round(CTL, 8); setPadding(dp(2), dp(2), dp(2), dp(2)) }
        for ((m, t) in listOf(AncMode.OFF to "Off", AncMode.ANC to "Noise cancel", AncMode.TRANSPARENCY to "Ambient", AncMode.CUSTOM to "Custom")) {
            val b = text(t, 13f).apply {
                gravity = Gravity.CENTER; setPadding(dp(4), dp(7), dp(4), dp(7)); maxLines = 1
                layoutParams = LinearLayout.LayoutParams(0, WRAP_CONTENT, 1f)
                setOnClickListener { if (!suppress) Conn.setAnc(m) }
            }
            ancButtons[m] = b; seg.addView(b)
        }
        ancLevelLabel = text("Depth 5", 12f, DIM).apply { width = dp(64) }
        ancLevelBar = tintSeek(SeekBar(this)).apply {
            max = ProtoA.ANC_LEVEL_MAX - ProtoA.ANC_LEVEL_MIN; progress = 4
            setOnSeekBarChangeListener(object : SeekBar.OnSeekBarChangeListener {
                override fun onProgressChanged(s: SeekBar?, p: Int, u: Boolean) { ancLevelLabel.text = "Depth ${p + 1}" }
                override fun onStartTrackingTouch(s: SeekBar?) {}
                override fun onStopTrackingTouch(s: SeekBar?) { if (!suppress) Conn.setAncLevel(progress + 1) }
            })
        }
        ancLevelBox = row(ancLevelLabel, weight(ancLevelBar)).apply { setPadding(0, dp(8), 0, 0) }
        ancOther = text("", 12f, DIM)
        ancBox = LinearLayout(this).apply { orientation = LinearLayout.VERTICAL; addView(seg); addView(ancLevelBox); addView(ancOther) }
        game = switch("Game mode") { sendGame(it) }
        controls.addView(card(row(weight(title("Noise control")), game), ancBox.apply { setPadding(0, dp(6), 0, 0) }))

        // ---- EQ ----
        presetSpinner = spinner(listOf<String>())
        val eqRow = LinearLayout(this).apply { orientation = LinearLayout.HORIZONTAL; setPadding(0, dp(8), 0, 0) }
        for (i in 0 until 10) {
            val col = LinearLayout(this).apply { orientation = LinearLayout.VERTICAL; gravity = Gravity.CENTER_HORIZONTAL; layoutParams = LinearLayout.LayoutParams(0, WRAP_CONTENT, 1f) }
            val lbl = text("0", 10f, FG).apply { gravity = Gravity.CENTER }
            val bar = tintSeek(VerticalSeekBar(this)).apply {
                max = 48; progress = 24; setPadding(dp(10), 0, dp(10), 0)
                layoutParams = LinearLayout.LayoutParams(dp(30), dp(170))
                setOnSeekBarChangeListener(object : SeekBar.OnSeekBarChangeListener {
                    override fun onProgressChanged(s: SeekBar?, p: Int, u: Boolean) { updateEqLabel(i) }
                    override fun onStartTrackingTouch(s: SeekBar?) {}
                    override fun onStopTrackingTouch(s: SeekBar?) {}
                })
            }
            val f = EqGrid.FREQS[i]
            val fl = text(if (f >= 1000) "${f / 1000.0}".removeSuffix(".0") + "k" else "$f", 10f, DIM).apply { gravity = Gravity.CENTER }
            eqLabels += lbl; eqBars += bar
            col.addView(lbl); col.addView(bar); col.addView(fl)
            eqRow.addView(col)
        }
        for (i in 0 until 10) updateEqLabel(i)
        controls.addView(card(
            row(weight(title("Equalizer · ±12 dB")), btn("Flat") { setEqUi(FloatArray(10)) }, btn("Apply", accent = true) { sendEq() }),
            row(weight(presetSpinner), space(6), btn("Load") { loadPreset() }, btn("Save") { savePresetDialog() }, btn("Del") { deletePreset() }).apply { setPadding(0, dp(8), 0, 0) },
            row(btn("Import presets") { importPresets() }, btn("Export presets") { exportPresets() }).apply { setPadding(0, dp(6), 0, 0) },
            eqRow))
        refreshPresets()

        // ---- touch grid ----
        val grid = GridLayout(this).apply { columnCount = 3; setPadding(0, dp(6), 0, 0) }
        fun cell(v: View, col: Int, w: Float) = v.apply {
            layoutParams = GridLayout.LayoutParams(GridLayout.spec(GridLayout.UNDEFINED), GridLayout.spec(col, w)).apply { width = 0; setMargins(0, dp(2), dp(4), dp(2)) }
        }
        grid.addView(cell(text("", 11f), 0, 0.8f)); grid.addView(cell(text("Left", 11f, DIM), 1, 1f)); grid.addView(cell(text("Right", 11f, DIM), 2, 1f))
        val actions = TouchAction.entries.map { it.label }
        for (g in Gesture.entries) {
            grid.addView(cell(text(g.label, 12f), 0, 0.8f))
            for (left in listOf(true, false)) {
                val sp = spinner(actions); touchSpinners[left to g] = sp
                grid.addView(cell(sp, if (left) 1 else 2, 1f))
            }
        }
        controls.addView(card(row(weight(title("Touch controls")), btn("Defaults") { touch = TouchMap.defaults(); renderTouch() }, btn("Apply", accent = true) { sendTouch() }), grid))

        // ---- balance + find ----
        balanceLabel = text("0", 12f, FG).apply { width = dp(28); gravity = Gravity.END }
        balanceBar = tintSeek(SeekBar(this)).apply {
            max = 22; progress = 11
            setOnSeekBarChangeListener(object : SeekBar.OnSeekBarChangeListener {
                override fun onProgressChanged(s: SeekBar?, p: Int, u: Boolean) { balanceLabel.text = "%+d".format(p - 11).replace("+0", "0") }
                override fun onStartTrackingTouch(s: SeekBar?) {}
                override fun onStopTrackingTouch(s: SeekBar?) {}
            })
        }
        balanceBox = card(row(weight(title("Balance")), btn("Centre") { balanceBar.progress = 11 }, btn("Apply", accent = true) { sendBalance() }),
            row(text("L", 12f, DIM), weight(balanceBar), text("R", 12f, DIM), balanceLabel))
        controls.addView(balanceBox)
        controls.addView(card(title("Find earbuds — take them out of your ears"),
            row(btn("Left") { tone.play(true, false) }, btn("Right") { tone.play(false, true) }, btn("Both") { tone.play(true, true) }, btn("Stop") { tone.stop() }).apply { setPadding(0, dp(6), 0, 0) }))

        // ---- app options ----
        val auto = switch("Auto-reconnect to last device") { Conn.autoReconnect = it }.apply { isChecked = Conn.autoReconnect }
        val notif = switch("Status notification (battery + noise buttons)") { if (it) askNotifPermission(); Conn.statusNotification = it }.apply { isChecked = Conn.statusNotification }
        root.addView(card(title("App"), auto, notif,
            text("Quick Settings: add the “Noise” tile to cycle Off → Noise cancel → Ambient.", 11f, DIM).apply { setPadding(0, dp(4), 0, 0) }))
        setControlsEnabled(false)
        return ScrollView(this).apply { setBackgroundColor(BG); addView(root) }
    }

    private fun setControlsEnabled(on: Boolean) {
        controls.alpha = if (on) 1f else 0.45f
        fun walk(v: View) { v.isEnabled = on; (v as? ViewGroup)?.let { g -> for (i in 0 until g.childCount) walk(g.getChildAt(i)) } }
        walk(controls)
    }

    private fun gainOf(i: Int) = (eqBars[i].progress - 24) / 2f
    private fun updateEqLabel(i: Int) { eqLabels[i].text = "%.1f".format(gainOf(i)).removeSuffix(".0").replace("-0", "0").let { if (gainOf(i) > 0) "+$it" else it } }
    private fun setEqUi(g: FloatArray) { for (i in 0 until 10) eqBars[i].progress = Math.round(g[i].coerceIn(-12f, 12f) * 2) + 24 }
    private fun eqUi() = FloatArray(10) { gainOf(it) }

    private fun refreshPresets(select: String? = null) {
        val names = presets.all().keys.toList()
        presetSpinner.adapter = darkAdapter(names)
        val want = select ?: presets.lastUsed()
        names.indexOf(want).takeIf { it >= 0 }?.let { presetSpinner.setSelection(it) }
    }
    private fun loadPreset() {
        val n = presetSpinner.selectedItem as? String ?: return
        presets.all()[n]?.let { setEqUi(it); presets.setLastUsed(n); toast("Loaded \"$n\" — tap Apply to send") }
    }
    private fun savePresetDialog() {
        val et = EditText(this).apply { hint = "Preset name" }
        AlertDialog.Builder(this).setTitle("Save EQ preset").setView(et)
            .setPositiveButton("Save") { _, _ ->
                val n = et.text.toString().trim()
                if (n.isEmpty() || presets.isBuiltIn(n)) toast("Pick a new name") else { presets.save(n, eqUi()); refreshPresets(n) }
            }.setNegativeButton("Cancel", null).show()
    }
    private fun deletePreset() {
        val n = presetSpinner.selectedItem as? String ?: return
        if (presets.isBuiltIn(n)) { toast("Built-in presets can't be deleted"); return }
        presets.delete(n); refreshPresets()
    }
    private fun importPresets() {
        startActivityForResult(Intent(Intent.ACTION_OPEN_DOCUMENT).addCategory(Intent.CATEGORY_OPENABLE).setType("*/*"), RC_IMPORT)
    }
    private fun exportPresets() {
        startActivityForResult(Intent(Intent.ACTION_CREATE_DOCUMENT).addCategory(Intent.CATEGORY_OPENABLE)
            .setType("application/json").putExtra(Intent.EXTRA_TITLE, "bluntybuds-presets.json"), RC_EXPORT)
    }
    @Deprecated("platform API") override fun onActivityResult(rc: Int, res: Int, data: Intent?) {
        super.onActivityResult(rc, res, data)
        val uri = data?.data ?: return
        if (res != RESULT_OK) return
        try {
            when (rc) {
                RC_IMPORT -> {
                    val json = contentResolver.openInputStream(uri)?.use { it.readBytes().toString(Charsets.UTF_8) } ?: return
                    val n = presets.importJson(json); refreshPresets(); toast("Imported $n preset(s)")
                }
                RC_EXPORT -> {
                    contentResolver.openOutputStream(uri, "wt")?.use { it.write(presets.exportJson().toByteArray()) }
                    toast("Exported ${presets.user().size} custom preset(s)")
                }
            }
        } catch (e: Exception) { onStatus("Preset file error: ${e.message}") }
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
            Protocol.A -> listOf(ProtoA.GET_BATTERY, ProtoA.GET_FW_VERSION, ProtoA.GET_GAME, ProtoA.GET_BUTTONS,
                ProtoA.GET_EQ, ProtoA.GET_BALANCE)
                .plus(if (Models.features(model).anc) listOf(ProtoA.GET_ANC_MODE) else emptyList())
                .plus(if (Models.features(model).ancLevel) listOf(ProtoA.GET_CUSTOM_ANC_LEVEL) else emptyList())
                .forEach { send(ProtoA.get(it)) }
            Protocol.J -> listOf(ProtoJ.BATTERY_LEFT, ProtoJ.BATTERY_RIGHT, ProtoJ.VERSION, ProtoJ.GAME_GET,
                ProtoJ.BUTTON_GET, ProtoJ.EQ_GET).forEach { send(ProtoJ.get(it)) }
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
        onStatus("Connected · ${Conn.name}")
        val f = Models.features(model)
        modelLabel.text = if (model.isUnknown) "Earbuds" else if (model.tested) "$model" else "$model (untested)"
        balanceBox.visibility = if (f.balance && protocol == Protocol.A) View.VISIBLE else View.GONE
        ancBox.visibility = if (f.anc && protocol == Protocol.A) View.VISIBLE else View.GONE
        ancLevelBox.visibility = if (f.ancLevel && protocol == Protocol.A) View.VISIBLE else View.GONE
        ancButtons[AncMode.CUSTOM]?.visibility = ancLevelBox.visibility
        game.visibility = if (f.game) View.VISIBLE else View.GONE
        setControlsEnabled(true)
        showAnc(Conn.ancMode)
        refreshAll()
    }
    override fun onDisconnected() {
        setControlsEnabled(false)
        onStatus(if (Conn.autoReconnect) "Disconnected — will reconnect when the earbuds are back." else "Disconnected")
    }
    override fun onEvent(e: Event) {
        when (e) {
            is Event.Battery -> {
                e.left?.let { batL = it }; e.right?.let { batR = it }
                battery.text = "L ${batL?.let { "$it%" } ?: "—"}  R ${batR?.let { "$it%" } ?: "—"}"
            }
            is Event.Firmware -> firmware.text = "FW ${e.left}" + (e.right?.takeIf { it != e.left }?.let { " / $it" } ?: "")
            is Event.Game -> { suppress = true; game.isChecked = e.on; suppress = false }
            is Event.Touch -> {
                for (i in 0 until 5) { if (e.map.left[i] >= 0) touch.left[i] = e.map.left[i]; if (e.map.right[i] >= 0) touch.right[i] = e.map.right[i] }
                renderTouch()
            }
            is Event.Eq -> if (e.gains.size == 10) setEqUi(e.gains.toFloatArray())
            is Event.Balance -> balanceBar.progress = (e.value + 11).coerceIn(0, 22)
            is Event.Ack -> toast(if (e.ok) "Saved ✓" else "Earbuds rejected the change (0x%04X)".format(e.cmd))
            is Event.Anc -> showAnc(e.mode)
            is Event.AncLevel -> { suppress = true; ancLevelBar.progress = e.level - 1; suppress = false }
            is Event.Raw -> {}
        }
    }

    private fun toast(s: String) = Toast.makeText(this, s, Toast.LENGTH_SHORT).show()
}
