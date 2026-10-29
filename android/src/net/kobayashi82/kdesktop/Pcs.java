package net.kobayashi82.kdesktop;

import android.content.SharedPreferences;

import org.json.JSONArray;
import org.json.JSONObject;

import java.util.ArrayList;
import java.util.Collections;
import java.util.List;
import java.util.UUID;

/**
 * The PCs this phone is paired with (one or more), each with its token, the addresses it reported (home and away), what
 * it takes to wake it while it is off (its MACs and broadcast, «Encender mediante URL») and when it was last used. One is
 * the current one: the app opens it. Before 1.2.0 the app knew a single PC: {@link #migrate} turns it into the first of
 * the list, with its token, so nothing has to be paired again.
 */
final class Pcs {
    private static final String KEY_PCS = "pcs";
    private static final String KEY_CURRENT = "current";

    /** A PC that has not said its id yet (paired from an address typed or scanned): its own until it does. */
    static final String NEW = "new-";

    static final class Pc {
        String id;
        String name = "";
        String token = "";
        final List<String> addresses = new ArrayList<>();
        /** The address that answered last: tried first. */
        String last;
        /** [{mac, broadcast, wired}], from the PC. */
        JSONArray adapters = new JSONArray();
        /** «Encender mediante URL»: {url, method, body, header, value}; null when the PC has none or power is off there. */
        JSONObject wakeUrl;
        /** The PC lets this phone switch it off, restart it… («Energía»). */
        boolean power;
        long lastUsed;

        boolean canWake() {
            return adapters.length() > 0 || wakeUrl != null;
        }

        List<String> candidates() {
            List<String> list = new ArrayList<>();
            if (last != null) list.add(last);
            for (String a : addresses) if (!list.contains(a)) list.add(a);
            return list;
        }

        String mac() {
            JSONObject first = adapters.optJSONObject(0);
            return first == null ? "" : first.optString("mac");
        }

        JSONObject toJson() throws Exception {
            JSONObject o = new JSONObject();
            o.put("id", id).put("name", name).put("token", token).put("last", last == null ? JSONObject.NULL : last);
            o.put("addresses", new JSONArray(addresses)).put("adapters", adapters).put("power", power).put("lastUsed", lastUsed);
            if (wakeUrl != null) o.put("wakeUrl", wakeUrl);
            return o;
        }

        /** What the app's own pages may see: no token, no secret of the wake address. */
        JSONObject toPublicJson(boolean current) throws Exception {
            JSONObject o = new JSONObject();
            o.put("id", id).put("name", name).put("addresses", new JSONArray(addresses)).put("last", last == null ? JSONObject.NULL : last);
            o.put("canWake", canWake()).put("wakeUrl", wakeUrl != null).put("mac", mac()).put("lastUsed", lastUsed).put("current", current);
            o.put("paired", !token.isEmpty());
            return o;
        }

        static Pc from(JSONObject o) {
            Pc pc = new Pc();
            pc.id = o.optString("id", NEW + UUID.randomUUID());
            pc.name = o.optString("name");
            pc.token = o.optString("token");
            pc.last = o.isNull("last") ? null : o.optString("last", null);
            JSONArray list = o.optJSONArray("addresses");
            if (list != null) for (int i = 0; i < list.length(); i++) pc.addresses.add(list.optString(i));
            JSONArray adapters = o.optJSONArray("adapters");
            if (adapters != null) pc.adapters = adapters;
            pc.wakeUrl = o.optJSONObject("wakeUrl");
            pc.power = o.optBoolean("power");
            pc.lastUsed = o.optLong("lastUsed");
            return pc;
        }
    }

    private final SharedPreferences prefs;

    Pcs(SharedPreferences prefs) {
        this.prefs = prefs;
        migrate();
    }

    /** All of them, the one used last first. */
    synchronized List<Pc> all() {
        List<Pc> list = new ArrayList<>();
        try {
            JSONArray array = new JSONArray(prefs.getString(KEY_PCS, "[]"));
            for (int i = 0; i < array.length(); i++) list.add(Pc.from(array.getJSONObject(i)));
        } catch (Exception ignored) {
        }
        Collections.sort(list, (a, b) -> Long.compare(b.lastUsed, a.lastUsed));
        return list;
    }

    synchronized Pc get(String id) {
        if (id == null) return null;
        for (Pc pc : all()) if (pc.id.equals(id)) return pc;
        return null;
    }

    synchronized Pc current() {
        Pc pc = get(prefs.getString(KEY_CURRENT, null));
        if (pc != null) return pc;
        List<Pc> list = all();
        return list.isEmpty() ? null : list.get(0);
    }

    synchronized void setCurrent(String id) {
        prefs.edit().putString(KEY_CURRENT, id).apply();
    }

    /** Saves it (new or changed), by id. */
    synchronized void save(Pc pc) {
        List<Pc> list = all();
        boolean found = false;
        for (int i = 0; i < list.size(); i++) {
            if (list.get(i).id.equals(pc.id)) {
                list.set(i, pc);
                found = true;
            }
        }
        if (!found) list.add(pc);
        write(list);
    }

    synchronized void remove(String id) {
        List<Pc> list = all();
        for (int i = list.size() - 1; i >= 0; i--) if (list.get(i).id.equals(id)) list.remove(i);
        write(list);
        if (id.equals(prefs.getString(KEY_CURRENT, null))) prefs.edit().remove(KEY_CURRENT).apply();
    }

    /** The saved PC one of these addresses belongs to, or null. */
    synchronized Pc byAddress(String address) {
        if (address == null) return null;
        for (Pc pc : all()) if (pc.addresses.contains(address) || address.equals(pc.last)) return pc;
        return null;
    }

    /**
     * The PC told its id (its session): if another entry already is that PC (paired again from its QR, say), the two
     * become one, keeping this one's token and address.
     */
    synchronized Pc identify(Pc pc, String realId) {
        if (realId == null || realId.isEmpty() || realId.equals(pc.id)) return pc;
        List<Pc> list = all();
        for (int i = list.size() - 1; i >= 0; i--) {
            Pc other = list.get(i);
            if (other.id.equals(pc.id) || other.id.equals(realId)) list.remove(i);
        }
        boolean wasCurrent = pc.id.equals(prefs.getString(KEY_CURRENT, null));
        pc.id = realId;
        list.add(pc);
        write(list);
        if (wasCurrent) setCurrent(realId);
        return pc;
    }

    private void write(List<Pc> list) {
        JSONArray array = new JSONArray();
        try {
            for (Pc pc : list) array.put(pc.toJson());
        } catch (Exception ignored) {
        }
        prefs.edit().putString(KEY_PCS, array.toString()).apply();
    }

    /** The app of before (one PC: server, addresses, machine, token) → the first PC of the list. */
    private void migrate() {
        if (prefs.contains(KEY_PCS)) return;
        String server = prefs.getString("server", null);
        List<Pc> list = new ArrayList<>();
        if (server != null) {
            Pc pc = new Pc();
            pc.id = NEW + UUID.randomUUID();
            pc.name = prefs.getString("machine", "");
            pc.token = prefs.getString("token", "");
            pc.last = server;
            try {
                JSONArray old = new JSONArray(prefs.getString("addresses", "[]"));
                for (int i = 0; i < old.length(); i++) pc.addresses.add(old.getString(i));
            } catch (Exception ignored) {
            }
            if (!pc.addresses.contains(server)) pc.addresses.add(server);
            pc.lastUsed = System.currentTimeMillis();
            list.add(pc);
            prefs.edit().putString(KEY_CURRENT, pc.id).apply();
        }
        write(list);
    }
}
