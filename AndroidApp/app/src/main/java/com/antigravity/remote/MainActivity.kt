package com.antigravity.remote

import android.app.Activity
import android.content.Context
import android.content.Intent
import android.media.projection.MediaProjectionManager
import android.net.wifi.WifiManager
import android.os.Bundle
import android.provider.Settings
import android.text.format.Formatter
import android.util.Log
import android.view.View
import android.view.WindowManager
import android.widget.*
import androidx.appcompat.app.AppCompatActivity
import java.net.URI

/**
 * Antigravity Remote - MainActivity
 * Asosiy boshqaruv ekrani:
 * - Server IP va Port kiritish
 * - Accessibility Service va Screen Capture ruxsatlari
 * - Ulanish holati ko'rish
 * - Live log
 */
class MainActivity : AppCompatActivity() {

    companion object {
        private const val TAG = "AG_Main"
        private const val REQUEST_MEDIA_PROJECTION = 1001
        private const val PREFS_NAME = "antigravity_prefs"
    }

    // UI Elements
    private lateinit var etServerIp: EditText
    private lateinit var etServerPort: EditText
    private lateinit var btnConnect: Button
    private lateinit var btnDisconnect: Button
    private lateinit var btnEnableAccessibility: Button
    private lateinit var btnStartCapture: Button
    private lateinit var btnStopCapture: Button
    private lateinit var tvStatus: TextView
    private lateinit var tvDeviceInfo: TextView
    private lateinit var tvLocalIp: TextView
    private lateinit var tvAccessibilityStatus: TextView
    private lateinit var tvCaptureStatus: TextView
    private lateinit var tvLog: TextView
    private lateinit var scrollLog: ScrollView
    private lateinit var seekFps: SeekBar
    private lateinit var tvFpsLabel: TextView
    private lateinit var seekQuality: SeekBar
    private lateinit var tvQualityLabel: TextView

    // State
    private var wsClient: AntigravityWebSocketClient? = null
    private var isConnected = false

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        window.addFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON)

        // Programmatik UI yaratish (XML layout o'rniga)
        buildUI()

        // Saved preferences
        loadPreferences()

        // Device info
        updateDeviceInfo()

        addLog("Antigravity Remote v2026 ready!")
        addLog("1. Enable Accessibility Service")
        addLog("2. Enter PC server IP and port")
        addLog("3. Connect")
        addLog("4. Start Screen Capture")
    }

    override fun onResume() {
        super.onResume()
        updateAccessibilityStatus()
        updateCaptureStatus()
    }

    override fun onDestroy() {
        wsClient?.disconnectPermanently()
        super.onDestroy()
    }

    // ==================== UI BUILDING ====================

    private fun buildUI() {
        val rootLayout = LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            setBackgroundColor(0xFF0A0A0F.toInt())
            setPadding(32, 48, 32, 32)
        }

        val scrollView = ScrollView(this).apply {
            setBackgroundColor(0xFF0A0A0F.toInt())
        }

        val contentLayout = LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
        }

        // Title
        contentLayout.addView(TextView(this).apply {
            text = "ANTIGRAVITY REMOTE"
            textSize = 24f
            setTextColor(0xFF00FF88.toInt())
            gravity = android.view.Gravity.CENTER
            setPadding(0, 0, 0, 8)
        })

        contentLayout.addView(TextView(this).apply {
            text = "Mobile Control Companion v2026"
            textSize = 12f
            setTextColor(0xFF888888.toInt())
            gravity = android.view.Gravity.CENTER
            setPadding(0, 0, 0, 24)
        })

        // Local IP
        tvLocalIp = TextView(this).apply {
            text = "Local IP: ..."
            textSize = 13f
            setTextColor(0xFF00CCFF.toInt())
            setPadding(0, 0, 0, 16)
        }
        contentLayout.addView(tvLocalIp)

        // Device Info
        tvDeviceInfo = TextView(this).apply {
            text = "Device: ..."
            textSize = 12f
            setTextColor(0xFFAAAAAA.toInt())
            setPadding(0, 0, 0, 24)
        }
        contentLayout.addView(tvDeviceInfo)

        // --- Accessibility Section ---
        contentLayout.addView(makeSectionTitle("Accessibility Service"))

        tvAccessibilityStatus = TextView(this).apply {
            text = "Status: Not Enabled"
            textSize = 13f
            setTextColor(0xFFFF4444.toInt())
            setPadding(0, 0, 0, 8)
        }
        contentLayout.addView(tvAccessibilityStatus)

        btnEnableAccessibility = Button(this).apply {
            text = "Enable Accessibility Service"
            setBackgroundColor(0xFF1A1A2E.toInt())
            setTextColor(0xFF00FF88.toInt())
            setPadding(24, 16, 24, 16)
            setOnClickListener { openAccessibilitySettings() }
        }
        contentLayout.addView(btnEnableAccessibility)

        contentLayout.addView(makeDivider())

        // --- Connection Section ---
        contentLayout.addView(makeSectionTitle("Server Connection"))

        val ipRow = LinearLayout(this).apply { orientation = LinearLayout.HORIZONTAL }
        etServerIp = EditText(this).apply {
            hint = "Server IP (e.g. 192.168.1.100)"
            setText("192.168.1.100")
            textSize = 14f
            setTextColor(0xFFE0E0E0.toInt())
            setHintTextColor(0xFF666666.toInt())
            setBackgroundColor(0xFF12121F.toInt())
            setPadding(16, 12, 16, 12)
            layoutParams = LinearLayout.LayoutParams(0, LinearLayout.LayoutParams.WRAP_CONTENT, 2f)
        }
        ipRow.addView(etServerIp)

        etServerPort = EditText(this).apply {
            hint = "Port"
            setText("8888")
            textSize = 14f
            setTextColor(0xFFE0E0E0.toInt())
            setHintTextColor(0xFF666666.toInt())
            setBackgroundColor(0xFF12121F.toInt())
            setPadding(16, 12, 16, 12)
            layoutParams = LinearLayout.LayoutParams(0, LinearLayout.LayoutParams.WRAP_CONTENT, 1f).apply {
                marginStart = 8
            }
        }
        ipRow.addView(etServerPort)
        contentLayout.addView(ipRow)

        tvStatus = TextView(this).apply {
            text = "Status: Disconnected"
            textSize = 14f
            setTextColor(0xFFFF4444.toInt())
            setPadding(0, 12, 0, 8)
        }
        contentLayout.addView(tvStatus)

        val btnRow = LinearLayout(this).apply { orientation = LinearLayout.HORIZONTAL }
        btnConnect = Button(this).apply {
            text = "Connect"
            setBackgroundColor(0xFF1A1A2E.toInt())
            setTextColor(0xFF00FF88.toInt())
            setPadding(24, 16, 24, 16)
            layoutParams = LinearLayout.LayoutParams(0, LinearLayout.LayoutParams.WRAP_CONTENT, 1f)
            setOnClickListener { connectToServer() }
        }
        btnRow.addView(btnConnect)

        btnDisconnect = Button(this).apply {
            text = "Disconnect"
            setBackgroundColor(0xFF1A1A2E.toInt())
            setTextColor(0xFFFF4444.toInt())
            setPadding(24, 16, 24, 16)
            isEnabled = false
            layoutParams = LinearLayout.LayoutParams(0, LinearLayout.LayoutParams.WRAP_CONTENT, 1f).apply {
                marginStart = 8
            }
            setOnClickListener { disconnectFromServer() }
        }
        btnRow.addView(btnDisconnect)
        contentLayout.addView(btnRow)

        contentLayout.addView(makeDivider())

        // --- Screen Capture Section ---
        contentLayout.addView(makeSectionTitle("Screen Capture"))

        tvCaptureStatus = TextView(this).apply {
            text = "Capture: Stopped"
            textSize = 13f
            setTextColor(0xFFFF4444.toInt())
            setPadding(0, 0, 0, 8)
        }
        contentLayout.addView(tvCaptureStatus)

        // FPS slider
        tvFpsLabel = TextView(this).apply {
            text = "FPS: 10"
            textSize = 12f
            setTextColor(0xFFAAAAAA.toInt())
        }
        contentLayout.addView(tvFpsLabel)

        seekFps = SeekBar(this).apply {
            max = 29
            progress = 9
            setOnSeekBarChangeListener(object : SeekBar.OnSeekBarChangeListener {
                override fun onProgressChanged(seekBar: SeekBar?, progress: Int, fromUser: Boolean) {
                    tvFpsLabel.text = "FPS: ${progress + 1}"
                }
                override fun onStartTrackingTouch(seekBar: SeekBar?) {}
                override fun onStopTrackingTouch(seekBar: SeekBar?) {}
            })
        }
        contentLayout.addView(seekFps)

        // Quality slider
        tvQualityLabel = TextView(this).apply {
            text = "JPEG Quality: 50%"
            textSize = 12f
            setTextColor(0xFFAAAAAA.toInt())
        }
        contentLayout.addView(tvQualityLabel)

        seekQuality = SeekBar(this).apply {
            max = 90
            progress = 40
            setOnSeekBarChangeListener(object : SeekBar.OnSeekBarChangeListener {
                override fun onProgressChanged(seekBar: SeekBar?, progress: Int, fromUser: Boolean) {
                    tvQualityLabel.text = "JPEG Quality: ${progress + 10}%"
                }
                override fun onStartTrackingTouch(seekBar: SeekBar?) {}
                override fun onStopTrackingTouch(seekBar: SeekBar?) {}
            })
        }
        contentLayout.addView(seekQuality)

        val captureRow = LinearLayout(this).apply {
            orientation = LinearLayout.HORIZONTAL
            setPadding(0, 8, 0, 0)
        }
        btnStartCapture = Button(this).apply {
            text = "Start Capture"
            setBackgroundColor(0xFF1A1A2E.toInt())
            setTextColor(0xFF00CCFF.toInt())
            setPadding(24, 16, 24, 16)
            layoutParams = LinearLayout.LayoutParams(0, LinearLayout.LayoutParams.WRAP_CONTENT, 1f)
            setOnClickListener { requestScreenCapture() }
        }
        captureRow.addView(btnStartCapture)

        btnStopCapture = Button(this).apply {
            text = "Stop Capture"
            setBackgroundColor(0xFF1A1A2E.toInt())
            setTextColor(0xFFFF8800.toInt())
            setPadding(24, 16, 24, 16)
            isEnabled = false
            layoutParams = LinearLayout.LayoutParams(0, LinearLayout.LayoutParams.WRAP_CONTENT, 1f).apply {
                marginStart = 8
            }
            setOnClickListener { stopScreenCapture() }
        }
        captureRow.addView(btnStopCapture)
        contentLayout.addView(captureRow)

        contentLayout.addView(makeDivider())

        // --- Log Section ---
        contentLayout.addView(makeSectionTitle("Live Log"))

        scrollLog = ScrollView(this).apply {
            layoutParams = LinearLayout.LayoutParams(
                LinearLayout.LayoutParams.MATCH_PARENT, 400
            )
            setBackgroundColor(0xFF050510.toInt())
        }

        tvLog = TextView(this).apply {
            textSize = 11f
            setTextColor(0xFF00FF88.toInt())
            setPadding(12, 12, 12, 12)
            typeface = android.graphics.Typeface.MONOSPACE
        }
        scrollLog.addView(tvLog)
        contentLayout.addView(scrollLog)

        scrollView.addView(contentLayout)
        rootLayout.addView(scrollView)
        setContentView(rootLayout)
    }

    private fun makeSectionTitle(title: String): TextView {
        return TextView(this).apply {
            text = title
            textSize = 16f
            setTextColor(0xFF00FF88.toInt())
            setPadding(0, 16, 0, 8)
        }
    }

    private fun makeDivider(): View {
        return View(this).apply {
            setBackgroundColor(0xFF222244.toInt())
            layoutParams = LinearLayout.LayoutParams(
                LinearLayout.LayoutParams.MATCH_PARENT, 2
            ).apply {
                topMargin = 16
                bottomMargin = 16
            }
        }
    }

    // ==================== CONNECTION ====================

    private fun connectToServer() {
        val ip = etServerIp.text.toString().trim()
        val port = etServerPort.text.toString().trim()
        if (ip.isEmpty() || port.isEmpty()) {
            Toast.makeText(this, "IP va Port kiriting!", Toast.LENGTH_SHORT).show()
            return
        }

        savePreferences()

        val deviceId = Settings.Secure.getString(contentResolver, Settings.Secure.ANDROID_ID)
        val wsUrl = "ws://$ip:$port/?deviceId=$deviceId&deviceName=${android.os.Build.DEVICE}&model=${android.os.Build.MANUFACTURER}%20${android.os.Build.MODEL}&width=${resources.displayMetrics.widthPixels}&height=${resources.displayMetrics.heightPixels}"

        addLog("Connecting to $wsUrl ...")
        tvStatus.text = "Status: Connecting..."
        tvStatus.setTextColor(0xFFFFCC00.toInt())

        try {
            wsClient = AntigravityWebSocketClient(this, URI(wsUrl))
            wsClient?.onConnectedCallback = {
                runOnUiThread {
                    isConnected = true
                    tvStatus.text = "Status: Connected"
                    tvStatus.setTextColor(0xFF00FF88.toInt())
                    btnConnect.isEnabled = false
                    btnDisconnect.isEnabled = true
                    addLog("Connected to server!")
                }
            }
            wsClient?.onDisconnectedCallback = { reason ->
                runOnUiThread {
                    isConnected = false
                    tvStatus.text = "Status: Disconnected ($reason)"
                    tvStatus.setTextColor(0xFFFF4444.toInt())
                    btnConnect.isEnabled = true
                    btnDisconnect.isEnabled = false
                    addLog("Disconnected: $reason")
                }
            }
            wsClient?.onLogCallback = { msg ->
                runOnUiThread { addLog(msg) }
            }
            wsClient?.connect()
        } catch (e: Exception) {
            addLog("Connection error: ${e.message}")
            tvStatus.text = "Status: Error"
            tvStatus.setTextColor(0xFFFF4444.toInt())
        }
    }

    private fun disconnectFromServer() {
        wsClient?.disconnectPermanently()
        isConnected = false
        tvStatus.text = "Status: Disconnected"
        tvStatus.setTextColor(0xFFFF4444.toInt())
        btnConnect.isEnabled = true
        btnDisconnect.isEnabled = false
        addLog("Disconnected manually")
    }

    // ==================== SCREEN CAPTURE ====================

    private fun requestScreenCapture() {
        val projectionManager = getSystemService(Context.MEDIA_PROJECTION_SERVICE) as MediaProjectionManager
        startActivityForResult(projectionManager.createScreenCaptureIntent(), REQUEST_MEDIA_PROJECTION)
    }

    override fun onActivityResult(requestCode: Int, resultCode: Int, data: Intent?) {
        super.onActivityResult(requestCode, resultCode, data)

        if (requestCode == REQUEST_MEDIA_PROJECTION && resultCode == Activity.RESULT_OK && data != null) {
            val fps = seekFps.progress + 1
            val quality = seekQuality.progress + 10

            val serviceIntent = Intent(this, ScreenCaptureService::class.java).apply {
                putExtra(ScreenCaptureService.EXTRA_RESULT_CODE, resultCode)
                putExtra(ScreenCaptureService.EXTRA_RESULT_DATA, data)
                putExtra(ScreenCaptureService.EXTRA_FPS, fps)
                putExtra(ScreenCaptureService.EXTRA_QUALITY, quality)
                putExtra(ScreenCaptureService.EXTRA_SCALE, 50)
            }

            startForegroundService(serviceIntent)

            tvCaptureStatus.text = "Capture: Running (${fps}fps, ${quality}%)"
            tvCaptureStatus.setTextColor(0xFF00FF88.toInt())
            btnStartCapture.isEnabled = false
            btnStopCapture.isEnabled = true
            addLog("Screen capture started: ${fps}fps, ${quality}% quality")
        } else {
            addLog("Screen capture permission denied")
        }
    }

    private fun stopScreenCapture() {
        stopService(Intent(this, ScreenCaptureService::class.java))
        tvCaptureStatus.text = "Capture: Stopped"
        tvCaptureStatus.setTextColor(0xFFFF4444.toInt())
        btnStartCapture.isEnabled = true
        btnStopCapture.isEnabled = false
        addLog("Screen capture stopped")
    }

    // ==================== ACCESSIBILITY ====================

    private fun openAccessibilitySettings() {
        val intent = Intent(Settings.ACTION_ACCESSIBILITY_SETTINGS)
        startActivity(intent)
        addLog("Opening Accessibility Settings...")
        Toast.makeText(this, "Find 'Antigravity Remote' and enable it", Toast.LENGTH_LONG).show()
    }

    private fun updateAccessibilityStatus() {
        val enabled = RemoteAccessibilityService.instance != null
        tvAccessibilityStatus.text = if (enabled) "Status: ENABLED" else "Status: Not Enabled"
        tvAccessibilityStatus.setTextColor(if (enabled) 0xFF00FF88.toInt() else 0xFFFF4444.toInt())
        btnEnableAccessibility.isEnabled = !enabled
    }

    private fun updateCaptureStatus() {
        val running = ScreenCaptureService.instance != null
        tvCaptureStatus.text = if (running) "Capture: Running" else "Capture: Stopped"
        tvCaptureStatus.setTextColor(if (running) 0xFF00FF88.toInt() else 0xFFFF4444.toInt())
        btnStartCapture.isEnabled = !running
        btnStopCapture.isEnabled = running
    }

    // ==================== UTILITY ====================

    private fun updateDeviceInfo() {
        val wm = applicationContext.getSystemService(Context.WIFI_SERVICE) as WifiManager
        val ip = Formatter.formatIpAddress(wm.connectionInfo.ipAddress)
        tvLocalIp.text = "Local IP: $ip"
        tvDeviceInfo.text = "Device: ${android.os.Build.MANUFACTURER} ${android.os.Build.MODEL} | Android ${android.os.Build.VERSION.RELEASE} | ${resources.displayMetrics.widthPixels}x${resources.displayMetrics.heightPixels}"
    }

    private fun addLog(msg: String) {
        val line = "[${java.text.SimpleDateFormat("HH:mm:ss", java.util.Locale.getDefault()).format(java.util.Date())}] $msg\n"
        tvLog.append(line)
        scrollLog.post { scrollLog.fullScroll(View.FOCUS_DOWN) }
    }

    private fun savePreferences() {
        getSharedPreferences(PREFS_NAME, MODE_PRIVATE).edit().apply {
            putString("server_ip", etServerIp.text.toString())
            putString("server_port", etServerPort.text.toString())
            apply()
        }
    }

    private fun loadPreferences() {
        val prefs = getSharedPreferences(PREFS_NAME, MODE_PRIVATE)
        etServerIp.setText(prefs.getString("server_ip", "192.168.1.100"))
        etServerPort.setText(prefs.getString("server_port", "8888"))
    }
}
