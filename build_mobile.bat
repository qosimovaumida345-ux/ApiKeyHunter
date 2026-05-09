@echo off
echo ====================================================
echo    ANTIGRAVITY APK BUILDER v2026
echo ====================================================
cd AndroidApp
echo [*] Checking Environment...
java -version >nul 2>&1
if %errorlevel% neq 0 (
    echo [!] ERROR: Java (JDK 17+) o'rnatilmagan!
    echo Iltimos, JDK 17 o'rnating va qayta urining.
    pause
    exit /b
)

echo [*] Initializing Gradle...
call gradlew.bat clean

echo [*] Building Antigravity Remote APK (Debug)...
call gradlew.bat assembleDebug

if %errorlevel% equ 0 (
    echo ====================================================
    echo [SUCCESS] APK tayyor! 
    echo Manzil: AndroidApp\app\build\outputs\apk\debug\app-debug.apk
    echo ====================================================
    start AndroidApp\app\build\outputs\apk\debug\
) else (
    echo [!] Build xatosi yuz berdi.
)
pause
