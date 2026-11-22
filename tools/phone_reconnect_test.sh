#!/usr/bin/env bash
# Phase C phone test (1 phone + a Node teammate that hosts and watches from the server side).
# Usage: bash tools/phone_reconnect_test.sh [airplane|wifi2data|lock|all] [seconds]
#   airplane : airplane mode ON 10 s at +8 s, then OFF                      (link dies silently, reconnect after)
#   wifi2data: Wi-Fi OFF at +8 s (mobile data must be ON), back ON at +25 s  (network switch mid-match)
#   lock     : screen OFF at +8 s for 20 s, then wakes it (unlock it by hand if a PIN is set)
#   all      : airplane at +8 s, wifi2data at +35 s, lock at +60 s
# Output: Node timeline (PAUSE / RESUME), phone [NET] lines, screenshots in Docs/v4/reconnect_*.png
set -e
cd "$(dirname "$0")/.."
MODE="${1:-airplane}"; SECS="${2:-75}"
ADB="$(ls "/c/Program Files/Unity/Hub/Editor/"*/Editor/Data/PlaybackEngines/AndroidPlayer/SDK/platform-tools/adb.exe | head -1)"
PKG=com.supcom.mottainai
ROOM="PR$(( RANDOM % 9000 + 1000 ))"
mkdir -p Docs/v4
[ "$NO_INSTALL" = 1 ] || "$ADB" install -r Builds/Mottainai.apk >/dev/null
ACT="$("$ADB" shell cmd package resolve-activity --brief $PKG | tail -1 | tr -d '\r')"
"$ADB" shell am force-stop --user 0 $PKG
"$ADB" logcat -c
( cd server && E2E_ROOM=$ROOM E2E_MODE=watch E2E_SECONDS=$SECS node tools/reconnect.ts T10 > ../Builds/phone_reconnect_node.txt 2>&1 ) &
NODE=$!
sleep 2
"$ADB" shell am start -n "$ACT" -e unity "'-autoplay -room $ROOM -name Phone -role 3'" --user 0 >/dev/null
echo "room $ROOM - waiting for the match"
for i in $(seq 1 120); do "$ADB" logcat -d -s Unity:I | grep -q "AUTOPLAY\] screen=Game\|SHOT20" && break; sleep 1; done
sleep 4
shot() { "$ADB" exec-out screencap -p > "Docs/v4/reconnect_$1.png"; echo "screenshot Docs/v4/reconnect_$1.png"; }
airplane() {
  # Samsung keeps Wi-Fi ON in airplane mode ("Wi-Fi stays on"): turn Wi-Fi off too so every link really dies
  echo "$(date +%T) airplane ON + Wi-Fi OFF"; "$ADB" shell cmd connectivity airplane-mode enable; "$ADB" shell svc wifi disable
  sleep 3; shot airplane_on; sleep 7
  echo "$(date +%T) airplane OFF + Wi-Fi ON"; "$ADB" shell cmd connectivity airplane-mode disable; "$ADB" shell svc wifi enable
  sleep 6; shot airplane_back
}
wifi2data() {
  echo "$(date +%T) Wi-Fi OFF (switch to mobile data)"; "$ADB" shell svc wifi disable
  sleep 8; shot wifi_off
  echo "$(date +%T) Wi-Fi ON"; "$ADB" shell svc wifi enable; sleep 8
}
lock() {
  echo "$(date +%T) screen OFF"; "$ADB" shell input keyevent KEYCODE_SLEEP
  sleep 20
  echo "$(date +%T) screen ON (unlock now if a PIN is set)"; "$ADB" shell input keyevent KEYCODE_WAKEUP; "$ADB" shell wm dismiss-keyguard >/dev/null 2>&1 || true
  sleep 8; shot lock_back
}
case "$MODE" in
  airplane) airplane ;;
  wifi2data) wifi2data ;;
  lock) lock ;;
  all) airplane; sleep 10; wifi2data; sleep 8; lock ;;
esac
wait $NODE || true
echo "---- server-side timeline (Node teammate)"; grep -v "^\s*$" Builds/phone_reconnect_node.txt
echo "---- phone"; "$ADB" logcat -d -s Unity:I | grep -E "\[NET\]|Exception" | sed 's/.*Unity *: //' | head -20
"$ADB" shell svc wifi enable >/dev/null 2>&1 || true; "$ADB" shell cmd connectivity airplane-mode disable >/dev/null 2>&1 || true
