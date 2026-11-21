#!/usr/bin/env bash
set -e
source "$(dirname "$0")/_unity_path.sh"
echo "[Mottainai] Building Android APK with $UNITY (close the editor first)"
"$UNITY" -batchmode -quit -projectPath "$PROJ" -executeMethod Mottainai.EditorTools.BuildTools.BuildAndroid -logFile "$ROOT/Builds/build_android.log" "$@"
echo "[Mottainai] OK: Builds/Mottainai.apk  (adb install -r ...)"
