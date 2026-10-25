package io.github.arkivx0.bluntybuds.proto

/** What goes on the wire for one EQ change. Same as 1.4.4: a single set-EQ frame, no mode command before it. */
object EqWire {
    fun frames(p: Protocol, gains: FloatArray): List<ByteArray> = when (p) {
        Protocol.A -> listOf(ProtoA.setEq(gains))
        Protocol.J -> listOf(ProtoJ.setEq(gains))
    }
}
