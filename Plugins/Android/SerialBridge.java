package com.sosxr.serial;

import android.app.Activity;
import android.content.Context;
import android.hardware.usb.*;

import java.util.Arrays;

import com.hoho.android.usbserial.driver.*;

public class SerialBridge {
    private static UsbSerialPort port;
    private static byte[] readTemp = new byte[4096];

    public static boolean open(Activity activity, int baudRate) {
        UsbManager manager = (UsbManager) activity.getSystemService(Context.USB_SERVICE);

        for (UsbDevice device : manager.getDeviceList().values()) {
            UsbSerialDriver driver = UsbSerialProber.getDefaultProber().probeDevice(device);
            if (driver == null) continue;

            try {
                UsbDeviceConnection conn = manager.openDevice(device);
                if (conn == null) return false;

                port = driver.getPorts().get(0);
                port.open(conn);
                port.setParameters(baudRate, 8, 1, UsbSerialPort.PARITY_NONE);

                return true;
            } catch (Exception e) {
                return false;
            }
        }
        return false;
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
     * Legacy read into a caller-supplied buffer.
     * Note: JNI does not copy the array back to C#, so Unity uses readBytes instead.
     */
    public static int read(byte[] buffer, int maxLen) {
        if (port == null) return 0;

        try {
            int n = port.read(readTemp, 50);
            if (n <= 0) return 0;

            int copyLen = Math.min(n, maxLen);
            System.arraycopy(readTemp, 0, buffer, 0, copyLen);
            return copyLen;
        } catch (Exception e) {
            return 0;
        }
    }

    // Bytes read from USB but not yet handed to Unity (a USB read returns at least one 64-byte packet).
    private static byte[] leftover = new byte[0];

    /**
     * Reads up to maxLen available bytes. Bytes beyond maxLen are kept and returned by the next call,
     * so small reads (such as 1 byte) lose nothing.
     * @return a new array with at most maxLen bytes (empty when nothing was read or on error).
     */
    public static byte[] readBytes(int maxLen) {
        if (port == null || maxLen <= 0) return new byte[0];

        if (leftover.length == 0) {
            try {
                // USB serial reads whole packets: the buffer must hold at least 64 bytes.
                byte[] buffer = new byte[Math.max(64, Math.min(maxLen, readTemp.length))];
                int n = port.read(buffer, 50);
                if (n <= 0) return new byte[0];

                leftover = Arrays.copyOf(buffer, n);
            } catch (Exception e) {
                return new byte[0];
            }
        }

        int count = Math.min(maxLen, leftover.length);
        byte[] result = Arrays.copyOf(leftover, count);
        leftover = Arrays.copyOfRange(leftover, count, leftover.length);

        return result;
    }

    public static void close() {
        if (port != null) {
            try {
                port.close();
            } catch (Exception ignored) {}
        }
        port = null;
        leftover = new byte[0];
    }
}
