using System;
using System.Collections.Generic;
using System.Threading;
using Concentus.Enums;
using Concentus.Structs;
using Mottainai.Net;
using UnityEngine;

namespace Mottainai.Client
{
    /// Music/SFX gain while a teammate talks (-8 dB).
    public static class VoiceDuck { public static float Gain = 1f; }

    /// Per-speaker adaptive jitter buffer + Opus decoder. Push on the socket thread, Pull on the audio thread.
    /// target = clamp(p95 transit jitter + 1 frame, 30, 120 ms), smoothed; above target + 2 frames the oldest frames are dropped
    /// (TCP bursts never accumulate latency); gaps -> Opus PLC (max 3 frames) then silence; a talk spurt after silence restarts at target.
    public sealed class JitterBuffer
    {
        const int Slots = 64, Mask = Slots - 1;
        readonly byte[][] pkt = new byte[Slots][];
        readonly int[] pseq = new int[Slots];
        readonly double[] parr = new double[Slots];
        readonly uint[] pcap = new uint[Slots];
        readonly object gate = new object();
        readonly OpusDecoder dec;
        public readonly int Frame; public readonly float FrameMs;
        int nextSeq = -1, highest = -1, plcRun, lastSeq16 = -1, extBase;
        bool playing;
        double lastArrival, prevArr; uint prevCap; bool havePrev;
        readonly float[] dev = new float[100], sortBuf = new float[100]; int devN, devH;
        public float TargetMs = 60f, CurrentMs; public int Received, Late, Dropped, Concealed, Played;
        public double LastTransitMs, PlayedTransitMs, SumDelayMs; public int DelayN;
        public bool Talking => playing;
        public bool Muted; public bool MicOn; public string Name = "";

        public JitterBuffer(int frame) { Frame = frame; FrameMs = frame / 48f; dec = OpusDecoder.Create(48000, 1); }

        /// u16 wire seq -> monotonic int.
        int Extend(int s16)
        {
            if (lastSeq16 < 0) { lastSeq16 = s16; extBase = 0; return s16; }
            int d = (short)(s16 - lastSeq16);
            int ext = extBase + lastSeq16 + d;
            if (d > 0) { if (s16 < lastSeq16) extBase += 65536; lastSeq16 = s16; }
            return ext;
        }

        public void Push(int seq16, uint cap, byte[] data, double arrival)
        {
            lock (gate)
            {
                int seq = Extend(seq16);
                Received++;
                LastTransitMs = (int)((uint)(long)arrival - cap); // meaningful for loopback (same clock)
                if (havePrev)
                {
                    float d = Mathf.Abs((float)((arrival - prevArr) - (int)(cap - prevCap)));
                    dev[devH] = d; devH = (devH + 1) % dev.Length; devN = Math.Min(devN + 1, dev.Length);
                }
                prevArr = arrival; prevCap = cap; havePrev = true;
                if (nextSeq >= 0 && seq < nextSeq) { Late++; return; }
                int i = seq & Mask;
                pkt[i] = data; pseq[i] = seq; parr[i] = arrival; pcap[i] = cap;
                if (seq > highest) highest = seq;
                lastArrival = arrival;
                if (!playing && (nextSeq < 0 || seq < nextSeq || nextSeq > highest)) nextSeq = Math.Min(nextSeq < 0 ? seq : nextSeq, seq);
            }
        }

        void Retarget()
        {
            if (devN < 5) return;
            Array.Copy(dev, sortBuf, devN);
            Array.Sort(sortBuf, 0, devN);
            float p95 = sortBuf[Mathf.Clamp(Mathf.CeilToInt(devN * 0.95f) - 1, 0, devN - 1)];
            float t = Mathf.Clamp(p95 + FrameMs, 30f, 120f);
            TargetMs = TargetMs * 0.7f + t * 0.3f; // smoothed: no swings
        }

        bool Has(int seq) => pseq[seq & Mask] == seq && pkt[seq & Mask] != null;

        /// Next 20 ms of audio (48 kHz mono) or false = silence. Audio thread.
        public bool Pull(short[] outPcm, double now)
        {
            lock (gate)
            {
                if (!playing)
                {
                    if (highest < 0 || nextSeq < 0 || nextSeq > highest) return false;
                    while (nextSeq <= highest && !Has(nextSeq)) nextSeq++;
                    if (!Has(nextSeq) || now - parr[nextSeq & Mask] < TargetMs) return false; // first frame of a spurt waits `target`
                    playing = true; plcRun = 0;
                }
                while ((highest - nextSeq + 1) * FrameMs > TargetMs + 2 * FrameMs)
                {
                    if (Has(nextSeq)) { Dropped++; pkt[nextSeq & Mask] = null; }
                    nextSeq++;
                }
                int s = nextSeq & Mask;
                if (Has(nextSeq))
                {
                    var d = pkt[s]; pkt[s] = null;
                    try { dec.Decode(d, 0, d.Length, outPcm, 0, Frame, false); } catch { Array.Clear(outPcm, 0, Frame); }
                    CurrentMs = (float)(now - parr[s]);
                    PlayedTransitMs = (int)((uint)(long)parr[s] - pcap[s]);
                    SumDelayMs += CurrentMs; DelayN++;
                    nextSeq++; plcRun = 0; Played++;
                    if (Played % 50 == 0) Retarget();
                    return true;
                }
                if (plcRun < 3 && now - lastArrival < 500)
                {
                    try { dec.Decode(null, 0, 0, outPcm, 0, Frame, false); } catch { Array.Clear(outPcm, 0, Frame); }
                    plcRun++; Concealed++; nextSeq++;
                    return true;
                }
                playing = false; // talk spurt over: the next one restarts at the target delay
                return false;
            }
        }
    }

    /// Voice chat engine: capture (Android plugin / Unity Microphone) -> VAD -> Opus -> one message per frame;
    /// receive on the socket thread -> jitter buffers -> OnAudioFilterRead mix.
    public sealed class VoiceEngine : MonoBehaviour
    {
        public const byte C2S_VOICE = 0x30, C2S_STATE = 0x31, C2S_ECHO = 0x32, S2C_VOICE = 0x88, S2C_STATE = 0x89;
        public static bool Enabled { get => PlayerPrefs.GetInt("voiceEnabled", 1) == 1; set => PlayerPrefs.SetInt("voiceEnabled", value ? 1 : 0); }
        public bool MicOn { get; private set; }
        public float Volume = 1f;
        public float Sensitivity = 0.5f; // 0..1 -> gate -55..-30 dBFS
        public int FrameMs = 20;
        public string Effects = "", Status = "";
        public int MyId;

        NetClient net;
        AudioSource src;
        readonly Dictionary<int, JitterBuffer> speakers = new Dictionary<int, JitterBuffer>();
        readonly List<JitterBuffer> mixList = new List<JitterBuffer>();
        OpusEncoder enc;
        int frame = 960;
        ushort seq;
        bool capturing;
        Thread capThread;
        AndroidJavaObject plugin;
        int captureBufMs;
        int hangover;
        public bool Voiced { get; private set; }
        // stats
        long upBytes, downBytes; public float UpKbps, DownKbps, EncodeMs; float statT; long lastUp, lastDown;
        public int SentFrames;
        int outRate = 48000; public float OutputMs;

        public static VoiceEngine Create(NetClient n)
        {
            var go = new GameObject("Voice");
            DontDestroyOnLoad(go);
            var v = go.AddComponent<VoiceEngine>();
            v.net = n;
            v.src = go.AddComponent<AudioSource>();
            v.src.playOnAwake = false; v.src.spatialBlend = 0f; v.src.loop = true;
            v.Volume = PlayerPrefs.GetFloat("voiceVolume", 1f);
            v.Sensitivity = PlayerPrefs.GetFloat("voiceSens", 0.5f);
            v.FrameMs = PlayerPrefs.GetInt("voiceFrameMs", 20) == 10 ? 10 : 20;
            v.outRate = AudioSettings.outputSampleRate;
            AudioSettings.GetDSPBufferSize(out int len, out int num);
            v.OutputMs = 1000f * len * Mathf.Max(1, num) / Mathf.Max(1, v.outRate);
            n.ws.VoiceSink = v.OnVoice;
            v.src.Play(); // OnAudioFilterRead generates the mix (no AudioClip allocations)
            return v;
        }

        // ---------------- receive (socket thread) ----------------
        void OnVoice(byte[] m, double t)
        {
            if (!Enabled) return;
            Interlocked.Add(ref downBytes, m.Length);
            if (m[0] == S2C_STATE && m.Length >= 4)
            {
                var jb = Get(m[1]);
                jb.MicOn = m[2] != 0;
                return;
            }
            if (m[0] != S2C_VOICE || m.Length < 9) return;
            int speaker = m[1], s16 = m[2] | (m[3] << 8);
            uint cap = (uint)(m[4] | (m[5] << 8) | (m[6] << 16) | (m[7] << 24));
            int n = m[8], o = 9;
            var j = Get(speaker);
            for (int k = 0; k < n && o < m.Length; k++)
            {
                int len = m[o++];
                if (o + len > m.Length) break;
                var f = new byte[len]; Buffer.BlockCopy(m, o, f, 0, len); o += len;
                j.Push((s16 + k) & 0xFFFF, cap + (uint)(k * FrameMs), f, t);
            }
        }

        JitterBuffer Get(int id)
        {
            lock (speakers)
            {
                if (!speakers.TryGetValue(id, out var j)) { j = new JitterBuffer(960); j.Muted = PlayerPrefs.GetInt("voiceMute" + id, 0) == 1; speakers[id] = j; }
                return j;
            }
        }

        public JitterBuffer Speaker(int id) { lock (speakers) return speakers.TryGetValue(id, out var j) ? j : null; }
        public void SetMuted(int id, bool m) { Get(id).Muted = m; }
        public bool AnyTalking { get { lock (speakers) foreach (var kv in speakers) if (kv.Key != MyId && kv.Value.Talking) return true; return false; } }
        public void ResetSpeakers() { lock (speakers) speakers.Clear(); }

        // ---------------- playback (audio thread) ----------------
        short[] tmp = new short[960];
        readonly Dictionary<JitterBuffer, (short[] buf, int len, double pos)> cur = new Dictionary<JitterBuffer, (short[], int, double)>();

        void OnAudioFilterRead(float[] data, int channels)
        {
            if (!Enabled) return;
            double now = NetTime.Ms;
            mixList.Clear();
            lock (speakers) foreach (var kv in speakers) if (kv.Key != MyId || loopbackPlay) mixList.Add(kv.Value);
            double step = 48000.0 / outRate;
            int frames = data.Length / channels;
            float vol = Volume;
            foreach (var j in mixList)
            {
                if (!cur.TryGetValue(j, out var c)) c = (new short[960], 0, 0);
                for (int i = 0; i < frames; i++)
                {
                    if (c.pos >= c.len)
                    {
                        c.pos -= c.len;
                        c.len = j.Pull(c.buf, now) ? 960 : 0;
                        if (c.len == 0) { c.pos = 0; break; }
                    }
                    float v = j.Muted ? 0f : c.buf[(int)c.pos] * (vol / 32768f);
                    for (int ch = 0; ch < channels; ch++) data[i * channels + ch] += v;
                    c.pos += step;
                }
                cur[j] = c;
            }
        }
        bool loopbackPlay;

        // ---------------- capture ----------------
        /// Mic button: first ON asks RECORD_AUDIO; denied -> toast, stays OFF. OFF really releases the mic.
        public void SetMic(bool on, Action<string> toast)
        {
            if (!Enabled) { MicOn = false; return; }
            if (on && Application.platform == RuntimePlatform.Android && !UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.Microphone))
            {
                var cb = new UnityEngine.Android.PermissionCallbacks();
                cb.PermissionGranted += _ => SetMic(true, toast);
                cb.PermissionDenied += _ => { toast?.Invoke("Microphone permission denied - mic stays OFF"); ApplyMic(false); };
                cb.PermissionDeniedAndDontAskAgain += _ => { toast?.Invoke("Microphone blocked - enable it in Android settings"); ApplyMic(false); };
                UnityEngine.Android.Permission.RequestUserPermission(UnityEngine.Android.Permission.Microphone, cb);
                return;
            }
            ApplyMic(on);
        }

        void ApplyMic(bool on)
        {
            MicOn = on;
            PlayerPrefs.SetInt("micOn", on ? 1 : 0);
            if (on) StartCapture(); else StopCapture();
            SendState();
        }

        public void SendState() { if (Enabled) net.ws.Send(new byte[] { C2S_STATE, (byte)(MicOn ? 1 : 0) }); }
        public void RestoreMic(Action<string> toast) { if (Enabled && PlayerPrefs.GetInt("micOn", 0) == 1 && !MicOn) SetMic(true, toast); else SendState(); }

        void NewEncoder()
        {
            frame = 48 * FrameMs;
            enc = OpusEncoder.Create(48000, 1, OpusApplication.OPUS_APPLICATION_VOIP);
            enc.Bitrate = 24000; enc.Complexity = 5; enc.UseDTX = true; enc.UseInbandFEC = false; enc.SignalType = OpusSignal.OPUS_SIGNAL_VOICE;
        }

        void StartCapture()
        {
            if (capturing) return;
            NewEncoder();
            capturing = true;
            if (Application.platform == RuntimePlatform.Android)
            {
                try
                {
                    if (plugin == null)
                    {
                        using var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                        var act = player.GetStatic<AndroidJavaObject>("currentActivity");
                        plugin = new AndroidJavaObject("com.supcom.mottainai.VoicePlugin", act);
                    }
                    int rate = plugin.Call<int>("start", FrameMs);
                    if (rate != 48000) { Status = rate == 0 ? "mic failed" : "mic rate " + rate + " (need 48000)"; plugin.Call("stop"); capturing = false; MicOn = false; return; }
                    Effects = plugin.Call<string>("effects");
                    captureBufMs = plugin.Call<int>("bufferMs") + FrameMs;
                    capThread = new Thread(AndroidCaptureLoop) { IsBackground = true, Priority = System.Threading.ThreadPriority.AboveNormal, Name = "VoiceEncode" };
                    capThread.Start();
                    Status = "mic ON";
                }
                catch (Exception e) { Status = "mic error: " + e.Message; capturing = false; MicOn = false; }
            }
            else
            {
                micClip = Microphone.Start(null, true, 1, 48000);
                micPos = 0; Effects = "(desktop Microphone)"; captureBufMs = FrameMs + 20; Status = micClip != null ? "mic ON" : "no microphone";
            }
        }

        void StopCapture()
        {
            capturing = false;
            try { capThread?.Join(300); } catch { }
            capThread = null;
            try { plugin?.Call("stop"); } catch { }
            if (micClip != null) { Microphone.End(null); micClip = null; }
            Voiced = false; hangover = 0;
            Status = "mic OFF";
        }

        void AndroidCaptureLoop()
        {
            AndroidJNI.AttachCurrentThread();
            var pcm = new short[frame];
            try
            {
                while (capturing)
                {
                    var sb = plugin.Call<sbyte[]>("poll");
                    if (sb == null) { Thread.Sleep(2); continue; }
                    Buffer.BlockCopy(sb, 0, pcm, 0, Math.Min(sb.Length, pcm.Length * 2));
                    Process(pcm, false);
                }
            }
            catch (Exception e) { Status = "capture: " + e.Message; }
            finally { AndroidJNI.DetachCurrentThread(); }
        }

        AudioClip micClip; int micPos; float[] micBuf;
        void DesktopCapture()
        {
            if (micClip == null) return;
            int pos = Microphone.GetPosition(null);
            int avail = (pos - micPos + micClip.samples) % micClip.samples;
            while (avail >= frame)
            {
                if (micBuf == null || micBuf.Length != frame) micBuf = new float[frame];
                micClip.GetData(micBuf, micPos);
                var pcm = new short[frame];
                for (int i = 0; i < frame; i++) pcm[i] = (short)Mathf.Clamp(micBuf[i] * 32767f, -32768f, 32767f);
                Process(pcm, false);
                micPos = (micPos + frame) % micClip.samples; avail -= frame;
            }
        }

        readonly byte[] encOut = new byte[400];
        /// VAD (energy gate + 200 ms hangover) -> Opus -> one VOICE (or VOICE_ECHO) message, sent immediately.
        public void Process(short[] pcm, bool echo)
        {
            double t0 = NetTime.Ms;
            if (!echo)
            {
                double e = 0; for (int i = 0; i < pcm.Length; i++) e += (double)pcm[i] * pcm[i];
                float db = 10f * Mathf.Log10((float)(e / pcm.Length) / (32768f * 32768f) + 1e-12f);
                float gate = Mathf.Lerp(-30f, -55f, Sensitivity);
                if (db > gate) hangover = 200 / FrameMs; else if (hangover > 0) hangover--;
                Voiced = hangover > 0;
                if (!Voiced) return;
            }
            if (enc == null) NewEncoder();
            int len;
            try { len = enc.Encode(pcm, 0, pcm.Length, encOut, 0, encOut.Length); } catch { return; }
            EncodeMs = EncodeMs * 0.9f + (float)(NetTime.Ms - t0) * 0.1f;
            if (len <= 2 && !echo) return; // DTX: nothing worth sending
            uint cap = (uint)(long)t0;
            var msg = new byte[10 + len];
            msg[0] = echo ? C2S_ECHO : C2S_VOICE;
            msg[1] = (byte)seq; msg[2] = (byte)(seq >> 8); seq++;
            msg[3] = (byte)cap; msg[4] = (byte)(cap >> 8); msg[5] = (byte)(cap >> 16); msg[6] = (byte)(cap >> 24);
            msg[7] = 1; msg[8] = (byte)len;
            Buffer.BlockCopy(encOut, 0, msg, 9, len);
            Array.Resize(ref msg, 9 + len);
            net.ws.Send(msg);
            Interlocked.Add(ref upBytes, msg.Length);
            SentFrames++;
        }

        public int CaptureBufMs => captureBufMs;

        void Update()
        {
            if (!Enabled) { VoiceDuck.Gain = 1f; return; }
            if (capturing && micClip != null) DesktopCapture();
            float target = AnyTalking ? 0.398f : 1f; // -8 dB
            VoiceDuck.Gain = Mathf.MoveTowards(VoiceDuck.Gain, target, Time.unscaledDeltaTime * 4f);
            statT += Time.unscaledDeltaTime;
            if (statT >= 1f)
            {
                long u = Interlocked.Read(ref upBytes), d = Interlocked.Read(ref downBytes);
                UpKbps = (u - lastUp) * 8f / 1000f / statT; DownKbps = (d - lastDown) * 8f / 1000f / statT;
                lastUp = u; lastDown = d; statT = 0f;
            }
        }

        /// Summary over remote speakers for the Net HUD.
        public void Summary(out float target, out float current, out float latePct, out float dropPct, out float concPct, out string talkers)
        {
            target = 0; current = 0; int rec = 0, late = 0, drop = 0, conc = 0; var sb = new System.Text.StringBuilder();
            lock (speakers)
                foreach (var kv in speakers)
                {
                    if (kv.Key == MyId) continue;
                    var j = kv.Value;
                    target = Mathf.Max(target, j.TargetMs); current = Mathf.Max(current, j.Talking ? j.CurrentMs : 0f);
                    rec += j.Received; late += j.Late; drop += j.Dropped; conc += j.Concealed;
                    if (j.Talking) sb.Append(kv.Key).Append(' ');
                }
            float r = Mathf.Max(1, rec);
            latePct = 100f * late / r; dropPct = 100f * drop / r; concPct = 100f * conc / r; talkers = sb.ToString();
        }

        void OnDestroy() { StopCapture(); }
        public void OnAppPause(bool paused) { if (!Enabled) return; if (paused) StopCapture(); else if (MicOn) StartCapture(); }
    }

    /// Network Lab "Voice loopback": 10 s of generated chirp encoded live -> VOICE_ECHO -> server -> back -> jitter buffer.
    /// Mouth-to-ear estimate per frame = capture frame + transit (encode + up + server + down) + jitter buffer + output latency.
    public sealed class VoiceLoopback
    {
        public float E2eAvg, E2eP95, Jitter, LossPct; public int Sent, Got;
        public bool Done;
        readonly List<float> e2e = new List<float>();

        public void Run(WsClient ws, float outputMs, int seconds = 10)
        {
            var jb = new JitterBuffer(960);
            ws.VoiceSink = (m, t) =>
            {
                if (m[0] != VoiceEngine.S2C_VOICE || m.Length < 11) return;
                int s16 = m[2] | (m[3] << 8); uint cap = (uint)(m[4] | (m[5] << 8) | (m[6] << 16) | (m[7] << 24));
                int len = m[9]; var f = new byte[len]; Buffer.BlockCopy(m, 10, f, 0, Math.Min(len, m.Length - 10));
                jb.Push(s16, cap, f, t);
                lock (e2e) Got++;
            };
            var enc = OpusEncoder.Create(48000, 1, OpusApplication.OPUS_APPLICATION_VOIP);
            enc.Bitrate = 24000; enc.Complexity = 5; enc.UseDTX = false; enc.SignalType = OpusSignal.OPUS_SIGNAL_VOICE;
            var pcm = new short[960]; var outPcm = new short[960]; var buf = new byte[400];
            var sw = System.Diagnostics.Stopwatch.StartNew();
            double phase = 0; ushort seq = 0;
            int frames = seconds * 50;
            for (int k = 0; k < frames + 25; k++)
            {
                double due = k * 20.0;
                while (sw.Elapsed.TotalMilliseconds < due) Thread.Sleep(1);
                if (k < frames)
                {
                    for (int i = 0; i < 960; i++) { double f = 300 + 2700 * ((k % 50) / 50.0); phase += 2 * Math.PI * f / 48000; pcm[i] = (short)(Math.Sin(phase) * 9000); }
                    double t0 = NetTime.Ms;
                    int len = enc.Encode(pcm, 0, 960, buf, 0, buf.Length);
                    uint cap = (uint)(long)t0;
                    var msg = new byte[9 + len];
                    msg[0] = VoiceEngine.C2S_ECHO; msg[1] = (byte)seq; msg[2] = (byte)(seq >> 8); seq++;
                    msg[3] = (byte)cap; msg[4] = (byte)(cap >> 8); msg[5] = (byte)(cap >> 16); msg[6] = (byte)(cap >> 24); msg[7] = 1; msg[8] = (byte)len;
                    Buffer.BlockCopy(buf, 0, msg, 9, len);
                    ws.Send(msg); Sent++;
                }
                // playout clock (as the audio thread would): one frame per 20 ms
                double now = NetTime.Ms;
                if (jb.Pull(outPcm, now) && jb.CurrentMs > 0) lock (e2e) e2e.Add(20f + (float)jb.PlayedTransitMs + jb.CurrentMs + outputMs);
            }
            ws.VoiceSink = null;
            lock (e2e)
            {
                if (e2e.Count > 0)
                {
                    e2e.Sort();
                    float sum = 0; foreach (var x in e2e) sum += x;
                    E2eAvg = sum / e2e.Count; E2eP95 = e2e[Mathf.Clamp(Mathf.CeilToInt(e2e.Count * 0.95f) - 1, 0, e2e.Count - 1)];
                    Jitter = E2eP95 - e2e[e2e.Count / 2];
                }
                LossPct = Sent > 0 ? 100f * Mathf.Max(0, Sent - Got) / Sent : 0;
            }
            Done = true;
        }
    }
}
