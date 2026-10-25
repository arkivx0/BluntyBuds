package io.github.arkivx0.bluntybuds

import android.annotation.SuppressLint
import android.content.Context
import android.graphics.Canvas
import android.view.MotionEvent
import android.widget.SeekBar

/** SeekBar drawn bottom-to-top, for the side-by-side EQ bands. */
class VerticalSeekBar(ctx: Context) : SeekBar(ctx) {
    /** Called when a drag ends (the EQ is sent then). */
    var onRelease: (() -> Unit)? = null
    /** True while touches are refused (Lock EQ). */
    var blocked: (() -> Boolean)? = null
    /** Called once per refused finger-down (shows "EQ locked"). */
    var onBlockedTap: (() -> Unit)? = null
    override fun onSizeChanged(w: Int, h: Int, ow: Int, oh: Int) = super.onSizeChanged(h, w, oh, ow)
    @Synchronized override fun onMeasure(wm: Int, hm: Int) {
        super.onMeasure(hm, wm)
        setMeasuredDimension(measuredHeight, measuredWidth)
    }
    override fun onDraw(c: Canvas) { c.rotate(-90f); c.translate(-height.toFloat(), 0f); super.onDraw(c) }
    @SuppressLint("ClickableViewAccessibility")
    override fun onTouchEvent(e: MotionEvent): Boolean {
        if (!isEnabled) return false
        if (blocked?.invoke() == true) { if (e.action == MotionEvent.ACTION_DOWN) onBlockedTap?.invoke(); return true }
        when (e.action) {
            MotionEvent.ACTION_DOWN, MotionEvent.ACTION_MOVE, MotionEvent.ACTION_UP -> {
                parent?.requestDisallowInterceptTouchEvent(e.action != MotionEvent.ACTION_UP)
                val span = (height - paddingLeft - paddingRight).coerceAtLeast(1)
                val frac = ((height - paddingLeft - e.y) / span).coerceIn(0f, 1f)
                progress = Math.round(max * frac)
                onSizeChanged(width, height, 0, 0)
                if (e.action == MotionEvent.ACTION_UP) onRelease?.invoke()
            }
            MotionEvent.ACTION_CANCEL -> parent?.requestDisallowInterceptTouchEvent(false)
        }
        return true
    }
    @Synchronized override fun setProgress(p: Int) { super.setProgress(p); onSizeChanged(width, height, 0, 0) }
}
