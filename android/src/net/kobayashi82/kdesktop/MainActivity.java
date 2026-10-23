package net.kobayashi82.kdesktop;

import android.Manifest;
import android.annotation.SuppressLint;
import android.app.Activity;
import android.app.PendingIntent;
import android.content.Intent;
import android.content.pm.PackageInstaller;
import android.content.SharedPreferences;
import android.content.pm.PackageManager;
import android.content.res.Configuration;
import android.graphics.Bitmap;
import android.graphics.Color;
import android.media.AudioFormat;
import android.media.AudioRecord;
import android.media.MediaRecorder;
import android.net.Uri;
import android.os.Build;
import android.os.Bundle;
import android.os.Handler;
import android.os.Looper;
import android.util.Base64;
import android.view.View;
import android.view.Window;
import android.webkit.CookieManager;
import android.webkit.JavascriptInterface;
import android.webkit.WebChromeClient;
import android.webkit.WebResourceError;
import android.webkit.WebResourceRequest;
import android.webkit.WebResourceResponse;
import android.webkit.WebSettings;
import android.webkit.WebView;
import android.webkit.WebViewClient;

import org.json.JSONArray;
import org.json.JSONObject;

import java.io.ByteArrayOutputStream;
import java.io.InputStream;
import java.io.OutputStream;
import java.net.HttpURLConnection;
import java.net.URL;
import java.util.ArrayList;
import java.util.HashSet;
import java.util.List;
import java.util.Locale;
import java.util.Set;

/**
 * K-Desktop for Android: the K-Desktop web app in a window of its own. The first time (or from the app's
 * settings) a local page asks for the address of K-Desktop (the Caddy HTTPS address or http://IP:2502) and
 * it is remembered. Links to other sites open in the browser; the back button goes back inside the app.
 * Once paired, K-Desktop tells the app all its addresses (see {@link Addresses}): on opening it uses the one that
 * answers (home or away) with the same token, so one pairing is enough.
 */
public class MainActivity extends Activity {
    private static final String PREFS = "kdesktop";
    private static final String KEY_SERVER = "server";
    private static final String KEY_TOKEN = "token";

    /** Back after this long in the background, the app checks whether another address suits better (left home…). */
    private static final long RECHECK_AFTER_MS = 20000;
    private static final String SETUP = "file:///android_asset/setup.html";

    /** The system installer answers here (onNewIntent) while updating the app. */
    private static final String ACTION_INSTALLED = "net.kobayashi82.kdesktop.INSTALLED";

    /** A PC that is off often leaves the connection hanging instead of refusing it: give up after this. */
    private static final long LOAD_TIMEOUT_MS = 8000;

    private WebView web;
    private SharedPreferences prefs;
    private Addresses addresses;

    /** The address loaded now (null: the set-up page or nothing yet). */
    private String current;

    /** A failure already made it try the other addresses (or none answered already: nothing left to try). */
    private boolean retried;

    /** Each search for the PC; a newer one (or the set-up page) makes the answer of an older one irrelevant. */
    private int attempt;

    /** The "connecting" page is on screen; until then what comes next waits (it would replace it unseen). */
    private boolean connectingShown;
    private Runnable afterConnecting;
    private final Runnable connectingFallback = () -> connectingVisible();

    private long pausedAt;

    /**
     * Sites the PC's address sent the app to while loading (the login page of a proxy in front of it: Authelia…). They
     * open here, not in the browser: the login has to happen in the app, whose cookies then reach K-Desktop.
     */
    private final Set<String> loginHosts = new HashSet<>();

    /** A page of K-Desktop is loading and nothing of it has arrived yet (Android 5-6 cannot tell a redirect apart). */
    private boolean loading;

    /** An update waiting for the user to allow installing apps from K-Desktop (Settings, then back here). */
    private String pendingUpdate;

    /** Voice orders: recorded here (a browser only lets pages use the microphone over HTTPS). */
    private static final int VOICE_PERMISSION = 7;
    private static final int VOICE_RATE = 16000;
    private static final int VOICE_MAX_BYTES = VOICE_RATE * 2 * 31 / 2; // 15.5 s, under what K-Desktop accepts
    private volatile boolean voiceRun;
    private volatile boolean voiceSend;
    private final Handler handler = new Handler(Looper.getMainLooper());
    private final Runnable loadTimeout = () -> {
        web.stopLoading();
        failed(getString(R.string.timeout));
    };

    @SuppressLint("SetJavaScriptEnabled")
    @Override
    protected void onCreate(Bundle state) {
        super.onCreate(state);
        prefs = getSharedPreferences(PREFS, MODE_PRIVATE);
        addresses = new Addresses(prefs);
        paintBars();

        web = new WebView(this);
        // See-through: until a page draws, the window shows the logo (res/drawable/splash.xml) instead of a blank screen.
        web.setBackgroundColor(Color.TRANSPARENT);
        setContentView(web);

        WebSettings s = web.getSettings();
        s.setJavaScriptEnabled(true);
        s.setDomStorageEnabled(true);
        s.setDatabaseEnabled(true);
        s.setMediaPlaybackRequiresUserGesture(false);
        s.setMixedContentMode(WebSettings.MIXED_CONTENT_COMPATIBILITY_MODE);
        s.setAllowFileAccess(false);
        // The web app shows "App" as the browser in the paired device's name and offers "Change server".
        s.setUserAgentString(s.getUserAgentString() + " KDesktopApp/" + versionName());
        CookieManager.getInstance().setAcceptCookie(true);
        CookieManager.getInstance().setAcceptThirdPartyCookies(web, false);

        web.addJavascriptInterface(new Bridge(), "KDesktopApp");
        web.setWebChromeClient(new WebChromeClient());
        web.setWebViewClient(new Client());

        if (state != null && web.restoreState(state) != null) return;
        String shared = sharedLink(getIntent());
        if (shared != null) openLink(shared);
        else openServer();
    }

    @Override
    protected void onNewIntent(Intent intent) {
        super.onNewIntent(intent);
        if (ACTION_INSTALLED.equals(intent.getAction())) {
            installerAnswered(intent);
            return;
        }
        String shared = sharedLink(intent);
        if (shared != null) openLink(shared);
    }

    @Override
    protected void onSaveInstanceState(Bundle out) {
        super.onSaveInstanceState(out);
        web.saveState(out);
    }

    @Override
    protected void onResume() {
        super.onResume();
        web.onResume();
        // Left home (or came back) meanwhile: maybe another address of the PC suits better now.
        if (pausedAt > 0 && System.currentTimeMillis() - pausedAt > RECHECK_AFTER_MS && current != null) {
            List<String> candidates = addresses.all(server());
            if (candidates.size() > 1) pick(candidates, best -> {
                if (best != null && !best.equals(current)) use(best);
            });
        }
        pausedAt = 0;
        if (pendingUpdate != null) {
            String url = pendingUpdate;
            pendingUpdate = null;
            if (mayInstall()) updateApp(url);
            else tellPage("cancelled", "");
        }
    }

    @Override
    protected void onPause() {
        pausedAt = System.currentTimeMillis();
        web.onPause();
        CookieManager.getInstance().flush();
        super.onPause();
    }

    @Override
    public void onConfigurationChanged(Configuration config) {
        super.onConfigurationChanged(config);
        paintBars();
    }

    @SuppressWarnings("deprecation")
    @Override
    public void onBackPressed() {
        // Submenus are history entries in the web app: back closes them first.
        if (web.canGoBack()) web.goBack();
        else super.onBackPressed();
    }

    // ── server ───────────────────────────────────────────────────────

    private String server() {
        return prefs.getString(KEY_SERVER, null);
    }

    /** Opens K-Desktop at the address that answers now (the only one known, or the best of several). */
    private void openServer() {
        String server = server();
        if (server == null) {
            current = null;
            web.loadUrl(SETUP);
            return;
        }
        // Something to look at at once: finding out that the PC is off can take a few seconds.
        showConnecting();
        int mine = ++attempt;
        pick(addresses.all(server), best -> whenConnectingShown(() -> {
            if (mine != attempt) return;
            if (best != null) {
                use(best);
                return;
            }
            // Nobody answers: still load it (over HTTPS the web app opens from its cache with the last commands),
            // but there is no point in asking the other addresses again when it fails.
            retried = true;
            load(server);
        }));
    }

    /** "Connecting to K-Desktop…" (the set-up page in its waiting mode), while the addresses are asked. */
    private void showConnecting() {
        String server = server();
        loading = false;
        connectingShown = false;
        afterConnecting = null;
        web.loadUrl(SETUP + "?connecting=1" + (server != null ? "&server=" + Uri.encode(server) : ""));
        // Should the page never say it is visible (old WebViews), go on anyway.
        handler.removeCallbacks(connectingFallback);
        handler.postDelayed(connectingFallback, 1500);
    }

    private void whenConnectingShown(Runnable next) {
        if (connectingShown) next.run();
        else afterConnecting = next;
    }

    private void connectingVisible() {
        handler.removeCallbacks(connectingFallback);
        connectingShown = true;
        Runnable next = afterConnecting;
        afterConnecting = null;
        if (next != null) next.run();
    }

    /** Uses <code>address</code> from now on and opens it. */
    private void use(String address) {
        prefs.edit().putString(KEY_SERVER, address).apply();
        load(address);
    }

    private interface Picked {
        void run(String best);
    }

    /** Asks every address in the background; <code>then</code> runs on the UI thread with the one to use (or null). */
    private void pick(List<String> candidates, Picked then) {
        new Thread(() -> {
            String best = addresses.pick(candidates);
            runOnUiThread(() -> then.run(best));
        }).start();
    }

    /** The page did not load: first try the other addresses of the PC, then show the set-up page with the error. */
    private void failed(String error) {
        handler.removeCallbacks(loadTimeout);
        String server = server();
        List<String> others = new ArrayList<>(addresses.all(server));
        others.remove(current);
        if (!retried && server != null && !others.isEmpty()) {
            retried = true;
            showConnecting();
            int mine = ++attempt;
            pick(others, best -> whenConnectingShown(() -> {
                if (mine != attempt) return;
                if (best != null) use(best);
                else openSetup(error);
            }));
            return;
        }
        openSetup(error);
    }

    private void openSetup(String error) {
        attempt++; // a search still running must not take the user away from here
        handler.removeCallbacks(loadTimeout);
        current = null;
        loading = false;
        String url = SETUP;
        if (server() != null) url += "?server=" + Uri.encode(server());
        if (error != null) url += (url.contains("?") ? "&" : "?") + "error=" + Uri.encode(error);
        web.loadUrl(url);
    }

    /** "kdesktop.casa.net", "192.168.1.5:2502", a full URL or a pairing link → the app's base address. */
    static String normalize(String input) {
        if (input == null) return null;
        String text = input.trim();
        if (text.isEmpty()) return null;
        if (!text.matches("(?i)^https?://.*")) {
            // An IP or a host with a port is most likely the plain web server; a bare name, the HTTPS proxy.
            boolean local = text.matches("^[0-9.]+(:\\d+)?(/.*)?$") || text.matches("^[^/]+:\\d+(/.*)?$");
            text = (local ? "http://" : "https://") + text;
        }
        Uri uri = Uri.parse(text);
        if (uri.getHost() == null || uri.getHost().isEmpty()) return null;
        String path = uri.getPath() == null ? "/" : uri.getPath();
        if (!path.endsWith("/")) path = path.substring(0, path.lastIndexOf('/') + 1);
        if (path.isEmpty()) path = "/";
        return uri.getScheme().toLowerCase() + "://" + uri.getEncodedAuthority() + path;
    }

    /** A pairing link (…/#pair=CODE): remember its server and open it, so the app pairs at once. */
    private void openLink(String link) {
        String server = normalize(link);
        if (server == null) {
            openServer();
            return;
        }
        prefs.edit().putString(KEY_SERVER, server).apply();
        String fragment = Uri.parse(link.trim()).getEncodedFragment();
        load(fragment != null ? server + "#" + fragment : server);
    }

    /** Loads a page of K-Desktop, with a deadline: a hanging connection shows the setup page with the error. */
    private void load(String url) {
        current = normalize(url);
        loading = true;
        handler.removeCallbacks(loadTimeout);
        handler.postDelayed(loadTimeout, LOAD_TIMEOUT_MS);
        web.loadUrl(url);
    }

    private static String sharedLink(Intent intent) {
        if (intent == null) return null;
        if (Intent.ACTION_SEND.equals(intent.getAction())) {
            String text = intent.getStringExtra(Intent.EXTRA_TEXT);
            if (text == null) return null;
            java.util.regex.Matcher m = java.util.regex.Pattern.compile("https?://\\S+").matcher(text);
            return m.find() ? m.group() : null;
        }
        return null;
    }

    // ── look ─────────────────────────────────────────────────────────

    private boolean isDark() {
        return (getResources().getConfiguration().uiMode & Configuration.UI_MODE_NIGHT_MASK) == Configuration.UI_MODE_NIGHT_YES;
    }

    @SuppressWarnings("deprecation")
    private void paintBars() {
        Window w = getWindow();
        boolean dark = isDark();
        int bar = dark ? Color.rgb(13, 13, 15) : Color.rgb(245, 243, 238);
        // Before Android 6 (status bar) and 8 (navigation bar) their icons are always white: a light bar would hide them.
        boolean lightStatus = !dark && Build.VERSION.SDK_INT >= 23;
        boolean lightNavigation = !dark && Build.VERSION.SDK_INT >= 26;
        w.setStatusBarColor(dark || lightStatus ? bar : Color.BLACK);
        w.setNavigationBarColor(dark || lightNavigation ? bar : Color.BLACK);
        int flags = 0;
        if (lightStatus) flags |= View.SYSTEM_UI_FLAG_LIGHT_STATUS_BAR;
        if (lightNavigation) flags |= View.SYSTEM_UI_FLAG_LIGHT_NAVIGATION_BAR;
        w.getDecorView().setSystemUiVisibility(flags);
    }

    private String versionName() {
        try {
            return getPackageManager().getPackageInfo(getPackageName(), 0).versionName;
        } catch (Exception e) {
            return "1";
        }
    }

    @SuppressWarnings("deprecation")
    private int versionCode() {
        try {
            return getPackageManager().getPackageInfo(getPackageName(), 0).versionCode;
        } catch (Exception e) {
            return 0;
        }
    }

    // ── updating the app ─────────────────────────────────────────────

    /**
     * Downloads the app K-Desktop carries (api/android, with the pairing token) and hands it to the system installer,
     * which asks the user. The page hears how it went through window.kdAppUpdate(state, detail).
     */
    private void updateApp(String url) {
        // Android 8+ asks once whether K-Desktop may install apps: ask first, carry on when the user is back.
        if (!mayInstall()) {
            pendingUpdate = url;
            try {
                startActivity(new Intent(android.provider.Settings.ACTION_MANAGE_UNKNOWN_APP_SOURCES, Uri.parse("package:" + getPackageName())));
                return;
            } catch (Exception e) {
                pendingUpdate = null;
            }
        }
        new Thread(() -> {
            PackageInstaller.Session session = null;
            try {
                HttpURLConnection http = (HttpURLConnection) new URL(url).openConnection();
                http.setConnectTimeout(15000);
                http.setReadTimeout(60000);
                String token = prefs.getString(KEY_TOKEN, "");
                if (!token.isEmpty()) http.setRequestProperty("Authorization", "Bearer " + token);
                String cookie = CookieManager.getInstance().getCookie(url);
                if (cookie != null) http.setRequestProperty("Cookie", cookie);
                if (http.getResponseCode() != 200) throw new Exception("HTTP " + http.getResponseCode());
                long length = http.getContentLength();

                PackageInstaller installer = getPackageManager().getPackageInstaller();
                PackageInstaller.SessionParams params = new PackageInstaller.SessionParams(PackageInstaller.SessionParams.MODE_FULL_INSTALL);
                if (length > 0) params.setSize(length);
                session = installer.openSession(installer.createSession(params));
                try (InputStream in = http.getInputStream(); OutputStream out = session.openWrite("KDesktop.apk", 0, length)) {
                    byte[] buffer = new byte[65536];
                    for (int n; (n = in.read(buffer)) > 0; ) out.write(buffer, 0, n);
                    session.fsync(out);
                }
                Intent answer = new Intent(this, MainActivity.class).setAction(ACTION_INSTALLED);
                // The installer adds its answer to the intent: it must be mutable (Android 12+).
                int flags = PendingIntent.FLAG_UPDATE_CURRENT | (Build.VERSION.SDK_INT >= 31 ? PendingIntent.FLAG_MUTABLE : 0);
                session.commit(PendingIntent.getActivity(this, 0, answer, flags).getIntentSender());
                session.close();
                tellPage("installing", "");
            } catch (Exception e) {
                if (session != null) session.abandon();
                tellPage("error", String.valueOf(e.getMessage()));
            }
        }).start();
    }

    private boolean mayInstall() {
        return Build.VERSION.SDK_INT < 26 || getPackageManager().canRequestPackageInstalls();
    }

    /** The system installer: it needs the user's confirmation (shown now), or it finished. */
    @SuppressWarnings("deprecation")
    private void installerAnswered(Intent intent) {
        int status = intent.getIntExtra(PackageInstaller.EXTRA_STATUS, PackageInstaller.STATUS_FAILURE);
        if (status == PackageInstaller.STATUS_PENDING_USER_ACTION) {
            Intent confirm = intent.getParcelableExtra(Intent.EXTRA_INTENT);
            if (confirm != null) {
                confirm.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK);
                startActivity(confirm);
            }
            return;
        }
        if (status == PackageInstaller.STATUS_SUCCESS) return;
        String message = intent.getStringExtra(PackageInstaller.EXTRA_STATUS_MESSAGE);
        tellPage(status == PackageInstaller.STATUS_FAILURE_ABORTED ? "cancelled" : "error", message == null ? "" : message);
    }

    // ── voice orders ─────────────────────────────────────────────────

    private void startVoice() {
        if (Build.VERSION.SDK_INT >= 23 && checkSelfPermission(Manifest.permission.RECORD_AUDIO) != PackageManager.PERMISSION_GRANTED) {
            requestPermissions(new String[] { Manifest.permission.RECORD_AUDIO }, VOICE_PERMISSION);
            return;
        }
        recordVoice();
    }

    @Override
    public void onRequestPermissionsResult(int requestCode, String[] permissions, int[] results) {
        super.onRequestPermissionsResult(requestCode, permissions, results);
        if (requestCode != VOICE_PERMISSION) return;
        if (results.length > 0 && results[0] == PackageManager.PERMISSION_GRANTED) recordVoice();
        else js("window.kdVoiceError && window.kdVoiceError('denied')");
    }

    @SuppressLint("MissingPermission")
    private void recordVoice() {
        int min = AudioRecord.getMinBufferSize(VOICE_RATE, AudioFormat.CHANNEL_IN_MONO, AudioFormat.ENCODING_PCM_16BIT);
        AudioRecord recorder;
        try {
            recorder = new AudioRecord(MediaRecorder.AudioSource.VOICE_RECOGNITION, VOICE_RATE,
                    AudioFormat.CHANNEL_IN_MONO, AudioFormat.ENCODING_PCM_16BIT, Math.max(min, 3200) * 2);
        } catch (Exception e) {
            js("window.kdVoiceError && window.kdVoiceError('mic')");
            return;
        }
        if (recorder.getState() != AudioRecord.STATE_INITIALIZED) {
            recorder.release();
            js("window.kdVoiceError && window.kdVoiceError('mic')");
            return;
        }
        voiceRun = true;
        voiceSend = false;
        new Thread(() -> record(recorder), "K-Desktop voice").start();
    }

    private void record(AudioRecord recorder) {
        ByteArrayOutputStream out = new ByteArrayOutputStream(VOICE_RATE * 2 * 6);
        short[] samples = new short[VOICE_RATE / 20]; // 50 ms
        byte[] bytes = new byte[samples.length * 2];
        try {
            recorder.startRecording();
            while (voiceRun && out.size() < VOICE_MAX_BYTES) {
                int n = recorder.read(samples, 0, samples.length);
                if (n < 0) throw new IllegalStateException("read " + n);
                if (n == 0) continue;
                double sum = 0;
                for (int i = 0; i < n; i++) {
                    sum += (double) samples[i] * samples[i];
                    bytes[2 * i] = (byte) (samples[i] & 0xff);
                    bytes[2 * i + 1] = (byte) ((samples[i] >> 8) & 0xff);
                }
                out.write(bytes, 0, n * 2);
                js("window.kdVoiceLevel && window.kdVoiceLevel(" + String.format(Locale.ROOT, "%.4f", Math.sqrt(sum / n) / 32768.0) + ")");
            }
            // Full: wait for the page to finish (it stops itself at its own limit).
            for (int i = 0; i < 60 && voiceRun; i++) Thread.sleep(50);
        } catch (Exception e) {
            voiceSend = false;
            js("window.kdVoiceError && window.kdVoiceError('mic')");
        } finally {
            try { recorder.stop(); } catch (Exception ignored) { }
            recorder.release();
        }
        if (voiceSend) js("window.kdVoiceData && window.kdVoiceData('" + Base64.encodeToString(out.toByteArray(), Base64.NO_WRAP) + "')");
    }

    private void js(String script) {
        runOnUiThread(() -> web.evaluateJavascript(script, null));
    }

    @Override
    protected void onStop() {
        voiceSend = false;
        voiceRun = false;
        super.onStop();
    }

    private void tellPage(String state, String detail) {
        String script = "window.kdAppUpdate && window.kdAppUpdate(" + JSONObject.quote(state) + ", " + JSONObject.quote(detail) + ")";
        runOnUiThread(() -> web.evaluateJavascript(script, null));
    }

    // ── web view ─────────────────────────────────────────────────────

    private class Client extends WebViewClient {
        @Override
        public void onPageStarted(WebView view, String url, Bitmap favicon) {
            if (url.startsWith(SETUP)) handler.removeCallbacks(loadTimeout);
        }

        private void loaded() {
            loading = false;
            handler.removeCallbacks(loadTimeout);
            retried = false;
        }

        @Override
        public void onPageCommitVisible(WebView view, String url) {
            // Something of the page arrived: K-Desktop answered (the set-up page itself does not count).
            if (!url.startsWith(SETUP)) loaded();
            else if (url.contains("connecting=1")) connectingVisible();
        }

        @Override
        public void onPageFinished(WebView view, String url) {
            // Android 5-6 have no onPageCommitVisible.
            if (Build.VERSION.SDK_INT >= 23) return;
            if (!url.startsWith(SETUP)) loaded();
            else if (url.contains("connecting=1")) connectingVisible();
        }

        @Override
        public void onReceivedHttpError(WebView view, WebResourceRequest request, WebResourceResponse response) {
            // The page itself failing (a proxy with K-Desktop off answers 502/503) is "cannot connect" too.
            if (!request.isForMainFrame() || response.getStatusCode() < 500) return;
            failed("HTTP " + response.getStatusCode());
        }

        @Override
        public boolean shouldOverrideUrlLoading(WebView view, WebResourceRequest request) {
            return openOutside(request.getUrl(), request.isForMainFrame() && request.isRedirect());
        }

        @SuppressWarnings("deprecation")
        @Override
        public boolean shouldOverrideUrlLoading(WebView view, String url) {
            // Android 5-6 only call this one.
            return openOutside(Uri.parse(url), loading);
        }

        private boolean openOutside(Uri url, boolean redirect) {
            String server = server();
            String scheme = url.getScheme();
            if ("file".equals(scheme)) return false;
            for (String address : addresses.all(server)) if (url.toString().startsWith(address)) return false;
            // Sent there by the PC's address itself: a login in front of K-Desktop, which comes back here when done.
            String host = url.getHost();
            if (host != null && (redirect || loginHosts.contains(host.toLowerCase(Locale.ROOT)))) {
                loginHosts.add(host.toLowerCase(Locale.ROOT));
                return false;
            }
            // Anything else (a command's web page, a help link…) goes to the browser.
            try {
                startActivity(new Intent(Intent.ACTION_VIEW, url));
            } catch (Exception ignored) {
            }
            return true;
        }

        @Override
        public void onReceivedError(WebView view, WebResourceRequest request, WebResourceError error) {
            // Only the page itself: the web app copes with failed API calls (it keeps the last commands).
            if (!request.isForMainFrame()) return;
            if (request.getUrl().toString().startsWith(SETUP)) return;
            failed(String.valueOf(error.getDescription()));
        }

        @SuppressWarnings("deprecation")
        @Override
        public void onReceivedError(WebView view, int errorCode, String description, String failingUrl) {
            // Android 5 only calls this one (and only for the page itself).
            if (Build.VERSION.SDK_INT >= 23 || failingUrl == null || failingUrl.startsWith(SETUP)) return;
            failed(description);
        }
    }

    /** What the pages can ask the app: window.KDesktopApp. */
    private class Bridge {
        @JavascriptInterface
        public String getServer() {
            String s = server();
            return s == null ? "" : s;
        }

        @JavascriptInterface
        public String setServer(String input) {
            String server = normalize(input);
            if (server == null) return "";
            // Another K-Desktop: the addresses of the previous one no longer apply.
            if (!addresses.knows(server)) addresses.forget();
            prefs.edit().putString(KEY_SERVER, server).apply();
            String fragment = Uri.parse(input.trim().matches("(?i)^https?://.*") ? input.trim() : "http://" + input.trim()).getEncodedFragment();
            final String target = fragment != null ? server + "#" + fragment : server;
            runOnUiThread(() -> load(target));
            return server;
        }

        /** The pairing token: kept by the app, so it works at every address of the PC. */
        @JavascriptInterface
        public String getToken() {
            return prefs.getString(KEY_TOKEN, "");
        }

        @JavascriptInterface
        public void setToken(String token) {
            if (token == null || token.isEmpty()) prefs.edit().remove(KEY_TOKEN).apply();
            else prefs.edit().putString(KEY_TOKEN, token).apply();
        }

        /** {"machine": "PC", "addresses": ["https://…/", "http://192.168.1.5:2502/"]}, from the session. */
        @JavascriptInterface
        public void setAddresses(String json) {
            try {
                JSONObject info = new JSONObject(json);
                JSONArray list = info.getJSONArray("addresses");
                List<String> clean = new ArrayList<>();
                for (int i = 0; i < list.length(); i++) {
                    String address = normalize(list.getString(i));
                    if (address != null && !clean.contains(address)) clean.add(address);
                }
                if (!clean.isEmpty()) addresses.save(clean, info.optString("machine"));
            } catch (Exception ignored) {
            }
        }

        @JavascriptInterface
        public void changeServer() {
            runOnUiThread(() -> openSetup(null));
        }

        @JavascriptInterface
        public void retry() {
            runOnUiThread(MainActivity.this::openServer);
        }

        @JavascriptInterface
        public String version() {
            return versionName();
        }

        @JavascriptInterface
        public int versionCode() {
            return MainActivity.this.versionCode();
        }

        /** Starts recording a voice order (asks for the microphone the first time). The level goes to kdVoiceLevel. */
        @JavascriptInterface
        public String startVoice() {
            runOnUiThread(MainActivity.this::startVoice);
            return "ok";
        }

        /** Done speaking: the recording goes to kdVoiceData (base64, 16-bit PCM, 16 kHz, mono). */
        @JavascriptInterface
        public void stopVoice() {
            voiceSend = true;
            voiceRun = false;
        }

        @JavascriptInterface
        public void cancelVoice() {
            voiceSend = false;
            voiceRun = false;
        }

        /** Updates the app from K-Desktop (url: its api/android). */
        @JavascriptInterface
        public void updateApp(String url) {
            runOnUiThread(() -> MainActivity.this.updateApp(url));
        }
    }
}
