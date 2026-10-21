package io.github.arkivx0.bluntybuds.proto

/** ATT / GATT status names (Bluetooth Core spec ATT errors plus common Android stack codes). */
object GattErrors {
    fun name(code: Int): String = when (code) {
        0x00 -> "success"
        0x01 -> "invalid handle"
        0x02 -> "read not permitted"
        0x03 -> "write not permitted"
        0x05 -> "insufficient authentication"
        0x06 -> "request not supported"
        0x07 -> "invalid offset"
        0x08 -> "insufficient authorization"
        0x0D -> "invalid attribute value length"
        0x0E -> "unlikely error"
        0x0F -> "insufficient encryption"
        0x13 -> "remote user terminated connection"
        0x16 -> "connection terminated by local host"
        0x22 -> "LMP response timeout"
        0x3E -> "connection failed to be established"
        0x80 -> "no resources"
        0x81 -> "internal error"
        0x85 -> "generic GATT error (often: out of range or link dropped)"
        0x93 -> "connection timeout"
        else -> "code $code"
    }
    fun describe(code: Int) = "GATT status $code: ${name(code)}"
}

/** Reconnect backoff: 15 s, 30 s, 60 s, 60 s ... Reset after a successful connect. */
class Backoff(private vararg val stepsMs: Long = longArrayOf(15_000, 30_000, 60_000)) {
    private var i = 0
    fun next(): Long = stepsMs[minOf(i++, stepsMs.size - 1)]
    val max: Long get() = stepsMs.max()
    fun reset() { i = 0 }
    /** A connect attempt must outlive the longest retry gap so a slow connect isn't abandoned and restarted. */
    val connectTimeoutMs: Long get() = max + 15_000
}

/** Coalesces bursts (advertisement floods) into at most one flush per interval. */
class Coalescer(private val minIntervalMs: Long) {
    private var last = Long.MIN_VALUE / 2
    private var dirty = false
    fun mark() { dirty = true }
    fun shouldFlush(nowMs: Long): Boolean {
        if (!dirty || nowMs - last < minIntervalMs) return false
        dirty = false; last = nowMs; return true
    }
}

/** Generation token: a delayed action only runs if no newer generation started meanwhile. */
class Generation {
    @Volatile private var g = 0
    @Synchronized fun begin(): Int = ++g
    fun isCurrent(x: Int) = g == x
}
