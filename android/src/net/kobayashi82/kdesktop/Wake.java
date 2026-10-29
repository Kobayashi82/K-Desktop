package net.kobayashi82.kdesktop;

import android.net.Uri;
import android.webkit.CookieManager;

import org.json.JSONObject;

import java.io.OutputStream;
import java.net.DatagramPacket;
import java.net.DatagramSocket;
import java.net.HttpURLConnection;
import java.net.InetAddress;
import java.net.URL;
import java.nio.charset.StandardCharsets;
import java.util.ArrayList;
import java.util.List;

/**
 * Waking a PC that is off, every way at once: the magic packet on the phone's network (UDP, ports 9 and 7, to the PC's
 * broadcast and to everyone), «Encender mediante URL» if the PC has it (from anywhere: a Raspberry of the house…), and
 * another of the saved PCs that is on, which sends the packet from inside the house. Blocks: call it off the UI thread.
 */
final class Wake {
    private Wake() {
    }

    /** What was done: {packets, url: "204" | "HTTP 401" | error | null, via: name of the PC that helped | null}. */
    static JSONObject send(Pcs.Pc pc, List<Pcs.Pc> others) {
        JSONObject result = new JSONObject();
        try {
            result.put("packets", packets(pc));
            result.put("url", pc.wakeUrl == null ? JSONObject.NULL : url(pc));
            String via = viaOther(pc, others);
            result.put("via", via == null ? JSONObject.NULL : via);
        } catch (Exception ignored) {
        }
        return result;
    }

    private static byte[] magic(String mac) {
        String hex = mac.replaceAll("[^0-9A-Fa-f]", "");
        if (hex.length() != 12) return null;
        byte[] packet = new byte[6 + 16 * 6];
        for (int i = 0; i < 6; i++) packet[i] = (byte) 0xff;
        for (int i = 0; i < 6; i++) {
            byte b = (byte) Integer.parseInt(hex.substring(i * 2, i * 2 + 2), 16);
            for (int r = 0; r < 16; r++) packet[6 + r * 6 + i] = b;
        }
        return packet;
    }

    /** The magic packet of every card of the PC, to its broadcast and to 255.255.255.255. How many were sent. */
    private static int packets(Pcs.Pc pc) {
        int sent = 0;
        try (DatagramSocket socket = new DatagramSocket()) {
            socket.setBroadcast(true);
            for (int i = 0; i < pc.adapters.length(); i++) {
                JSONObject adapter = pc.adapters.optJSONObject(i);
                if (adapter == null) continue;
                byte[] packet = magic(adapter.optString("mac"));
                if (packet == null) continue;
                List<String> targets = new ArrayList<>();
                if (!adapter.optString("broadcast").isEmpty()) targets.add(adapter.optString("broadcast"));
                targets.add("255.255.255.255");
                for (String target : targets) {
                    for (int port : new int[] { 9, 7 }) {
                        try {
                            InetAddress to = InetAddress.getByName(target);
                            // Three times: it is UDP, one can get lost.
                            for (int n = 0; n < 3; n++) socket.send(new DatagramPacket(packet, packet.length, to, port));
                            sent++;
                        } catch (Exception ignored) {
                            // Not on that network (away from home): the other ways are for that.
                        }
                    }
                }
            }
        } catch (Exception ignored) {
        }
        return sent;
    }

    /** «Encender mediante URL»: its method, body and header; {mac} becomes the PC's MAC. The answer's code or the error. */
    private static String url(Pcs.Pc pc) {
        JSONObject w = pc.wakeUrl;
        String mac = pc.mac();
        HttpURLConnection c = null;
        try {
            String address = w.optString("url").replace("{mac}", Uri.encode(mac));
            c = (HttpURLConnection) new URL(address).openConnection();
            c.setConnectTimeout(8000);
            c.setReadTimeout(8000);
            String method = "GET".equalsIgnoreCase(w.optString("method")) ? "GET" : "POST";
            c.setRequestMethod(method);
            String header = w.optString("header");
            if (!header.isEmpty() && !w.isNull("value")) c.setRequestProperty(header, w.optString("value"));
            String body = w.optString("body");
            if ("POST".equals(method)) {
                byte[] bytes = body.replace("{mac}", mac).getBytes(StandardCharsets.UTF_8);
                c.setDoOutput(true);
                c.setRequestProperty("Content-Type", body.trim().startsWith("{") ? "application/json" : "text/plain; charset=utf-8");
                c.setFixedLengthStreamingMode(bytes.length);
                try (OutputStream out = c.getOutputStream()) {
                    out.write(bytes);
                }
            }
            int code = c.getResponseCode();
            return code >= 200 && code < 300 ? String.valueOf(code) : "HTTP " + code;
        } catch (Exception e) {
            return String.valueOf(e.getMessage());
        } finally {
            if (c != null) c.disconnect();
        }
    }

    /** Another saved PC that answers now sends the packet from its network (api/wake). Its name, or null. */
    private static String viaOther(Pcs.Pc pc, List<Pcs.Pc> others) {
        JSONObject first = pc.adapters.optJSONObject(0);
        if (first == null) return null;
        for (Pcs.Pc other : others) {
            if (other.id.equals(pc.id) || other.token.isEmpty()) continue;
            String address = Addresses.pick(other.candidates(), other);
            if (address == null) continue;
            HttpURLConnection c = null;
            try {
                c = (HttpURLConnection) new URL(address + "api/wake").openConnection();
                c.setConnectTimeout(4000);
                c.setReadTimeout(6000);
                c.setRequestMethod("POST");
                c.setRequestProperty("X-KD", "1");
                c.setRequestProperty("Content-Type", "application/json");
                c.setRequestProperty("Authorization", "Bearer " + other.token);
                String cookie = CookieManager.getInstance().getCookie(address);
                if (cookie != null) c.setRequestProperty("Cookie", cookie);
                byte[] body = new JSONObject().put("mac", first.optString("mac")).put("broadcast", first.optString("broadcast"))
                        .toString().getBytes(StandardCharsets.UTF_8);
                c.setDoOutput(true);
                c.setFixedLengthStreamingMode(body.length);
                try (OutputStream out = c.getOutputStream()) {
                    out.write(body);
                }
                if (c.getResponseCode() == 200) return other.name;
            } catch (Exception ignored) {
            } finally {
                if (c != null) c.disconnect();
            }
        }
        return null;
    }
}
