package com.antigravity.remote

import android.accessibilityservice.AccessibilityService
import android.accessibilityservice.GestureDescription
import android.graphics.Path
import android.graphics.Rect
import android.os.Bundle
import android.util.Log
import android.view.accessibility.AccessibilityEvent
import android.view.accessibility.AccessibilityNodeInfo
import java.util.LinkedList

/**
 * Antigravity Remote Accessibility Service v2026
 *
 * Bu service telefondagi hamma narsani boshqarish imkonini beradi:
 * - Ekranda istalgan joyni bosish (click)
 * - Uzoq bosish (long press)
 * - Suring (swipe/scroll)
 * - Matn kiritish (type)
 * - Ekrandagi matnni o'qish
 * - Element topish va bosish (find by text)
 * - Input field topish va matn yozish
 * - Global actions (Back, Home, Recents)
 */
class RemoteAccessibilityService : AccessibilityService() {

    companion object {
        private const val TAG = "AG_Accessibility"

        @Volatile
        var instance: RemoteAccessibilityService? = null
            private set
    }

    // ==================== LIFECYCLE ====================

    override fun onServiceConnected() {
        super.onServiceConnected()
        instance = this
        Log.i(TAG, "Accessibility Service CONNECTED and ready!")
    }

    override fun onDestroy() {
        super.onDestroy()
        instance = null
        Log.i(TAG, "Accessibility Service DESTROYED")
    }

    override fun onAccessibilityEvent(event: AccessibilityEvent?) {
        // Events ni monitoring qilish (ixtiyoriy)
        // Biz asosan buyruqlar orqali ishlaymiz
    }

    override fun onInterrupt() {
        Log.w(TAG, "Accessibility Service interrupted")
    }

    // ==================== GESTURE: CLICK ====================

    /**
     * Berilgan koordinatada click (bosish) bajarish
     */
    fun performClick(x: Float, y: Float) {
        val path = Path()
        path.moveTo(x, y)

        val gesture = GestureDescription.Builder()
            .addStroke(GestureDescription.StrokeDescription(path, 0, 100))
            .build()

        dispatchGesture(gesture, object : GestureResultCallback() {
            override fun onCompleted(gestureDescription: GestureDescription?) {
                Log.d(TAG, "Click completed at ($x, $y)")
            }

            override fun onCancelled(gestureDescription: GestureDescription?) {
                Log.w(TAG, "Click cancelled at ($x, $y)")
            }
        }, null)
    }

    // ==================== GESTURE: LONG PRESS ====================

    /**
     * Uzoq bosish (long press)
     */
    fun performLongPress(x: Float, y: Float, duration: Long = 1000) {
        val path = Path()
        path.moveTo(x, y)

        val gesture = GestureDescription.Builder()
            .addStroke(GestureDescription.StrokeDescription(path, 0, duration))
            .build()

        dispatchGesture(gesture, object : GestureResultCallback() {
            override fun onCompleted(gestureDescription: GestureDescription?) {
                Log.d(TAG, "Long press completed at ($x, $y) for ${duration}ms")
            }
        }, null)
    }

    // ==================== GESTURE: SWIPE ====================

    /**
     * Suring (swipe)
     */
    fun performSwipe(startX: Float, startY: Float, endX: Float, endY: Float, duration: Long = 300) {
        val path = Path()
        path.moveTo(startX, startY)
        path.lineTo(endX, endY)

        val gesture = GestureDescription.Builder()
            .addStroke(GestureDescription.StrokeDescription(path, 0, duration))
            .build()

        dispatchGesture(gesture, object : GestureResultCallback() {
            override fun onCompleted(gestureDescription: GestureDescription?) {
                Log.d(TAG, "Swipe completed ($startX,$startY) -> ($endX,$endY)")
            }
        }, null)
    }

    // ==================== TEXT INPUT ====================

    /**
     * Hozirgi fokuslanagan joyga matn kiritish
     */
    fun performType(text: String) {
        val rootNode = rootInActiveWindow ?: return
        val focusedNode = findFocusedInput(rootNode)

        if (focusedNode != null) {
            val args = Bundle()
            args.putCharSequence(
                AccessibilityNodeInfo.ACTION_ARGUMENT_SET_TEXT_CHARSEQUENCE,
                text
            )
            focusedNode.performAction(AccessibilityNodeInfo.ACTION_SET_TEXT, args)
            Log.d(TAG, "Typed: $text")
            focusedNode.recycle()
        } else {
            // Fallback: clipboard orqali paste qilish
            pasteText(text)
        }
        rootNode.recycle()
    }

    /**
     * Avval tozalab keyin yozish
     */
    fun performClearAndType(text: String) {
        val rootNode = rootInActiveWindow ?: return
        val focusedNode = findFocusedInput(rootNode)

        if (focusedNode != null) {
            // Clear
            val clearArgs = Bundle()
            clearArgs.putCharSequence(
                AccessibilityNodeInfo.ACTION_ARGUMENT_SET_TEXT_CHARSEQUENCE,
                ""
            )
            focusedNode.performAction(AccessibilityNodeInfo.ACTION_SET_TEXT, clearArgs)

            // Then type
            val typeArgs = Bundle()
            typeArgs.putCharSequence(
                AccessibilityNodeInfo.ACTION_ARGUMENT_SET_TEXT_CHARSEQUENCE,
                text
            )
            focusedNode.performAction(AccessibilityNodeInfo.ACTION_SET_TEXT, typeArgs)
            Log.d(TAG, "Clear and typed: $text")
            focusedNode.recycle()
        }
        rootNode.recycle()
    }

    /**
     * Clipboard orqali paste qilish (fallback)
     */
    private fun pasteText(text: String) {
        val clipboard = getSystemService(CLIPBOARD_SERVICE) as android.content.ClipboardManager
        clipboard.setPrimaryClip(android.content.ClipData.newPlainText("ag", text))

        val rootNode = rootInActiveWindow ?: return
        val focusedNode = findFocusedInput(rootNode)
        focusedNode?.performAction(AccessibilityNodeInfo.ACTION_PASTE)
        focusedNode?.recycle()
        rootNode.recycle()
    }

    // ==================== KEY EVENTS ====================

    /**
     * Global action bajarish (Back, Home, Recents va h.k.)
     */
    override fun performGlobalAction(action: Int): Boolean {
        Log.d(TAG, "Global action: $action")
        return super.performGlobalAction(action)
    }

    /**
     * Klaviatura tugmasi simulyatsiya qilish
     */
    fun performKeyPress(keyCode: Int) {
        // Accessibility orqali key event yuborish cheklangan
        // Shuning uchun ENTER uchun focused node da ACTION_CLICK ishlatamiz
        when (keyCode) {
            66 -> { // ENTER
                val rootNode = rootInActiveWindow ?: return
                val focused = rootNode.findFocus(AccessibilityNodeInfo.FOCUS_INPUT)
                focused?.performAction(AccessibilityNodeInfo.ACTION_CLICK)
                focused?.recycle()
                rootNode.recycle()
            }
            else -> {
                Log.d(TAG, "Key press: $keyCode (limited support)")
            }
        }
    }

    // ==================== SCREEN TEXT READING ====================

    /**
     * Ekrandagi barcha matnlarni o'qish (BFS traversal)
     */
    fun readScreenText(): String {
        val rootNode = rootInActiveWindow ?: return ""
        val textBuilder = StringBuilder()
        val queue: LinkedList<AccessibilityNodeInfo> = LinkedList()
        queue.add(rootNode)

        val visited = mutableSetOf<Int>()

        while (queue.isNotEmpty()) {
            val node = queue.poll() ?: continue

            val nodeHash = System.identityHashCode(node)
            if (nodeHash in visited) {
                node.recycle()
                continue
            }
            visited.add(nodeHash)

            // Matnni qo'shish
            val nodeText = node.text?.toString() ?: ""
            val contentDesc = node.contentDescription?.toString() ?: ""
            val hintText = node.hintText?.toString() ?: ""

            if (nodeText.isNotEmpty()) textBuilder.append(nodeText).append(" | ")
            if (contentDesc.isNotEmpty() && contentDesc != nodeText) textBuilder.append(contentDesc).append(" | ")
            if (hintText.isNotEmpty()) textBuilder.append("[hint:$hintText] | ")

            // Bolalar
            for (i in 0 until node.childCount) {
                val child = node.getChild(i)
                if (child != null) queue.add(child)
            }

            if (node != rootNode) node.recycle()
        }

        rootNode.recycle()
        return textBuilder.toString().trim()
    }

    // ==================== FIND AND CLICK ====================

    /**
     * Berilgan matn bo'yicha elementni topib bosish
     */
    fun findAndClick(text: String): Boolean {
        val rootNode = rootInActiveWindow ?: return false

        // 1. text bo'yicha izlash
        var nodes = rootNode.findAccessibilityNodeInfosByText(text)
        if (nodes.isNotEmpty()) {
            for (node in nodes) {
                if (node.isClickable) {
                    node.performAction(AccessibilityNodeInfo.ACTION_CLICK)
                    Log.d(TAG, "Clicked on: '$text' (direct)")
                    nodes.forEach { it.recycle() }
                    rootNode.recycle()
                    return true
                }
                // Parent ni tekshirish
                var parent = node.parent
                var depth = 0
                while (parent != null && depth < 6) {
                    if (parent.isClickable) {
                        parent.performAction(AccessibilityNodeInfo.ACTION_CLICK)
                        Log.d(TAG, "Clicked on parent of: '$text'")
                        nodes.forEach { it.recycle() }
                        rootNode.recycle()
                        return true
                    }
                    val grandParent = parent.parent
                    parent.recycle()
                    parent = grandParent
                    depth++
                }
            }
            nodes.forEach { it.recycle() }
        }

        // 2. Content description bo'yicha izlash
        val found = findNodeByContentDescription(rootNode, text)
        if (found != null) {
            if (found.isClickable) {
                found.performAction(AccessibilityNodeInfo.ACTION_CLICK)
            } else {
                // Coordinate bo'yicha click
                val rect = Rect()
                found.getBoundsInScreen(rect)
                performClick(rect.centerX().toFloat(), rect.centerY().toFloat())
            }
            found.recycle()
            rootNode.recycle()
            return true
        }

        // 3. View ID bo'yicha
        val byId = findNodeByViewId(rootNode, text)
        if (byId != null) {
            val rect = Rect()
            byId.getBoundsInScreen(rect)
            performClick(rect.centerX().toFloat(), rect.centerY().toFloat())
            byId.recycle()
            rootNode.recycle()
            return true
        }

        rootNode.recycle()
        Log.w(TAG, "Element not found: '$text'")
        return false
    }

    // ==================== FIND FIELD AND TYPE ====================

    /**
     * Hint text bo'yicha input field topib, unga matn yozish
     */
    fun findFieldAndType(hint: String, value: String): Boolean {
        val rootNode = rootInActiveWindow ?: return false

        // Hint bo'yicha EditText izlash
        val editTexts = findAllEditTexts(rootNode)
        for (editText in editTexts) {
            val nodeHint = editText.hintText?.toString() ?: ""
            val nodeText = editText.text?.toString() ?: ""
            val nodeDesc = editText.contentDescription?.toString() ?: ""
            val viewId = editText.viewIdResourceName ?: ""

            val combined = "$nodeHint $nodeText $nodeDesc $viewId".lowercase()

            if (combined.contains(hint.lowercase())) {
                // Fokusni olish
                editText.performAction(AccessibilityNodeInfo.ACTION_FOCUS)
                editText.performAction(AccessibilityNodeInfo.ACTION_CLICK)

                // Matn yozish
                val args = Bundle()
                args.putCharSequence(
                    AccessibilityNodeInfo.ACTION_ARGUMENT_SET_TEXT_CHARSEQUENCE,
                    value
                )
                editText.performAction(AccessibilityNodeInfo.ACTION_SET_TEXT, args)

                Log.d(TAG, "Found field '$hint' and typed '$value'")
                editTexts.forEach { it.recycle() }
                rootNode.recycle()
                return true
            }
        }

        editTexts.forEach { it.recycle() }

        // Fallback: Text label yonidagi keyingi editable ni izlash
        val label = rootNode.findAccessibilityNodeInfosByText(hint)
        if (label.isNotEmpty()) {
            val labelNode = label[0]
            val parent = labelNode.parent
            if (parent != null) {
                for (i in 0 until parent.childCount) {
                    val sibling = parent.getChild(i) ?: continue
                    if (sibling.isEditable || sibling.className?.toString()?.contains("EditText") == true) {
                        sibling.performAction(AccessibilityNodeInfo.ACTION_FOCUS)
                        sibling.performAction(AccessibilityNodeInfo.ACTION_CLICK)
                        val args = Bundle()
                        args.putCharSequence(
                            AccessibilityNodeInfo.ACTION_ARGUMENT_SET_TEXT_CHARSEQUENCE,
                            value
                        )
                        sibling.performAction(AccessibilityNodeInfo.ACTION_SET_TEXT, args)
                        sibling.recycle()
                        parent.recycle()
                        label.forEach { it.recycle() }
                        rootNode.recycle()
                        return true
                    }
                    sibling.recycle()
                }
                parent.recycle()
            }
            label.forEach { it.recycle() }
        }

        rootNode.recycle()
        return false
    }

    // ==================== CHECK ELEMENT EXISTS ====================

    /**
     * Berilgan matn yoki ID ga ega element borligini tekshirish
     */
    fun checkElementExists(textOrId: String): Boolean {
        val rootNode = rootInActiveWindow ?: return false
        val nodes = rootNode.findAccessibilityNodeInfosByText(textOrId)
        val exists = nodes.isNotEmpty()
        nodes.forEach { it.recycle() }
        rootNode.recycle()
        return exists
    }

    // ==================== HELPER TRAVERSAL METHODS ====================

    /**
     * Fokuslanagan input field ni topish
     */
    private fun findFocusedInput(root: AccessibilityNodeInfo): AccessibilityNodeInfo? {
        // INPUT_FOCUS orqali
        val focused = root.findFocus(AccessibilityNodeInfo.FOCUS_INPUT)
        if (focused != null && focused.isEditable) return focused
        focused?.recycle()

        // Accessibility FOCUS orqali
        val a11yFocused = root.findFocus(AccessibilityNodeInfo.FOCUS_ACCESSIBILITY)
        if (a11yFocused != null && a11yFocused.isEditable) return a11yFocused
        a11yFocused?.recycle()

        // DFS orqali birinchi editable ni topish
        return findFirstEditable(root)
    }

    /**
     * Birinchi editable elementni topish (DFS)
     */
    private fun findFirstEditable(node: AccessibilityNodeInfo): AccessibilityNodeInfo? {
        if (node.isEditable && node.isFocused) return AccessibilityNodeInfo.obtain(node)

        for (i in 0 until node.childCount) {
            val child = node.getChild(i) ?: continue
            val result = findFirstEditable(child)
            if (result != null) {
                child.recycle()
                return result
            }
            child.recycle()
        }
        return null
    }

    /**
     * ContentDescription bo'yicha node topish
     */
    private fun findNodeByContentDescription(root: AccessibilityNodeInfo, desc: String): AccessibilityNodeInfo? {
        val queue: LinkedList<AccessibilityNodeInfo> = LinkedList()
        queue.add(root)

        while (queue.isNotEmpty()) {
            val node = queue.poll() ?: continue
            val nodeDesc = node.contentDescription?.toString() ?: ""
            if (nodeDesc.contains(desc, ignoreCase = true)) {
                return AccessibilityNodeInfo.obtain(node)
            }
            for (i in 0 until node.childCount) {
                val child = node.getChild(i)
                if (child != null) queue.add(child)
            }
        }
        return null
    }

    /**
     * View ID bo'yicha node topish
     */
    private fun findNodeByViewId(root: AccessibilityNodeInfo, viewId: String): AccessibilityNodeInfo? {
        val queue: LinkedList<AccessibilityNodeInfo> = LinkedList()
        queue.add(root)

        while (queue.isNotEmpty()) {
            val node = queue.poll() ?: continue
            val nodeId = node.viewIdResourceName ?: ""
            if (nodeId.contains(viewId, ignoreCase = true)) {
                return AccessibilityNodeInfo.obtain(node)
            }
            for (i in 0 until node.childCount) {
                val child = node.getChild(i)
                if (child != null) queue.add(child)
            }
        }
        return null
    }

    /**
     * Hammma EditText/input elementlarni topish
     */
    private fun findAllEditTexts(root: AccessibilityNodeInfo): List<AccessibilityNodeInfo> {
        val result = mutableListOf<AccessibilityNodeInfo>()
        val queue: LinkedList<AccessibilityNodeInfo> = LinkedList()
        queue.add(root)

        while (queue.isNotEmpty()) {
            val node = queue.poll() ?: continue
            val className = node.className?.toString() ?: ""
            if (node.isEditable || className.contains("EditText") || className.contains("TextInput")) {
                result.add(AccessibilityNodeInfo.obtain(node))
            }
            for (i in 0 until node.childCount) {
                val child = node.getChild(i)
                if (child != null) queue.add(child)
            }
        }
        return result
    }
}
