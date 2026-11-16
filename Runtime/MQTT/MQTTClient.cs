/*
The MIT License (MIT)

Copyright (c) 2018 Giovanni Paolo Vigano' && 2025 SOSXR

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
*/

using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using SOSXR.Talkies;
using UnityEngine;
using uPLibrary.Networking.M2Mqtt;
using uPLibrary.Networking.M2Mqtt.Messages;


/// <summary>
/// Adaptation for Unity of the M2MQTT library (https://github.com/eclipse/paho.mqtt.m2mqtt),
/// modified to run on UWP (also tested on Microsoft HoloLens).
/// </summary>
namespace MQTTUnity
{
    /// <summary>
    ///     Generic MonoBehaviour wrapping a MQTT client. Messages arrive on M2MQTT's background thread,
    ///     are put in a thread-safe queue, and are dispatched to subscribers on the main thread in <c>Update</c>.
    ///     <para>
    ///         Subscriptions survive disconnects: every (re)connect subscribes to all registered topics again.
    ///         Use <see cref="ClearSubscriptions" /> to drop them explicitly.
    ///     </para>
    /// </summary>
    public class MQTTClient : MonoBehaviour
    {
        [UnityEngine.Header("MQTT broker configuration")]
        [Tooltip("IP address or URL of the host running the broker")]
        [SerializeField] private string m_brokerAddress = "localhost";

        [Tooltip("Port where the broker accepts connections")]
        [SerializeField] private int m_brokerPort = 1883;

        [Tooltip("Use encrypted connection (TLS 1.2)")]
        [SerializeField]
        private bool m_isEncrypted;

        [UnityEngine.Header("Connection parameters")]
        [Tooltip("Connection to the broker is delayed by the the given milliseconds")]
        [SerializeField] [Suffix("ms")] private int m_connectionDelay = 500;

        [Tooltip("Connection timeout in milliseconds")]
        [SerializeField] [Suffix("ms")] private int m_timeoutOnConnection = MqttSettings.MQTT_CONNECT_TIMEOUT;

        [Tooltip("Connect on startup")]
        [SerializeField] private bool m_autoConnect;

        [Tooltip("Keep this client (and its connection) alive when a new scene loads")]
        [SerializeField] private bool m_persistAcrossScenes;

        [Tooltip("Reconnect automatically after the connection is lost or a connection attempt fails. Waits 1, 2, 4, ... seconds between attempts, up to the maximum below. Stops on Disconnect.")]
        [SerializeField] private bool m_autoReconnect;

        [Tooltip("Longest wait between two reconnect attempts")]
        [SerializeField] [Suffix("s")] private float m_maxReconnectDelay = 30f;

        [Tooltip("Most publications kept while not connected. When full, the oldest is dropped. 0 or less: no limit.")]
        [SerializeField] private int m_maxPendingPublications = DefaultMaxPendingPublications;

        [Tooltip("Publications older than this are dropped instead of sent when the connection is made, e.g. for time-critical markers. 0 or less: no age limit.")]
        [SerializeField] [Suffix("s")] private float m_maxPendingPublicationAge;

        [Tooltip("UserName for the MQTT broker. Keep blank if no user name is required.")]
        [SerializeField] private string m_MQTTUserName = string.Empty;

        [Tooltip("Password for the MQTT broker. Keep blank if no password is required.")]
        [SerializeField] private string m_MQTTPassword = string.Empty;

        /// <summary>Default for the maximum number of publications queued while not connected.</summary>
        public const int DefaultMaxPendingPublications = 100;

        private static MQTTBackend _MQTTBackend;

        /// <summary>
        ///     Backend of a finished connect attempt that the connect coroutine has not picked up yet. Guarded by
        ///     <see cref="_connectLock" />, together with <see cref="_connectionGeneration" /> changes, so a
        ///     disconnect either sees it here and shuts it down, or the connect thread sees the new generation.
        /// </summary>
        private static MQTTBackend _connectingBackend;
        private static readonly object _connectLock = new();

        private static readonly ConcurrentQueue<MqttMsgPublishEventArgs> _incomingMessages = new();
        private static readonly Dictionary<string, Subscription> _topicCallbacks = new();
        private static readonly List<(string topic, byte[] message, byte qosLevel, bool retain, long queuedAt)> _pendingPublications = new(); // For anyone publishing before we're connected
        private static bool _warnedPendingOverflow;

        private static int _connectionGeneration;
        private static volatile bool _connectionClosed;
        private static volatile bool _connected;

        private Coroutine _connect;
        private Coroutine _disconnect;
        private Coroutine _reconnect;
        private int _reconnectAttempts;
        private bool _wantConnected; // Connect() was called and Disconnect() was not: auto-reconnect and re-enable may connect again
        private bool _resumeOnEnable;

        /// <summary>Gets or sets the broker hostname or IP address.</summary>
        public string BrokerAddress
        {
            get => m_brokerAddress;
            set => m_brokerAddress = value;
        }

        /// <summary>Gets or sets the broker port as a string. Invalid values are silently ignored.</summary>
        public string BrokerPortString
        {
            get => m_brokerPort.ToString();
            set => m_brokerPort = int.TryParse(value, out var port) ? port : m_brokerPort;
        }

        /// <summary>Gets or sets the broker port number.</summary>
        public int BrokerPort
        {
            get => m_brokerPort;
            set => m_brokerPort = value;
        }

        /// <summary>Gets or sets whether to use a TLS 1.2 encrypted connection to the broker.</summary>
        public bool IsEncrypted
        {
            get => m_isEncrypted;
            set => m_isEncrypted = value;
        }

        /// <summary><c>true</c> while a connection attempt is in progress.</summary>
        public bool IsConnecting => _connect != null;


        /// <summary>Gets a value indicating whether the MQTT backend is currently connected to the broker.</summary>
        public static bool IsConnected => _MQTTBackend is { IsConnected: true };
        /// <summary>Invoked on the main thread after a successful broker connection.</summary>
        public static Action OnConnected;
        /// <summary>Invoked on the main thread when the broker connection is lost, fails, or is explicitly closed.</summary>
        public static Action OnDisconnected;


        /// <summary>Singleton instance. Only one <see cref="MQTTClient" /> may exist; extras are destroyed on Awake.</summary>
        public static MQTTClient Instance;

        /// <summary>The topic filters that currently have at least one callback registered.</summary>
        public static IReadOnlyCollection<string> SubscribedTopics => _topicCallbacks.Keys.ToArray();

        /// <summary>Number of publications waiting for a connection.</summary>
        public static int PendingPublicationCount => _pendingPublications.Count;


        private sealed class Subscription
        {
            public readonly List<Action<string, byte[]>> Callbacks = new();
            public byte QosLevel;
        }


        /// <summary>
        ///     Resets all static state. Runs before the first scene loads, so Play mode with domain reload
        ///     disabled starts clean.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        internal static void ResetStaticState()
        {
            ShutDownBackend(InvalidateConnectAttempts());

            var backend = _MQTTBackend;
            _MQTTBackend = null;
            ShutDownBackend(backend);

            while (_incomingMessages.TryDequeue(out _))
            {
            }

            _topicCallbacks.Clear();
            _pendingPublications.Clear();
            _warnedPendingOverflow = false;
            _connectionClosed = false;
            _connected = false;
            OnConnected = null;
            OnDisconnected = null;
            Instance = null;

#if UNITY_EDITOR
            UnityEditor.AssemblyReloadEvents.beforeAssemblyReload -= OnBeforeAssemblyReload;
            UnityEditor.AssemblyReloadEvents.beforeAssemblyReload += OnBeforeAssemblyReload;
#endif
        }


        /// <summary>
        ///     Makes every connect attempt in progress stale, and takes the backend of an attempt that finished
        ///     but was not picked up yet. The caller must shut that backend down.
        /// </summary>
        private static MQTTBackend InvalidateConnectAttempts()
        {
            lock (_connectLock)
            {
                Interlocked.Increment(ref _connectionGeneration);
                var connecting = _connectingBackend;
                _connectingBackend = null;

                return connecting;
            }
        }


#if UNITY_EDITOR
        /// <summary>
        ///     A script recompile in Play mode reloads the domain without OnDestroy / OnApplicationQuit.
        ///     Close the socket and its threads first, so they do not outlive the old domain as zombies.
        /// </summary>
        private static void OnBeforeAssemblyReload()
        {
            if (Instance != null)
            {
                Instance.StopAllCoroutines();
            }

            ShutDownBackend(InvalidateConnectAttempts());

            _connected = false;
            var backend = _MQTTBackend;
            _MQTTBackend = null;
            ShutDownBackend(backend);
        }
#endif


        /// <summary>
        ///     Initialize MQTT message queue
        /// </summary>
        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning($"A second {nameof(MQTTClient)} was found on {name}. Destroying it; only one may exist.");
                Destroy(gameObject);

                return;
            }

            Instance = this;

            if (m_persistAcrossScenes && Application.isPlaying)
            {
                transform.SetParent(null);
                DontDestroyOnLoad(gameObject);
            }

            if (m_autoConnect)
            {
                Connect();
            }
        }


        /// <summary>
        ///     Initiates an asynchronous connection to the configured MQTT broker.
        ///     Does nothing if already connected or already connecting. All registered topics are subscribed
        ///     again and pending publications are flushed once connected.
        /// </summary>
        [ContextMenu(nameof(Connect))]
        public void Connect()
        {
            _wantConnected = true;

            if (IsConnected || _connect != null)
            {
                return;
            }

            _connect = StartCoroutine(ConnectCR());
        }


        private IEnumerator ConnectCR()
        {
            yield return new WaitForSecondsRealtime(m_connectionDelay / 1000f);
            yield return new WaitForEndOfFrame();

            var generation = Volatile.Read(ref _connectionGeneration);

            // Drop a backend that is no longer connected; it is never reused.
            if (_MQTTBackend != null && !_MQTTBackend.IsConnected)
            {
                var stale = _MQTTBackend;
                _MQTTBackend = null;
                ShutDownBackend(stale);
            }

            var brokerAddress = m_brokerAddress;
            var brokerPort = m_brokerPort;
            var isEncrypted = m_isEncrypted;
            var clientId = Guid.NewGuid().ToString();
            var userName = m_MQTTUserName;
            var password = m_MQTTPassword;
            var timeout = m_timeoutOnConnection;
            var connectResult = false;
            var initFailed = false;
            string errorMsg = null;

            // Run connect in a background thread, so a slow broker does not freeze the main thread.
            var connectThread = new Thread(() =>
            {
                MQTTBackend created;

                try
                {
                    // The constructor resolves the host name (blocking DNS), so it runs here and not on the main thread.
                    created = new MQTTBackend(brokerAddress, brokerPort, isEncrypted, null, null,
                        isEncrypted ? MqttSslProtocols.TLSv1_2 : MqttSslProtocols.None);
                }
                catch (Exception e)
                {
                    initFailed = true;
                    errorMsg = e.Message;

                    return;
                }

                try
                {
                    created.Settings.TimeoutOnConnection = timeout;
                    created.Connect(clientId, userName, password);
                    connectResult = created.IsConnected;
                }
                catch (Exception e)
                {
                    connectResult = false;
                    errorMsg = e.Message;
                }

                // Hand the backend to the coroutine, unless Disconnect() / OnDestroy() ran while we were connecting.
                // The handover is under the lock, so a disconnect after this point finds it in _connectingBackend
                // and shuts it down, even when the coroutine is stopped before it resumes.
                var handedOver = false;

                lock (_connectLock)
                {
                    if (generation == _connectionGeneration)
                    {
                        _connectingBackend = created;
                        handedOver = true;
                    }
                }

                if (!handedOver)
                {
                    ShutDownBackend(created);
                }
            })
            {
                IsBackground = true,
                Name = "MQTTClient.Connect"
            };

            connectThread.Start();

            // Wait for thread to finish but yield each frame
            while (connectThread.IsAlive)
            {
                yield return null;
            }

            _connect = null;

            if (initFailed)
            {
                Debug.LogError($"Initialization failed: {errorMsg}");
                OnDisconnected?.Invoke();
                ScheduleReconnect();

                yield break;
            }

            MQTTBackend backend = null;

            lock (_connectLock)
            {
                if (generation == _connectionGeneration)
                {
                    backend = _connectingBackend;
                    _connectingBackend = null;
                }
            }

            if (backend == null)
            {
                // Aborted: the connect thread or the disconnect already shut the backend down.
                yield break;
            }

            if (connectResult)
            {
                _MQTTBackend = backend;
                _MQTTBackend.ConnectionClosed += OnMqttConnectionClosed;
                _MQTTBackend.MqttMsgPublishReceived += OnMqttMessageReceived;

                if (_MQTTBackend.IsConnected)
                {
                    BackendIsConnected();

                    yield break;
                }

                errorMsg = "Connection closed right after connecting.";
                _MQTTBackend = null;
                DetachHandlers(backend);
            }

            ShutDownBackend(backend);
            Debug.LogError($"Failed to connect to {m_brokerAddress}:{m_brokerPort}\n{errorMsg}");
            OnDisconnected?.Invoke();
            ScheduleReconnect();
        }


        /// <summary>
        ///     Starts the next reconnect attempt after an exponential backoff (1, 2, 4, ... seconds, capped at
        ///     <c>m_maxReconnectDelay</c>). Only when auto-reconnect is on and nobody asked to disconnect.
        /// </summary>
        private void ScheduleReconnect()
        {
            if (!m_autoReconnect || !_wantConnected || _reconnect != null || !isActiveAndEnabled)
            {
                return;
            }

            var delay = Mathf.Min(Mathf.Max(1f, m_maxReconnectDelay), Mathf.Pow(2f, Mathf.Min(_reconnectAttempts, 16)));
            _reconnectAttempts++;

            Debug.Log($"Reconnecting to {m_brokerAddress}:{m_brokerPort} in {delay:0.#} s (attempt {_reconnectAttempts}).");
            _reconnect = StartCoroutine(ReconnectCR(delay));
        }


        private IEnumerator ReconnectCR(float delay)
        {
            yield return new WaitForSecondsRealtime(delay);

            _reconnect = null;

            if (_wantConnected && !IsConnected)
            {
                Connect();
            }
        }


        private void StopReconnect()
        {
            if (_reconnect != null)
            {
                StopCoroutine(_reconnect);
                _reconnect = null;
            }

            _reconnectAttempts = 0;
        }


        private void BackendIsConnected()
        {
            _connected = true;
            _connectionClosed = false;
            _reconnectAttempts = 0;

            Debug.Log($"Connected to {m_brokerAddress}:{m_brokerPort}...");

            ResubscribeAll();

            FlushPendingPublications();

            OnConnected?.Invoke();
        }


        /// <summary>The broker session is clean on every connect, so every known topic must be subscribed again.</summary>
        private static void ResubscribeAll()
        {
            if (_topicCallbacks.Count == 0)
            {
                return;
            }

            var topics = _topicCallbacks.Keys.ToArray();
            var qosLevels = topics.Select(topic => _topicCallbacks[topic].QosLevel).ToArray();

            try
            {
                _MQTTBackend.Subscribe(topics, qosLevels);
            }
            catch (Exception e)
            {
                Debug.LogError($"Subscribing to {topics.Length} topic(s) failed: {e.Message}");
            }
        }


        private static void FlushPendingPublications()
        {
            if (_pendingPublications.Count == 0)
            {
                return;
            }

            var pending = _pendingPublications.ToArray();
            _pendingPublications.Clear();
            _warnedPendingOverflow = false;

            var maxAge = Instance != null ? Instance.m_maxPendingPublicationAge : 0f;
            var now = System.Diagnostics.Stopwatch.GetTimestamp();
            var expired = 0;

            foreach (var (topic, message, qosLevel, retain, queuedAt) in pending)
            {
                if (maxAge > 0f && (now - queuedAt) / (double) System.Diagnostics.Stopwatch.Frequency > maxAge)
                {
                    expired++;

                    continue;
                }

                try
                {
                    _MQTTBackend.Publish(topic, message, qosLevel, retain);
                }
                catch (Exception e)
                {
                    Debug.LogError($"Publishing pending message on {topic} failed: {e.Message}");
                }
            }

            if (expired > 0)
            {
                Debug.LogWarning($"Dropped {expired} queued publication(s) older than {maxAge} s instead of sending them late.");
            }
        }


        /// <summary>
        ///     Queues a publication for the next connection. When the queue is full
        ///     (<see cref="DefaultMaxPendingPublications" />, or the client's setting), the oldest is dropped.
        /// </summary>
        private static void QueuePublication(string topic, byte[] payload, byte qosLevel, bool retain)
        {
            var max = Instance != null ? Instance.m_maxPendingPublications : DefaultMaxPendingPublications;

            if (max > 0)
            {
                var overflow = _pendingPublications.Count - max + 1;

                if (overflow > 0)
                {
                    _pendingPublications.RemoveRange(0, Math.Min(overflow, _pendingPublications.Count));

                    if (!_warnedPendingOverflow)
                    {
                        _warnedPendingOverflow = true;
                        Debug.LogWarning($"More than {max} publications are waiting for a connection to the broker. Dropping the oldest.");
                    }
                }
            }

            _pendingPublications.Add((topic, payload, qosLevel, retain, System.Diagnostics.Stopwatch.GetTimestamp()));
        }


        /// <summary>Empty or null topics mean "all topics" (<c>#</c>). An empty filter is not allowed by the MQTT protocol.</summary>
        private static string NormalizeTopic(string topic)
        {
            return string.IsNullOrEmpty(topic) ? "#" : topic;
        }


        /// <summary>
        ///     Registers <paramref name="callback" /> for messages matching <paramref name="topic" />.
        ///     Defaults to subscribing to all topics ("#") with QoS level 2 (exactly once).
        ///     An empty or null topic is treated as "#". Safe to call while not connected: the topic is
        ///     subscribed when the connection is made. The callback runs on the main thread.
        /// </summary>
        /// <param name="callback">Receives the topic and the raw payload.</param>
        /// <param name="topic">Topic filter. Supports the <c>+</c> and <c>#</c> wildcards.</param>
        /// <param name="qosLevel">QoS level used when subscribing to the broker.</param>
        public static void Subscribe(Action<string, byte[]> callback, string topic = "#", byte qosLevel = MqttMsgBase.QOS_LEVEL_EXACTLY_ONCE)
        {
            topic = NormalizeTopic(topic);

            if (!_topicCallbacks.TryGetValue(topic, out var subscription))
            {
                subscription = new Subscription { QosLevel = qosLevel };
                _topicCallbacks[topic] = subscription;

                if (IsConnected)
                {
                    try
                    {
                        _MQTTBackend.Subscribe(new[] { topic }, new[] { qosLevel });
                    }
                    catch (Exception e)
                    {
                        Debug.LogError($"Subscribing to {topic} failed: {e.Message}");
                    }
                }
            }

            if (!subscription.Callbacks.Contains(callback))
            {
                subscription.Callbacks.Add(callback);
            }
        }


        /// <summary>
        ///     Removes a previously registered callback from the given topic.
        ///     When the last callback for a topic is removed, the broker subscription is also cancelled.
        ///     Safe to call while not connected.
        /// </summary>
        /// <param name="callback">The callback to remove. Must match the instance passed to <see cref="Subscribe" />.</param>
        /// <param name="topic">The MQTT topic to unsubscribe from. Defaults to <c>#</c> (all topics). Empty or null means <c>#</c>.</param>
        public static void Unsubscribe(Action<string, byte[]> callback, string topic = "#")
        {
            topic = NormalizeTopic(topic);

            if (!_topicCallbacks.TryGetValue(topic, out var subscription))
            {
                return;
            }

            subscription.Callbacks.Remove(callback);

            if (subscription.Callbacks.Count != 0)
            {
                return;
            }

            _topicCallbacks.Remove(topic);

            if (IsConnected)
            {
                try
                {
                    _MQTTBackend.Unsubscribe(new[] { topic });
                }
                catch (Exception e)
                {
                    Debug.LogError($"Unsubscribing from {topic} failed: {e.Message}");
                }
            }
        }


        /// <summary>Removes every registered callback and, when connected, unsubscribes from all topics.</summary>
        public static void ClearSubscriptions()
        {
            var topics = _topicCallbacks.Keys.ToArray();
            _topicCallbacks.Clear();

            if (topics.Length == 0 || !IsConnected)
            {
                return;
            }

            try
            {
                _MQTTBackend.Unsubscribe(topics);
            }
            catch (Exception e)
            {
                Debug.LogError($"Unsubscribing from {topics.Length} topic(s) failed: {e.Message}");
            }
        }


        /// <summary>
        ///     Publishes a UTF-8 string payload to the given topic.
        ///     If not currently connected, the message is queued and sent on connect. The queue is capped
        ///     (oldest dropped first) and may drop messages older than the configured maximum age.
        /// </summary>
        /// <param name="topic">Target MQTT topic.</param>
        /// <param name="payload">String payload to publish.</param>
        /// <param name="qosLevel">QoS level (default: exactly-once).</param>
        /// <param name="retain">Whether the broker should retain the message for new subscribers.</param>
        public static void Publish(string topic, string payload, byte qosLevel = MqttMsgBase.QOS_LEVEL_EXACTLY_ONCE, bool retain = false)
        {
            Publish(topic, Encoding.UTF8.GetBytes(payload ?? string.Empty), qosLevel, retain);
        }


        /// <summary>
        ///     Publishes a raw byte payload to the given topic.
        ///     If not currently connected, the message is queued and sent on connect.
        /// </summary>
        /// <param name="topic">Target MQTT topic. Must not be empty or contain wildcards.</param>
        /// <param name="payload">Raw byte payload to publish.</param>
        /// <param name="qosLevel">QoS level (default: exactly-once).</param>
        /// <param name="retain">Whether the broker should retain the message for new subscribers.</param>
        public static void Publish(string topic, byte[] payload, byte qosLevel = MqttMsgBase.QOS_LEVEL_EXACTLY_ONCE, bool retain = false)
        {
            if (string.IsNullOrEmpty(topic) || topic.IndexOf('#') >= 0 || topic.IndexOf('+') >= 0)
            {
                Debug.LogError($"Cannot publish to topic '{topic}': it must not be empty or contain wildcards.");

                return;
            }

            payload ??= Array.Empty<byte>();

            if (IsConnected)
            {
                try
                {
                    _MQTTBackend.Publish(topic, payload, qosLevel, retain);

                    return;
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"Publishing on {topic} failed, queued for the next connection: {e.Message}");
                }
            }

            QueuePublication(topic, payload, qosLevel, retain);
        }


        /// <summary>
        ///     Calls every callback whose filter matches <paramref name="topic" />. Iterates over snapshots, so
        ///     callbacks may subscribe or unsubscribe while being called. A throwing callback is logged and does
        ///     not stop the others.
        /// </summary>
        internal static void DispatchMessage(string topic, byte[] payload)
        {
            foreach (var pair in _topicCallbacks.ToArray())
            {
                if (!TopicMatches(pair.Key, topic))
                {
                    continue;
                }

                foreach (var callback in pair.Value.Callbacks.ToArray())
                {
                    try
                    {
                        callback?.Invoke(topic, payload);
                    }
                    catch (Exception e)
                    {
                        Debug.LogError($"Error in MQTT callback for topic {pair.Key} (received: {topic}): {e}");
                    }
                }
            }
        }


        /// <summary>
        ///     Returns <c>true</c> when <paramref name="topic" /> matches the MQTT topic <paramref name="filter" />.
        ///     <c>+</c> matches exactly one level. <c>#</c> matches the parent level and any number of child levels,
        ///     so <c>sport/#</c> matches <c>sport</c>, <c>sport/tennis</c> and <c>sport/tennis/player1</c>.
        /// </summary>
        public static bool TopicMatches(string filter, string topic)
        {
            if (filter == null || topic == null)
            {
                return false;
            }

            var filterLevels = filter.Split('/');
            var topicLevels = topic.Split('/');

            for (var i = 0; i < filterLevels.Length; i++)
            {
                if (filterLevels[i] == "#")
                {
                    return true;
                }

                if (i >= topicLevels.Length)
                {
                    return false;
                }

                if (filterLevels[i] == "+")
                {
                    continue;
                }

                if (filterLevels[i] != topicLevels[i])
                {
                    return false;
                }
            }

            return filterLevels.Length == topicLevels.Length;
        }


        /// <summary>
        ///     Processing of income messages and events is postponed here in the main thread.
        /// </summary>
        private void Update()
        {
            ProcessMqttEvents();
        }


        /// <summary>
        ///     Dispatches the messages that were queued when this frame started, then handles a lost connection.
        /// </summary>
        internal void ProcessMqttEvents()
        {
            var count = _incomingMessages.Count;

            while (count-- > 0 && _incomingMessages.TryDequeue(out var msg))
            {
                DispatchMessage(msg.Topic, msg.Message);
            }

            if (_connectionClosed)
            {
                _connectionClosed = false;
                HandleUnexpectedDisconnect();
            }
        }


        private void HandleUnexpectedDisconnect()
        {
            _connected = false;

            var backend = _MQTTBackend;
            _MQTTBackend = null;
            ShutDownBackend(backend);

            Debug.Log("Connection to the broker closed.");
            OnDisconnected?.Invoke();
            ScheduleReconnect();
        }


        /// <summary>Called on M2MQTT's dispatch thread. Only enqueues; never touches Unity or the subscriber lists.</summary>
        private static void OnMqttMessageReceived(object sender, MqttMsgPublishEventArgs msg)
        {
            if (!ReferenceEquals(sender, _MQTTBackend))
            {
                return;
            }

            _incomingMessages.Enqueue(msg);
        }


        /// <summary>Called on an M2MQTT thread. Only sets a flag; the main thread handles it in <c>Update</c>.</summary>
        private static void OnMqttConnectionClosed(object sender, EventArgs e)
        {
            // Only an unexpected close of the current connection counts (avoid event handling in case of controlled disconnection)
            if (_connected && ReferenceEquals(sender, _MQTTBackend))
            {
                _connectionClosed = true;
            }
        }


        private static void DetachHandlers(MQTTBackend backend)
        {
            if (backend == null)
            {
                return;
            }

            backend.MqttMsgPublishReceived -= OnMqttMessageReceived;
            backend.ConnectionClosed -= OnMqttConnectionClosed;
        }


        private static void ShutDownBackend(MQTTBackend backend)
        {
            if (backend == null)
            {
                return;
            }

            DetachHandlers(backend);

            try
            {
                if (backend.IsConnected)
                {
                    backend.Disconnect();
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"Error while closing the MQTT connection: {e.Message}");
            }
        }


        /// <summary>
        ///     Begins an asynchronous disconnection from the broker.
        ///     Registered subscriptions are kept and restored on the next <see cref="Connect" />.
        ///     Pending publications are dropped. Fires <see cref="OnDisconnected" />.
        /// </summary>
        [ContextMenu(nameof(Disconnect))]
        public void Disconnect()
        {
            _wantConnected = false;
            StopReconnect();

            if (_disconnect != null)
            {
                StopCoroutine(_disconnect);
            }

            _disconnect = StartCoroutine(DisconnectCR());
        }


        private IEnumerator DisconnectCR()
        {
            yield return new WaitForEndOfFrame();
            DisconnectImmediately(true);

            _disconnect = null;
        }


        /// <summary>Closes the connection right away and cancels a connection attempt in progress.</summary>
        /// <param name="dropPendingPublications">Also forget publications that were waiting for a connection.</param>
        private void DisconnectImmediately(bool dropPendingPublications)
        {
            // Also closes a connection that finished connecting but was not picked up by the (stopped) coroutine.
            ShutDownBackend(InvalidateConnectAttempts());
            StopReconnect();

            if (_connect != null)
            {
                StopCoroutine(_connect);
                _connect = null;
            }

            _connected = false;
            _connectionClosed = false;

            var backend = _MQTTBackend;
            _MQTTBackend = null;

            while (_incomingMessages.TryDequeue(out _))
            {
            }

            if (dropPendingPublications)
            {
                _pendingPublications.Clear();
                _warnedPendingOverflow = false;
            }

            if (backend == null)
            {
                return;
            }

            ShutDownBackend(backend);

            OnDisconnected?.Invoke();

            Debug.Log("Disconnected from broker: " + m_brokerAddress + ":" + m_brokerPort);
        }


        /// <summary>
        ///     Disconnect before the application quits.
        /// </summary>
        private void OnApplicationQuit()
        {
            StopAllCoroutines();
            _disconnect = null;
            _reconnect = null;
            _wantConnected = false;
            DisconnectImmediately(true);
        }


        /// <summary>Reconnects when the client is enabled again after <see cref="OnDisable" /> closed a wanted connection.</summary>
        private void OnEnable()
        {
            if (Instance != this || !_resumeOnEnable)
            {
                return;
            }

            _resumeOnEnable = false;
            Connect();
        }


        /// <summary>
        ///     A disabled client no longer runs <c>Update</c>, so incoming messages would pile up undispatched.
        ///     Close the connection instead; subscriptions and pending publications are kept, and
        ///     <see cref="OnEnable" /> connects again if the connection was wanted.
        /// </summary>
        private void OnDisable()
        {
            if (Instance != this)
            {
                return;
            }

            _resumeOnEnable = _wantConnected;
            StopAllCoroutines();
            _disconnect = null;
            _reconnect = null;
            DisconnectImmediately(false);
        }


        /// <summary>
        ///     Closes the connection when the singleton goes away (scene change, explicit Destroy), so no
        ///     background thread keeps feeding a client that no longer exists. Subscriptions are kept for the next client.
        /// </summary>
        private void OnDestroy()
        {
            if (Instance != this)
            {
                return;
            }

            StopAllCoroutines();
            _disconnect = null;
            _reconnect = null;
            _wantConnected = false;
            DisconnectImmediately(false);
            Instance = null;
        }


#if ((!UNITY_EDITOR && UNITY_WSA_10_0 || !UNITY_EDITOR && UNITY_ANDROID))
        private void OnApplicationFocus(bool focus)
        {
            // On UWP 10 (HoloLeng) / Android we cannot tell whether the application actually got closed or just minimized.
            // (https://forum.unity.com/threads/onapplicationquit-and-ondestroy-are-not-called-on-uwp-10.462597/)
            // Subscriptions are kept, so they come back when focus returns (e.g. after the Quest system menu).
            if (focus)
            {
                Connect();
            }
            else
            {
                DisconnectImmediately(false);
            }
        }
#endif
    }
}
