package net.kobayashi82.kdesktop;

import android.net.Uri;
import android.webkit.CookieManager;

import org.json.JSONObject;

import java.io.ByteArrayOutputStream;
import java.io.InputStream;
import java.net.HttpURLConnection;
import java.net.URL;
import java.util.List;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;

/**
 * Which address of a PC answers right now (its IP at home, the proxy's domain outside). The same pairing works at all of
 * them: the app keeps the token itself. A PC is recognised by its id (K-Desktop 5.0.402+) or else by its name, so
 * another PC at the same IP is not taken for it.
 */
final class Addresses {
    /** How long an address may take to answer. */
    static final int PROBE_TIMEOUT_MS = 2500;

    /** What an address said: nothing, a login page in front of it (Authelia…: K-Desktop is behind), or K-Desktop itself. */
    static final int SILENT = 0, LOGIN = 1, ANSWERS = 2;

    /** When another address answers first, how much longer the local one may take (it is the fastest to use). */
    private static final long LOCAL_GRACE_MS = 700;

    private Addresses() {
    }

    /** The PC's own network (a private IP or a .local name): fastest, and it works without internet. */
    static boolean isLocal(String address) {
        String host = Uri.parse(address).getHost();
        if (host == null) return false;
        host = host.toLowerCase();
        if (host.equals("localhost") || host.endsWith(".local") || host.startsWith("10.") || host.startsWith("192.168.")) return true;
        if (host.startsWith("172.")) {
            String[] parts = host.split("\\.");
            try {
                int second = Integer.parseInt(parts[1]);
                return second >= 16 && second <= 31;
            } catch (Exception e) {
                return false;
            }
        }
        return false;
    }

    /** What each candidate says, all at once (a few seconds at most). Blocks: call it off the UI thread. */
    static int[] askAll(List<String> candidates, Pcs.Pc pc) {
        int n = candidates.size();
        int[] state = new int[n];
        if (n == 0) return state;
        ExecutorService pool = Executors.newFixedThreadPool(n);
        try {
            for (int i = 0; i < n; i++) {
                final int index = i;
                pool.execute(() -> {
                    int answered = ask(candidates.get(index), pc);
                    synchronized (state) {
                        state[index] = answered;
                    }
                });
            }
            pool.shutdown();
            pool.awaitTermination(PROBE_TIMEOUT_MS * 2L + 500, java.util.concurrent.TimeUnit.MILLISECONDS);
        } catch (InterruptedException e) {
            Thread.currentThread().interrupt();
        } finally {
            pool.shutdownNow();
        }
        synchronized (state) {
            return state.clone();
        }
    }

    /**
     * Asks every candidate at once and returns the one to use: a local one if it answers, else the first that did, else
     * one with a login page in front (the web view shows it and then K-Desktop). Null when none answers. Blocks (call it
     * off the UI thread).
     */
    static String pick(List<String> candidates, Pcs.Pc pc) {
        int n = candidates.size();
        if (n == 0) return null;
        int[] state = new int[n];
        int[] finished = {0};
        Object lock = new Object();
        ExecutorService pool = Executors.newFixedThreadPool(n);
        try {
            for (int i = 0; i < n; i++) {
                final int index = i;
                pool.execute(() -> {
                    int answered = ask(candidates.get(index), pc);
                    synchronized (lock) {
                        state[index] = answered;
                        finished[0]++;
                        lock.notifyAll();
                    }
                });
            }
            long start = System.currentTimeMillis();
            long firstAnswer = -1;
            synchronized (lock) {
                while (true) {
                    String any = null, login = null;
                    for (int i = 0; i < n; i++) {
                        if (state[i] == LOGIN && login == null) login = candidates.get(i);
                        if (state[i] != ANSWERS) continue;
                        if (isLocal(candidates.get(i))) return candidates.get(i);
                        if (any == null) any = candidates.get(i);
                    }
                    long now = System.currentTimeMillis();
                    if (any != null && firstAnswer < 0) firstAnswer = now;
                    if (finished[0] == n) return any != null ? any : login;
                    if (any != null && now - firstAnswer >= LOCAL_GRACE_MS) return any;
                    if (now - start > PROBE_TIMEOUT_MS + 500) return any != null ? any : login;
                    lock.wait(50);
                }
            }
        } catch (InterruptedException e) {
            Thread.currentThread().interrupt();
            return null;
        } finally {
            pool.shutdownNow();
        }
    }

    /**
     * Whether K-Desktop answers at {@code address} and it is that PC. With the web view's cookies: once logged in to a
     * proxy that asks for one (Authelia…), the ping goes through; before, its redirect or 401 says so.
     */
    static int ask(String address, Pcs.Pc pc) {
        HttpURLConnection c = null;
        try {
            c = (HttpURLConnection) new URL(address + "api/ping").openConnection();
            c.setConnectTimeout(PROBE_TIMEOUT_MS);
            c.setReadTimeout(PROBE_TIMEOUT_MS);
            c.setUseCaches(false);
            c.setInstanceFollowRedirects(false);
            String cookie = CookieManager.getInstance().getCookie(address);
            if (cookie != null) c.setRequestProperty("Cookie", cookie);
            int status = c.getResponseCode();
            if ((status >= 300 && status < 400) || status == 401 || status == 403) return LOGIN;
            if (status != 200) return SILENT;
            ByteArrayOutputStream body = new ByteArrayOutputStream();
            try (InputStream in = c.getInputStream()) {
                byte[] buffer = new byte[1024];
                int read;
                while ((read = in.read(buffer)) > 0 && body.size() < 8192) body.write(buffer, 0, read);
            }
            JSONObject json = new JSONObject(body.toString("UTF-8"));
            return same(pc, json) ? ANSWERS : SILENT;
        } catch (Exception e) {
            return SILENT;
        } finally {
            if (c != null) c.disconnect();
        }
    }

    /** The answer is from this PC: its id when both know it, else its name when known, else anything that answers. */
    private static boolean same(Pcs.Pc pc, JSONObject ping) {
        if (pc == null) return true;
        String id = ping.optString("pc");
        if (!id.isEmpty() && !pc.id.startsWith(Pcs.NEW)) return id.equals(pc.id);
        return pc.name.isEmpty() || pc.name.equalsIgnoreCase(ping.optString("machine"));
    }
}
