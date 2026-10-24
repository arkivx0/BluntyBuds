package io.github.arkivx0.bluntybuds

import android.graphics.drawable.Icon
import android.service.quicksettings.Tile
import android.service.quicksettings.TileService
import io.github.arkivx0.bluntybuds.proto.AncMode

/** Quick Settings tile: tap to cycle noise mode (Off -> Noise cancelling -> Ambient). Connects to the last device if needed. */
class AncTileService : TileService() {
    override fun onStartListening() { Conn.init(this); render() }
    override fun onClick() {
        Conn.init(this)
        try {
            if (Conn.connected && !Conn.features().anc) { render("No noise control on ${Conn.name}"); return }
            Conn.cycleAnc()
            render(if (Conn.connected) null else "Connecting…")
        } catch (e: Exception) { render("Error") }
    }
    private fun render(sub: String? = null) {
        val t = qsTile ?: return
        t.icon = Icon.createWithResource(this, android.R.drawable.stat_sys_headset)
        t.label = "Noise: " + (Conn.ancMode?.let { AncMode.of(it)?.label?.substringBefore(" /") } ?: "—")
        if (android.os.Build.VERSION.SDK_INT >= 29) t.subtitle = sub ?: if (Conn.connected) Conn.name else "Not connected"
        t.state = if (Conn.connected && Conn.ancMode != null && Conn.ancMode != 0) Tile.STATE_ACTIVE else Tile.STATE_INACTIVE
        t.updateTile()
    }
}
