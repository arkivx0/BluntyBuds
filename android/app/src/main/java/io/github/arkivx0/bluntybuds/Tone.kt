package io.github.arkivx0.bluntybuds

import android.content.Context
import android.media.*
import kotlin.math.PI
import kotlin.math.sin

/** "Find earbuds": loud repeating chirp over the normal media (A2DP) route. */
class Tone(private val ctx: Context) {
    private var track: AudioTrack? = null
    private var savedVol = -1

    fun play(left: Boolean, right: Boolean) {
        stop()
        val am = ctx.getSystemService(Context.AUDIO_SERVICE) as AudioManager
        savedVol = am.getStreamVolume(AudioManager.STREAM_MUSIC)
        am.setStreamVolume(AudioManager.STREAM_MUSIC, am.getStreamMaxVolume(AudioManager.STREAM_MUSIC), 0)
        val rate = 44100
        val frames = rate // 1 s loop: 3 short chirps + silence
        val pcm = ShortArray(frames * 2)
        for (i in 0 until frames) {
            val t = i.toDouble() / rate
            val inBeep = (t % 0.25) < 0.12 && t < 0.75
            val f = 2600.0 + 1200.0 * ((t % 0.25) / 0.12)
            val s = if (inBeep) (sin(2 * PI * f * t) * 30000).toInt().toShort() else 0
            pcm[2 * i] = if (left) s else 0
            pcm[2 * i + 1] = if (right) s else 0
        }
        val t = AudioTrack.Builder()
            .setAudioAttributes(AudioAttributes.Builder().setUsage(AudioAttributes.USAGE_MEDIA)
                .setContentType(AudioAttributes.CONTENT_TYPE_SONIFICATION).build())
            .setAudioFormat(AudioFormat.Builder().setEncoding(AudioFormat.ENCODING_PCM_16BIT).setSampleRate(rate)
                .setChannelMask(AudioFormat.CHANNEL_OUT_STEREO).build())
            .setTransferMode(AudioTrack.MODE_STATIC).setBufferSizeInBytes(pcm.size * 2).build()
        t.write(pcm, 0, pcm.size)
        t.setLoopPoints(0, frames, -1)
        t.play()
        track = t
    }

    fun stop() {
        track?.let { try { it.stop() } catch (_: Exception) {}; it.release() }
        track = null
        if (savedVol >= 0) {
            (ctx.getSystemService(Context.AUDIO_SERVICE) as AudioManager).setStreamVolume(AudioManager.STREAM_MUSIC, savedVol, 0)
            savedVol = -1
        }
    }
}
