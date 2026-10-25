package io.github.arkivx0.bluntybuds

import android.graphics.Canvas
import android.graphics.ColorFilter
import android.graphics.Paint
import android.graphics.Path
import android.graphics.PixelFormat
import android.graphics.RectF
import android.graphics.drawable.Drawable

/** Tiny line icons drawn in a 24x24 viewport, so the app needs no image assets or icon fonts. */
class IconDrawable(private val kind: Kind, var color: Int, private val sizePx: Int) : Drawable() {
    enum class Kind { SHIELD, LOCK, UNLOCK, GESTURE, BALANCE, BOLT, PING, INFO, CLOSE, TUNE }
    private val p = Paint(Paint.ANTI_ALIAS_FLAG).apply { style = Paint.Style.STROKE; strokeWidth = 2f; strokeCap = Paint.Cap.ROUND; strokeJoin = Paint.Join.ROUND }
    override fun getIntrinsicWidth() = sizePx
    override fun getIntrinsicHeight() = sizePx
    override fun draw(c: Canvas) {
        val b = bounds; if (b.width() <= 0) return
        c.save(); c.translate(b.left.toFloat(), b.top.toFloat()); c.scale(b.width() / 24f, b.height() / 24f)
        p.color = color; p.style = Paint.Style.STROKE
        val path = Path()
        when (kind) {
            Kind.SHIELD -> { path.moveTo(12f, 3f); path.lineTo(20f, 6f); path.lineTo(20f, 12f); path.cubicTo(20f, 17f, 16f, 20f, 12f, 21.5f); path.cubicTo(8f, 20f, 4f, 17f, 4f, 12f); path.lineTo(4f, 6f); path.close()
                path.moveTo(8.5f, 12f); path.lineTo(11f, 14.5f); path.lineTo(15.5f, 9.5f) }
            Kind.LOCK, Kind.UNLOCK -> { path.addRoundRect(RectF(5f, 11f, 19f, 21f), 2f, 2f, Path.Direction.CW)
                path.moveTo(8f, 11f); path.lineTo(8f, 7.5f); path.cubicTo(8f, 2.5f, 16f, 2.5f, 16f, 7.5f)
                if (kind == Kind.LOCK) path.lineTo(16f, 11f) }
            Kind.GESTURE -> { path.moveTo(9f, 13f); path.lineTo(9f, 5f); path.cubicTo(9f, 3f, 12f, 3f, 12f, 5f); path.lineTo(12f, 11f)
                path.lineTo(17f, 12f); path.cubicTo(19f, 12.5f, 19.5f, 14f, 19f, 16f); path.lineTo(18f, 21f); path.lineTo(10f, 21f); path.lineTo(6f, 15f); path.cubicTo(5.5f, 13.5f, 7.5f, 12.5f, 9f, 14f)
                path.moveTo(5f, 6f); path.lineTo(7f, 6f); path.moveTo(14f, 6f); path.lineTo(16f, 6f) }
            Kind.BALANCE -> { path.moveTo(4f, 9f); path.lineTo(20f, 9f); path.moveTo(16f, 5f); path.lineTo(20f, 9f); path.lineTo(16f, 13f)
                path.moveTo(20f, 15f); path.lineTo(4f, 15f); path.moveTo(8f, 11f); path.lineTo(4f, 15f); path.lineTo(8f, 19f) }
            Kind.BOLT -> { path.moveTo(13f, 2.5f); path.lineTo(5f, 13.5f); path.lineTo(11.5f, 13.5f); path.lineTo(10.5f, 21.5f); path.lineTo(19f, 10f); path.lineTo(12.5f, 10f); path.close() }
            Kind.PING -> { path.addCircle(12f, 12f, 2.5f, Path.Direction.CW)
                path.addArc(RectF(6.5f, 6.5f, 17.5f, 17.5f), -60f, 120f); path.addArc(RectF(6.5f, 6.5f, 17.5f, 17.5f), 120f, 120f)
                path.addArc(RectF(2.5f, 2.5f, 21.5f, 21.5f), -60f, 120f); path.addArc(RectF(2.5f, 2.5f, 21.5f, 21.5f), 120f, 120f) }
            Kind.INFO -> { path.addCircle(12f, 12f, 9f, Path.Direction.CW); path.moveTo(12f, 11f); path.lineTo(12f, 16.5f); path.moveTo(12f, 7.6f); path.lineTo(12f, 7.7f) }
            Kind.CLOSE -> { path.moveTo(6f, 6f); path.lineTo(18f, 18f); path.moveTo(18f, 6f); path.lineTo(6f, 18f) }
            Kind.TUNE -> { for ((x, y) in listOf(6f to 9f, 12f to 15f, 18f to 7f)) { path.moveTo(x, 4f); path.lineTo(x, 20f); path.addCircle(x, y, 2f, Path.Direction.CW) } }
        }
        c.drawPath(path, p)
        c.restore()
    }
    override fun setAlpha(a: Int) { p.alpha = a }
    override fun setColorFilter(f: ColorFilter?) { p.colorFilter = f }
    @Deprecated("platform API") override fun getOpacity() = PixelFormat.TRANSLUCENT
}
