package net.kobayashi82.kdesktop;

import android.annotation.SuppressLint;
import android.app.Activity;
import android.app.PendingIntent;
import android.content.Intent;
import android.content.pm.PackageInstaller;
import android.content.SharedPreferences;
import android.content.res.Configuration;
import android.graphics.Bitmap;
import android.graphics.Color;
import android.net.Uri;
import android.os.Build;
import android.os.Bundle;
import android.os.Handler;
import android.os.Looper;
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

import java.io.InputStream;
import java.io.OutputStream;
import java.net.HttpURLConnection;
import java.net.URL;
import java.util.ArrayList;
import java.util.List;

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
    private static final long LOAD_TIMEOUT_MS = 12000;

    private WebView web;
    private SharedPreferences prefs;
    private Addresses addresses;

    /** The address loaded now (null: the set-up page or nothing yet). */
    private String current;

    /** A failure already made it try the other addresses. */
    private boolean retried;

    private long pausedAt;

    /** An update waiting for the user to allow installing apps from K-Desktop (Settings, then back here). */
    private String pendingUpdate;
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
        web.setBackgroundColor(isDark() ? Color.rgb(13, 13, 15) : Color.rgb(245, 243, 238));
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
        List<String> candidates = addresses.all(server);
        if (candidates.size() < 2) {
            load(server);
            return;
        }
        pick(candidates, best -> {
            if (best != null) use(best);
            else load(server);
        });
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
            pick(others, best -> {
                if (best != null) use(best);
                else openSetup(error);
            });
            return;
        }
        openSetup(error);
    }

    private void openSetup(String error) {
        current = null;
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
            handler.removeCallbacks(loadTimeout);
            retried = false;
        }

        @Override
        public void onPageCommitVisible(WebView view, String url) {
            // Something of the page arrived: K-Desktop answered.
            loaded();
        }

        @Override
        public void onPageFinished(WebView view, String url) {
            // Android 5-6 have no onPageCommitVisible.
            if (Build.VERSION.SDK_INT < 23 && !url.startsWith(SETUP)) loaded();
        }

        @Override
        public void onReceivedHttpError(WebView view, WebResourceRequest request, WebResourceResponse response) {
            // The page itself failing (a proxy with K-Desktop off answers 502/503) is "cannot connect" too.
            if (!request.isForMainFrame() || response.getStatusCode() < 500) return;
            failed("HTTP " + response.getStatusCode());
        }

        @Override
        public boolean shouldOverrideUrlLoading(WebView view, WebResourceRequest request) {
            return openOutside(request.getUrl());
        }

        @SuppressWarnings("deprecation")
        @Override
        public boolean shouldOverrideUrlLoading(WebView view, String url) {
            // Android 5-6 only call this one.
            return openOutside(Uri.parse(url));
        }

        private boolean openOutside(Uri url) {
            String server = server();
            String scheme = url.getScheme();
            if ("file".equals(scheme)) return false;
            for (String address : addresses.all(server)) if (url.toString().startsWith(address)) return false;
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

        /** Updates the app from K-Desktop (url: its api/android). */
        @JavascriptInterface
        public void updateApp(String url) {
            runOnUiThread(() -> MainActivity.this.updateApp(url));
        }
    }
}
