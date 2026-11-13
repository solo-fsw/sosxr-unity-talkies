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

        [Tooltip("UserName for the MQTT broker. Keep blank if no user name is required.")]
        [SerializeField] private string m_MQTTUserName = string.Empty;

        [Tooltip("Password for the MQTT broker. Keep blank if no password is required.")]
        [SerializeField] private string m_MQTTPassword = string.Empty;

        private static MQTTBackend _MQTTBackend;

        private static readonly ConcurrentQueue<MqttMsgPublishEventArgs> _incomingMessages = new();
        private static readonly Dictionary<string, Subscription> _topicCallbacks = new();
        private static readonly List<(string topic, byte[] message, byte qosLevel, bool retain)> _pendingPublications = new(); // For anyone publishing before we're connected

        private static int _connectionGeneration;
        private static volatile bool _connectionClosed;
        private static volatile bool _connected;

        private Coroutine _connect;
        private Coroutine _disconnect;

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
            Interlocked.Increment(ref _connectionGeneration);

            var backend = _MQTTBackend;
            _MQTTBackend = null;
            ShutDownBackend(backend);

            while (_incomingMessages.TryDequeue(out _))
            {
            }

            _topicCallbacks.Clear();
            _pendingPublications.Clear();
            _connectionClosed = false;
            _connected = false;
            OnConnected = null;
            OnDisconnected = null;
            Instance = null;
        }


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

            MQTTBackend backend;

            try
            {
                backend = new MQTTBackend(m_brokerAddress, m_brokerPort, m_isEncrypted, null, null,
                    m_isEncrypted ? MqttSslProtocols.TLSv1_2 : MqttSslProtocols.None);
            }
            catch (Exception e)
            {
                Debug.LogError($"Initialization failed: {e.Message}");
                _connect = null;
                OnDisconnected?.Invoke();

                yield break;
            }

            var clientId = Guid.NewGuid().ToString();
            var userName = m_MQTTUserName;
            var password = m_MQTTPassword;
            var timeout = m_timeoutOnConnection;
            var connectResult = false;
            var aborted = false;
            string errorMsg = null;

            // Run connect in a background thread, so a slow broker does not freeze the main thread.
            var connectThread = new Thread(() =>
            {
                try
                {
                    backend.Settings.TimeoutOnConnection = timeout;
                    backend.Connect(clientId, userName, password);
                    connectResult = backend.IsConnected;
                }
                catch (Exception e)
                {
                    connectResult = false;
                    errorMsg = e.Message;
                }

                // Disconnect() or OnDestroy() ran while we were connecting: do not leak a live connection.
                if (generation != Volatile.Read(ref _connectionGeneration))
                {
                    aborted = true;
                    ShutDownBackend(backend);
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

            if (aborted || generation != Volatile.Read(ref _connectionGeneration))
            {
                ShutDownBackend(backend);

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
        }


        private void BackendIsConnected()
        {
            _connected = true;
            _connectionClosed = false;

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

            foreach (var (topic, message, qosLevel, retain) in pending)
            {
                try
                {
                    _MQTTBackend.Publish(topic, message, qosLevel, retain);
                }
                catch (Exception e)
                {
                    Debug.LogError($"Publishing pending message on {topic} failed: {e.Message}");
                }
            }
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
        ///     If not currently connected, the message is queued and sent on connect.
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

            _pendingPublications.Add((topic, payload, qosLevel, retain));
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
            Interlocked.Increment(ref _connectionGeneration);

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
            DisconnectImmediately(true);
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
