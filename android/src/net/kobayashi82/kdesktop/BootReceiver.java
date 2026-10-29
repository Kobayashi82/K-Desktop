package net.kobayashi82.kdesktop;

import android.content.BroadcastReceiver;
import android.content.Context;
import android.content.Intent;

/** «Avisos con la app cerrada» again after the phone starts or the app is updated (B11). */
public class BootReceiver extends BroadcastReceiver {
    @Override
    public void onReceive(Context context, Intent intent) {
        String action = intent.getAction();
        if (!Intent.ACTION_BOOT_COMPLETED.equals(action) && !Intent.ACTION_MY_PACKAGE_REPLACED.equals(action)) return;
        if (InboxService.enabled(context)) InboxService.start(context);
    }
}
