# Phase E [MAX] — low-latency voice chat per team (mic ON/OFF) + 5G SA check

Part of `UPDATE_V4.md` (same branch `feature/v4`, same rules: no game-engine or graphics changes, v4 Worker only, protocol v3).
Latency and jitter are the whole point of the hackathon: every choice minimizes **mouth-to-ear latency** and **jitter**. If a choice trades latency against bandwidth, CPU or simplicity, pick latency and log it in DECISIONS.md.
The whole feature sits behind `voiceEnabled`: a client Settings toggle plus a server kill switch. When the flag is off, the game behaves exactly as before.

## E0. Quick 5G/latency check (report, then fix BUG items only)
The dev merge already added: WsClient DisableNagle + crash guard, socket-thread RTT, adaptive jitter buffer, drift-free DO loop (see HANDOFF.md). Verify these work on the phone, and check:
- `NetInfoPlugin.java`:
  - 5G SA = `getDataNetworkType()==NR`; dual-SIM → use `createForSubscriptionId(getDefaultDataSubscriptionId())`;
  - the network type updates after READ_PHONE_STATE is granted, without restart, and on network change via callback;
  - latency slice (API 33+): Android 14 requires the `android.net.PROPERTY_SELF_CERTIFIED_NETWORK_CAPABILITIES` manifest property + XML, added through the Gradle patcher; retry once on network change.
- Voice traffic must not inflate `backlogMs`, which triggers the server's 60 → 30 Hz fallback.

## E1. Features
- **Mic ON/OFF button** on the HUD (top-right, next to timer/menu; keep the 5-widget rule) and in the lobby. Default OFF, saved in PlayerPrefs, restored after reconnect.
- ON = open mic with voice-activity detection: noise gate + ~200 ms hangover, sensitivity slider in Settings. Only voiced frames are sent (Opus DTX).
- OFF = `AudioRecord` really stopped (Android mic indicator off), zero voice bytes sent.
- `RECORD_AUDIO` is requested the first time the player turns the mic ON. Denied → toast, mic stays OFF. Add the permission through the Gradle patcher.
- Per-teammate mute + master voice volume (local). Speaking indicator next to each teammate's role/name. Music/SFX ducked −8 dB while someone speaks.
- Voice only between the humans of the same room team, in the lobby and in the match. Never echoed to the speaker. Bots ignored.
- App in background → capture paused; resumed on return if the mic was ON. During a PAUSE (Phase C) voice keeps working, so players can talk while waiting.

## E2. Low-latency design
- **Capture (Java plugin):**
  - `AudioRecord`, source `VOICE_COMMUNICATION`, mono, native output rate (usually 48 kHz, no resampling), smallest valid buffer.
  - `AcousticEchoCanceler` + `NoiseSuppressor` + `AutomaticGainControl` when available (phones use the loudspeaker).
  - `MODE_IN_COMMUNICATION` while the mic is ON.
  - 20 ms frames into a lock-free ring buffer, never through the Unity main thread.
  - Editor/desktop fallback: Unity `Microphone`.
- **Codec:** Opus via **Concentus** (pure C#, IL2CPP-safe; DLL in `Plugins/` + `link.xml`). VOIP mode, 20 ms frames (test 10 ms on 5G SA), 24 kbps, DTX on, complexity 5, FEC off (TCP). Encoding on a worker thread.
- **Uplink:** one message per frame, sent immediately. Free-plan math: ~50 msg/s per talker ≈ 2.5 billed req/s, ~600 per 2-min match with 2 talkers; log it. Batching (1–2 frames/msg) is a tuning value, default 1.
- **Protocol v3 additions** (document them in PROTOCOL.md):
  - C2S `0x30 VOICE` {u16 seq, u32 captureMs, u8 n, n×(u8 len, bytes)}
  - C2S `0x31 VOICE_STATE` {u8 micOn}
  - C2S `0x32 VOICE_ECHO` (same as VOICE; sent back to the sender only)
  - S2C `0x88 VOICE` {u8 speakerId, u16 seq, u32 captureMs, u8 n, frames}
  - S2C `0x89 VOICE_STATE` {u8 playerId, u8 micOn, u8 talking}
- **Server = pure relay, no decoding.** Forward to the sender's teammates right away, before any snapshot work. Limits per player: ≤ 60 msg/s and ≤ 400 B/msg; drop and count the excess. Kill switch in the v4 server config.
- **Receive:**
  - `WsClient` routes 0x88/0x89 to a separate voice queue, handled off the main thread; decoding on a worker thread.
  - One adaptive jitter buffer per speaker: target = clamp(p95 inter-arrival jitter + 1 frame, 30, 120 ms), smoothed.
  - Above target + 2 frames → drop the oldest frames (TCP bursts must never add up to more latency).
  - Gaps → Opus PLC (max 3 frames), then silence.
  - Reset at each new talk spurt after 500 ms of silence.
- **Playback:** `OnAudioFilterRead` streaming mix (no AudioClip allocations), DSP buffer "Best latency", read back the real output latency, resample once if needed. FPS ≥ 55 with both phones talking.

## E3. Measurement (for the jury)
- Net HUD debug panel, Voice section: mic state, slice status, voice up/down kbps, jitter-buffer target/current ms, late/dropped/concealed %, **estimated mouth-to-ear** (capture + encode + RTT/2 + jitter buffer + output latency).
- Network Lab "Voice loopback" via `VOICE_ECHO`: 10 s of generated chirp → avg/p95/jitter mouth-to-ear. New CSV columns `voiceE2eAvg, voiceE2eP95, voiceJitter, voiceLossPct` in TelemetryDO.

## E4. Acceptance
| # | Test | Pass |
|---|---|---|
| V1 | 2–3 phones, Wi-Fi OFF, 5G SA, mics ON, full match | clear audio, mouth-to-ear p95 < 150 ms (target ≤ 100), no drift (buffer = target ± 2 frames all match) |
| V2 | 5G NSA / LTE | works, p95 < 200 ms, logged in the CSV |
| V3 | loudspeaker, phones 1 m apart | no echo/feedback |
| V4 | mic OFF | 0 voice bytes, Android mic indicator off |
| V5 | permission denied | toast, mic OFF, game OK |
| V6 | during V1 | snapshots 60 Hz, FPS ≥ 55, RTT within ±5 ms of voice-off |
| V7 | reconnect mid-match (Phase C) | mic state restored, voice back in < 2 s |
| V8 | `voiceEnabled=false` | game identical to before |
| V9 | another room's team | never hears this team |
