package io.github.arkivx0.bluntybuds

import android.content.Context

/** EQ presets, stored only in local SharedPreferences. */
class Presets(ctx: Context) {
    private val sp = ctx.getSharedPreferences("eq_presets", Context.MODE_PRIVATE)

    companion object {
        val BUILT_IN = linkedMapOf(
            "Flat" to FloatArray(10),
            "Bass boost" to floatArrayOf(6f, 5f, 4f, 2f, 0f, 0f, 0f, 0f, 0f, 0f),
            "Treble boost" to floatArrayOf(0f, 0f, 0f, 0f, 0f, 1f, 2f, 4f, 5f, 6f),
            "Vocal" to floatArrayOf(-2f, -1f, 0f, 1f, 3f, 3f, 2f, 1f, 0f, -1f),
            "Rock" to floatArrayOf(4f, 3f, 1f, 0f, -1f, 0f, 1f, 3f, 4f, 4f),
            "Classical" to floatArrayOf(3f, 2f, 1f, 0f, 0f, 0f, 1f, 2f, 3f, 3f),
            "Podcast" to floatArrayOf(-4f, -3f, -1f, 1f, 3f, 4f, 3f, 1f, -1f, -3f),
        )
    }

    fun all(): LinkedHashMap<String, FloatArray> {
        val m = LinkedHashMap<String, FloatArray>(BUILT_IN)
        for ((k, v) in sp.all) if (k.startsWith("u:") && v is String) parse(v)?.let { m[k.removePrefix("u:")] = it }
        return m
    }
    fun isBuiltIn(name: String) = name in BUILT_IN
    fun save(name: String, gains: FloatArray) = sp.edit().putString("u:$name", gains.joinToString(",")).apply()
    fun delete(name: String) = sp.edit().remove("u:$name").apply()
    fun lastUsed(): String? = sp.getString("last", null)
    fun setLastUsed(n: String) = sp.edit().putString("last", n).apply()
    fun user(): Map<String, FloatArray> = all().filterKeys { !isBuiltIn(it) }
    /** JSON object {"name": [10 gains]} — same format as the Windows app. */
    fun exportJson(): String {
        val o = org.json.JSONObject()
        for ((k, v) in user()) o.put(k, org.json.JSONArray(v.map { it.toDouble() }))
        return o.toString(2)
    }
    fun importJson(json: String): Int {
        val o = org.json.JSONObject(json); var n = 0
        for (k in o.keys()) {
            val a = o.optJSONArray(k) ?: continue
            if (a.length() != 10 || isBuiltIn(k) || k.isBlank()) continue
            save(k.trim(), FloatArray(10) { a.getDouble(it).toFloat().coerceIn(-12f, 12f) }); n++
        }
        return n
    }
    private fun parse(s: String) = s.split(",").mapNotNull { it.toFloatOrNull() }.takeIf { it.size == 10 }?.toFloatArray()
}
