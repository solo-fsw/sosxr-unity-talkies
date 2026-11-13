using System;
using UnityEngine;
using Object = UnityEngine.Object;


namespace SOSXR.Talkies
{
    /// <summary>
    ///     Low-level byte-based serial communication component.
    ///     Sends single or dual-byte commands to a connected device (e.g. an Arduino Leonardo)
    ///     and reads single-byte responses.
    ///     <para>
    ///         Needs an <see cref="ISerialConnect" /> component (such as <see cref="DesktopSerialConnector" /> or
    ///         <see cref="AndroidSerialConnector" />) on the same GameObject. All reads and writes go through that connector.
    ///     </para>
    ///     <para>
    ///         <b>Note:</b> the Arduino IDE must not be connected to the same device simultaneously;
    ///         if it is, Unity will fail to connect or the IDE will override the baud rate.
    ///     </para>
    /// </summary>
    public class ByteSize : MonoBehaviour
    {
        [Tooltip("Careful: if theres an issue in reading, this will make your life a little more interesting than it needs to be.")]
        [SerializeField] private bool m_readEveryFrame;

        [SerializeField] [DisableEditing] private float m_sendTime;
        [SerializeField] [DisableEditing] private float m_readTime;
        [SerializeField] [DisableEditing] private float m_duration;

        private readonly byte[] _readBuffer = new byte[1];

        private ISerialConnect _connector;
        private bool _warnedNoConnector;


        /// <summary>
        ///     The serial connector used for all reads and writes. Resolved lazily from this GameObject,
        ///     so it also works in builds and after <c>AddComponent</c> at runtime. Can be set explicitly.
        /// </summary>
        public ISerialConnect Connector
        {
            get
            {
                if (IsMissing(_connector))
                {
                    _connector = GetComponent<ISerialConnect>();
                }

                if (IsMissing(_connector) && !_warnedNoConnector)
                {
                    _warnedNoConnector = true;
                    Debug.LogWarning($"{nameof(ByteSize)} on {name} has no {nameof(ISerialConnect)} component. Add a {nameof(DesktopSerialConnector)} or {nameof(AndroidSerialConnector)}.");
                }

                return IsMissing(_connector) ? null : _connector;
            }
            set
            {
                _connector = value;
                _warnedNoConnector = false;
            }
        }

        /// <summary><c>true</c> when a connector exists and reports that it is connected.</summary>
        public bool IsConnected => Connector is { IsConnected: true };

        /// <summary>The last byte received by <see cref="ReadBuffer" />, or -1 when nothing was received yet.</summary>
        public int LastReceivedByte { get; private set; } = -1;


        private static bool IsMissing(ISerialConnect connector)
        {
            if (connector == null)
            {
                return true;
            }

            // A destroyed Unity component compares equal to null only through UnityEngine.Object's operator.
            return connector is Object unityObject && unityObject == null;
        }


        private void Awake()
        {
            _ = Connector;
        }


        /// <summary>
        ///     Sends a single-byte command to the connected device.
        /// </summary>
        /// <param name="command">The byte to send (maps to an ASCII command character).</param>
        /// <returns><c>true</c> when the byte was written.</returns>
        public bool SendCommand(byte command)
        {
            return SendBytes(new[] {command});
        }


        /// <summary>
        ///     Sends a position byte and a speed byte to the connected device.
        /// </summary>
        /// <param name="command">Position byte (ASCII-mapped).</param>
        /// <param name="speed">Speed byte (ASCII-mapped).</param>
        /// <returns><c>true</c> when both bytes were written.</returns>
        public bool SendCommand(byte command, byte speed)
        {
            return SendBytes(new[] {command, speed});
        }


        /// <summary>
        ///     Sends a position and speed command using single-character strings.
        ///     Each string must be exactly one character long.
        /// </summary>
        /// <param name="position">A single character representing the position command.</param>
        /// <param name="speed">A single character representing the speed command.</param>
        [Button]
        public void SendCommand(string position, string speed)
        {
            if (position is not { Length: 1 })
            {
                Debug.LogWarning("Incorrect position provided. Needs to be a single character.");

                return;
            }

            if (speed is not { Length: 1 })
            {
                Debug.LogWarning("Incorrect speed provided. Needs to be a single character.");

                return;
            }

            SendCommand(ASCIITable.FromASCII(position), ASCIITable.FromASCII(speed));
        }


        /// <summary>
        ///     Sends a combined position+speed command from a two-character string.
        ///     The first character is the position command, the second is the speed command.
        /// </summary>
        /// <param name="posSpeed">A two-character string encoding position and speed.</param>
        [Button]
        public void SendCommand(string posSpeed)
        {
            if (posSpeed is not { Length: 2 })
            {
                Debug.LogWarning("This requires 2 characters");

                return;
            }

            SendCommand(ASCIITable.FromASCII(posSpeed[0]), ASCIITable.FromASCII(posSpeed[1]));
        }


        /// <summary>Sends the <c>'u'</c> (unlock) command to the device.</summary>
        [Button]
        public void Unlock()
        {
            SendCommand(ASCIITable.FromASCII('u'));
        }


        /// <summary>Sends the <c>'i'</c> (info) command, asking the device to report its state.</summary>
        [Button]
        public void Info()
        {
            SendCommand(ASCIITable.FromASCII('i'));
        }


        /// <summary>Sends the <c>'s'</c> (stop) command to the device.</summary>
        [Button]
        public void Stop()
        {
            SendCommand(ASCIITable.FromASCII('s'));
        }


        /// <summary>Sends the <c>'v'</c> (version) command to the device.</summary>
        [Button]
        public void Version()
        {
            SendCommand(ASCIITable.FromASCII('v'));
        }


        private bool SendBytes(byte[] data)
        {
            var connector = Connector;

            if (connector is not { IsConnected: true })
            {
                Debug.LogWarning("Not connected! Cannot send command.");

                return false;
            }

            int written;

            try
            {
                written = connector.Write(data, 0, data.Length);
            }
            catch (Exception e)
            {
                Debug.LogError($"Serial write failed: {e.Message}");

                return false;
            }

            if (written != data.Length)
            {
                Debug.LogWarning($"We may not have sent all. Written {written}/{data.Length} bytes.");

                return false;
            }

            m_sendTime = Time.time;

            return true;
        }


        private void Update()
        {
            if (m_readEveryFrame)
            {
                ReadBuffer();
            }
        }


        /// <summary>
        ///     Reads a single byte through the connector, if one is available, into <see cref="LastReceivedByte" />.
        ///     Records the time since the last send. Does nothing when not connected or when no byte is available.
        ///     Safe to call manually or from <c>Update</c> (controlled by <c>m_readEveryFrame</c>).
        /// </summary>
        [Button]
        public void ReadBuffer()
        {
            TryReadByte(out _);
        }


        /// <summary>Reads a single byte through the connector, if one is available.</summary>
        /// <returns><c>false</c> when not connected or when nothing was available.</returns>
        public bool TryReadByte(out byte value)
        {
            value = 0;
            var connector = Connector;

            if (connector is not { IsConnected: true })
            {
                return false;
            }

            int read;

            try
            {
                read = connector.Read(_readBuffer, 0, 1);
            }
            catch (Exception e)
            {
                Debug.LogError($"Serial read failed: {e.Message}");

                return false;
            }

            if (read < 1)
            {
                return false; // No data available
            }

            value = _readBuffer[0];
            LastReceivedByte = value;
            m_readTime = Time.time;
            m_duration = m_readTime - m_sendTime;

            return true;
        }
    }
}
