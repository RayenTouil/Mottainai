#!/usr/bin/env bash
# Builds Builds/Mottainai.apk from a mirrored copy of the project (.bc/Mottainai) so the Unity editor can stay open.
# First run imports the copy (slow, one time); later runs are incremental. Usage: tools/build_copy.sh [-dev]
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
SRC="$ROOT/Mottainai"; DST="$ROOT/.bc/Mottainai"
UVER="$(grep -m1 '^m_EditorVersion:' "$SRC/ProjectSettings/ProjectVersion.txt" | awk '{print $2}' | tr -d '\r')"
UNITY="${UNITY_EXE:-/c/Program Files/Unity/Hub/Editor/$UVER/Editor/Unity.exe}"
mkdir -p "$DST" "$ROOT/Builds"
for d in Assets Packages ProjectSettings; do
  robocopy "$(cygpath -w "$SRC/$d")" "$(cygpath -w "$DST/$d")" //MIR //NFL //NDL //NJH //NJS //NP //R:1 //W:1 > /dev/null
  [ $? -ge 8 ] && { echo "robocopy $d failed"; exit 1; }
done
LOG="$ROOT/Builds/build_copy.log"
echo "[build_copy] Unity $UVER -> Builds/Mottainai.apk (log: Builds/build_copy.log)"
"$UNITY" -batchmode -quit -projectPath "$(cygpath -w "$DST")" -buildTarget Android -executeMethod Mottainai.EditorTools.BuildTools.BuildAndroid \
  -out "$(cygpath -w "$ROOT/Builds/Mottainai.apk")" -logFile "$(cygpath -w "$LOG")" "$@"
st=$?
grep -E "\[BuildTools\]|error CS|Build Finished|BuildFailedException" "$LOG" | tail -5
[ $st -eq 0 ] && echo "[build_copy] OK" || echo "[build_copy] FAILED ($st)"
exit $st
