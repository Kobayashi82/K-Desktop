package net.kobayashi82.kdesktop;

import android.app.Activity;
import android.content.ContentResolver;
import android.database.Cursor;
import android.net.Uri;
import android.provider.OpenableColumns;
import android.webkit.CookieManager;
import android.widget.Toast;

import org.json.JSONObject;

import java.io.ByteArrayOutputStream;
import java.io.InputStream;
import java.io.OutputStream;
import java.net.HttpURLConnection;
import java.net.URL;
import java.nio.charset.StandardCharsets;

/**
 * «Compartir → K-Desktop» (phase 7, 5.0.412): what another app shares reaches the PC of the app. A text goes to its
 * clipboard, a link opens in its browser or goes to the clipboard (as chosen), a file is saved in its Downloads
 * (api/share, api/share/file). It works with the app's page closed too: the address is the one in use, else the last
 * one, else the best one now.
 */
final class ShareSender {
    private ShareSender() {
    }

    static void text(Activity activity, Pcs.Pc pc, String address, String text, boolean open) {
        new Thread(() -> {
            String at = addressOf(pc, address);
            int message;
            try {
                if (at == null) throw new Exception("offline");
                JSONObject body = new JSONObject().put("text", text).put("open", open);
                JSONObject answer = new JSONObject(send(at + "api/share", pc.token, "application/json",
                        body.toString().getBytes(StandardCharsets.UTF_8), null));
                message = !answer.optBoolean("ok") ? R.string.share_failed
                        : "opened".equals(answer.optString("did")) ? R.string.share_opened : R.string.share_copied;
            } catch (Exception e) {
                message = R.string.share_offline;
            }
            tell(activity, message, pc.name);
        }).start();
    }

    static void file(Activity activity, Pcs.Pc pc, String address, Uri uri) {
        ContentResolver resolver = activity.getContentResolver();
        String name = nameOf(resolver, uri);
        Toast.makeText(activity, activity.getString(R.string.share_sending, pc.name), Toast.LENGTH_SHORT).show();
        new Thread(() -> {
            String at = addressOf(pc, address);
            int message;
            try (InputStream in = resolver.openInputStream(uri)) {
                if (at == null || in == null) throw new Exception("offline");
                JSONObject answer = new JSONObject(send(at + "api/share/file?name=" + Uri.encode(name), pc.token,
                        "application/octet-stream", null, in));
                message = answer.optBoolean("ok") ? R.string.share_saved : R.string.share_failed;
            } catch (Exception e) {
                message = R.string.share_offline;
            }
            tell(activity, message, pc.name);
        }).start();
    }

    private static void tell(Activity activity, int message, String pc) {
        activity.runOnUiThread(() -> Toast.makeText(activity, activity.getString(message, pc), Toast.LENGTH_LONG).show());
    }

    private static String addressOf(Pcs.Pc pc, String address) {
        if (address != null) return address;
        String best = Addresses.pick(pc.candidates(), pc);
        return best != null ? best : pc.last;
    }

    private static String nameOf(ContentResolver resolver, Uri uri) {
        try (Cursor cursor = resolver.query(uri, new String[] { OpenableColumns.DISPLAY_NAME }, null, null, null)) {
            if (cursor != null && cursor.moveToFirst() && !cursor.isNull(0)) return cursor.getString(0);
        } catch (Exception ignored) {
        }
        String last = uri.getLastPathSegment();
        return last == null || last.isEmpty() ? "archivo" : last;
    }

    /** POSTs the bytes (or the stream, as it is read) with the PC's token; the JSON answer. */
    private static String send(String url, String token, String type, byte[] bytes, InputStream stream) throws Exception {
        HttpURLConnection c = (HttpURLConnection) new URL(url).openConnection();
        try {
            c.setConnectTimeout(10000);
            c.setReadTimeout(120000);
            c.setRequestMethod("POST");
            c.setDoOutput(true);
            c.setRequestProperty("X-KD", "1");
            c.setRequestProperty("Content-Type", type);
            if (!token.isEmpty()) c.setRequestProperty("Authorization", "Bearer " + token);
            String cookie = CookieManager.getInstance().getCookie(url);
            if (cookie != null) c.setRequestProperty("Cookie", cookie);
            if (bytes != null) c.setFixedLengthStreamingMode(bytes.length);
            else c.setChunkedStreamingMode(64 * 1024);
            try (OutputStream out = c.getOutputStream()) {
                if (bytes != null) out.write(bytes);
                else {
                    byte[] buffer = new byte[64 * 1024];
                    for (int n; (n = stream.read(buffer)) > 0; ) out.write(buffer, 0, n);
                }
            }
            int code = c.getResponseCode();
            InputStream answer = code < 400 ? c.getInputStream() : c.getErrorStream();
            ByteArrayOutputStream text = new ByteArrayOutputStream();
            if (answer != null) {
                try (InputStream in = answer) {
                    byte[] buffer = new byte[8192];
                    for (int n; (n = in.read(buffer)) > 0; ) text.write(buffer, 0, n);
                }
            }
            return text.size() > 0 ? text.toString("UTF-8") : "{}";
        } finally {
            c.disconnect();
        }
    }
}
