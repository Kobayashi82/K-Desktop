package net.kobayashi82.kdesktop;

import android.content.Context;
import android.os.Handler;
import android.os.Looper;
import android.webkit.CookieManager;
import android.widget.Toast;

import org.json.JSONArray;
import org.json.JSONObject;

import java.io.ByteArrayOutputStream;
import java.io.InputStream;
import java.io.OutputStream;
import java.net.HttpURLConnection;
import java.net.URL;
import java.nio.charset.StandardCharsets;

/**
 * Asking the app's PC something without its page (the tiles and the widget, phase 7): the current PC with its token, at
 * the address it used last or the best one now. Runs a command (api/run) and says how it went in a toast.
 */
final class Remote {
    private Remote() {
    }

    static Pcs.Pc pc(Context context) {
        return new Pcs(context.getSharedPreferences(InboxService.PREFS, Context.MODE_PRIVATE)).current();
    }

    /** The favourite commands the web app keeps for the PC ([{id, name}], first the first one). */
    static JSONArray favorites(Context context) {
        Pcs.Pc pc = pc(context);
        if (pc == null) return new JSONArray();
        try {
            return new JSONArray(pc.prefs.optString("favoriteInfo", "[]"));
        } catch (Exception e) {
            return new JSONArray();
        }
    }

    /** Runs the command on the PC (any thread) and says how it went. */
    static void run(Context context, String id, String name) {
        Context app = context.getApplicationContext();
        new Thread(() -> {
            Pcs.Pc pc = pc(app);
            int message = R.string.run_offline;
            if (pc != null && !pc.token.isEmpty()) {
                String at = pc.last != null ? pc.last : Addresses.pick(pc.candidates(), pc);
                for (int attempt = 0; attempt < 2 && at != null; attempt++) {
                    try {
                        JSONObject answer = new JSONObject(post(at + "api/run", pc.token, new JSONObject().put("id", id)));
                        String status = answer.optString("status");
                        message = "ok".equals(status) || "running".equals(status) ? R.string.run_ok : R.string.run_failed;
                        break;
                    } catch (Exception e) {
                        // The last address does not answer (home ↔ away): the best one now.
                        at = attempt == 0 ? Addresses.pick(pc.candidates(), pc) : null;
                    }
                }
            }
            int shown = message;
            new Handler(Looper.getMainLooper()).post(() -> Toast.makeText(app, app.getString(shown, name), Toast.LENGTH_SHORT).show());
        }).start();
    }

    private static String post(String url, String token, JSONObject body) throws Exception {
        HttpURLConnection c = (HttpURLConnection) new URL(url).openConnection();
        try {
            c.setConnectTimeout(6000);
            c.setReadTimeout(30000);
            c.setRequestMethod("POST");
            c.setDoOutput(true);
            c.setRequestProperty("X-KD", "1");
            c.setRequestProperty("Content-Type", "application/json");
            c.setRequestProperty("Authorization", "Bearer " + token);
            String cookie = CookieManager.getInstance().getCookie(url);
            if (cookie != null) c.setRequestProperty("Cookie", cookie);
            try (OutputStream out = c.getOutputStream()) {
                out.write(body.toString().getBytes(StandardCharsets.UTF_8));
            }
            int code = c.getResponseCode();
            if (code == 401) throw new Exception("unpaired");
            InputStream in = code < 400 ? c.getInputStream() : c.getErrorStream();
            ByteArrayOutputStream text = new ByteArrayOutputStream();
            if (in != null) {
                try (InputStream answer = in) {
                    byte[] buffer = new byte[8192];
                    for (int n; (n = answer.read(buffer)) > 0; ) text.write(buffer, 0, n);
                }
            }
            return text.size() > 0 ? text.toString("UTF-8") : "{}";
        } finally {
            c.disconnect();
        }
    }
}
