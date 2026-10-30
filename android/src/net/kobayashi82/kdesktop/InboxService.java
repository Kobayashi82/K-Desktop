package net.kobayashi82.kdesktop;

import android.app.Notification;
import android.app.NotificationChannel;
import android.app.NotificationManager;
import android.app.PendingIntent;
import android.app.Service;
import android.content.ClipData;
import android.content.ClipboardManager;
import android.content.Context;
import android.content.Intent;
import android.content.SharedPreferences;
import android.content.pm.PackageManager;
import android.content.pm.ServiceInfo;
import android.net.ConnectivityManager;
import android.net.Network;
import android.net.Uri;
import android.os.Build;
import android.os.IBinder;
import android.webkit.CookieManager;
import android.widget.Toast;

import org.json.JSONArray;
import org.json.JSONObject;

import java.io.BufferedReader;
import java.io.ByteArrayOutputStream;
import java.io.InputStream;
import java.io.InputStreamReader;
import java.io.OutputStream;
import java.net.HttpURLConnection;
import java.net.URL;
import java.nio.charset.StandardCharsets;

/**
 * «Avisos con la app cerrada» (B11): keeps K-Desktop's event stream open in the background and, when the PC says
 * «inbox», fetches what it sent (api/inbox), shows each one as a notification and tells the PC it arrived. Android
 * requires a fixed notification while it runs. It uses the current PC's token and addresses ({@link Pcs}, {@link Addresses}):
 * at home the PC's IP, away the proxy's address. Nothing goes through anybody's servers.
 */
public class InboxService extends Service {
    static final String PREFS = "kdesktop";
    static final String KEY_INBOX = "inbox";
    private static final String KEY_PENDING = "inboxPending";
    static final String ACTION_COPY = "net.kobayashi82.kdesktop.COPY";
    static final String ACTION_OPEN_INBOX = "net.kobayashi82.kdesktop.OPEN_INBOX";
    private static final String CHANNEL_LINK = "link";
    private static final String CHANNEL_MESSAGES = "messages";
    private static final int ONGOING_ID = 1;
    private static final int KEEP = 30;

    /** How long the stream may stay silent (K-Desktop pings every 25 s): then it is reopened. */
    private static final int READ_TIMEOUT_MS = 90000;

    private volatile boolean running;
    private volatile HttpURLConnection stream;
    private final Object wake = new Object();
    private Thread worker;
    private ConnectivityManager.NetworkCallback networkCallback;
    private String shown = "";

    static boolean enabled(Context context) {
        return context.getSharedPreferences(PREFS, MODE_PRIVATE).getBoolean(KEY_INBOX, false);
    }

    static void setEnabled(Context context, boolean on) {
        context.getSharedPreferences(PREFS, MODE_PRIVATE).edit().putBoolean(KEY_INBOX, on).apply();
        if (on) start(context);
        else context.stopService(new Intent(context, InboxService.class));
    }

    static void start(Context context) {
        Intent intent = new Intent(context, InboxService.class);
        try {
            if (Build.VERSION.SDK_INT >= 26) context.startForegroundService(intent);
            else context.startService(intent);
        } catch (Exception ignored) {
            // Not allowed now (in the background on Android 12+): the app starts it when it is opened.
        }
    }

    @Override
    public void onCreate() {
        super.onCreate();
        channels();
        Notification ongoing = ongoing(getString(R.string.inbox_connecting));
        if (Build.VERSION.SDK_INT >= 34) startForeground(ONGOING_ID, ongoing, ServiceInfo.FOREGROUND_SERVICE_TYPE_REMOTE_MESSAGING);
        else startForeground(ONGOING_ID, ongoing);
        running = true;
        worker = new Thread(this::loop, "K-Desktop inbox");
        worker.start();
        if (Build.VERSION.SDK_INT >= 24) {
            // A new network (home Wi-Fi, mobile data): connect again at once instead of waiting.
            networkCallback = new ConnectivityManager.NetworkCallback() {
                @Override
                public void onAvailable(Network network) {
                    kick();
                }
            };
            try {
                ((ConnectivityManager) getSystemService(CONNECTIVITY_SERVICE)).registerDefaultNetworkCallback(networkCallback);
            } catch (Exception ignored) {
                networkCallback = null;
            }
        }
    }

    @Override
    public int onStartCommand(Intent intent, int flags, int startId) {
        if (intent != null && ACTION_COPY.equals(intent.getAction())) {
            String text = intent.getStringExtra("text");
            ClipboardManager clipboard = (ClipboardManager) getSystemService(CLIPBOARD_SERVICE);
            if (clipboard != null && text != null) {
                clipboard.setPrimaryClip(ClipData.newPlainText("K-Desktop", text));
                Toast.makeText(this, R.string.inbox_copied, Toast.LENGTH_SHORT).show();
            }
            int id = intent.getIntExtra("id", 0);
            NotificationManager manager = (NotificationManager) getSystemService(NOTIFICATION_SERVICE);
            if (manager != null && id != 0) manager.cancel(id);
        }
        if (!enabled(this)) stopSelf();
        return START_STICKY;
    }

    @Override
    public void onDestroy() {
        running = false;
        kick();
        if (worker != null) worker.interrupt();
        if (networkCallback != null && Build.VERSION.SDK_INT >= 24) {
            try {
                ((ConnectivityManager) getSystemService(CONNECTIVITY_SERVICE)).unregisterNetworkCallback(networkCallback);
            } catch (Exception ignored) {
            }
        }
        super.onDestroy();
    }

    @Override
    public IBinder onBind(Intent intent) {
        return null;
    }

    // ── the connection ───────────────────────────────────────────────

    private void loop() {
        int delay = 5;
        while (running) {
            // The PC the app opens (its current one in the list of PCs).
            Pcs.Pc pc = new Pcs(getSharedPreferences(PREFS, MODE_PRIVATE)).current();
            String token = pc == null ? "" : pc.token;
            if (pc == null || token.isEmpty()) {
                update(getString(R.string.inbox_unpaired));
                pause(60);
                continue;
            }
            String address = Addresses.pick(pc.candidates(), pc);
            if (address == null) {
                update(getString(R.string.inbox_offline));
                pause(delay);
                delay = Math.min(delay * 2, 120);
                continue;
            }
            long since = System.currentTimeMillis();
            try {
                fetch(address, token);
                update(getString(R.string.inbox_waiting));
                listen(address, token);
            } catch (Exception e) {
                update(getString(R.string.inbox_offline));
            }
            // A connection that lasted a while: the next try is quick again.
            delay = System.currentTimeMillis() - since > 60000 ? 5 : Math.min(delay * 2, 120);
            if (running) pause(delay);
        }
    }

    /** Reads the event stream until it breaks; «inbox» fetches what is waiting. */
    private void listen(String address, String token) throws Exception {
        HttpURLConnection c = open(address + "api/events", token);
        c.setReadTimeout(READ_TIMEOUT_MS);
        stream = c;
        try {
            if (c.getResponseCode() != 200) throw new Exception("HTTP " + c.getResponseCode());
            BufferedReader reader = new BufferedReader(new InputStreamReader(c.getInputStream(), StandardCharsets.UTF_8));
            String event = null;
            for (String line; running && (line = reader.readLine()) != null; ) {
                if (line.startsWith("event:")) event = line.substring(6).trim();
                else if (line.isEmpty()) {
                    if ("inbox".equals(event)) fetch(address, token);
                    else if ("media".equals(event) && MediaNotice.enabled(this)) media(address, token);
                    event = null;
                }
            }
        } finally {
            stream = null;
            c.disconnect();
        }
    }

    /** What plays on the PC changed: the notification of what plays follows it, also with the app closed. */
    private void media(String address, String token) {
        try {
            HttpURLConnection c = open(address + "api/media", token);
            try {
                if (c.getResponseCode() == 200) MediaNotice.update(this, new JSONObject(read(c.getInputStream())), "", address, token);
            } finally {
                c.disconnect();
            }
        } catch (Exception ignored) {
            // Next time.
        }
    }

    /** What K-Desktop sent: each one a notification, kept for the app, then acknowledged. */
    private void fetch(String address, String token) throws Exception {
        HttpURLConnection c = open(address + "api/inbox", token);
        JSONArray messages;
        try {
            if (c.getResponseCode() != 200) throw new Exception("HTTP " + c.getResponseCode());
            messages = new JSONObject(read(c.getInputStream())).optJSONArray("messages");
        } finally {
            c.disconnect();
        }
        if (messages == null || messages.length() == 0) return;
        JSONArray ids = new JSONArray();
        for (int i = 0; i < messages.length(); i++) {
            JSONObject message = messages.getJSONObject(i);
            notify(message);
            keep(this, message);
            ids.put(message.optString("id"));
        }
        HttpURLConnection ack = open(address + "api/inbox/ack", token);
        try {
            ack.setRequestMethod("POST");
            ack.setDoOutput(true);
            ack.setRequestProperty("Content-Type", "application/json");
            try (OutputStream out = ack.getOutputStream()) {
                out.write(new JSONObject().put("ids", ids).toString().getBytes(StandardCharsets.UTF_8));
            }
            ack.getResponseCode();
        } finally {
            ack.disconnect();
        }
    }

    private HttpURLConnection open(String url, String token) throws Exception {
        HttpURLConnection c = (HttpURLConnection) new URL(url).openConnection();
        c.setConnectTimeout(10000);
        c.setReadTimeout(15000);
        c.setUseCaches(false);
        c.setRequestProperty("X-KD", "1");
        c.setRequestProperty("Authorization", "Bearer " + token);
        String cookie = CookieManager.getInstance().getCookie(url);
        if (cookie != null) c.setRequestProperty("Cookie", cookie);
        return c;
    }

    private static String read(InputStream in) throws Exception {
        ByteArrayOutputStream body = new ByteArrayOutputStream();
        try (InputStream input = in) {
            byte[] buffer = new byte[8192];
            for (int n; (n = input.read(buffer)) > 0 && body.size() < 4 * 1024 * 1024; ) body.write(buffer, 0, n);
        }
        return body.toString("UTF-8");
    }

    private void pause(int seconds) {
        synchronized (wake) {
            try {
                wake.wait(seconds * 1000L);
            } catch (InterruptedException ignored) {
            }
        }
    }

    /** Connects again now: the stream is closed and a wait ends. */
    private void kick() {
        HttpURLConnection c = stream;
        if (c != null) new Thread(c::disconnect).start();
        synchronized (wake) {
            wake.notifyAll();
        }
    }

    // ── what the app shows ───────────────────────────────────────────

    /** Kept until the app shows them (KDesktopApp.takeInbox): the PC gives them only once. */
    private static synchronized void keep(Context context, JSONObject message) {
        SharedPreferences prefs = context.getSharedPreferences(PREFS, MODE_PRIVATE);
        try {
            JSONArray list = new JSONArray(prefs.getString(KEY_PENDING, "[]"));
            list.put(message);
            JSONArray trimmed = new JSONArray();
            for (int i = Math.max(0, list.length() - KEEP); i < list.length(); i++) trimmed.put(list.get(i));
            prefs.edit().putString(KEY_PENDING, trimmed.toString()).apply();
        } catch (Exception ignored) {
        }
    }

    static synchronized String take(Context context) {
        SharedPreferences prefs = context.getSharedPreferences(PREFS, MODE_PRIVATE);
        String list = prefs.getString(KEY_PENDING, "[]");
        prefs.edit().remove(KEY_PENDING).apply();
        return list;
    }

    // ── notifications ────────────────────────────────────────────────

    private void channels() {
        if (Build.VERSION.SDK_INT < 26) return;
        NotificationManager manager = (NotificationManager) getSystemService(NOTIFICATION_SERVICE);
        NotificationChannel link = new NotificationChannel(CHANNEL_LINK, getString(R.string.inbox_channel_link), NotificationManager.IMPORTANCE_MIN);
        link.setShowBadge(false);
        manager.createNotificationChannel(link);
        NotificationChannel messages = new NotificationChannel(CHANNEL_MESSAGES, getString(R.string.inbox_channel_messages), NotificationManager.IMPORTANCE_HIGH);
        manager.createNotificationChannel(messages);
    }

    private boolean mayNotify() {
        return Build.VERSION.SDK_INT < 33 || checkSelfPermission(android.Manifest.permission.POST_NOTIFICATIONS) == PackageManager.PERMISSION_GRANTED;
    }

    @SuppressWarnings("deprecation")
    private Notification.Builder builder(String channel) {
        return Build.VERSION.SDK_INT >= 26 ? new Notification.Builder(this, channel) : new Notification.Builder(this);
    }

    private PendingIntent openApp() {
        Intent intent = new Intent(this, MainActivity.class).setAction(ACTION_OPEN_INBOX).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK | Intent.FLAG_ACTIVITY_SINGLE_TOP);
        return PendingIntent.getActivity(this, 0, intent, PendingIntent.FLAG_UPDATE_CURRENT | immutable());
    }

    private static int immutable() {
        return Build.VERSION.SDK_INT >= 23 ? PendingIntent.FLAG_IMMUTABLE : 0;
    }

    @SuppressWarnings("deprecation")
    private Notification ongoing(String text) {
        Notification.Builder b = builder(CHANNEL_LINK)
                .setSmallIcon(R.drawable.ic_notify)
                .setContentTitle(getString(R.string.inbox_channel_link))
                .setContentText(text)
                .setOngoing(true)
                .setContentIntent(openApp());
        if (Build.VERSION.SDK_INT < 26) b.setPriority(Notification.PRIORITY_MIN);
        return b.build();
    }

    private void update(String text) {
        if (text.equals(shown)) return;
        shown = text;
        NotificationManager manager = (NotificationManager) getSystemService(NOTIFICATION_SERVICE);
        if (manager != null) manager.notify(ONGOING_ID, ongoing(text));
    }

    @SuppressWarnings("deprecation")
    private void notify(JSONObject m) {
        if (!mayNotify()) return;
        String title = m.optString("title");
        String text = m.optString("text");
        String url = m.isNull("url") ? "" : m.optString("url");
        JSONObject file = m.optJSONObject("file");
        if (title.isEmpty()) title = getString(R.string.inbox_from, m.optString("from", "K-Desktop"));
        String body = !text.isEmpty() ? text : !url.isEmpty() ? url : file != null ? file.optString("name") : "";
        int id = 1000 + (m.optString("id").hashCode() & 0x7ffffff);
        Notification.Builder b = builder(CHANNEL_MESSAGES)
                .setSmallIcon(R.drawable.ic_notify)
                .setContentTitle(title)
                .setContentText(body)
                .setStyle(new Notification.BigTextStyle().bigText(body))
                .setAutoCancel(true)
                .setContentIntent(openApp());
        if (Build.VERSION.SDK_INT < 26) b.setPriority(Notification.PRIORITY_HIGH).setDefaults(Notification.DEFAULT_ALL);
        if (!url.isEmpty()) {
            PendingIntent open = PendingIntent.getActivity(this, id, new Intent(Intent.ACTION_VIEW, Uri.parse(url)).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK),
                    PendingIntent.FLAG_UPDATE_CURRENT | immutable());
            b.addAction(new Notification.Action.Builder(0, getString(R.string.inbox_open), open).build());
        }
        if (!text.isEmpty()) {
            Intent copy = new Intent(this, InboxService.class).setAction(ACTION_COPY).putExtra("text", text).putExtra("id", id);
            PendingIntent copyIntent = PendingIntent.getService(this, id, copy, PendingIntent.FLAG_UPDATE_CURRENT | immutable());
            b.addAction(new Notification.Action.Builder(0, getString(R.string.inbox_copy), copyIntent).build());
        }
        NotificationManager manager = (NotificationManager) getSystemService(NOTIFICATION_SERVICE);
        if (manager != null) manager.notify(id, b.build());
    }
}
