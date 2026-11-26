# CU Admin (Android)

Minimal WebView wrapper that opens the store admin (https://chic-unique.runasp.net/Admin/Dashboard)
full-screen with the existing admin login. Only permission: INTERNET. HTTPS only.

## Build
Needs JDK 11+ and Android SDK (platform 33, build-tools 33.0.2).

    echo "sdk.dir=/path/to/android-sdk" > local.properties
    gradle assembleDebug   # APK lands in app/build/outputs/apk/debug/

The prebuilt debug-signed APK for sideloading is in ../dist/cu-admin-1.0.apk.
For a Play Store release later, generate an upload key and sign a release build.
