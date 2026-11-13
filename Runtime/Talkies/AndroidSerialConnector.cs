using System;
using UnityEngine;


namespace SOSXR.Talkies
{
    /// <summary>
    ///     Connects to an Android USB serial device via a Java bridge (<c>com.sosxr.serial.SerialBridge</c>,
    ///     in <c>Plugins/Android/SerialBridge.java</c>). Implements <see cref="ISerialConnect" /> for use alongside
    ///     <see cref="PinController" />.
    ///     Only functional on Android; other platforms get silent no-op stubs.
    ///     <para>
    ///         The Java bridge depends on <c>usb-serial-for-android</c> (<c>com.hoho.android.usbserial</c>), which
    ///         is not shipped with this package. Add it to your Gradle dependencies.
    ///     </para>
    /// </summary>
    public class AndroidSerialConnector : MonoBehaviour, ISerialConnect
    {
        [DisableEditing] [SerializeField] private bool m_isConnected = false;

        /// <inheritdoc />
        public bool IsConnected => m_isConnected;

#if UNITY_ANDROID && !UNITY_EDITOR
        private readonly int _baudRate = 115200;

        private AndroidJavaClass serialClass;
        private AndroidJavaObject activity;


        private void Awake()
        {
            try
            {
                serialClass = new AndroidJavaClass("com.sosxr.serial.SerialBridge");

                using var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
            }
            catch (Exception ex)
            {
                Debug.LogError($"Could not load the Android serial bridge: {ex.Message}");
                serialClass = null;
            }
        }


        /// <summary>
        ///     Opens the USB serial connection at 115200 baud via the Android Java bridge.
        ///     Sets <see cref="IsConnected" /> based on the result.
        /// </summary>
        [Button]
        public void Connect()
        {
            if (serialClass == null)
            {
                m_isConnected = false;

                return;
            }

            try
            {
                m_isConnected = serialClass.CallStatic<bool>("open", activity, _baudRate);
            }
            catch (Exception ex)
            {
                Debug.LogError($"Exception while connecting: {ex}");
                m_isConnected = false;
            }
        }


        /// <summary>
        ///     Closes the USB serial connection. Does nothing if not currently connected.
        /// </summary>
        [Button]
        public void Disconnect()
        {
            if (!m_isConnected || serialClass == null)
            {
                return;
            }

            serialClass.CallStatic("close");
            m_isConnected = false;
            Debug.Log("Disconnected from USB device");
        }


        /// <inheritdoc />
        public int Write(byte[] data, int offset, int count)
        {
            if (!m_isConnected || serialClass == null || data == null || count <= 0)
            {
                return 0;
            }

            count = Math.Min(count, data.Length - offset);
            var slice = new sbyte[count];
            Buffer.BlockCopy(data, offset, slice, 0, count);

            try
            {
                return serialClass.CallStatic<int>("write", slice, count);
            }
            catch (Exception ex)
            {
                Debug.LogError($"Serial write failed: {ex.Message}");

                return 0;
            }
        }


        /// <inheritdoc />
        /// <remarks>
        ///     JNI copies array arguments into Java but not back, so the bridge returns a new array
        ///     (<c>readBytes</c>) instead of filling one we pass in.
        /// </remarks>
        public int Read(byte[] buffer, int offset, int count)
        {
            if (!m_isConnected || serialClass == null || buffer == null || count <= 0)
            {
                return 0;
            }

            count = Math.Min(count, buffer.Length - offset);

            try
            {
                var received = serialClass.CallStatic<sbyte[]>("readBytes", count);

                if (received == null || received.Length == 0)
                {
                    return 0;
                }

                var length = Math.Min(received.Length, count);
                Buffer.BlockCopy(received, 0, buffer, offset, length);

                return length;
            }
            catch (Exception ex)
            {
                Debug.LogError($"Serial read failed: {ex.Message}");

                return 0;
            }
        }


        private void OnDestroy()
        {
            Disconnect();
        }
#else
        /// <summary>No-op outside Android.</summary>
        public void Connect()
        {
            Debug.LogWarning("This is the Android Serial connector, which cannot Connect on non-Android systems");
        }


        /// <summary>No-op outside Android.</summary>
        public void Disconnect()
        {
        }


        /// <summary>Always 0 outside Android.</summary>
        public int Write(byte[] data, int offset, int count)
        {
            return 0;
        }


        /// <summary>Always 0 outside Android.</summary>
        public int Read(byte[] buffer, int offset, int count)
        {
            return 0;
        }
#endif
    }
}
