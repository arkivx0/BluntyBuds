package io.github.arkivx0.bluntybuds.proto

import org.junit.Assert.*
import org.junit.Test

class LinkTest {
    @Test fun backoffStepsAndCap() {
        val b = Backoff()
        assertEquals(listOf(15_000L, 30_000L, 60_000L, 60_000L), List(4) { b.next() })
        b.reset(); assertEquals(15_000L, b.next())
        assertTrue(b.connectTimeoutMs > b.max)
    }
    @Test fun coalescerAtMostTwicePerSecond() {
        val c = Coalescer(500)
        assertFalse(c.shouldFlush(0))
        var n = 0
        for (t in 0L until 1000L step 10) { c.mark(); if (c.shouldFlush(t)) n++ }
        assertEquals(2, n)
    }
    @Test fun staleScanTimerIgnored() {
        val g = Generation(); val a = g.begin(); val b = g.begin()
        assertFalse(g.isCurrent(a)); assertTrue(g.isCurrent(b))
    }
    @Test fun gattNames() {
        assertEquals("insufficient authentication", GattErrors.name(5))
        assertTrue(GattErrors.describe(133).contains("generic GATT error"))
    }
}
