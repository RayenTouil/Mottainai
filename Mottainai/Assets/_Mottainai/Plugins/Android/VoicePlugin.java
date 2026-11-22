package com.supcom.mottainai;

import android.content.Context;
import android.media.AudioFormat;
import android.media.AudioManager;
import android.media.AudioRecord;
import android.media.MediaRecorder;
import android.media.audiofx.AcousticEchoCanceler;
import android.media.audiofx.AutomaticGainControl;
import android.media.audiofx.NoiseSuppressor;

/** Low-latency voice capture: AudioRecord VOICE_COMMUNICATION, mono, native rate, smallest buffer, AEC/NS/AGC,
 *  MODE_IN_COMMUNICATION while ON. A Java thread fills a lock-free ring of 20 ms PCM16 frames; C# drains it from its own
 *  worker thread (never the Unity main thread). stop() releases the mic (Android privacy indicator off). */
public class VoicePlugin {
    private final AudioManager am;
    private volatile AudioRecord rec;
    private volatile Thread thread;
    private volatile boolean running;
    private AcousticEchoCanceler aec; private NoiseSuppressor ns; private AutomaticGainControl agc;
    private int rate = 48000, frame = 960, prevMode = AudioManager.MODE_NORMAL;
    private static final int SLOTS = 32;                 // 640 ms ring
    private short[][] ring;
    private volatile int head, tail;                     // single producer (Java thread), single consumer (C# thread)
    private volatile String effects = "";
    private volatile int minBufferBytes;

    public VoicePlugin(Context c) { am = (AudioManager) c.getApplicationContext().getSystemService(Context.AUDIO_SERVICE); }

    public int nativeRate() {
        try { String s = am.getProperty(AudioManager.PROPERTY_OUTPUT_SAMPLE_RATE); if (s != null) return Integer.parseInt(s); } catch (Throwable t) { }
        return 48000;
    }

    /** frameMs 10 or 20. Returns the sample rate used (0 = failed). */
    public synchronized int start(int frameMs) {
        stop();
        rate = nativeRate();
        frame = rate * frameMs / 1000;
        ring = new short[SLOTS][frame];
        head = tail = 0;
        int min = AudioRecord.getMinBufferSize(rate, AudioFormat.CHANNEL_IN_MONO, AudioFormat.ENCODING_PCM_16BIT);
        if (min <= 0) return 0;
        minBufferBytes = Math.max(min, frame * 2);
        try {
            rec = new AudioRecord(MediaRecorder.AudioSource.VOICE_COMMUNICATION, rate, AudioFormat.CHANNEL_IN_MONO, AudioFormat.ENCODING_PCM_16BIT, minBufferBytes);
            if (rec.getState() != AudioRecord.STATE_INITIALIZED) { rec.release(); rec = null; return 0; }
            int sid = rec.getAudioSessionId();
            StringBuilder e = new StringBuilder();
            if (AcousticEchoCanceler.isAvailable()) { aec = AcousticEchoCanceler.create(sid); if (aec != null) { aec.setEnabled(true); e.append("AEC "); } }
            if (NoiseSuppressor.isAvailable()) { ns = NoiseSuppressor.create(sid); if (ns != null) { ns.setEnabled(true); e.append("NS "); } }
            if (AutomaticGainControl.isAvailable()) { agc = AutomaticGainControl.create(sid); if (agc != null) { agc.setEnabled(true); e.append("AGC"); } }
            effects = e.toString().trim();
            prevMode = am.getMode();
            am.setMode(AudioManager.MODE_IN_COMMUNICATION);
            rec.startRecording();
        } catch (Throwable t) { effects = "err " + t; stop(); return 0; }
        running = true;
        final AudioRecord r = rec;
        thread = new Thread(() -> {
            android.os.Process.setThreadPriority(android.os.Process.THREAD_PRIORITY_URGENT_AUDIO);
            while (running) {
                int h = head;
                if (((h + 1) % SLOTS) == tail) tail = (tail + 1) % SLOTS; // consumer late: drop the oldest frame (never add latency)
                short[] buf = ring[h];
                int got = 0;
                while (got < frame && running) { int n = r.read(buf, got, frame - got); if (n <= 0) break; got += n; }
                if (got == frame) head = (h + 1) % SLOTS;
            }
        }, "MottainaiVoiceCapture");
        thread.start();
        return rate;
    }

    /** Next 20 ms frame as little-endian PCM16 bytes, or null if none is ready. */
    public byte[] poll() {
        if (ring == null || tail == head) return null;
        short[] s = ring[tail];
        byte[] b = new byte[s.length * 2];
        for (int i = 0; i < s.length; i++) { b[2 * i] = (byte) s[i]; b[2 * i + 1] = (byte) (s[i] >> 8); }
        tail = (tail + 1) % SLOTS;
        return b;
    }

    public int queued() { int d = head - tail; return d < 0 ? d + SLOTS : d; }
    public String effects() { return effects; }
    public int bufferMs() { return rate > 0 ? minBufferBytes * 500 / rate : 0; }

    public synchronized void stop() {
        running = false;
        Thread t = thread; thread = null;
        AudioRecord r = rec; rec = null;
        try { if (r != null) r.stop(); } catch (Throwable e) { }
        try { if (t != null) t.join(200); } catch (Throwable e) { }
        try { if (r != null) r.release(); } catch (Throwable e) { }
        try { if (aec != null) aec.release(); if (ns != null) ns.release(); if (agc != null) agc.release(); } catch (Throwable e) { }
        aec = null; ns = null; agc = null;
        try { if (am.getMode() == AudioManager.MODE_IN_COMMUNICATION) am.setMode(prevMode); } catch (Throwable e) { }
    }
}
