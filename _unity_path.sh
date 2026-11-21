#!/usr/bin/env bash
# Resolves $UNITY for the editor version in ProjectVersion.txt (override with UNITY_EXE).
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJ="$ROOT/Mottainai"
UVER="$(grep -m1 '^m_EditorVersion:' "$PROJ/ProjectSettings/ProjectVersion.txt" | awk '{print $2}' | tr -d '\r')"
if [ -n "$UNITY_EXE" ]; then UNITY="$UNITY_EXE"
else
  for c in "$HOME/Unity/Hub/Editor/$UVER/Editor/Unity" "/Applications/Unity/Hub/Editor/$UVER/Unity.app/Contents/MacOS/Unity" \
           "/c/Program Files/Unity/Hub/Editor/$UVER/Editor/Unity.exe" "/opt/unity/editors/$UVER/Editor/Unity"; do
    [ -x "$c" ] && UNITY="$c" && break
  done
  # Unity Hub custom install folder (Hub > Preferences > Installs)
  for f in "${APPDATA:-/nonexistent}/UnityHub/secondaryInstallPath.json" "$HOME/.config/UnityHub/secondaryInstallPath.json" "$HOME/Library/Application Support/UnityHub/secondaryInstallPath.json"; do
    [ -z "$UNITY" ] && [ -f "$f" ] || continue
    d="$(tr -d '"\r\n' < "$f" | sed 's#\\\\#/#g')"
    for c in "$d/$UVER/Editor/Unity.exe" "$d/$UVER/Editor/Unity" "$d/$UVER/Unity.app/Contents/MacOS/Unity"; do
      [ -n "$d" ] && [ -x "$c" ] && UNITY="$c" && break
    done
  done
fi
[ -z "$UNITY" ] && { echo "[Mottainai] Unity $UVER not found. Set UNITY_EXE."; exit 1; }
mkdir -p "$ROOT/Builds"
