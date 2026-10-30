package net.kobayashi82.kdesktop;

import android.app.PendingIntent;
import android.appwidget.AppWidgetManager;
import android.appwidget.AppWidgetProvider;
import android.content.ComponentName;
import android.content.Context;
import android.content.Intent;
import android.net.Uri;
import android.os.Build;
import android.view.View;
import android.widget.RemoteViews;

import org.json.JSONArray;
import org.json.JSONObject;

/**
 * The home screen widget (phase 7): the first four favourite commands of the app's PC as buttons (a touch runs it on the
 * PC, without opening the app) and a button to talk. The web app keeps the favourites (favoriteInfo); when they change
 * the widget is drawn again.
 */
public final class Widget extends AppWidgetProvider {
    static final String ACTION_RUN = "net.kobayashi82.kdesktop.WIDGET_RUN";
    private static final int[] BUTTONS = { R.id.widget_fav0, R.id.widget_fav1, R.id.widget_fav2, R.id.widget_fav3 };

    @Override
    public void onUpdate(Context context, AppWidgetManager manager, int[] ids) {
        for (int id : ids) manager.updateAppWidget(id, views(context));
    }

    @Override
    public void onReceive(Context context, Intent intent) {
        if (ACTION_RUN.equals(intent.getAction()) && intent.getData() != null) {
            Uri data = intent.getData();
            Remote.run(context, data.getQueryParameter("id"), data.getQueryParameter("name"));
            return;
        }
        super.onReceive(context, intent);
    }

    /** Draws every widget again (the favourites changed). */
    static void refresh(Context context) {
        AppWidgetManager manager = AppWidgetManager.getInstance(context);
        int[] ids = manager.getAppWidgetIds(new ComponentName(context, Widget.class));
        if (ids.length == 0) return;
        RemoteViews views = views(context);
        for (int id : ids) manager.updateAppWidget(id, views);
    }

    private static int immutable() {
        return Build.VERSION.SDK_INT >= 23 ? PendingIntent.FLAG_IMMUTABLE : 0;
    }

    private static RemoteViews views(Context context) {
        RemoteViews views = new RemoteViews(context.getPackageName(), R.layout.widget);
        JSONArray favorites = Remote.favorites(context);
        for (int i = 0; i < BUTTONS.length; i++) {
            JSONObject command = favorites.optJSONObject(i);
            if (command == null) {
                views.setViewVisibility(BUTTONS[i], View.GONE);
                continue;
            }
            String name = command.optString("name");
            views.setViewVisibility(BUTTONS[i], View.VISIBLE);
            views.setTextViewText(BUTTONS[i], name);
            Uri data = new Uri.Builder().scheme("kdrun").authority("run")
                    .appendQueryParameter("id", command.optString("id")).appendQueryParameter("name", name).build();
            Intent run = new Intent(context, Widget.class).setAction(ACTION_RUN).setData(data);
            views.setOnClickPendingIntent(BUTTONS[i], PendingIntent.getBroadcast(context, i, run, PendingIntent.FLAG_UPDATE_CURRENT | immutable()));
        }
        views.setViewVisibility(R.id.widget_empty, favorites.length() == 0 ? View.VISIBLE : View.GONE);
        Intent talk = new Intent(context, MainActivity.class).setAction(MainActivity.ACTION_TALK).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK);
        views.setOnClickPendingIntent(R.id.widget_talk, PendingIntent.getActivity(context, 90, talk, PendingIntent.FLAG_UPDATE_CURRENT | immutable()));
        Intent open = new Intent(context, MainActivity.class).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK);
        views.setOnClickPendingIntent(R.id.widget_empty, PendingIntent.getActivity(context, 91, open, PendingIntent.FLAG_UPDATE_CURRENT | immutable()));
        return views;
    }
}
