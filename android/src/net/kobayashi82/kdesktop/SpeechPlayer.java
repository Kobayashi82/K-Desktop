package net.kobayashi82.kdesktop;

import android.content.Context;
import android.media.AudioAttributes;
import android.media.AudioFormat;
import android.media.AudioManager;
import android.media.AudioTrack;
import android.webkit.CookieManager;

import java.io.ByteArrayOutputStream;
import java.io.InputStream;
import java.net.HttpURLConnection;
import java.net.URL;
import java.util.concurrent.LinkedBlockingQueue;

/**
 * The voice of an assistant's answer on the phone (phase 6, 5.0.410): the pieces the PC sends (WAV, 16-bit mono) played
 * one after another with no gaps, also in the background or with the screen off. The phone's own music ducks meanwhile.
 * When a turn the PC said is over has played to the end, <c>done</c> gets its id.
 */
final class SpeechPlayer {
    /** A turn played to the end. */
    interface Done {
        void turn(String id);
    }

    /** A piece to play, or (url null) the end of a turn. */
    private static final class Item {
        final String turn;
        final String url;

        Item(String turn, String url) {
            this.turn = turn;
            this.url = url;
        }
    }

    private final Context context;
    private final Done done;
    private final LinkedBlockingQueue<Item> queue = new LinkedBlockingQueue<>();
    private final Object lock = new Object();
    private Thread worker;
    private AudioTrack track;
    private int trackRate;
    private volatile int generation;
    private volatile String token = "";
    private boolean focused;

    @SuppressWarnings("deprecation")
    private final AudioManager.OnAudioFocusChangeListener focus = change -> {
    };

    SpeechPlayer(Context context, Done done) {
        this.context = context.getApplicationContext();
        this.done = done;
    }

    /** One more piece of <c>turn</c> at <c>url</c> (the PC's address + api/assistants/audio…). */
    void play(String url, String turn, String key) {
        token = key == null ? "" : key;
        queue.add(new Item(turn, url));
        start();
    }

    /** The PC said all of <c>turn</c>: when what is queued has played, it is done. */
    void end(String turn) {
        queue.add(new Item(turn, null));
        start();
    }

    /** Silence at once: what is queued and what sounds. */
    void stop() {
        generation++;
        queue.clear();
        synchronized (lock) {
            if (track != null) {
                try {
                    track.pause();
                    track.flush();
                } catch (Exception ignored) {
                }
            }
        }
        release();
    }

    private synchronized void start() {
        if (worker != null && worker.isAlive()) return;
        worker = new Thread(this::run, "speech");
        worker.setDaemon(true);
        worker.start();
    }

    private void run() {
        try {
            while (true) {
                Item item = queue.poll(20, java.util.concurrent.TimeUnit.SECONDS);
                if (item == null) break;
                int mine = generation;
                if (item.url == null) {
                    waitPlayed(mine);
                    if (mine == generation) done.turn(item.turn);
                    continue;
                }
                byte[] wav = fetch(item.url);
                if (wav == null || mine != generation) continue;
                write(wav, mine);
            }
        } catch (InterruptedException ignored) {
        } finally {
            synchronized (lock) {
                if (track != null) {
                    track.release();
                    track = null;
                }
            }
            release();
        }
    }

    private byte[] fetch(String url) {
        try {
            HttpURLConnection c = (HttpURLConnection) new URL(url).openConnection();
            c.setConnectTimeout(8000);
            c.setReadTimeout(15000);
            c.setRequestProperty("X-KD", "1");
            if (!token.isEmpty()) c.setRequestProperty("Authorization", "Bearer " + token);
            String cookie = CookieManager.getInstance().getCookie(url);
            if (cookie != null) c.setRequestProperty("Cookie", cookie);
            try {
                if (c.getResponseCode() != 200) return null;
                ByteArrayOutputStream body = new ByteArrayOutputStream();
                try (InputStream in = c.getInputStream()) {
                    byte[] buffer = new byte[16384];
                    for (int n; (n = in.read(buffer)) > 0 && body.size() < 8 * 1024 * 1024; ) body.write(buffer, 0, n);
                }
                return body.toByteArray();
            } finally {
                c.disconnect();
            }
        } catch (Exception e) {
            return null;
        }
    }

    /** The samples of a 16-bit mono WAV into the track (a new one when the rate changes). */
    private void write(byte[] wav, int mine) {
        if (wav.length <= 44 || wav[0] != 'R' || wav[8] != 'W') return;
        int rate = (wav[24] & 0xff) | (wav[25] & 0xff) << 8 | (wav[26] & 0xff) << 16 | (wav[27] & 0xff) << 24;
        AudioTrack t;
        synchronized (lock) {
            if (track == null || trackRate != rate) {
                if (track != null) track.release();
                int min = AudioTrack.getMinBufferSize(rate, AudioFormat.CHANNEL_OUT_MONO, AudioFormat.ENCODING_PCM_16BIT);
                track = new AudioTrack(new AudioAttributes.Builder()
                        .setUsage(AudioAttributes.USAGE_MEDIA)
                        .setContentType(AudioAttributes.CONTENT_TYPE_SPEECH).build(),
                        new AudioFormat.Builder().setSampleRate(rate).setChannelMask(AudioFormat.CHANNEL_OUT_MONO)
                                .setEncoding(AudioFormat.ENCODING_PCM_16BIT).build(),
                        Math.max(min, rate), AudioTrack.MODE_STREAM, AudioManager.AUDIO_SESSION_ID_GENERATE);
                trackRate = rate;
            }
            t = track;
        }
        duck();
        if (t.getPlayState() != AudioTrack.PLAYSTATE_PLAYING) t.play();
        for (int at = 44; at < wav.length && mine == generation; ) {
            int written = t.write(wav, at, Math.min(8192, wav.length - at));
            if (written <= 0) break;
            at += written;
        }
    }

    /** What was written has sounded (the track's head reached the end), or it was stopped. */
    private void waitPlayed(int mine) throws InterruptedException {
        AudioTrack t;
        synchronized (lock) {
            t = track;
        }
        if (t == null) return;
        int last = -1, still = 0;
        while (mine == generation && still < 4) {
            int head = t.getPlaybackHeadPosition();
            still = head == last ? still + 1 : 0;
            last = head;
            Thread.sleep(80);
        }
        release();
    }

    @SuppressWarnings("deprecation")
    private void duck() {
        if (focused) return;
        AudioManager audio = (AudioManager) context.getSystemService(Context.AUDIO_SERVICE);
        if (audio == null) return;
        focused = audio.requestAudioFocus(focus, AudioManager.STREAM_MUSIC, AudioManager.AUDIOFOCUS_GAIN_TRANSIENT_MAY_DUCK)
                == AudioManager.AUDIOFOCUS_REQUEST_GRANTED;
    }

    @SuppressWarnings("deprecation")
    private void release() {
        if (!focused) return;
        focused = false;
        AudioManager audio = (AudioManager) context.getSystemService(Context.AUDIO_SERVICE);
        if (audio != null) audio.abandonAudioFocus(focus);
    }
}
