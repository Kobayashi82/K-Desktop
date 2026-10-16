package net.kobayashi82.kdesktop;

import android.annotation.SuppressLint;
import android.app.Activity;
import android.content.Intent;
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

/**
 * K-Desktop for Android: the K-Desktop web app in a window of its own. The first time (or from the app's
 * settings) a local page asks for the address of K-Desktop (the Caddy HTTPS address or http://IP:2502) and
 * it is remembered. Links to other sites open in the browser; the back button goes back inside the app.
 */
public class MainActivity extends Activity {
    private static final String PREFS = "kdesktop";
    private static final String KEY_SERVER = "server";
    private static final String SETUP = "file:///android_asset/setup.html";

    /** A PC that is off often leaves the connection hanging instead of refusing it: give up after this. */
    private static final long LOAD_TIMEOUT_MS = 12000;

    private WebView web;
    private SharedPreferences prefs;
    private final Handler handler = new Handler(Looper.getMainLooper());
    private final Runnable loadTimeout = () -> {
        web.stopLoading();
        openSetup(getString(R.string.timeout));
    };

    @SuppressLint("SetJavaScriptEnabled")
    @Override
    protected void onCreate(Bundle state) {
        super.onCreate(state);
        prefs = getSharedPreferences(PREFS, MODE_PRIVATE);
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
    }

    @Override
    protected void onPause() {
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

    private void openServer() {
        String server = server();
        if (server == null) web.loadUrl(SETUP);
        else load(server);
    }

    private void openSetup(String error) {
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
        w.setStatusBarColor(bar);
        w.setNavigationBarColor(bar);
        int flags = 0;
        if (!dark) {
            flags |= View.SYSTEM_UI_FLAG_LIGHT_STATUS_BAR;
            if (Build.VERSION.SDK_INT >= 26) flags |= View.SYSTEM_UI_FLAG_LIGHT_NAVIGATION_BAR;
        }
        w.getDecorView().setSystemUiVisibility(flags);
    }

    private String versionName() {
        try {
            return getPackageManager().getPackageInfo(getPackageName(), 0).versionName;
        } catch (Exception e) {
            return "1";
        }
    }

    // ── web view ─────────────────────────────────────────────────────

    private class Client extends WebViewClient {
        @Override
        public void onPageStarted(WebView view, String url, Bitmap favicon) {
            if (url.startsWith(SETUP)) handler.removeCallbacks(loadTimeout);
        }

        @Override
        public void onPageCommitVisible(WebView view, String url) {
            // Something of the page arrived: K-Desktop answered.
            handler.removeCallbacks(loadTimeout);
        }

        @Override
        public void onReceivedHttpError(WebView view, WebResourceRequest request, WebResourceResponse response) {
            // The page itself failing (a proxy with K-Desktop off answers 502/503) is "cannot connect" too.
            if (!request.isForMainFrame() || response.getStatusCode() < 500) return;
            handler.removeCallbacks(loadTimeout);
            openSetup("HTTP " + response.getStatusCode());
        }

        @Override
        public boolean shouldOverrideUrlLoading(WebView view, WebResourceRequest request) {
            Uri url = request.getUrl();
            String server = server();
            String scheme = url.getScheme();
            if ("file".equals(scheme)) return false;
            if (server != null && url.toString().startsWith(server)) return false;
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
            handler.removeCallbacks(loadTimeout);
            openSetup(String.valueOf(error.getDescription()));
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
            prefs.edit().putString(KEY_SERVER, server).apply();
            String fragment = Uri.parse(input.trim().matches("(?i)^https?://.*") ? input.trim() : "http://" + input.trim()).getEncodedFragment();
            final String target = fragment != null ? server + "#" + fragment : server;
            runOnUiThread(() -> load(target));
            return server;
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
    }
}
