package net.kobayashi82.kdesktop;

import android.Manifest;
import android.annotation.SuppressLint;
import android.app.Activity;
import android.app.DownloadManager;
import android.app.PendingIntent;
import android.content.ClipData;
import android.content.ClipboardManager;
import android.content.Context;
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
import android.net.ConnectivityManager;
import android.net.Network;
import android.net.NetworkInfo;
import android.net.Uri;
import android.os.Build;
import android.os.Bundle;
import android.os.Environment;
import android.os.Handler;
import android.os.Looper;
import android.os.PowerManager;
import android.util.Base64;
import android.view.View;
import android.view.Window;
import android.webkit.CookieManager;
import android.webkit.JavascriptInterface;
import android.webkit.PermissionRequest;
import android.webkit.WebChromeClient;
import android.webkit.WebResourceError;
import android.webkit.WebResourceRequest;
import android.webkit.WebResourceResponse;
import android.webkit.WebSettings;
import android.webkit.WebView;
import android.webkit.WebViewClient;
import android.widget.Toast;

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
import java.util.UUID;

/**
 * K-Desktop for Android: the K-Desktop web app of each of your PCs in a window of its own. The app keeps a list of
 * paired PCs ({@link Pcs}); it opens the last one used at the address that answers (home or away, see {@link Addresses})
 * and, when none does, shows its own page of PCs (assets/setup.html): what each one says, connect by hand, wake it,
 * rename it, forget it, add another (typing its address or scanning the QR of K-Desktop). Links to other sites open in
 * the browser; the back button goes back inside the app.
 */
public class MainActivity extends Activity {
    private static final String PREFS = "kdesktop";

    /** What the pages may count on from this app (window.KDesktopApp.api()): 2 = list of PCs, wake, QR. */
    private static final int BRIDGE_API = 2;

    /** Back after this long in the background, the app checks whether another address suits better (left home…). */
    private static final long RECHECK_AFTER_MS = 20000;
    private static final String SETUP = "file:///android_asset/setup.html";

    /** The system installer answers here (onNewIntent) while updating the app. */
    private static final String ACTION_INSTALLED = "net.kobayashi82.kdesktop.INSTALLED";

    /** A PC that is off often leaves the connection hanging instead of refusing it: give up after this. */
    private static final long LOAD_TIMEOUT_MS = 8000;

    /** How long «Encender» waits for the PC to answer. */
    private static final long WAKE_WAIT_MS = 120000;

    private WebView web;
    private SharedPreferences prefs;
    private Pcs pcs;

    /** The address loaded now (null: the app's own page or nothing yet). */
    private volatile String current;

    /** The app's own page (the PCs) is the one on screen: only it may manage the list. */
    private volatile boolean onSetup;

    /** A failure already made it try the other addresses (or none answered already: nothing left to try). */
    private boolean retried;

    /** Each search for a PC; a newer one (or the app's page) makes the answer of an older one irrelevant. */
    private int attempt;

    /** The «connecting» page is on screen; until then what comes next waits (it would replace it unseen). */
    private boolean connectingShown;
    private Runnable afterConnecting;
    private final Runnable connectingFallback = () -> connectingVisible();

    private long pausedAt;
    private boolean resumed;

    /** Each «Encender»: a newer one, or cancelling, stops the wait of an older one. */
    private volatile int wakeRun;

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

    /** «Avisos con la app cerrada»: Android 13+ asks for the notifications first. */
    private static final int INBOX_PERMISSION = 8;

    /** Scanning K-Desktop's QR on the app's page: the camera. */
    private static final int CAMERA_PERMISSION = 9;
    private PermissionRequest pendingCamera;

    /** Opened from a notification of K-Desktop: the page shows what arrived once it is loaded. */
    private boolean openInboxPending;
    private static final int VOICE_RATE = 16000;
    private static final int VOICE_MAX_BYTES = VOICE_RATE * 2 * 31 / 2; // 15.5 s, under what K-Desktop accepts
    private volatile boolean voiceRun;
    private volatile boolean voiceSend;
    private final Handler handler = new Handler(Looper.getMainLooper());
    private final Runnable loadTimeout = () -> {
        web.stopLoading();
        failed(getString(R.string.timeout));
    };
    private final Runnable networkSettled = this::networkChanged;

    @SuppressLint("SetJavaScriptEnabled")
    @Override
    protected void onCreate(Bundle state) {
        super.onCreate(state);
        prefs = getSharedPreferences(PREFS, MODE_PRIVATE);
        pcs = new Pcs(prefs);
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
        web.setWebChromeClient(new Chrome());
        web.setWebViewClient(new Client());
        watchNetwork();

        // Messages with the app closed: the service runs again whenever the app is opened (it may have been stopped).
        if (InboxService.enabled(this)) InboxService.start(this);
        if (InboxService.ACTION_OPEN_INBOX.equals(getIntent().getAction())) openInboxPending = true;

        if (state != null && web.restoreState(state) != null) return;
        String shared = sharedLink(getIntent());
        if (shared != null) openLink(shared);
        else openCurrent();
    }

    @Override
    protected void onNewIntent(Intent intent) {
        super.onNewIntent(intent);
        if (ACTION_INSTALLED.equals(intent.getAction())) {
            installerAnswered(intent);
            return;
        }
        if (InboxService.ACTION_OPEN_INBOX.equals(intent.getAction())) {
            if (current != null && !loading) js("window.kdOpenInbox && window.kdOpenInbox()");
            else openInboxPending = true;
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
        resumed = true;
        web.onResume();
        // Left home (or came back) meanwhile: maybe another address of the PC suits better now.
        if (pausedAt > 0 && System.currentTimeMillis() - pausedAt > RECHECK_AFTER_MS && current != null) recheck();
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
        resumed = false;
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

    // ── PCs and their addresses ──────────────────────────────────────

    private Pcs.Pc currentPc() {
        return pcs.current();
    }

    /** Opens the last PC used at the address that answers; none saved: the welcome page. */
    private void openCurrent() {
        Pcs.Pc pc = currentPc();
        if (pc == null) {
            openPcs(null, null);
            return;
        }
        connectTo(pc, null);
    }

    /** Connects to a PC: at <code>address</code>, or at the best of its addresses now. None answers: the page of PCs. */
    private void connectTo(Pcs.Pc pc, String address) {
        pcs.setCurrent(pc.id);
        retried = false;
        if (address != null) {
            use(pc, address);
            return;
        }
        // Something to look at at once: finding out that the PC is off can take a few seconds.
        showConnecting(pc);
        int mine = ++attempt;
        pick(pc, best -> whenConnectingShown(() -> {
            if (mine != attempt) return;
            if (best != null) use(pc, best);
            else openPcs(pc.id, "offline");
        }));
    }

    /** "Connecting to <PC>…" (the app's page in its waiting mode), while the addresses are asked. */
    private void showConnecting(Pcs.Pc pc) {
        loading = false;
        connectingShown = false;
        afterConnecting = null;
        current = null;
        web.loadUrl(SETUP + "?connecting=" + Uri.encode(pc.id));
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

    /** Uses <code>address</code> for the PC from now on and opens it. */
    private void use(Pcs.Pc pc, String address) {
        pc.last = address;
        if (!pc.addresses.contains(address)) pc.addresses.add(address);
        pc.lastUsed = System.currentTimeMillis();
        pcs.save(pc);
        pcs.setCurrent(pc.id);
        load(address);
    }

    private interface Picked {
        void run(String best);
    }

    /** Asks every address of the PC in the background; <code>then</code> runs on the UI thread with the one to use (or null). */
    private void pick(Pcs.Pc pc, Picked then) {
        new Thread(() -> {
            String best = Addresses.pick(pc.candidates(), pc);
            runOnUiThread(() -> then.run(best));
        }).start();
    }

    /** Back from the background, or the phone changed network: the address in use, if it no longer answers, for another. */
    private void recheck() {
        Pcs.Pc pc = currentPc();
        String now = current;
        if (pc == null || now == null) return;
        new Thread(() -> {
            if (Addresses.ask(now, pc) == Addresses.ANSWERS) return;
            String best = Addresses.pick(pc.candidates(), pc);
            runOnUiThread(() -> {
                if (best != null && !best.equals(current) && now.equals(current)) use(pc, best);
            });
        }).start();
    }

    /** Wi-Fi ↔ mobile data and the like: once it settles, the address may need to change (without reloading if it still works). */
    @SuppressWarnings("deprecation")
    private void watchNetwork() {
        if (Build.VERSION.SDK_INT < 24) return;
        ConnectivityManager cm = (ConnectivityManager) getSystemService(Context.CONNECTIVITY_SERVICE);
        if (cm == null) return;
        try {
            cm.registerDefaultNetworkCallback(new ConnectivityManager.NetworkCallback() {
                @Override
                public void onAvailable(Network network) {
                    handler.removeCallbacks(networkSettled);
                    handler.postDelayed(networkSettled, 1500);
                }

                @Override
                public void onLost(Network network) {
                    handler.post(() -> {
                        if (onSetup) js("window.kdNetwork && window.kdNetwork(false)");
                    });
                }
            });
        } catch (Exception ignored) {
        }
    }

    private void networkChanged() {
        if (onSetup) {
            js("window.kdNetwork && window.kdNetwork(true)");
            return;
        }
        if (resumed && current != null && !loading) recheck();
    }

    @SuppressWarnings("deprecation")
    private boolean online() {
        ConnectivityManager cm = (ConnectivityManager) getSystemService(Context.CONNECTIVITY_SERVICE);
        if (cm == null) return true;
        NetworkInfo info = cm.getActiveNetworkInfo();
        return info != null && info.isConnected();
    }

    /** The page did not load: first try the other addresses of the PC, then the page of PCs with the error. */
    private void failed(String error) {
        handler.removeCallbacks(loadTimeout);
        Pcs.Pc pc = currentPc();
        if (pc == null) {
            openPcs(null, error);
            return;
        }
        List<String> others = new ArrayList<>(pc.candidates());
        others.remove(current);
        if (!retried && !others.isEmpty()) {
            retried = true;
            showConnecting(pc);
            int mine = ++attempt;
            new Thread(() -> {
                String best = Addresses.pick(others, pc);
                runOnUiThread(() -> whenConnectingShown(() -> {
                    if (mine != attempt) return;
                    if (best != null) use(pc, best);
                    else openPcs(pc.id, error);
                }));
            }).start();
            return;
        }
        openPcs(pc.id, error);
    }

    /** The app's page of PCs; <code>focus</code> is the one it is about, <code>error</code> why it is shown (or null). */
    private void openPcs(String focus, String error) {
        attempt++; // a search still running must not take the user away from here
        handler.removeCallbacks(loadTimeout);
        current = null;
        loading = false;
        String url = SETUP + "?view=pcs";
        if (focus != null) url += "&pc=" + Uri.encode(focus);
        if (error != null) url += "&error=" + Uri.encode(error);
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
        return uri.getScheme().toLowerCase(Locale.ROOT) + "://" + uri.getEncodedAuthority() + path;
    }

    /**
     * An address typed, scanned or shared (a pairing link …/#pair=CODE): the PC it belongs to, or a new one, opened
     * there (with the link's code the web app pairs at once). False when it is not an address.
     */
    private boolean openLink(String link) {
        String server = normalize(link);
        if (server == null) return false;
        Pcs.Pc pc = pcs.byAddress(server);
        if (pc == null) {
            pc = new Pcs.Pc();
            pc.id = Pcs.NEW + UUID.randomUUID();
            pc.addresses.add(server);
        }
        pc.last = server;
        pc.lastUsed = System.currentTimeMillis();
        pcs.save(pc);
        pcs.setCurrent(pc.id);
        retried = false;
        String fragment = Uri.parse(link.trim().matches("(?i)^https?://.*") ? link.trim() : "http://" + link.trim()).getEncodedFragment();
        load(fragment != null ? server + "#" + fragment : server);
        return true;
    }

    /** Loads a page of K-Desktop, with a deadline: a hanging connection shows the page of PCs with the error. */
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

    /** What the PC said about itself (its session): name, id, addresses, how to wake it, whether it lets the phone switch it off. */
    private void pcInfo(JSONObject info) {
        Pcs.Pc pc = currentPc();
        String at = current;
        if (pc == null || at == null) return;
        String machine = info.optString("machine");
        if (!machine.isEmpty()) pc.name = machine;
        JSONArray list = info.optJSONArray("addresses");
        if (list != null) {
            List<String> clean = new ArrayList<>();
            for (int i = 0; i < list.length(); i++) {
                String address = normalize(list.optString(i));
                if (address != null && !clean.contains(address)) clean.add(address);
            }
            if (!clean.contains(at)) clean.add(at);
            if (!clean.isEmpty()) {
                pc.addresses.clear();
                pc.addresses.addAll(clean);
            }
        }
        JSONObject wake = info.optJSONObject("wake");
        if (wake != null) {
            JSONArray adapters = wake.optJSONArray("adapters");
            if (adapters != null && adapters.length() > 0) pc.adapters = adapters;
            pc.wakeUrl = wake.optJSONObject("url");
        }
        if (info.has("power")) pc.power = info.optBoolean("power");
        pcs.save(pc);
        pcs.identify(pc, info.optString("pc"));
    }

    // ── waking a PC ──────────────────────────────────────────────────

    /**
     * «Encender»: every way at once ({@link Wake}), then it waits for the PC to answer (asking every few seconds, two
     * minutes at most) and opens it. The page hears it through window.kdWake(id, {phase, …}).
     */
    private void wake(Pcs.Pc pc, boolean send) {
        int mine = ++wakeRun;
        new Thread(() -> {
            JSONObject result = send ? Wake.send(pc, pcs.all()) : new JSONObject();
            wakeState(pc.id, "sent", result);
            long start = System.currentTimeMillis();
            while (mine == wakeRun && System.currentTimeMillis() - start < WAKE_WAIT_MS) {
                String best = Addresses.pick(pc.candidates(), pc);
                if (mine != wakeRun) return;
                if (best != null) {
                    wakeRun++;
                    runOnUiThread(() -> use(pc, best));
                    return;
                }
                try {
                    JSONObject progress = new JSONObject().put("seconds", (System.currentTimeMillis() - start) / 1000);
                    wakeState(pc.id, "waiting", progress);
                    Thread.sleep(3000);
                } catch (Exception e) {
                    return;
                }
            }
            if (mine == wakeRun) wakeState(pc.id, "failed", result);
        }).start();
    }

    private void wakeState(String id, String phase, JSONObject detail) {
        try {
            detail.put("phase", phase);
        } catch (Exception ignored) {
        }
        js("window.kdWake && window.kdWake(" + JSONObject.quote(id) + ", " + detail + ")");
    }

    /** What each PC says now, for the page: {state, addresses: [{url, local, state}]} per PC, as each finishes. */
    private void probeAll() {
        for (Pcs.Pc pc : pcs.all()) {
            new Thread(() -> {
                List<String> candidates = pc.candidates();
                int[] states = Addresses.askAll(candidates, pc);
                try {
                    JSONArray list = new JSONArray();
                    int best = Addresses.SILENT;
                    for (int i = 0; i < candidates.size(); i++) {
                        list.put(new JSONObject().put("url", candidates.get(i)).put("local", Addresses.isLocal(candidates.get(i)))
                                .put("state", name(states[i])));
                        best = Math.max(best, states[i]);
                    }
                    JSONObject answer = new JSONObject().put("state", name(best)).put("addresses", list);
                    js("window.kdPcState && window.kdPcState(" + JSONObject.quote(pc.id) + ", " + answer + ")");
                } catch (Exception ignored) {
                }
            }).start();
        }
    }

    private static String name(int state) {
        return state == Addresses.ANSWERS ? "online" : state == Addresses.LOGIN ? "login" : "offline";
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
        Pcs.Pc pc = currentPc();
        String token = pc == null ? "" : pc.token;
        new Thread(() -> {
            PackageInstaller.Session session = null;
            try {
                HttpURLConnection http = (HttpURLConnection) new URL(url).openConnection();
                http.setConnectTimeout(15000);
                http.setReadTimeout(60000);
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

    // ── messages with the app closed (B11) ───────────────────────────

    /** Turns the service on: notifications first (Android 13+), then the battery exemption so it is not frozen. */
    private void setInbox(boolean on) {
        if (!on) {
            InboxService.setEnabled(this, false);
            return;
        }
        if (Build.VERSION.SDK_INT >= 33 && checkSelfPermission(Manifest.permission.POST_NOTIFICATIONS) != PackageManager.PERMISSION_GRANTED) {
            requestPermissions(new String[] { Manifest.permission.POST_NOTIFICATIONS }, INBOX_PERMISSION);
            return;
        }
        enableInbox();
    }

    @SuppressLint("BatteryLife")
    private void enableInbox() {
        InboxService.setEnabled(this, true);
        js("window.kdInboxState && window.kdInboxState(true)");
        // Android freezes the connection of apps asleep to save battery: ask to leave this one alone.
        if (Build.VERSION.SDK_INT >= 23) {
            PowerManager power = (PowerManager) getSystemService(POWER_SERVICE);
            if (power != null && !power.isIgnoringBatteryOptimizations(getPackageName())) {
                try {
                    startActivity(new Intent(android.provider.Settings.ACTION_REQUEST_IGNORE_BATTERY_OPTIMIZATIONS, Uri.parse("package:" + getPackageName())));
                } catch (Exception ignored) {
                }
            }
        }
    }

    /** A file of the inbox, with Android's downloads (it shows its own notification and keeps it in Downloads). */
    private void download(String url, String name) {
        try {
            DownloadManager.Request request = new DownloadManager.Request(Uri.parse(url));
            Pcs.Pc pc = currentPc();
            if (pc != null && !pc.token.isEmpty()) request.addRequestHeader("Authorization", "Bearer " + pc.token);
            String cookie = CookieManager.getInstance().getCookie(url);
            if (cookie != null) request.addRequestHeader("Cookie", cookie);
            request.setTitle(name);
            request.setNotificationVisibility(DownloadManager.Request.VISIBILITY_VISIBLE_NOTIFY_COMPLETED);
            // Before Android 10 the shared Downloads folder needs a permission: the app's own one instead.
            if (Build.VERSION.SDK_INT >= 29) request.setDestinationInExternalPublicDir(Environment.DIRECTORY_DOWNLOADS, name);
            else request.setDestinationInExternalFilesDir(this, Environment.DIRECTORY_DOWNLOADS, name);
            ((DownloadManager) getSystemService(DOWNLOAD_SERVICE)).enqueue(request);
        } catch (Exception e) {
            Toast.makeText(this, String.valueOf(e.getMessage()), Toast.LENGTH_LONG).show();
        }
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
        boolean granted = results.length > 0 && results[0] == PackageManager.PERMISSION_GRANTED;
        if (requestCode == INBOX_PERMISSION) {
            if (granted) enableInbox();
            else js("window.kdInboxState && window.kdInboxState(false)");
            return;
        }
        if (requestCode == CAMERA_PERMISSION) {
            PermissionRequest request = pendingCamera;
            pendingCamera = null;
            if (request == null) return;
            if (granted) request.grant(new String[] { PermissionRequest.RESOURCE_VIDEO_CAPTURE });
            else request.deny();
            return;
        }
        if (requestCode != VOICE_PERMISSION) return;
        if (granted) recordVoice();
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

    /** The camera for the QR, only for the app's own page (and with Android's permission). */
    private class Chrome extends WebChromeClient {
        @Override
        public void onPermissionRequest(PermissionRequest request) {
            runOnUiThread(() -> {
                boolean video = false;
                for (String r : request.getResources()) if (PermissionRequest.RESOURCE_VIDEO_CAPTURE.equals(r)) video = true;
                Uri origin = request.getOrigin();
                if (!video || origin == null || !"file".equals(origin.getScheme()) || !onSetup) {
                    request.deny();
                    return;
                }
                if (Build.VERSION.SDK_INT >= 23 && checkSelfPermission(Manifest.permission.CAMERA) != PackageManager.PERMISSION_GRANTED) {
                    if (pendingCamera != null) pendingCamera.deny();
                    pendingCamera = request;
                    requestPermissions(new String[] { Manifest.permission.CAMERA }, CAMERA_PERMISSION);
                    return;
                }
                request.grant(new String[] { PermissionRequest.RESOURCE_VIDEO_CAPTURE });
            });
        }
    }

    private class Client extends WebViewClient {
        @Override
        public void onPageStarted(WebView view, String url, Bitmap favicon) {
            onSetup = url.startsWith(SETUP);
            if (onSetup) handler.removeCallbacks(loadTimeout);
        }

        private void loaded() {
            loading = false;
            handler.removeCallbacks(loadTimeout);
            retried = false;
            if (openInboxPending) {
                openInboxPending = false;
                handler.postDelayed(() -> js("window.kdOpenInbox && window.kdOpenInbox()"), 900);
            }
        }

        private void setupVisible(String url) {
            // The app's page starts its own history: back from it leaves the app, not to a PC that did not answer.
            web.clearHistory();
            if (url.contains("connecting=")) connectingVisible();
        }

        @Override
        public void onPageCommitVisible(WebView view, String url) {
            // Something of the page arrived: K-Desktop answered (the app's page itself does not count).
            if (!url.startsWith(SETUP)) loaded();
            else setupVisible(url);
        }

        @Override
        public void onPageFinished(WebView view, String url) {
            // Android 5-6 have no onPageCommitVisible.
            if (Build.VERSION.SDK_INT >= 23) return;
            if (!url.startsWith(SETUP)) loaded();
            else setupVisible(url);
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
            String scheme = url.getScheme();
            if ("file".equals(scheme)) return false;
            Pcs.Pc pc = currentPc();
            if (pc != null) for (String address : pc.candidates()) if (url.toString().startsWith(address)) return false;
            String now = current;
            if (now != null && url.toString().startsWith(now)) return false;
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

    /**
     * What the pages can ask the app: window.KDesktopApp. The pages of a PC get what concerns that PC; managing the
     * list of PCs (and seeing it) is only for the app's own page.
     */
    private class Bridge {
        @JavascriptInterface
        public int api() {
            return BRIDGE_API;
        }

        @JavascriptInterface
        public String getServer() {
            String s = current;
            return s == null ? "" : s;
        }

        /** The pairing token of the PC on screen: kept by the app, so it works at every address of the PC. */
        @JavascriptInterface
        public String getToken() {
            Pcs.Pc pc = currentPc();
            return pc == null || onSetup ? "" : pc.token;
        }

        @JavascriptInterface
        public void setToken(String token) {
            runOnUiThread(() -> {
                Pcs.Pc pc = currentPc();
                if (pc == null || onSetup) return;
                pc.token = token == null ? "" : token;
                pcs.save(pc);
            });
        }

        /** Before 5.0.402: {"machine": "PC", "addresses": [...]}, from the session. */
        @JavascriptInterface
        public void setAddresses(String json) {
            setPcInfo(json);
        }

        /** The session of the PC on screen: {pc, machine, addresses, wake: {adapters, url}, power}. */
        @JavascriptInterface
        public void setPcInfo(String json) {
            runOnUiThread(() -> {
                if (onSetup) return;
                try {
                    pcInfo(new JSONObject(json));
                } catch (Exception ignored) {
                }
            });
        }

        /** A value the web app keeps for the PC on screen (JSON text; "" when there is none). */
        @JavascriptInterface
        public String getPref(String key) {
            Pcs.Pc pc = currentPc();
            return pc == null || onSetup || key == null ? "" : pc.prefs.optString(key, "");
        }

        @JavascriptInterface
        public void setPref(String key, String value) {
            if (key == null || key.length() > 60 || (value != null && value.length() > 64000)) return;
            runOnUiThread(() -> {
                Pcs.Pc pc = currentPc();
                if (pc == null || onSetup) return;
                try {
                    if (value == null || value.isEmpty()) pc.prefs.remove(key);
                    else pc.prefs.put(key, value);
                    pcs.save(pc);
                } catch (Exception ignored) {
                }
            });
        }

        /** «Cambiar de PC»: the page of PCs. */
        @JavascriptInterface
        public void changeServer() {
            showPcs();
        }

        @JavascriptInterface
        public void showPcs() {
            runOnUiThread(() -> {
                Pcs.Pc pc = currentPc();
                openPcs(pc == null ? null : pc.id, null);
            });
        }

        /**
         * The PC on screen was switched off from here (shutdown, sleep, hibernate) or is restarting: the page of PCs
         * shows it so; after a restart the app waits for it and goes back in by itself.
         */
        @JavascriptInterface
        public void poweredOff(String action) {
            runOnUiThread(() -> {
                Pcs.Pc pc = currentPc();
                if (pc == null) return;
                openPcs(pc.id, "restart".equals(action) ? "restarting" : "off");
                if ("restart".equals(action)) handler.postDelayed(() -> MainActivity.this.wake(pc, false), 6000);
            });
        }

        @JavascriptInterface
        public void retry() {
            runOnUiThread(MainActivity.this::openCurrent);
        }

        @JavascriptInterface
        public String version() {
            return versionName();
        }

        @JavascriptInterface
        public int versionCode() {
            return MainActivity.this.versionCode();
        }

        @JavascriptInterface
        public boolean isOnline() {
            return online();
        }

        // ── only the app's page ──

        @JavascriptInterface
        public String getPcs() {
            if (!onSetup) return "[]";
            JSONArray list = new JSONArray();
            Pcs.Pc now = currentPc();
            try {
                for (Pcs.Pc pc : pcs.all()) list.put(pc.toPublicJson(now != null && now.id.equals(pc.id)));
            } catch (Exception ignored) {
            }
            return list.toString();
        }

        /** Asks every PC now: the answers come to window.kdPcState(id, state). */
        @JavascriptInterface
        public void probe() {
            if (onSetup) probeAll();
        }

        /** Opens a PC (at <code>address</code>, or at the one that answers when empty). */
        @JavascriptInterface
        public void connect(String id, String address) {
            runOnUiThread(() -> {
                Pcs.Pc pc = onSetup ? pcs.get(id) : null;
                if (pc == null) return;
                wakeRun++;
                connectTo(pc, address == null || address.isEmpty() ? null : normalize(address));
            });
        }

        /** An address typed or a QR scanned: adds the PC (or finds it) and opens it. False when it is not an address. */
        @JavascriptInterface
        public boolean addPc(String input) {
            if (!onSetup || normalize(input) == null) return false;
            runOnUiThread(() -> openLink(input));
            return true;
        }

        @JavascriptInterface
        public void renamePc(String id, String name) {
            if (!onSetup) return;
            Pcs.Pc pc = pcs.get(id);
            if (pc == null || name == null || name.trim().isEmpty()) return;
            pc.name = name.trim();
            pcs.save(pc);
        }

        @JavascriptInterface
        public void removePc(String id) {
            if (onSetup) pcs.remove(id);
        }

        /** «Encender»: see {@link MainActivity#wake}. */
        @JavascriptInterface
        public void wake(String id) {
            if (!onSetup) return;
            Pcs.Pc pc = pcs.get(id);
            if (pc != null) MainActivity.this.wake(pc, true);
        }

        @JavascriptInterface
        public void cancelWake() {
            wakeRun++;
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

        /** «Avisos con la app cerrada»: whether the service is on. */
        @JavascriptInterface
        public boolean inboxEnabled() {
            return InboxService.enabled(MainActivity.this);
        }

        @JavascriptInterface
        public void setInbox(boolean on) {
            runOnUiThread(() -> MainActivity.this.setInbox(on));
        }

        /** What the service showed as notifications (and the PC no longer gives): a JSON array, then forgotten. */
        @JavascriptInterface
        public String takeInbox() {
            return InboxService.take(MainActivity.this);
        }

        @JavascriptInterface
        public void copy(String text) {
            ClipboardManager clipboard = (ClipboardManager) getSystemService(CLIPBOARD_SERVICE);
            if (clipboard != null) clipboard.setPrimaryClip(ClipData.newPlainText("K-Desktop", text));
        }

        @JavascriptInterface
        public void download(String url, String name) {
            runOnUiThread(() -> MainActivity.this.download(url, name));
        }

        /** Updates the app from K-Desktop (url: its api/android). */
        @JavascriptInterface
        public void updateApp(String url) {
            runOnUiThread(() -> MainActivity.this.updateApp(url));
        }
    }
}
