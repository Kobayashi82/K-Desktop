package net.kobayashi82.kdesktop;

import android.annotation.SuppressLint;
import android.annotation.TargetApi;
import android.app.PendingIntent;
import android.content.Intent;
import android.os.Build;
import android.service.quicksettings.Tile;
import android.service.quicksettings.TileService;

import org.json.JSONArray;
import org.json.JSONObject;

/**
 * The Quick Settings tiles (Android 7+, phase 7): talk, switch the PC on, and the first of the favourite commands (its
 * name on the tile). Talking and switching on open the app where it does it; the command runs without it.
 */
@TargetApi(24)
public final class Tiles {
    private Tiles() {
    }

    /** Opens the app with that shortcut's action, closing the panel. */
    @SuppressLint("StartActivityAndCollapseDeprecated")
    @SuppressWarnings("deprecation")
    static void open(TileService service, String action) {
        Intent intent = new Intent(service, MainActivity.class).setAction(action).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK);
        if (Build.VERSION.SDK_INT >= 34) {
            service.startActivityAndCollapse(PendingIntent.getActivity(service, action.hashCode(), intent,
                    PendingIntent.FLAG_UPDATE_CURRENT | PendingIntent.FLAG_IMMUTABLE));
        } else {
            service.startActivityAndCollapse(intent);
        }
    }

    public static final class Talk extends TileService {
        @Override
        public void onClick() {
            open(this, MainActivity.ACTION_TALK);
        }
    }

    public static final class Wake extends TileService {
        @Override
        public void onClick() {
            open(this, MainActivity.ACTION_WAKE);
        }
    }

    /** The first favourite command: its name on the tile, a touch runs it on the PC. */
    public static final class Run extends TileService {
        private JSONObject first() {
            JSONArray favorites = Remote.favorites(this);
            return favorites.length() > 0 ? favorites.optJSONObject(0) : null;
        }

        @Override
        public void onStartListening() {
            Tile tile = getQsTile();
            if (tile == null) return;
            JSONObject command = first();
            tile.setLabel(command == null ? getString(R.string.tile_run) : command.optString("name"));
            tile.setState(command == null ? Tile.STATE_UNAVAILABLE : Tile.STATE_INACTIVE);
            tile.updateTile();
        }

        @Override
        public void onClick() {
            JSONObject command = first();
            if (command == null) {
                open(this, Intent.ACTION_MAIN);
                return;
            }
            Remote.run(this, command.optString("id"), command.optString("name"));
        }
    }
}
