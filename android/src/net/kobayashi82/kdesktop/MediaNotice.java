package net.kobayashi82.kdesktop;

import android.app.Notification;
import android.app.NotificationChannel;
import android.app.NotificationManager;
import android.app.PendingIntent;
import android.content.BroadcastReceiver;
import android.content.Context;
import android.content.Intent;
import android.content.SharedPreferences;
import android.content.pm.PackageManager;
import android.graphics.Bitmap;
import android.graphics.BitmapFactory;
import android.media.MediaMetadata;
import android.media.VolumeProvider;
import android.media.session.MediaSession;
import android.media.session.PlaybackState;
import android.net.Uri;
import android.os.Build;
import android.os.Handler;
import android.os.Looper;
import android.webkit.CookieManager;

import org.json.JSONArray;
import org.json.JSONObject;

import java.io.ByteArrayOutputStream;
import java.io.InputStream;
import java.io.OutputStream;
import java.net.HttpURLConnection;
import java.net.URL;
import java.nio.charset.StandardCharsets;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;

/**
 * What plays on the PC as a media notification (5.0.408): the cover, the title and the player's buttons, also on the
 * lock screen and in Android's media controls, and the PC's volume with the phone's volume keys (a remote media
 * session). The web app feeds it while it is open (Bridge.setMedia) and {@link InboxService} with the app closed (the
 * «media» event). Its buttons ask the PC straight away (api/media, api/audio). Everything here runs on the main thread
 * but the requests, which go one after the other.
 */
public final class MediaNotice {
    static final String KEY_ENABLED = "mediaNotice";
    static final String ACTION = "net.kobayashi82.kdesktop.MEDIA";
    private static final String CHANNEL = "media";
    private static final int ID = 7;

    private static final ExecutorService NET = Executors.newSingleThreadExecutor();
    private static final Handler MAIN = new Handler(Looper.getMainLooper());

    private static MediaSession session;
    private static VolumeProvider volume;
    /** The program on screen (one of api/media's sessions). */
    private static JSONObject shown;
    private static String address = "";
    private static String token = "";
    private static String artKey = "";
    private static Bitmap art;
    /** Swiped away: not again until the track changes. */
    private static String dismissed = "";
    private static volatile int pendingVolume = -1;

    private MediaNotice() {
    }

    private static SharedPreferences prefs(Context context) {
        return context.getSharedPreferences(InboxService.PREFS, Context.MODE_PRIVATE);
    }

    static boolean enabled(Context context) {
        return prefs(context).getBoolean(KEY_ENABLED, true);
    }

    static void setEnabled(Context context, boolean on) {
        prefs(context).edit().putBoolean(KEY_ENABLED, on).apply();
        if (!on) MAIN.post(() -> hide(context.getApplicationContext()));
    }

    private static boolean mayNotify(Context context) {
        return Build.VERSION.SDK_INT < 33 || context.checkSelfPermission(android.Manifest.permission.POST_NOTIFICATIONS) == PackageManager.PERMISSION_GRANTED;
    }

    /**
     * The state of api/media ({sessions: [...]}) of the PC at <c>at</c>. <c>chosen</c>: the program picked in the app
     * ("" = the one on screen if it is still there, else the first: the one playing). Null state: nothing to show.
     */
    static void update(Context context, JSONObject state, String chosen, String at, String key) {
        MAIN.post(() -> show(context.getApplicationContext(), state, chosen, at, key));
    }

    private static void show(Context app, JSONObject state, String chosen, String at, String key) {
        if (!enabled(app) || !mayNotify(app) || state == null || at == null || at.isEmpty()) {
            hide(app);
            return;
        }
        address = at;
        token = key == null ? "" : key;
        JSONObject s = pick(state.optJSONArray("sessions"), chosen);
        if (s == null) {
            hide(app);
            return;
        }
        String track = s.optString("appId") + "|" + s.optString("title");
        if (track.equals(dismissed)) return;
        dismissed = "";
        shown = s;
        if (session == null) start(app);
        String title = s.isNull("title") ? app.getString(R.string.media_unknown) : s.optString("title");
        String artist = s.isNull("artist") ? "" : s.optString("artist");
        long length = (long) (s.optDouble("length", 0) * 1000);
        MediaMetadata.Builder meta = new MediaMetadata.Builder()
                .putString(MediaMetadata.METADATA_KEY_TITLE, title)
                .putString(MediaMetadata.METADATA_KEY_ARTIST, artist.isEmpty() ? s.optString("app") : artist)
                .putString(MediaMetadata.METADATA_KEY_ALBUM, s.isNull("album") ? "" : s.optString("album"));
        if (length > 0) meta.putLong(MediaMetadata.METADATA_KEY_DURATION, length);
        String cover = s.isNull("art") ? "" : s.optString("art");
        if (!cover.equals(artKey)) {
            artKey = cover;
            art = null;
            if (!cover.isEmpty()) loadArt(app, s.optString("appId"), cover);
        }
        if (art != null) meta.putBitmap(MediaMetadata.METADATA_KEY_ALBUM_ART, art);
        session.setMetadata(meta.build());

        boolean playing = "playing".equals(s.optString("state"));
        long actions = PlaybackState.ACTION_PLAY | PlaybackState.ACTION_PAUSE | PlaybackState.ACTION_PLAY_PAUSE;
        if (s.optBoolean("canNext")) actions |= PlaybackState.ACTION_SKIP_TO_NEXT;
        if (s.optBoolean("canPrevious")) actions |= PlaybackState.ACTION_SKIP_TO_PREVIOUS;
        if (s.optBoolean("canSeek")) actions |= PlaybackState.ACTION_SEEK_TO;
        long position = s.isNull("position") ? PlaybackState.PLAYBACK_POSITION_UNKNOWN : (long) (s.optDouble("position", 0) * 1000);
        session.setPlaybackState(new PlaybackState.Builder()
                .setActions(actions)
                .setState(playing ? PlaybackState.STATE_PLAYING : PlaybackState.STATE_PAUSED, position, playing ? 1f : 0f)
                .build());
        notify(app, s, title, artist, playing);
        if (volume == null) fetchVolume(app);
    }

    private static JSONObject pick(JSONArray list, String chosen) {
        if (list == null || list.length() == 0) return null;
        String want = chosen != null && !chosen.isEmpty() ? chosen : shown != null ? shown.optString("appId") : "";
        for (int i = 0; i < list.length(); i++) {
            JSONObject s = list.optJSONObject(i);
            if (s != null && s.optString("appId").equals(want)) return s;
        }
        return list.optJSONObject(0);
    }

    private static void start(Context app) {
        session = new MediaSession(app, "K-Desktop");
        session.setCallback(new MediaSession.Callback() {
            @Override
            public void onPlay() {
                control(app, "play", null);
            }

            @Override
            public void onPause() {
                control(app, "pause", null);
            }

            @Override
            public void onSkipToNext() {
                control(app, "next", null);
            }

            @Override
            public void onSkipToPrevious() {
                control(app, "previous", null);
            }

            @Override
            public void onSeekTo(long ms) {
                seek(app, ms / 1000.0);
            }
        });
        session.setSessionActivity(openApp(app));
        session.setActive(true);
    }

    /** Hides it and lets go of the session (the volume keys are the phone's again). */
    static void hide(Context app) {
        shown = null;
        if (session != null) {
            session.setActive(false);
            session.release();
            session = null;
        }
        volume = null;
        NotificationManager manager = app.getSystemService(NotificationManager.class);
        if (manager != null) manager.cancel(ID);
    }

    @SuppressWarnings("deprecation")
    private static void notify(Context app, JSONObject s, String title, String artist, boolean playing) {
        NotificationManager manager = app.getSystemService(NotificationManager.class);
        if (manager == null) return;
        Notification.Builder b;
        if (Build.VERSION.SDK_INT >= 26) {
            NotificationChannel channel = new NotificationChannel(CHANNEL, app.getString(R.string.media_channel), NotificationManager.IMPORTANCE_LOW);
            channel.setShowBadge(false);
            manager.createNotificationChannel(channel);
            b = new Notification.Builder(app, CHANNEL);
        } else {
            b = new Notification.Builder(app);
        }
        int actions = 0;
        int[] compact = new int[3];
        if (s.optBoolean("canPrevious")) {
            b.addAction(new Notification.Action.Builder(android.R.drawable.ic_media_previous, app.getString(R.string.media_previous), button(app, "previous")).build());
            compact[actions] = actions++;
        }
        b.addAction(new Notification.Action.Builder(playing ? android.R.drawable.ic_media_pause : android.R.drawable.ic_media_play,
                app.getString(playing ? R.string.media_pause : R.string.media_play), button(app, "toggle")).build());
        compact[actions] = actions++;
        if (s.optBoolean("canNext")) {
            b.addAction(new Notification.Action.Builder(android.R.drawable.ic_media_next, app.getString(R.string.media_next), button(app, "next")).build());
            compact[actions] = actions++;
        }
        int[] shownActions = new int[actions];
        System.arraycopy(compact, 0, shownActions, 0, actions);
        b.setSmallIcon(R.drawable.ic_notify)
                .setContentTitle(title)
                .setContentText(artist.isEmpty() ? s.optString("app") : artist)
                .setSubText(s.optString("app"))
                .setLargeIcon(art)
                .setShowWhen(false)
                .setOngoing(playing)
                .setVisibility(Notification.VISIBILITY_PUBLIC)
                .setContentIntent(openApp(app))
                .setDeleteIntent(button(app, "dismiss"))
                .setStyle(new Notification.MediaStyle().setMediaSession(session.getSessionToken()).setShowActionsInCompactView(shownActions));
        try {
            manager.notify(ID, b.build());
        } catch (SecurityException ignored) {
            // No permission to notify any more.
        }
    }

    private static int immutable() {
        return Build.VERSION.SDK_INT >= 23 ? PendingIntent.FLAG_IMMUTABLE : 0;
    }

    private static PendingIntent openApp(Context app) {
        Intent open = new Intent(app, MainActivity.class).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK | Intent.FLAG_ACTIVITY_SINGLE_TOP);
        return PendingIntent.getActivity(app, 70, open, PendingIntent.FLAG_UPDATE_CURRENT | immutable());
    }

    private static PendingIntent button(Context app, String op) {
        Intent intent = new Intent(app, Receiver.class).setAction(ACTION).setData(Uri.parse("kdmedia:" + op));
        return PendingIntent.getBroadcast(app, op.hashCode(), intent, PendingIntent.FLAG_UPDATE_CURRENT | immutable());
    }

    /** The notification's buttons. */
    public static final class Receiver extends BroadcastReceiver {
        @Override
        public void onReceive(Context context, Intent intent) {
            String op = intent.getData() == null ? "" : intent.getData().getSchemeSpecificPart();
            Context app = context.getApplicationContext();
            if ("dismiss".equals(op)) {
                if (shown != null) dismissed = shown.optString("appId") + "|" + shown.optString("title");
                hide(app);
                return;
            }
            PendingResult result = goAsync();
            control(app, op, result::finish);
        }
    }

    // ── asking the PC ───────────────────────────────────────────────

    /** The PC of the app when nothing was shown yet in this run (the app was closed and Android kept the notification). */
    private static boolean knowPc(Context app) {
        if (!address.isEmpty()) return true;
        Pcs.Pc pc = new Pcs(prefs(app)).current();
        if (pc == null || pc.last == null || pc.token.isEmpty()) return false;
        address = pc.last;
        token = pc.token;
        return true;
    }

    private static void control(Context app, String op, Runnable done) {
        String appId = shown == null ? "" : shown.optString("appId");
        NET.execute(() -> {
            try {
                if (!knowPc(app)) return;
                String target = appId;
                if (target.isEmpty()) {
                    JSONArray list = new JSONObject(request("GET", "api/media", null)).optJSONArray("sessions");
                    if (list == null || list.length() == 0) return;
                    target = list.getJSONObject(0).optString("appId");
                }
                JSONObject answer = new JSONObject(request("POST", "api/media", new JSONObject().put("app", target).put("op", op)));
                update(app, answer.optJSONObject("state"), target, address, token);
            } catch (Exception ignored) {
                // The PC does not answer: the notification stays as it was.
            } finally {
                if (done != null) done.run();
            }
        });
    }

    private static void seek(Context app, double seconds) {
        if (shown == null) return;
        String appId = shown.optString("appId");
        NET.execute(() -> {
            try {
                JSONObject answer = new JSONObject(request("POST", "api/media", new JSONObject().put("app", appId).put("position", seconds)));
                update(app, answer.optJSONObject("state"), appId, address, token);
            } catch (Exception ignored) {
            }
        });
    }

    /** The PC's volume, for the volume keys: the session plays «remotely», on the PC. */
    private static void fetchVolume(Context app) {
        NET.execute(() -> {
            try {
                JSONObject state = new JSONObject(request("GET", "api/audio", null));
                if (state.isNull("volume")) return;
                int level = state.optInt("volume");
                MAIN.post(() -> {
                    if (session == null || volume != null) return;
                    volume = new VolumeProvider(VolumeProvider.VOLUME_CONTROL_ABSOLUTE, 100, level) {
                        @Override
                        public void onSetVolumeTo(int value) {
                            setVolume(this, value);
                        }

                        @Override
                        public void onAdjustVolume(int direction) {
                            setVolume(this, getCurrentVolume() + direction * 2);
                        }
                    };
                    session.setPlaybackToRemote(volume);
                });
            } catch (Exception ignored) {
            }
        });
    }

    /** Pressing the keys sends many: only the last one still waiting goes. */
    private static void setVolume(VolumeProvider provider, int value) {
        int level = Math.max(0, Math.min(100, value));
        provider.setCurrentVolume(level);
        boolean waiting = pendingVolume >= 0;
        pendingVolume = level;
        if (waiting) return;
        NET.execute(() -> {
            int send = pendingVolume;
            pendingVolume = -1;
            try {
                request("POST", "api/audio", new JSONObject().put("volume", send));
            } catch (Exception ignored) {
            }
        });
    }

    private static void loadArt(Context app, String appId, String key) {
        NET.execute(() -> {
            try {
                HttpURLConnection c = open("api/media/art?app=" + Uri.encode(appId) + "&k=" + key);
                Bitmap bitmap;
                try {
                    if (c.getResponseCode() != 200) return;
                    try (InputStream in = c.getInputStream()) {
                        bitmap = BitmapFactory.decodeStream(in);
                    }
                } finally {
                    c.disconnect();
                }
                if (bitmap == null) return;
                MAIN.post(() -> {
                    if (!key.equals(artKey) || session == null || shown == null) return;
                    art = bitmap;
                    JSONObject state = new JSONObject();
                    try {
                        state.put("sessions", new JSONArray().put(shown));
                    } catch (Exception ignored) {
                    }
                    show(app, state, shown.optString("appId"), address, token);
                });
            } catch (Exception ignored) {
            }
        });
    }

    private static HttpURLConnection open(String path) throws Exception {
        String url = address + path;
        HttpURLConnection c = (HttpURLConnection) new URL(url).openConnection();
        c.setConnectTimeout(8000);
        c.setReadTimeout(12000);
        c.setUseCaches(false);
        c.setRequestProperty("X-KD", "1");
        if (!token.isEmpty()) c.setRequestProperty("Authorization", "Bearer " + token);
        String cookie = CookieManager.getInstance().getCookie(url);
        if (cookie != null) c.setRequestProperty("Cookie", cookie);
        return c;
    }

    private static String request(String method, String path, JSONObject body) throws Exception {
        HttpURLConnection c = open(path);
        try {
            c.setRequestMethod(method);
            if (body != null) {
                c.setDoOutput(true);
                c.setRequestProperty("Content-Type", "application/json");
                try (OutputStream out = c.getOutputStream()) {
                    out.write(body.toString().getBytes(StandardCharsets.UTF_8));
                }
            }
            if (c.getResponseCode() != 200) throw new Exception("HTTP " + c.getResponseCode());
            ByteArrayOutputStream text = new ByteArrayOutputStream();
            try (InputStream in = c.getInputStream()) {
                byte[] buffer = new byte[8192];
                for (int n; (n = in.read(buffer)) > 0 && text.size() < 1024 * 1024; ) text.write(buffer, 0, n);
            }
            return text.toString("UTF-8");
        } finally {
            c.disconnect();
        }
    }
}
