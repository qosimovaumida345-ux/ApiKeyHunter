package com.antigravity.remote

import android.app.Activity
import android.app.Notification
import android.app.NotificationChannel
import android.app.NotificationManager
import android.app.Service
import android.content.Context
import android.content.Intent
import android.graphics.Bitmap
import android.graphics.PixelFormat
import android.hardware.display.DisplayManager
import android.hardware.display.VirtualDisplay
import android.media.Image
import android.media.ImageReader
import android.media.projection.MediaProjection
import android.media.projection.MediaProjectionManager
import android.os.Build
import android.os.Handler
import android.os.IBinder
import android.os.Looper
import android.util.DisplayMetrics
import android.util.Log
import android.view.WindowManager
import java.io.ByteArrayOutputStream
import java.util.Timer
import java.util.TimerTask

/**
 * Antigravity Screen Capture Service v2026
 *
 * MediaProjection API orqali telefonning ekranini real-vaqtda
 * JPEG formatida suratga olib, WebSocket orqali PC ga yuboradi.
 *
 * Xususiyatlari:
 * - Sozlanuvchi FPS (1-30)
 * - Sozlanuvchi sifat (10-100 JPEG quality)
 * - Sozlanuvchi resolution (50%-100%)
 * - Foreground Service sifatida ishlaydi (Android 10+ talabi)
 * - Low-latency mode
 */
class ScreenCaptureService : Service() {

    companion object {
        private const val TAG = "AG_ScreenCapture"
        private const val NOTIFICATION_CHANNEL_ID = "antigravity_capture"
        private const val NOTIFICATION_ID = 1001

        const val EXTRA_RESULT_CODE = "result_code"
        const val EXTRA_RESULT_DATA = "result_data"
        const val EXTRA_FPS = "fps"
        const val EXTRA_QUALITY = "quality"
        const val EXTRA_SCALE = "scale"

        @Volatile
        var instance: ScreenCaptureService? = null
            private set

        var mediaProjectionIntent: Intent? = null
        var resultCode: Int = 0
    }

    // Display
    private var screenWidth = 1080
    private var screenHeight = 2400
    private var screenDensity = 440

    // Capture config
    private var targetFps = 10
    private var jpegQuality = 50
    private var scalePercent = 50

    // MediaProjection
    private var mediaProjection: MediaProjection? = null
    private var virtualDisplay: VirtualDisplay? = null
    private var imageReader: ImageReader? = null

    // Timer for frame capture
    private var captureTimer: Timer? = null
    private val mainHandler = Handler(Looper.getMainLooper())

    // Stats
    private var frameCount = 0L
    private var totalBytesSent = 0L
    private var lastFpsTime = System.currentTimeMillis()
    private var currentFps = 0.0

    // Frame capture flag
    @Volatile
    private var captureRequested = false

    override fun onBind(intent: Intent?): IBinder? = null

    override fun onCreate() {
        super.onCreate()
        instance = this
        createNotificationChannel()
        Log.i(TAG, "ScreenCaptureService created")
    }

    override fun onStartCommand(intent: Intent?, flags: Int, startId: Int): Int {
        if (intent == null) {
            stopSelf()
            return START_NOT_STICKY
        }

        // Config
        targetFps = intent.getIntExtra(EXTRA_FPS, 10).coerceIn(1, 30)
        jpegQuality = intent.getIntExtra(EXTRA_QUALITY, 50).coerceIn(10, 100)
        scalePercent = intent.getIntExtra(EXTRA_SCALE, 50).coerceIn(25, 100)

        val rc = intent.getIntExtra(EXTRA_RESULT_CODE, 0)
        val rd = if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.TIRAMISU) {
            intent.getParcelableExtra(EXTRA_RESULT_DATA, Intent::class.java)
        } else {
            @Suppress("DEPRECATION")
            intent.getParcelableExtra(EXTRA_RESULT_DATA)
        }

        if (rc == 0 || rd == null) {
            Log.e(TAG, "Invalid MediaProjection data")
            stopSelf()
            return START_NOT_STICKY
        }

        // Start foreground
        val notification = buildNotification()
        startForeground(NOTIFICATION_ID, notification)

        // Get screen metrics
        val wm = getSystemService(Context.WINDOW_SERVICE) as WindowManager
        val metrics = DisplayMetrics()
        wm.defaultDisplay.getRealMetrics(metrics)
        screenWidth = metrics.widthPixels
        screenHeight = metrics.heightPixels
        screenDensity = metrics.densityDpi

        // Start capture
        startCapture(rc, rd)

        Log.i(TAG, "Screen capture started: ${screenWidth}x${screenHeight} @ ${targetFps}fps, quality=$jpegQuality%, scale=$scalePercent%")
        return START_STICKY
    }

    override fun onDestroy() {
        stopCapture()
        instance = null
        super.onDestroy()
        Log.i(TAG, "ScreenCaptureService destroyed. Frames sent: $frameCount, Total: ${totalBytesSent / 1024}KB")
    }

    // ==================== CAPTURE LOGIC ====================

    private fun startCapture(resultCode: Int, resultData: Intent) {
        val projectionManager = getSystemService(Context.MEDIA_PROJECTION_SERVICE) as MediaProjectionManager
        mediaProjection = projectionManager.getMediaProjection(resultCode, resultData)

        if (mediaProjection == null) {
            Log.e(TAG, "Failed to create MediaProjection")
            stopSelf()
            return
        }

        // Register callback for state changes
        mediaProjection?.registerCallback(object : MediaProjection.Callback() {
            override fun onStop() {
                Log.w(TAG, "MediaProjection stopped by system")
                stopCapture()
                stopSelf()
            }
        }, mainHandler)

        // Calculate capture dimensions
        val captureWidth = (screenWidth * scalePercent / 100)
        val captureHeight = (screenHeight * scalePercent / 100)

        // Create ImageReader
        imageReader = ImageReader.newInstance(captureWidth, captureHeight, PixelFormat.RGBA_8888, 2)

        // Create VirtualDisplay
        virtualDisplay = mediaProjection?.createVirtualDisplay(
            "AntigravityCapture",
            captureWidth,
            captureHeight,
            screenDensity,
            DisplayManager.VIRTUAL_DISPLAY_FLAG_AUTO_MIRROR,
            imageReader?.surface,
            null,
            mainHandler
        )

        // Start frame capture timer
        val intervalMs = (1000 / targetFps).toLong()
        captureTimer = Timer("FrameCapture").apply {
            scheduleAtFixedRate(object : TimerTask() {
                override fun run() {
                    captureFrame()
                }
            }, 0, intervalMs)
        }
    }

    private fun stopCapture() {
        captureTimer?.cancel()
        captureTimer = null
        virtualDisplay?.release()
        virtualDisplay = null
        imageReader?.close()
        imageReader = null
        mediaProjection?.stop()
        mediaProjection = null
    }

    /**
     * Bitta kadrni olish va WebSocket orqali yuborish
     */
    private fun captureFrame() {
        val reader = imageReader ?: return
        var image: Image? = null

        try {
            image = reader.acquireLatestImage() ?: return

            val planes = image.planes
            val buffer = planes[0].buffer
            val pixelStride = planes[0].pixelStride
            val rowStride = planes[0].rowStride
            val rowPadding = rowStride - pixelStride * image.width

            // Bitmap yaratish
            val bitmap = Bitmap.createBitmap(
                image.width + rowPadding / pixelStride,
                image.height,
                Bitmap.Config.ARGB_8888
            )
            bitmap.copyPixelsFromBuffer(buffer)

            // Crop (agar padding bo'lsa)
            val croppedBitmap = if (rowPadding > 0) {
                Bitmap.createBitmap(bitmap, 0, 0, image.width, image.height)
            } else {
                bitmap
            }

            // JPEG ga compress qilish
            val outputStream = ByteArrayOutputStream()
            croppedBitmap.compress(Bitmap.CompressFormat.JPEG, jpegQuality, outputStream)
            val jpegData = outputStream.toByteArray()

            // WebSocket orqali yuborish
            val wsClient = AntigravityWebSocketClient.instance
            if (wsClient != null && wsClient.isOpen) {
                wsClient.sendScreenFrame(jpegData)
                frameCount++
                totalBytesSent += jpegData.size

                // FPS hisoblash
                val now = System.currentTimeMillis()
                if (now - lastFpsTime >= 1000) {
                    currentFps = frameCount.toDouble() / ((now - lastFpsTime) / 1000.0)
                    lastFpsTime = now
                    frameCount = 0
                }
            }

            // Cleanup
            if (croppedBitmap != bitmap) croppedBitmap.recycle()
            bitmap.recycle()

        } catch (e: Exception) {
            Log.e(TAG, "Frame capture error: ${e.message}")
        } finally {
            image?.close()
        }
    }

    /**
     * Bir martalik screenshot (on-demand)
     */
    fun captureNow() {
        captureRequested = true
        captureFrame()
        captureRequested = false
    }

    // ==================== NOTIFICATION ====================

    private fun createNotificationChannel() {
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
            val channel = NotificationChannel(
                NOTIFICATION_CHANNEL_ID,
                "Screen Capture",
                NotificationManager.IMPORTANCE_LOW
            ).apply {
                description = "Antigravity remote screen capture"
                setShowBadge(false)
            }

            val manager = getSystemService(NotificationManager::class.java)
            manager.createNotificationChannel(channel)
        }
    }

    private fun buildNotification(): Notification {
        val builder = if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
            Notification.Builder(this, NOTIFICATION_CHANNEL_ID)
        } else {
            @Suppress("DEPRECATION")
            Notification.Builder(this)
        }

        return builder
            .setContentTitle("Antigravity Remote")
            .setContentText("Screen capture active")
            .setSmallIcon(android.R.drawable.ic_menu_camera)
            .setOngoing(true)
            .build()
    }

    // ==================== PUBLIC API ====================

    fun updateConfig(fps: Int? = null, quality: Int? = null, scale: Int? = null) {
        fps?.let { targetFps = it.coerceIn(1, 30) }
        quality?.let { jpegQuality = it.coerceIn(10, 100) }
        scale?.let { scalePercent = it.coerceIn(25, 100) }

        // Restart capture with new settings
        if (mediaProjection != null) {
            stopCapture()
            // Note: Need to re-request MediaProjection permission
        }
    }

    fun getStats(): Map<String, Any> {
        return mapOf(
            "fps" to currentFps,
            "frameCount" to frameCount,
            "totalBytesSent" to totalBytesSent,
            "resolution" to "${screenWidth * scalePercent / 100}x${screenHeight * scalePercent / 100}",
            "quality" to jpegQuality
        )
    }
}
