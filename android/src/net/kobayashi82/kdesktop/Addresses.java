package net.kobayashi82.kdesktop;

import android.content.SharedPreferences;
import android.net.Uri;

import org.json.JSONArray;
import org.json.JSONObject;

import java.io.ByteArrayOutputStream;
import java.io.InputStream;
import java.net.HttpURLConnection;
import java.net.URL;
import java.util.ArrayList;
import java.util.LinkedHashSet;
import java.util.List;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;

/**
 * The addresses of the PC (its IP at home, the proxy's domain outside), as K-Desktop reports them once paired, and
 * which one answers right now. The same pairing works at all of them: the app keeps the token itself.
 */
final class Addresses {
    private static final String KEY_ADDRESSES = "addresses";
    private static final String KEY_MACHINE = "machine";

    /** How long an address may take to answer. */
    private static final int PROBE_TIMEOUT_MS = 2500;

    /** When another address answers first, how much longer the local one may take (it is the fastest to use). */
    private static final long LOCAL_GRACE_MS = 700;

    private final SharedPreferences prefs;

    Addresses(SharedPreferences prefs) {
        this.prefs = prefs;
    }

    /** The known addresses plus {@code server} (the one in use or typed in the set-up). */
    List<String> all(String server) {
        LinkedHashSet<String> set = new LinkedHashSet<>();
        try {
            JSONArray list = new JSONArray(prefs.getString(KEY_ADDRESSES, "[]"));
            for (int i = 0; i < list.length(); i++) set.add(list.getString(i));
        } catch (Exception ignored) {
        }
        if (server != null) set.add(server);
        return new ArrayList<>(set);
    }

    boolean knows(String address) {
        return address != null && all(null).contains(address);
    }

    /** What K-Desktop said about itself: its addresses and the PC's name (another PC at the same IP is not it). */
    void save(List<String> addresses, String machine) {
        JSONArray list = new JSONArray();
        for (String a : addresses) list.put(a);
        prefs.edit().putString(KEY_ADDRESSES, list.toString()).putString(KEY_MACHINE, machine == null ? "" : machine).apply();
    }

    /** Another K-Desktop was chosen: the addresses of the old one no longer apply. */
    void forget() {
        prefs.edit().remove(KEY_ADDRESSES).remove(KEY_MACHINE).apply();
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

    /**
     * Asks every candidate at once and returns the one to use: a local one if it answers, else the first that did.
     * Null when none answers. Blocks (call it off the UI thread).
     */
    String pick(List<String> candidates) {
        int n = candidates.size();
        if (n == 0) return null;
        String machine = prefs.getString(KEY_MACHINE, "");
        boolean[] ok = new boolean[n];
        int[] finished = {0};
        Object lock = new Object();
        ExecutorService pool = Executors.newFixedThreadPool(n);
        try {
            for (int i = 0; i < n; i++) {
                final int index = i;
                pool.execute(() -> {
                    boolean answered = answers(candidates.get(index), machine);
                    synchronized (lock) {
                        ok[index] = answered;
                        finished[0]++;
                        lock.notifyAll();
                    }
                });
            }
            long start = System.currentTimeMillis();
            long firstAnswer = -1;
            synchronized (lock) {
                while (true) {
                    String any = null;
                    for (int i = 0; i < n; i++) {
                        if (!ok[i]) continue;
                        if (isLocal(candidates.get(i))) return candidates.get(i);
                        if (any == null) any = candidates.get(i);
                    }
                    long now = System.currentTimeMillis();
                    if (any != null && firstAnswer < 0) firstAnswer = now;
                    if (finished[0] == n) return any;
                    if (any != null && now - firstAnswer >= LOCAL_GRACE_MS) return any;
                    if (now - start > PROBE_TIMEOUT_MS + 500) return any;
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

    /** Whether K-Desktop answers at {@code address} (and, when known, it is the same PC). */
    private static boolean answers(String address, String machine) {
        HttpURLConnection c = null;
        try {
            c = (HttpURLConnection) new URL(address + "api/ping").openConnection();
            c.setConnectTimeout(PROBE_TIMEOUT_MS);
            c.setReadTimeout(PROBE_TIMEOUT_MS);
            c.setUseCaches(false);
            if (c.getResponseCode() != 200) return false;
            ByteArrayOutputStream body = new ByteArrayOutputStream();
            try (InputStream in = c.getInputStream()) {
                byte[] buffer = new byte[1024];
                int read;
                while ((read = in.read(buffer)) > 0 && body.size() < 8192) body.write(buffer, 0, read);
            }
            JSONObject json = new JSONObject(body.toString("UTF-8"));
            return machine.isEmpty() || machine.equalsIgnoreCase(json.optString("machine"));
        } catch (Exception e) {
            return false;
        } finally {
            if (c != null) c.disconnect();
        }
    }
}
