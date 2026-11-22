#!/usr/bin/env bash
# Usage: bash tools/phone_art_test.sh <phase>   -> installs Builds/Mottainai.apk, plays one autoplay match in a private room,
# screenshots 20 s into the match to Docs/art_v3/<phase>.png and prints the [PERF] / RESULT lines.
set -e
cd "$(dirname "$0")/.."
PH="${1:-Px}"
ADB="$(ls "/c/Program Files/Unity/Hub/Editor/"*/Editor/Data/PlaybackEngines/AndroidPlayer/SDK/platform-tools/adb.exe | head -1)"
PKG=com.supcom.mottainai
ROOM="AV$(( RANDOM % 9000 + 1000 ))"
"$ADB" install -r Builds/Mottainai.apk >/dev/null
ACT="$("$ADB" shell cmd package resolve-activity --brief $PKG | tail -1 | tr -d '\r')"
"$ADB" shell am force-stop --user 0 $PKG
"$ADB" logcat -c
"$ADB" shell am start -n "$ACT" -e unity "'-autoplay -room $ROOM -name ArtBot'" --user 0 >/dev/null
mkdir -p Docs/art_v3
shot=0
for i in $(seq 1 400); do
  L="$("$ADB" logcat -d -s Unity:I | grep -E "\[AUTOPLAY\] SHOT20|\[PERF\]|RESULT|FAIL" || true)"
  if [ $shot = 0 ] && echo "$L" | grep -q SHOT20; then "$ADB" exec-out screencap -p > "Docs/art_v3/$PH.png"; shot=1; echo "screenshot Docs/art_v3/$PH.png"; fi
  if echo "$L" | grep -qE "RESULT|FAIL"; then break; fi
  sleep 1
done
"$ADB" logcat -d | grep -E "\[PERF\]|\[AUTOPLAY\] RESULT|FAIL|Fatal signal" | sed 's/.*Unity *: //' | head -5
echo "room $ROOM"
