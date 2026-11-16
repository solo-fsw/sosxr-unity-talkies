using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;


namespace SOSXR.Talkies
{
    /// <summary>
    ///     Baud rate presets supported by the native <c>SerialPlugin</c>.
    ///     Each value maps to a byte index that the plugin uses internally.
    ///     <list type="bullet">
    ///         <item><description><c>B4800</c> — command mode baud rate (used by <see cref="DesktopSerialConnector.ConnectInCommandMode" />).</description></item>
    ///         <item><description><c>B115200</c> — high-speed data mode baud rate (used by <see cref="DesktopSerialConnector.ConnectInDataMode" />).</description></item>
    ///     </list>
    /// </summary>
    public enum BaudRate : byte
    {
        /// <summary>4800 baud. Command mode rate.</summary>
        B4800 = 0,
        /// <summary>9600 baud.</summary>
        B9600 = 1,
        /// <summary>19200 baud.</summary>
        B19200 = 2,
        /// <summary>38400 baud.</summary>
        B38400 = 3,
        /// <summary>57600 baud.</summary>
        B57600 = 4,
        /// <summary>115200 baud. High-speed data mode rate.</summary>
        B115200 = 5,
        /// <summary>Test baud rate preset.</summary>
        TestBaud = 10
    }


    /// <summary>
    ///     Connects to a desktop serial device (macOS or Windows) via the native <c>SerialPlugin</c> library.
    ///     Auto-detects available ports on startup and connects in command mode (4800 baud) by default.
    ///     Implements <see cref="ISerialConnect" /> for use alongside <see cref="PinController" />.
    ///     <para>
    ///         The native plugin exports <c>SerialOpen</c>, <c>SerialClose</c>, <c>SerialWrite(byte)</c>,
    ///         <c>SerialWriteTwo</c> and <c>SerialRead()</c> (one byte, -1 when nothing is available,
    ///         -2 when the device is gone; plugins built before that code existed return -1 then too).
    ///         <see cref="Write" /> and <see cref="Read" /> loop over those single-byte calls.
    ///         The package ships only a macOS arm64 <c>SerialPlugin.bundle</c>. On Windows, build
    ///         <c>SerialPlugin.dll</c> from <c>Non-Unity/SerialPlugin_Windows.cpp</c> and add it to <c>Plugins/</c>.
    ///     </para>
    ///     <para>
    ///         The native plugin holds a single port handle for the whole process. Only one
    ///         <see cref="DesktopSerialConnector" /> can be connected at a time; a second one refuses to connect.
    ///     </para>
    /// </summary>
    public class DesktopSerialConnector : MonoBehaviour, ISerialConnect
    {
        [DisableEditing] [SerializeField] private string[] m_availablePorts = Array.Empty<string>();
        [SerializeField] private int m_selectedPortIndex = 0;
        [DisableEditing] [SerializeField] private string m_portName = "COM3";
        [DisableEditing] [SerializeField] private bool m_isConnected = false;
        [SerializeField] [DisableEditing] private BaudRate m_baudRate = BaudRate.B4800;
        [SerializeField] private bool m_stripBluetooth = true;

        /// <summary>Native <c>SerialRead</c> result when the device is gone (unplugged, I/O error).</summary>
        private const int SerialReadDisconnected = -2;

        /// <summary>The connector that currently owns the native plugin's single port handle.</summary>
        private static DesktopSerialConnector _owner;

        private bool _warnedNativeMissing;

        /// <inheritdoc />
        public bool IsConnected => m_isConnected;

        /// <summary>The port names found by the last <see cref="RefreshPorts" />.</summary>
        public IReadOnlyList<string> AvailablePorts => m_availablePorts;


        /// <summary>Forgets the port owner. Runs before the first scene loads, so Play mode without domain reload starts clean.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticState()
        {
            _owner = null;
        }


        /// <summary>
        ///     Opens the serial connection on the currently selected port.
        ///     Calls <see cref="Disconnect" /> first if a connection is already open.
        ///     Does nothing in Edit mode.
        /// </summary>
        public void Connect()
        {
            if (!Application.isPlaying)
            {
                return;
            }

            if (m_isConnected)
            {
                Disconnect(); // Close any existing connection
            }

            if (string.IsNullOrEmpty(m_portName))
            {
                Debug.LogWarning("No port selected. Run RefreshPorts() first.");

                return;
            }

            // The native plugin has one global port handle: opening a second port would orphan the first one's.
            if (_owner != null && _owner != this && _owner.m_isConnected)
            {
                Debug.LogError($"{nameof(DesktopSerialConnector)} on {name} cannot connect: {_owner.name} is already connected on {_owner.m_portName}. " +
                               "The native SerialPlugin supports one open port at a time. Disconnect that one first.");

                return;
            }

            try
            {
                var result = SerialOpen(m_portName, (byte) m_baudRate, false);

                if (result == 1)
                {
                    m_isConnected = true;
                    _owner = this;
                    Debug.Log($"Connected to device on {m_portName} with baud rate {m_baudRate}");
                }
                else
                {
                    m_isConnected = false;
                    ReleaseOwnership();
                    Debug.LogError($"Failed to open {m_portName}. Check connection. Is another debugger / IDE open (e.g. Thonny / Arduino IDE)?");
                }
            }
            catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
            {
                m_isConnected = false;
                LogNativeMissing(ex);
            }
            catch (Exception ex)
            {
                m_isConnected = false;
                Debug.LogError("Is another debugger / IDE open (e.g. Thonny / Arduino IDE)?");
                Debug.LogError($"Exception while connecting: {ex.Message}");
                SafeClose();
            }
        }


        /// <summary>
        ///     Closes the serial connection via the native plugin.
        ///     Does nothing in Edit mode.
        /// </summary>
        [Button]
        public void Disconnect()
        {
            if (!Application.isPlaying)
            {
                return;
            }

            // Only close the native port when this instance opened it: the plugin's handle is shared by every connector.
            if (!m_isConnected)
            {
                return;
            }

            SafeClose();
            Debug.Log("Disconnected from device on " + m_portName);
            m_isConnected = false;
            ReleaseOwnership();
        }


        private void ReleaseOwnership()
        {
            if (_owner == this)
            {
                _owner = null;
            }
        }


        /// <summary>The device disappeared (unplugged or I/O error): close the port and report not connected.</summary>
        private void HandleDeviceLost()
        {
            Debug.LogWarning($"Serial device on {m_portName} was disconnected (unplugged or I/O error).");
            SafeClose();
            m_isConnected = false;
            ReleaseOwnership();
        }


        /// <inheritdoc />
        public int Write(byte[] data, int offset, int count)
        {
            if (!m_isConnected || data == null)
            {
                return 0;
            }

            var end = Math.Min(data.Length, offset + count);
            var written = 0;

            try
            {
                for (var i = offset; i < end; i++)
                {
                    if (SerialWrite(data[i]) != 1)
                    {
                        break;
                    }

                    written++;
                }
            }
            catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
            {
                m_isConnected = false;
                LogNativeMissing(ex);
            }

            return written;
        }


        /// <inheritdoc />
        public int Read(byte[] buffer, int offset, int count)
        {
            if (!m_isConnected || buffer == null)
            {
                return 0;
            }

            var end = Math.Min(buffer.Length, offset + count);
            var read = 0;

            try
            {
                for (var i = offset; i < end; i++)
                {
                    var value = SerialRead();

                    if (value == SerialReadDisconnected)
                    {
                        HandleDeviceLost();

                        break;
                    }

                    if (value < 0)
                    {
                        break;
                    }

                    buffer[i] = (byte) value;
                    read++;
                }
            }
            catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
            {
                m_isConnected = false;
                LogNativeMissing(ex);
            }

            return read;
        }


        /// <summary>
        ///     Connects at 4800 baud — the default command mode for the connected device.
        /// </summary>
        [Button]
        public void ConnectInCommandMode()
        {
            m_baudRate = BaudRate.B4800;
            Connect();
        }


        /// <summary>
        ///     Connects at 115200 baud — high-speed data mode for the connected device.
        /// </summary>
        [Button]
        public void ConnectInDataMode()
        {
            m_baudRate = BaudRate.B115200;
            Connect();
        }


        [DllImport("SerialPlugin")]
        private static extern int SerialOpen(string portName, byte baudIndex, [MarshalAs(UnmanagedType.I1)] bool debug);


        [DllImport("SerialPlugin")]
        private static extern void SerialClose();


        /// <summary>Native: writes one byte. Returns 1 on success, 0 on error.</summary>
        [DllImport("SerialPlugin")]
        private static extern int SerialWrite(byte value);


        /// <summary>
        ///     Native: reads one byte without blocking. Returns 0-255, -1 when nothing is available, or -2 when the
        ///     device is gone. Plugin binaries built before -2 existed return -1 for a lost device as well.
        /// </summary>
        [DllImport("SerialPlugin")]
        private static extern int SerialRead();


        private void SafeClose()
        {
            try
            {
                SerialClose();
            }
            catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
            {
                LogNativeMissing(ex);
            }
        }


        private void LogNativeMissing(Exception ex)
        {
            if (_warnedNativeMissing)
            {
                return;
            }

            _warnedNativeMissing = true;
            Debug.LogError($"Native SerialPlugin not available for this platform/architecture ({ex.GetType().Name}: {ex.Message}). " +
                           "The package ships a macOS arm64 SerialPlugin.bundle only. Build the plugin from Non-Unity/ for other platforms.");
        }


        private void OnValidate()
        {
            if (m_availablePorts == null || m_availablePorts.Length == 0)
            {
                RefreshPorts();
            }
        }


        private void Awake()
        {
            RefreshPorts();
        }


        private void Start()
        {
            if (m_availablePorts.Length > 0)
            {
                ConnectInCommandMode();
            }
        }


        /// <summary>
        ///     Scans for available serial ports on the current platform and updates
        ///     <c>m_availablePorts</c>. Bluetooth ports are filtered out when
        ///     <c>m_stripBluetooth</c> is enabled, macOS system ports (debug-console, wlan-debug) always.
        ///     USB serial ports are listed first. Disables the component in Play mode if no ports are found.
        /// </summary>
        [Button]
        public void RefreshPorts()
        {
            if (!TryGetPlatformPorts(out var ports))
            {
                m_availablePorts = Array.Empty<string>();
                m_portName = string.Empty;

                if (Application.isPlaying)
                {
                    Debug.LogError($"{nameof(DesktopSerialConnector)} is not implemented for this platform. Cannot continue.");
                    enabled = false;
                }

                return;
            }

            m_availablePorts = FilterPorts(ports, m_stripBluetooth);

            if (m_availablePorts.Length == 0)
            {
                m_portName = string.Empty;

                if (Application.isPlaying)
                {
                    Debug.LogError("No serial ports found. Cannot continue. This now runs in Awake: was that too soon?");
                    enabled = false;
                }

                return;
            }

            m_selectedPortIndex = Mathf.Clamp(m_selectedPortIndex, 0, m_availablePorts.Length - 1);
            m_portName = m_availablePorts[m_selectedPortIndex];
        }


        /// <summary>macOS system ports that are never a connected device.</summary>
        private static readonly string[] SystemPortMarkers = { "debug-console", "wlan-debug" };

        /// <summary>Port name fragments of USB serial adapters (Arduino, Pico, FTDI, CH340, CP210x). These are listed first.</summary>
        private static readonly string[] UsbSerialPortMarkers = { "usbmodem", "usbserial", "wchusbserial", "SLAB_USBtoUART" };


        /// <summary>
        ///     Removes macOS system ports (debug-console, wlan-debug), removes Bluetooth ports when
        ///     <paramref name="stripBluetooth" /> is set, and sorts the result: USB serial ports
        ///     (usbmodem, usbserial, wchusbserial, SLAB_USBtoUART) first, then the rest, each group ordinally.
        ///     Index 0 is the port that auto-connect uses.
        /// </summary>
        public static string[] FilterPorts(IEnumerable<string> ports, bool stripBluetooth)
        {
            var result = new List<string>();

            if (ports != null)
            {
                foreach (var port in ports)
                {
                    if (string.IsNullOrEmpty(port))
                    {
                        continue;
                    }

                    if (stripBluetooth && port.IndexOf("Bluetooth", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        continue;
                    }

                    if (ContainsAny(port, SystemPortMarkers))
                    {
                        continue;
                    }

                    result.Add(port);
                }
            }

            result.Sort((a, b) =>
            {
                var aUsb = ContainsAny(a, UsbSerialPortMarkers);
                var bUsb = ContainsAny(b, UsbSerialPortMarkers);

                if (aUsb != bUsb)
                {
                    return aUsb ? -1 : 1;
                }

                return string.CompareOrdinal(a, b);
            });

            return result.ToArray();
        }


        private static bool ContainsAny(string value, string[] markers)
        {
            foreach (var marker in markers)
            {
                if (value.IndexOf(marker, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }


        private static bool TryGetPlatformPorts(out string[] ports)
        {
#if UNITY_EDITOR_OSX || UNITY_STANDALONE_OSX
            ports = Directory.Exists("/dev/") ? Directory.GetFiles("/dev/", "cu.*") : Array.Empty<string>();

            return true;
#elif UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
            ports = GetWindowsComPorts();

            return true;
#else
            ports = Array.Empty<string>();

            return false;
#endif
        }


#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern uint QueryDosDevice(string lpDeviceName, StringBuilder lpTargetPath, int ucchMax);


        /// <summary>
        ///     Lists COM ports without <c>System.IO.Ports</c>, which is not part of .NET Standard 2.1.
        /// </summary>
        private static string[] GetWindowsComPorts()
        {
            var found = new List<string>();
            var target = new StringBuilder(1024);

            for (var i = 1; i <= 256; i++)
            {
                var name = "COM" + i;

                if (QueryDosDevice(name, target, target.Capacity) != 0)
                {
                    found.Add(name);
                }
            }

            return found.ToArray();
        }
#endif


        private void OnDestroy()
        {
            Disconnect();
        }
    }
}
