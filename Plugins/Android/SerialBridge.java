package com.sosxr.serial;

import android.app.Activity;
import android.app.PendingIntent;
import android.content.Context;
import android.content.Intent;
import android.hardware.usb.*;
import android.os.Build;
import android.util.Log;

import java.io.ByteArrayOutputStream;
import java.util.Arrays;

import com.hoho.android.usbserial.driver.*;

public class SerialBridge {
    private static final int MAX_PENDING = 64 * 1024;
    private static final String TAG = "SerialBridge";
    private static final String ACTION_USB_PERMISSION = "com.sosxr.serial.USB_PERMISSION";

    private static UsbSerialPort port;
    private static final Object lock = new Object();
    private static final ByteArrayOutputStream pending = new ByteArrayOutputStream();
    private static Thread reader;
    private static volatile boolean running;

    public static boolean open(Activity activity, int baudRate) {
        close(); // never leak a previously opened port

        UsbManager manager = (UsbManager) activity.getSystemService(Context.USB_SERVICE);

        for (UsbDevice device : manager.getDeviceList().values()) {
            UsbSerialDriver driver = UsbSerialProber.getDefaultProber().probeDevice(device);
            if (driver == null) continue;

            // openDevice returns null without USB permission. Ask for it; the next open() succeeds once granted.
            if (!manager.hasPermission(device)) {
                requestPermission(activity, manager, device);
                return false;
            }

            try {
                UsbDeviceConnection conn = manager.openDevice(device);
                if (conn == null) return false;

                port = driver.getPorts().get(0);
                port.open(conn);
                port.setParameters(baudRate, 8, 1, UsbSerialPort.PARITY_NONE);
                startReader(port);

                return true;
            } catch (Exception e) {
                close();
                return false;
            }
        }
        return false;
    }

    private static void requestPermission(Activity activity, UsbManager manager, UsbDevice device) {
        try {
            // FLAG_IMMUTABLE is required from API 31 (Android 12) on, and exists from API 23 (value 0x04000000).
            int flags = Build.VERSION.SDK_INT >= 23 ? 0x04000000 /* PendingIntent.FLAG_IMMUTABLE */ : 0;
            Intent intent = new Intent(ACTION_USB_PERMISSION);
            intent.setPackage(activity.getPackageName());
            PendingIntent permissionIntent = PendingIntent.getBroadcast(activity, 0, intent, flags);
            manager.requestPermission(device, permissionIntent);
            Log.w(TAG, "No USB permission for " + device.getDeviceName() + ". Asked the user; connect again after granting it.");
        } catch (Exception e) {
            Log.e(TAG, "Requesting USB permission failed: " + e);
        }
    }

    /**
     * Writes the first len bytes of data.
     * Works with usb-serial-for-android 2.x (write returns int) and 3.x (write returns void).
     * @return len on success, 0 on error.
     */
    public static int write(byte[] data, int len) {
        if (port == null || data == null || len <= 0) return 0;

        try {
            byte[] slice = len >= data.length ? data : Arrays.copyOf(data, len);
            port.write(slice, 50);
            return slice.length;
        } catch (Exception e) {
            return 0;
        }
    }

    /**
     * Legacy read into a caller-supplied buffer. Never blocks.
     * Note: JNI does not copy the array back to C#, so Unity uses readBytes instead.
     */
    public static int read(byte[] buffer, int maxLen) {
        if (buffer == null) return 0;

        byte[] data = readBytes(Math.min(maxLen, buffer.length));
        System.arraycopy(data, 0, buffer, 0, data.length);
        return data.length;
    }

    /**
     * Returns up to maxLen bytes that the background reader has received. Never blocks the caller
     * (Unity polls this from the main thread every frame). Bytes beyond maxLen stay queued for the next call.
     * @return a new array with at most maxLen bytes (empty when nothing is waiting).
     */
    public static byte[] readBytes(int maxLen) {
        if (maxLen <= 0) return new byte[0];

        synchronized (lock) {
            if (pending.size() == 0) return new byte[0];

            byte[] all = pending.toByteArray();
            int count = Math.min(maxLen, all.length);
            byte[] result = Arrays.copyOf(all, count);

            pending.reset();
            pending.write(all, count, all.length - count);

            return result;
        }
    }

    private static void startReader(final UsbSerialPort readPort) {
        running = true;
        reader = new Thread(() -> {
            // USB serial reads whole packets: the buffer must hold at least 64 bytes.
            byte[] buffer = new byte[4096];

            while (running) {
                try {
                    int n = readPort.read(buffer, 200);
                    if (n <= 0) continue;

                    synchronized (lock) {
                        // Drop data rather than grow without bound if Unity stops draining.
                        if (pending.size() + n <= MAX_PENDING) pending.write(buffer, 0, n);
                    }
                } catch (Exception e) {
                    // Port closed or device unplugged.
                    running = false;
                }
            }
        }, "SerialBridgeReader");
        reader.setDaemon(true);
        reader.start();
    }

    /**
     * True while a port is open and the background reader is running.
     * Becomes false when the reader stops because the device was unplugged or the port failed.
     */
    public static boolean isOpen() {
        return port != null && running;
    }

    public static void close() {
        running = false;

        if (port != null) {
            try {
                port.close(); // also unblocks the reader's pending read
            } catch (Exception ignored) {}
        }

        if (reader != null) {
            try {
                reader.join(500);
            } catch (InterruptedException ignored) {}
            reader = null;
        }

        port = null;

        synchronized (lock) {
            pending.reset();
        }
    }
}
