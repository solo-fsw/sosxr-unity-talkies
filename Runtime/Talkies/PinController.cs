using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using Object = UnityEngine.Object;
using Random = UnityEngine.Random;


namespace SOSXR.Talkies
{
    /// <summary>
    ///     Controls GPIO pins on a Raspberry Pi Pico (or compatible device) connected via serial.
    ///     Communicates using a simple CSV command protocol: <c>SET,&lt;pin&gt;,&lt;0|1&gt;</c> and
    ///     <c>GET,&lt;pin&gt;</c> (see <see cref="PinProtocol" />). Responses are parsed and surfaced via
    ///     <see cref="OnPinSetEvent" /> and <see cref="OnPinGetEvent" />.
    ///     <para>
    ///         Needs an <see cref="ISerialConnect" /> component (such as <see cref="DesktopSerialConnector" /> or
    ///         <see cref="AndroidSerialConnector" />) on the same GameObject. All reads and writes go through that
    ///         connector. Consider also adding <see cref="SafetyPin" /> to automatically drive pins LOW
    ///         after a configurable timeout.
    ///     </para>
    /// </summary>
    public class PinController : MonoBehaviour
    {
        /// <summary>The onboard LED pin of a Raspberry Pi Pico.</summary>
        public const int LedPin = 25;

        private const int ReadChunkSize = 1024;
        private const int MaxReadChunksPerFrame = 16;

        [Header("Pin Control")]
        [SerializeField] [Range(0, 29)] private int m_defaultPin = 16;

        [Tooltip("A toggle that has not received the device's GET answer within this time is dropped, so a late answer cannot switch the pin minutes later.")]
        [SerializeField] [Suffix("s")] private float m_pendingToggleTimeout = 2f;

        [Header("Debug")]
        [Tooltip("Randomly toggles the onboard LED (pin 25) while enabled. For testing the connection only.")]
        [SerializeField] private bool m_debugToggleLED;
        [SerializeField] [ShowIf(nameof(m_debugToggleLED))] private Vector2 m_debugToggleRange = new(0.1f, 0.5f);

        private readonly byte[] _readBuffer = new byte[ReadChunkSize];
        private readonly SerialLineBuffer _lineBuffer = new();
        private readonly List<int> _pinList = new();
        private readonly Dictionary<int, PendingToggle> _pendingToggles = new();
        private readonly List<int> _expiredToggles = new();

        private ISerialConnect _connector;
        private bool _warnedNoConnector;
        private bool _wasConnected;

        /// <summary>Clock for pending-toggle timeouts, in seconds. Unscaled, so a paused game does not keep toggles alive. Replaceable for tests.</summary>
        internal Func<float> Clock = () => Time.unscaledTime;

        /// <summary>Fired when a GET response is received, reporting the pin number and its current value.</summary>
        public event Action<int, bool> OnPinGetEvent;

        /// <summary>Fired after a SET command confirmation is received, with the pin number and the new value.</summary>
        public event Action<int, bool> OnPinSetEvent;


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
                    Debug.LogWarning($"{nameof(PinController)} on {name} has no {nameof(ISerialConnect)} component. Add a {nameof(DesktopSerialConnector)} or {nameof(AndroidSerialConnector)}.");
                }

                return IsMissing(_connector) ? null : _connector;
            }
            set
            {
                _connector = value;
                _warnedNoConnector = false;
                _wasConnected = false;
                ResetPendingState();
            }
        }

        /// <summary>How long, in seconds, a toggle waits for the device's GET answer before it is dropped.</summary>
        public float PendingToggleTimeout
        {
            get => m_pendingToggleTimeout;
            set => m_pendingToggleTimeout = Mathf.Max(0.1f, value);
        }


        private readonly struct PendingToggle
        {
            public readonly Action<int, bool> Callback;
            public readonly float RequestedAt;


            public PendingToggle(Action<int, bool> callback, float requestedAt)
            {
                Callback = callback;
                RequestedAt = requestedAt;
            }
        }

        /// <summary><c>true</c> when a connector exists and reports that it is connected.</summary>
        public bool IsConnected => Connector is { IsConnected: true };

        /// <summary>Pins that have been set through this controller. They are driven LOW on disable.</summary>
        public IReadOnlyList<int> UsedPins => _pinList;


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


        private void OnEnable()
        {
            if (m_debugToggleLED && m_debugToggleRange != Vector2.zero)
            {
                StartCoroutine(DebugToggleCR());
            }
        }


        private void Start()
        {
            if (GetComponent<SafetyPin>() == null)
            {
                Debug.LogWarning($"You're running this without {nameof(SafetyPin)}. Is that wise?");
            }
        }


        private IEnumerator DebugToggleCR()
        {
            for (;;)
            {
                var duration = Random.Range(m_debugToggleRange.x, m_debugToggleRange.y);

                yield return new WaitForSeconds(duration);

                if (IsConnected)
                {
                    ToggleLED();
                }
            }
        }


        private void Update()
        {
            PollSerial();
        }


        /// <summary>
        ///     Reads all bytes the connector has available and processes every complete response line.
        ///     Called every frame from <c>Update</c>. Does nothing while not connected.
        /// </summary>
        public void PollSerial()
        {
            var connector = Connector;
            var connected = connector is { IsConnected: true };

            TrackConnectionState(connected);

            if (!connected)
            {
                return;
            }

            ExpirePendingToggles();

            for (var i = 0; i < MaxReadChunksPerFrame; i++)
            {
                int bytesRead;

                try
                {
                    bytesRead = connector.Read(_readBuffer, 0, _readBuffer.Length);
                }
                catch (Exception e)
                {
                    Debug.LogError($"Serial read failed: {e.Message}");

                    break;
                }

                if (bytesRead <= 0)
                {
                    break;
                }

                _lineBuffer.Append(_readBuffer, 0, bytesRead);

                if (bytesRead < _readBuffer.Length)
                {
                    break;
                }
            }

            while (_lineBuffer.TryReadLine(out var line))
            {
                ProcessResponse(line);
            }
        }


        /// <summary>
        ///     A connect or disconnect makes everything in flight meaningless: half a line from the old connection,
        ///     and toggles whose GET answer will never come (or would come from a different session).
        /// </summary>
        private void TrackConnectionState(bool connected)
        {
            if (connected == _wasConnected)
            {
                return;
            }

            _wasConnected = connected;
            ResetPendingState();
        }


        private void ResetPendingState()
        {
            _pendingToggles.Clear();
            _lineBuffer.Clear();
        }


        /// <summary>Drops toggles that have waited longer than <see cref="PendingToggleTimeout" /> for their GET answer.</summary>
        internal void ExpirePendingToggles()
        {
            if (_pendingToggles.Count == 0)
            {
                return;
            }

            var now = Clock();
            _expiredToggles.Clear();

            foreach (var pair in _pendingToggles)
            {
                if (IsExpired(pair.Value, now))
                {
                    _expiredToggles.Add(pair.Key);
                }
            }

            foreach (var pin in _expiredToggles)
            {
                _pendingToggles.Remove(pin);
                Debug.LogWarning($"Toggle of pin {pin} dropped: no answer from the device within {m_pendingToggleTimeout} s.");
            }
        }


        private bool IsExpired(PendingToggle toggle, float now)
        {
            return now - toggle.RequestedAt > m_pendingToggleTimeout;
        }


        /// <summary>Handles one response line from the device. Public so it can be fed from other sources and tested.</summary>
        public void ProcessResponse(string line)
        {
            var response = PinProtocol.Parse(line);

            switch (response.Kind)
            {
                case PinResponseKind.PinSet:
                    OnPinSetEvent?.Invoke(response.Pin, response.Value);

                    break;
                case PinResponseKind.PinGet:
                    if (_pendingToggles.TryGetValue(response.Pin, out var toggle))
                    {
                        _pendingToggles.Remove(response.Pin);

                        if (IsExpired(toggle, Clock()))
                        {
                            Debug.LogWarning($"Toggle of pin {response.Pin} dropped: the device answered after more than {m_pendingToggleTimeout} s.");
                        }
                        else
                        {
                            toggle.Callback?.Invoke(response.Pin, response.Value);
                        }
                    }

                    OnPinGetEvent?.Invoke(response.Pin, response.Value);

                    break;
                case PinResponseKind.Error:
                    Debug.LogError($"Pico Error: {response.Raw}");

                    break;
                case PinResponseKind.Malformed:
                    Debug.LogWarning($"Pico sent a malformed response, ignored: {response.Raw}");

                    break;
                default: // This includes the PING/PONG debug response
                    Debug.Log($"Pico: {response.Raw}");

                    break;
            }
        }


        private bool SendCommand(string command)
        {
            var connector = Connector;

            if (connector is not { IsConnected: true })
            {
                Debug.LogWarning($"Not connected. Cannot send: {command}");

                return false;
            }

            var data = Encoding.ASCII.GetBytes(command + "\n");
            int written;

            try
            {
                written = connector.Write(data, 0, data.Length);
            }
            catch (Exception e)
            {
                Debug.LogError($"Write failed for command {command}: {e.Message}");

                return false;
            }

            if (written != data.Length)
            {
                Debug.LogError($"Write failed. Sent {written}/{data.Length} bytes for command: {command}");

                return false;
            }

            return true;
        }


        /// <summary>
        ///     Basic debug method.
        /// </summary>
        [Button(space: 10, horizontalLine: true)]
        public void Ping()
        {
            SendCommand("PING");
        }


        /// <summary>
        ///     Handy debug method to toggle the onboard LED. Also demonstrates reading pin state before acting on it.
        /// </summary>
        [Button]
        public void ToggleLED()
        {
            TogglePin(LedPin);
        }


        /// <summary>
        ///     Sends a SET command for the default pin configured in the Inspector.
        /// </summary>
        /// <param name="value"><c>true</c> sets the pin HIGH; <c>false</c> sets it LOW.</param>
        [Button(space: 10, horizontalLine: true)]
        public void SetDefaultPin(bool value)
        {
            SetPin(m_defaultPin, value);
        }


        /// <summary>Sends a GET command for the default pin, which triggers <see cref="OnPinGetEvent" /> when the response arrives.</summary>
        [Button]
        public void GetDefaultPin()
        {
            GetPin(m_defaultPin);
        }


        /// <summary>Reads the current value of the default pin, then sets it to the opposite state.</summary>
        [Button]
        public void ToggleDefaultPin()
        {
            TogglePin(m_defaultPin);
        }


        /// <summary>
        ///     Sets a specific GPIO pin HIGH or LOW. Tracks the pin internally so it can be driven
        ///     LOW on disable.
        /// </summary>
        /// <param name="pin">GPIO pin number on the target device.</param>
        /// <param name="value"><c>true</c> = HIGH, <c>false</c> = LOW.</param>
        [Button(space: 10, horizontalLine: true)]
        public void SetPin(int pin, bool value)
        {
            if (!_pinList.Contains(pin))
            {
                _pinList.Add(pin);
            }

            SendCommand(PinProtocol.FormatSet(pin, value));
        }


        /// <summary>
        ///     Sends a GET command for the given pin. The response arrives asynchronously via <see cref="OnPinGetEvent" />.
        /// </summary>
        /// <param name="pin">GPIO pin number to query.</param>
        [Button]
        public void GetPin(int pin)
        {
            SendCommand(PinProtocol.FormatGet(pin));
        }


        /// <summary>
        ///     Reads the current value of the specified pin, then sets it to the opposite state.
        ///     Only one toggle per pin can be pending; a second call before the device answers replaces the first.
        ///     A toggle is dropped when the device does not answer within <see cref="PendingToggleTimeout" />, or when
        ///     the connection changes. Does nothing while not connected.
        /// </summary>
        /// <param name="pin">GPIO pin number to toggle.</param>
        [Button]
        public void TogglePin(int pin)
        {
            if (!IsConnected)
            {
                Debug.LogWarning($"Not connected. Cannot toggle pin {pin}.");

                return;
            }

            // Record the connection first, so the next poll does not mistake it for a fresh connection and clear this toggle.
            TrackConnectionState(true);

            _pendingToggles[pin] = new PendingToggle((p, currentValue) =>
            {
                var newValue = !currentValue;
                SetPin(p, newValue);
                Debug.Log($"Toggled pin {p} from {PinProtocol.HighLow(currentValue)} to {PinProtocol.HighLow(newValue)}");
            }, Clock());

            if (!SendCommand(PinProtocol.FormatGet(pin)))
            {
                _pendingToggles.Remove(pin);
            }
        }


        /// <summary>Number of toggles waiting for a GET answer from the device.</summary>
        public int PendingToggleCount => _pendingToggles.Count;


        private void OnDisable()
        {
            StopAllCoroutines();
            _pendingToggles.Clear();

            if (!IsConnected)
            {
                return;
            }

            foreach (var pin in _pinList.ToArray())
            {
                SetPin(pin, false);
            }
        }
    }
}
