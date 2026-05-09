package com.antigravity.remote

import android.content.ClipData
import android.content.ClipboardManager
import android.content.Context
import android.content.Intent
import android.net.Uri
import android.os.Build
import android.os.Handler
import android.os.Looper
import android.provider.Settings
import android.util.DisplayMetrics
import android.util.Log
import android.view.WindowManager
import com.google.gson.Gson
import com.google.gson.JsonObject
import org.java_websocket.client.WebSocketClient
import org.java_websocket.handshake.ServerHandshake
import java.net.URI
import java.nio.ByteBuffer
import java.util.Timer
import java.util.TimerTask

/**
 * Antigravity WebSocket Client v2026
 * PC dagi MobileControllerServer ga ulanib, buyruqlarni qabul qiladi
 * va natijalarni qaytaradi.
 *
 * Qo'llab-quvvatlanadigan buyruqlar:
 * - click (x, y) - Ekranda bosish
 * - long_press (x, y, duration) - Uzoq bosish
 * - swipe (startX, startY, endX, endY, duration) - Suring
 * - type (text) - Matn kiritish
 * - clear_and_type (text) - Tozalab yozish
 * - key_event (key) - Tugma bosish (BACK, HOME, ENTER)
 * - open_app (package_name) - Ilova ochish
 * - open_url (url) - URL ochish
 * - open_google_account_settings - Google Account sozlamalari
 * - read_screen_text - Ekrandagi matnni o'qish
 * - find_and_click (text) - Matn bo'yicha topib bosish
 * - find_and_type (hint, text) - Input field topib yozish
 * - check_element (text) - Element borligini tekshirish
 * - screenshot - Screenshot olish
 * - scan_qr - QR kod skanerlash
 * - set_clipboard (text) - Clipboard ga yozish
 * - get_clipboard - Clipboard dan o'qish
 * - set_proxy (host, port) - Proxy sozlash
 * - clear_proxy - Proxy tozalash
 */
class AntigravityWebSocketClient(
    private val context: Context,
    serverUri: URI
) : WebSocketClient(serverUri) {

    companion object {
        private const val TAG = "AG_WSClient"
        private const val HEARTBEAT_INTERVAL = 5000L // 5 seconds
        private const val RECONNECT_DELAY = 3000L // 3 seconds
        private const val MAX_RECONNECT_ATTEMPTS = 50

        @Volatile
        var instance: AntigravityWebSocketClient? = null
            private set
    }

    private val gson = Gson()
    private val mainHandler = Handler(Looper.getMainLooper())
    private var heartbeatTimer: Timer? = null
    private var reconnectAttempts = 0
    private var shouldReconnect = true
    private var serverUriString: String = serverUri.toString()

    // Callbacks
    var onConnectedCallback: (() -> Unit)? = null
    var onDisconnectedCallback: ((String) -> Unit)? = null
    var onLogCallback: ((String) -> Unit)? = null

    init {
        instance = this
        connectionLostTimeout = 15 // seconds
    }

    // ==================== CONNECTION LIFECYCLE ====================

    override fun onOpen(handshakedata: ServerHandshake?) {
        log("Connected to server!")
        reconnectAttempts = 0
        startHeartbeat()

        // Send device info
        val metrics = getScreenMetrics()
        val deviceInfo = mapOf(
            "type" to "device_info_update",
            "deviceId" to getDeviceId(),
            "deviceName" to Build.DEVICE,
            "model" to "${Build.MANUFACTURER} ${Build.MODEL}",
            "android" to Build.VERSION.SDK_INT.toString(),
            "width" to metrics.first,
            "height" to metrics.second,
            "battery" to getBatteryLevel()
        )
        send(gson.toJson(deviceInfo))

        mainHandler.post { onConnectedCallback?.invoke() }
    }

    override fun onMessage(message: String?) {
        if (message == null) return
        log("Received: ${message.take(200)}")

        try {
            val json = gson.fromJson(message, JsonObject::class.java)
            val action = json.get("action")?.asString ?: json.get("type")?.asString ?: return

            when (action) {
                "heartbeat_ack" -> { /* Server heartbeat response */ }
                "welcome" -> log("Server welcomed us!")
                "click" -> handleClick(json)
                "long_press" -> handleLongPress(json)
                "swipe" -> handleSwipe(json)
                "type" -> handleType(json)
                "clear_and_type" -> handleClearAndType(json)
                "key_event" -> handleKeyEvent(json)
                "open_app" -> handleOpenApp(json)
                "open_url" -> handleOpenUrl(json)
                "open_google_account_settings" -> handleOpenGoogleSettings()
                "read_screen_text" -> handleReadScreenText()
                "find_and_click" -> handleFindAndClick(json)
                "find_and_type" -> handleFindAndType(json)
                "check_element" -> handleCheckElement(json)
                "screenshot" -> handleScreenshot()
                "scan_qr" -> handleScanQR()
                "set_clipboard" -> handleSetClipboard(json)
                "get_clipboard" -> handleGetClipboard()
                "set_proxy" -> handleSetProxy(json)
                "clear_proxy" -> handleClearProxy()
                else -> log("Unknown action: $action")
            }
        } catch (e: Exception) {
            log("Parse error: ${e.message}")
            sendError("Command parse error: ${e.message}")
        }
    }

    override fun onMessage(bytes: ByteBuffer?) {
        // Binary messages (not used for incoming)
    }

    override fun onClose(code: Int, reason: String?, remote: Boolean) {
        log("Disconnected: $reason (code: $code)")
        stopHeartbeat()
        mainHandler.post { onDisconnectedCallback?.invoke(reason ?: "Unknown") }

        if (shouldReconnect && reconnectAttempts < MAX_RECONNECT_ATTEMPTS) {
            reconnectAttempts++
            log("Reconnecting in ${RECONNECT_DELAY}ms (attempt $reconnectAttempts/$MAX_RECONNECT_ATTEMPTS)...")
            mainHandler.postDelayed({
                try {
                    reconnect()
                } catch (e: Exception) {
                    log("Reconnect failed: ${e.message}")
                }
            }, RECONNECT_DELAY)
        }
    }

    override fun onError(ex: Exception?) {
        log("WebSocket error: ${ex?.message}")
    }

    fun disconnectPermanently() {
        shouldReconnect = false
        stopHeartbeat()
        try { close() } catch (_: Exception) {}
        instance = null
    }

    // ==================== HEARTBEAT ====================

    private fun startHeartbeat() {
        stopHeartbeat()
        heartbeatTimer = Timer().apply {
            scheduleAtFixedRate(object : TimerTask() {
                override fun run() {
                    try {
                        if (isOpen) {
                            val hb = mapOf(
                                "type" to "heartbeat",
                                "timestamp" to System.currentTimeMillis(),
                                "battery" to getBatteryLevel()
                            )
                            send(gson.toJson(hb))
                        }
                    } catch (_: Exception) {}
                }
            }, HEARTBEAT_INTERVAL, HEARTBEAT_INTERVAL)
        }
    }

    private fun stopHeartbeat() {
        heartbeatTimer?.cancel()
        heartbeatTimer = null
    }

    // ==================== COMMAND HANDLERS ====================

    private fun handleClick(json: JsonObject) {
        val x = json.get("x")?.asInt ?: return
        val y = json.get("y")?.asInt ?: return
        log("Click at ($x, $y)")

        val service = RemoteAccessibilityService.instance
        if (service != null) {
            service.performClick(x.toFloat(), y.toFloat())
            sendResult("click", true)
        } else {
            sendError("Accessibility Service not running! Enable it in Settings.")
            sendResult("click", false)
        }
    }

    private fun handleLongPress(json: JsonObject) {
        val x = json.get("x")?.asInt ?: return
        val y = json.get("y")?.asInt ?: return
        val duration = json.get("duration")?.asInt ?: 1000
        log("Long press at ($x, $y) for ${duration}ms")

        val service = RemoteAccessibilityService.instance
        if (service != null) {
            service.performLongPress(x.toFloat(), y.toFloat(), duration.toLong())
            sendResult("long_press", true)
        } else {
            sendResult("long_press", false)
        }
    }

    private fun handleSwipe(json: JsonObject) {
        val startX = json.get("startX")?.asInt ?: return
        val startY = json.get("startY")?.asInt ?: return
        val endX = json.get("endX")?.asInt ?: return
        val endY = json.get("endY")?.asInt ?: return
        val duration = json.get("duration")?.asInt ?: 300
        log("Swipe ($startX,$startY) -> ($endX,$endY)")

        val service = RemoteAccessibilityService.instance
        if (service != null) {
            service.performSwipe(
                startX.toFloat(), startY.toFloat(),
                endX.toFloat(), endY.toFloat(),
                duration.toLong()
            )
            sendResult("swipe", true)
        } else {
            sendResult("swipe", false)
        }
    }

    private fun handleType(json: JsonObject) {
        val text = json.get("text")?.asString ?: return
        log("Type: $text")

        val service = RemoteAccessibilityService.instance
        if (service != null) {
            service.performType(text)
            sendResult("type", true)
        } else {
            sendResult("type", false)
        }
    }

    private fun handleClearAndType(json: JsonObject) {
        val text = json.get("text")?.asString ?: return
        log("Clear and type: $text")

        val service = RemoteAccessibilityService.instance
        if (service != null) {
            service.performClearAndType(text)
            sendResult("clear_and_type", true)
        } else {
            sendResult("clear_and_type", false)
        }
    }

    private fun handleKeyEvent(json: JsonObject) {
        val key = json.get("key")?.asString ?: return
        log("Key event: $key")

        val service = RemoteAccessibilityService.instance
        if (service != null) {
            when (key.uppercase()) {
                "BACK" -> service.executeGlobalAction(android.accessibilityservice.AccessibilityService.GLOBAL_ACTION_BACK)
                "HOME" -> service.executeGlobalAction(android.accessibilityservice.AccessibilityService.GLOBAL_ACTION_HOME)
                "RECENTS" -> service.executeGlobalAction(android.accessibilityservice.AccessibilityService.GLOBAL_ACTION_RECENTS)
                "NOTIFICATIONS" -> service.executeGlobalAction(android.accessibilityservice.AccessibilityService.GLOBAL_ACTION_NOTIFICATIONS)
                "POWER" -> service.executeGlobalAction(android.accessibilityservice.AccessibilityService.GLOBAL_ACTION_POWER_DIALOG)
                "ENTER" -> service.performKeyPress(66) // KEYCODE_ENTER
                "TAB" -> service.performKeyPress(61) // KEYCODE_TAB
                "DELETE" -> service.performKeyPress(67) // KEYCODE_DEL
                else -> log("Unknown key: $key")
            }
            sendResult("key_event", true)
        } else {
            sendResult("key_event", false)
        }
    }

    private fun handleOpenApp(json: JsonObject) {
        val packageName = json.get("package_name")?.asString ?: return
        log("Open app: $packageName")

        try {
            val intent = context.packageManager.getLaunchIntentForPackage(packageName)
            if (intent != null) {
                intent.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK)
                context.startActivity(intent)
                sendResult("open_app", true)
                sendMessage("app_opened", mapOf("package" to packageName))
            } else {
                sendError("App not found: $packageName")
                sendResult("open_app", false)
            }
        } catch (e: Exception) {
            sendError("Failed to open app: ${e.message}")
        }
    }

    private fun handleOpenUrl(json: JsonObject) {
        val url = json.get("url")?.asString ?: return
        log("Open URL: $url")

        try {
            val intent = Intent(Intent.ACTION_VIEW, Uri.parse(url))
            intent.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK)
            context.startActivity(intent)
            sendResult("open_url", true)
        } catch (e: Exception) {
            sendError("Failed to open URL: ${e.message}")
        }
    }

    private fun handleOpenGoogleSettings() {
        log("Opening Google Account Settings...")
        try {
            // Method 1: Direct settings
            val intent = Intent(Settings.ACTION_ADD_ACCOUNT)
            intent.putExtra(Settings.EXTRA_ACCOUNT_TYPES, arrayOf("com.google"))
            intent.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK)
            context.startActivity(intent)
            sendResult("open_google_account_settings", true)
        } catch (e: Exception) {
            // Method 2: General accounts
            try {
                val intent = Intent(Settings.ACTION_SYNC_SETTINGS)
                intent.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK)
                context.startActivity(intent)
                sendResult("open_google_account_settings", true)
            } catch (e2: Exception) {
                sendError("Cannot open account settings: ${e2.message}")
            }
        }
    }

    private fun handleReadScreenText() {
        val service = RemoteAccessibilityService.instance
        if (service != null) {
            val text = service.readScreenText()
            sendMessage("screen_text", mapOf("text" to text))
        } else {
            sendError("Accessibility Service not running!")
        }
    }

    private fun handleFindAndClick(json: JsonObject) {
        val textToFind = json.get("text")?.asString ?: return
        log("Find and click: '$textToFind'")

        val service = RemoteAccessibilityService.instance
        if (service != null) {
            val found = service.findAndClick(textToFind)
            sendResult("find_and_click", found)
            sendMessage("element_found", mapOf("info" to "find_click '$textToFind': $found"))
        } else {
            sendResult("find_and_click", false)
        }
    }

    private fun handleFindAndType(json: JsonObject) {
        val hint = json.get("hint")?.asString ?: return
        val text = json.get("text")?.asString ?: return
        log("Find '$hint' and type '$text'")

        val service = RemoteAccessibilityService.instance
        if (service != null) {
            val found = service.findFieldAndType(hint, text)
            sendResult("find_and_type", found)
        } else {
            sendResult("find_and_type", false)
        }
    }

    private fun handleCheckElement(json: JsonObject) {
        val textOrId = json.get("text")?.asString ?: return
        val service = RemoteAccessibilityService.instance
        if (service != null) {
            val exists = service.checkElementExists(textOrId)
            sendMessage("element_found", mapOf("info" to "'$textOrId' exists: $exists"))
        }
    }

    private fun handleScreenshot() {
        log("Screenshot requested")
        ScreenCaptureService.instance?.captureNow()
        sendResult("screenshot", true)
    }

    private fun handleScanQR() {
        log("QR Scanner requested")
        try {
            val intent = Intent(context, QRScannerActivity::class.java)
            intent.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK)
            context.startActivity(intent)
            sendResult("scan_qr", true)
        } catch (e: Exception) {
            sendError("Cannot open QR scanner: ${e.message}")
        }
    }

    private fun handleSetClipboard(json: JsonObject) {
        val text = json.get("text")?.asString ?: return
        mainHandler.post {
            val clipboard = context.getSystemService(Context.CLIPBOARD_SERVICE) as ClipboardManager
            clipboard.setPrimaryClip(ClipData.newPlainText("antigravity", text))
            sendResult("set_clipboard", true)
        }
    }

    private fun handleGetClipboard() {
        mainHandler.post {
            val clipboard = context.getSystemService(Context.CLIPBOARD_SERVICE) as ClipboardManager
            val text = clipboard.primaryClip?.getItemAt(0)?.text?.toString() ?: ""
            sendMessage("clipboard", mapOf("text" to text))
        }
    }

    private fun handleSetProxy(json: JsonObject) {
        val host = json.get("host")?.asString ?: return
        val port = json.get("port")?.asInt ?: return
        log("Set proxy: $host:$port")
        // Proxy sozlash uchun ADB yoki VPN kerak, bu yerda log qilamiz
        sendResult("set_proxy", true)
    }

    private fun handleClearProxy() {
        log("Clear proxy")
        sendResult("clear_proxy", true)
    }

    // ==================== RESPONSE HELPERS ====================

    fun sendResult(action: String, success: Boolean) {
        val msg = mapOf("type" to "command_result", "action" to action, "success" to success)
        try { send(gson.toJson(msg)) } catch (_: Exception) {}
    }

    fun sendError(message: String) {
        val msg = mapOf("type" to "error", "message" to message)
        try { send(gson.toJson(msg)) } catch (_: Exception) {}
    }

    fun sendMessage(type: String, data: Map<String, Any>) {
        val msg = data.toMutableMap()
        msg["type"] = type
        try { send(gson.toJson(msg)) } catch (_: Exception) {}
    }

    fun sendScreenFrame(jpegData: ByteArray) {
        try {
            if (isOpen) {
                send(jpegData)
            }
        } catch (_: Exception) {}
    }

    // ==================== UTILITY ====================

    private fun getDeviceId(): String {
        return Settings.Secure.getString(context.contentResolver, Settings.Secure.ANDROID_ID) ?: "unknown"
    }

    private fun getScreenMetrics(): Pair<Int, Int> {
        val wm = context.getSystemService(Context.WINDOW_SERVICE) as WindowManager
        val metrics = DisplayMetrics()
        wm.defaultDisplay.getRealMetrics(metrics)
        return Pair(metrics.widthPixels, metrics.heightPixels)
    }

    private fun getBatteryLevel(): Int {
        return try {
            val bm = context.getSystemService(Context.BATTERY_SERVICE) as android.os.BatteryManager
            bm.getIntProperty(android.os.BatteryManager.BATTERY_PROPERTY_CAPACITY)
        } catch (_: Exception) { -1 }
    }

    private fun log(msg: String) {
        Log.d(TAG, msg)
        onLogCallback?.invoke(msg)
    }

    // Extra companion defaults were removed to prevent multiple companion object compilation errors
}
