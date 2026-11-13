using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using MQTTUnity;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;


namespace SOSXR.Talkies.Tests
{
    /// <summary>EditMode tests for the subscription bookkeeping and dispatching of <see cref="MQTTClient" />. No broker needed.</summary>
    public class MQTTClientTests
    {
        [SetUp]
        public void SetUp()
        {
            MQTTClient.ResetStaticState();
        }


        [TearDown]
        public void TearDown()
        {
            MQTTClient.ResetStaticState();
        }


        [TestCase("a/b", "a/b", true)]
        [TestCase("a/b", "a/c", false)]
        [TestCase("a/+", "a/b", true)]
        [TestCase("a/+", "a/b/c", false)]
        [TestCase("+/b", "a/b", true)]
        [TestCase("a/#", "a", true)]
        [TestCase("a/#", "a/b/c", true)]
        [TestCase("#", "anything/at/all", true)]
        [TestCase("a/b", "a", false)]
        [TestCase("a", "a/b", false)]
        public void TopicMatches_FollowsMqttWildcardRules(string filter, string topic, bool expected)
        {
            Assert.That(MQTTClient.TopicMatches(filter, topic), Is.EqualTo(expected));
        }


        [Test]
        public void TopicMatches_NullIsNoMatch()
        {
            Assert.That(MQTTClient.TopicMatches(null, "a"), Is.False);
            Assert.That(MQTTClient.TopicMatches("a", null), Is.False);
        }


        [TestCase(null)]
        [TestCase("")]
        public void Subscribe_EmptyTopic_BecomesHash(string topic)
        {
            MQTTClient.Subscribe((_, _) => { }, topic);

            Assert.That(MQTTClient.SubscribedTopics, Is.EquivalentTo(new[] {"#"}));
        }


        [Test]
        public void Subscribe_SameCallbackTwice_IsCalledOnce()
        {
            var calls = 0;
            Action<string, byte[]> callback = (_, _) => calls++;

            MQTTClient.Subscribe(callback, "a/b");
            MQTTClient.Subscribe(callback, "a/b");
            MQTTClient.DispatchMessage("a/b", Array.Empty<byte>());

            Assert.That(calls, Is.EqualTo(1));
        }


        [Test]
        public void Unsubscribe_LastCallback_RemovesTopic()
        {
            Action<string, byte[]> first = (_, _) => { };
            Action<string, byte[]> second = (_, _) => { };

            MQTTClient.Subscribe(first, "a/b");
            MQTTClient.Subscribe(second, "a/b");
            MQTTClient.Unsubscribe(first, "a/b");

            Assert.That(MQTTClient.SubscribedTopics, Does.Contain("a/b"));

            MQTTClient.Unsubscribe(second, "a/b");

            Assert.That(MQTTClient.SubscribedTopics, Is.Empty);
        }


        [Test]
        public void Unsubscribe_EmptyTopic_MatchesHashSubscription()
        {
            Action<string, byte[]> callback = (_, _) => { };

            MQTTClient.Subscribe(callback, "");
            MQTTClient.Unsubscribe(callback, null);

            Assert.That(MQTTClient.SubscribedTopics, Is.Empty);
        }


        [Test]
        public void Dispatch_UsesWildcardFilters()
        {
            var received = new List<string>();

            MQTTClient.Subscribe((topic, _) => received.Add("plus:" + topic), "sensors/+");
            MQTTClient.Subscribe((topic, _) => received.Add("hash:" + topic), "sensors/#");
            MQTTClient.Subscribe((topic, _) => received.Add("other:" + topic), "other");

            MQTTClient.DispatchMessage("sensors/temp", Encoding.UTF8.GetBytes("21"));

            Assert.That(received, Is.EquivalentTo(new[] {"plus:sensors/temp", "hash:sensors/temp"}));
        }


        [Test]
        public void Dispatch_CallbackMayUnsubscribeAndSubscribeDuringDispatch()
        {
            var calls = new List<string>();
            Action<string, byte[]> self = null;
            self = (_, _) =>
            {
                calls.Add("self");
                MQTTClient.Unsubscribe(self, "a");
                MQTTClient.Subscribe((_, _) => calls.Add("new"), "b");
            };

            MQTTClient.Subscribe(self, "a");
            MQTTClient.Subscribe((_, _) => calls.Add("other"), "a");

            Assert.DoesNotThrow(() => MQTTClient.DispatchMessage("a", Array.Empty<byte>()));
            Assert.That(calls, Is.EqualTo(new[] {"self", "other"}));

            calls.Clear();
            MQTTClient.DispatchMessage("a", Array.Empty<byte>());

            Assert.That(calls, Is.EqualTo(new[] {"other"}), "The callback that unsubscribed itself is not called again.");
        }


        [Test]
        public void Dispatch_ThrowingCallback_DoesNotStopOthers()
        {
            var otherCalled = false;

            MQTTClient.Subscribe((_, _) => throw new InvalidOperationException("boom"), "a");
            MQTTClient.Subscribe((_, _) => otherCalled = true, "a");

            LogAssert.Expect(LogType.Error, new Regex("boom"));
            MQTTClient.DispatchMessage("a", Array.Empty<byte>());

            Assert.That(otherCalled, Is.True);
        }


        [Test]
        public void Publish_WhileDisconnected_IsQueued()
        {
            Assert.That(MQTTClient.IsConnected, Is.False);

            MQTTClient.Publish("a/b", "hello");
            MQTTClient.Publish("a/b", (string) null);
            MQTTClient.Publish("a/b", (byte[]) null);

            Assert.That(MQTTClient.PendingPublicationCount, Is.EqualTo(3));
        }


        [TestCase("")]
        [TestCase("a/#")]
        [TestCase("a/+/b")]
        public void Publish_InvalidTopic_IsRejected(string topic)
        {
            LogAssert.Expect(LogType.Error, new Regex("Cannot publish"));
            MQTTClient.Publish(topic, "x");

            Assert.That(MQTTClient.PendingPublicationCount, Is.Zero);
        }


        [Test]
        public void ClearSubscriptions_RemovesAll()
        {
            MQTTClient.Subscribe((_, _) => { }, "a");
            MQTTClient.Subscribe((_, _) => { }, "b");

            MQTTClient.ClearSubscriptions();

            Assert.That(MQTTClient.SubscribedTopics, Is.Empty);
        }


        [Test]
        public void ResetStaticState_ClearsEverything()
        {
            MQTTClient.Subscribe((_, _) => { }, "a");
            MQTTClient.Publish("a", "x");
            MQTTClient.OnConnected += () => { };
            MQTTClient.OnDisconnected += () => { };

            MQTTClient.ResetStaticState();

            Assert.That(MQTTClient.SubscribedTopics, Is.Empty);
            Assert.That(MQTTClient.PendingPublicationCount, Is.Zero);
            Assert.That(MQTTClient.OnConnected, Is.Null);
            Assert.That(MQTTClient.OnDisconnected, Is.Null);
            Assert.That(MQTTClient.Instance, Is.Null);
        }


        [Test]
        public void ProcessMqttEvents_WithNothingQueued_DoesNotFireDisconnected()
        {
            var go = new GameObject("MQTTClientTests");

            try
            {
                var client = go.AddComponent<MQTTClient>();
                var disconnects = 0;
                MQTTClient.OnDisconnected += () => disconnects++;

                for (var i = 0; i < 3; i++)
                {
                    client.ProcessMqttEvents();
                }

                Assert.That(disconnects, Is.Zero);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }
    }


    /// <summary>EditMode tests for <see cref="MQTTFirstResponder" />.</summary>
    public class MQTTFirstResponderTests
    {
        private GameObject _go;
        private MQTTFirstResponder _responder;
        private readonly List<(string topic, string payload)> _received = new();


        [SetUp]
        public void SetUp()
        {
            MQTTClient.ResetStaticState();
            _received.Clear();

            _go = new GameObject("MQTTFirstResponderTests");
            _responder = _go.AddComponent<MQTTFirstResponder>();

            var field = typeof(MQTTFirstResponder).GetField("m_onMessageReceived", BindingFlags.Instance | BindingFlags.NonPublic);
            var unityEvent = (UnityEvent<string, string>) field!.GetValue(_responder);

            if (unityEvent == null)
            {
                unityEvent = new UnityEvent<string, string>();
                field.SetValue(_responder, unityEvent);
            }

            unityEvent.AddListener((topic, payload) => _received.Add((topic, payload)));
        }


        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_go);
            MQTTClient.ResetStaticState();
        }


        private void Configure(string topic, string payload)
        {
            var so = new SerializedObject(_responder);
            so.FindProperty("m_toggleTopic").boolValue = !string.IsNullOrEmpty(topic);
            so.FindProperty("m_togglePayload").boolValue = !string.IsNullOrEmpty(payload);
            so.FindProperty("m_onlyRespondToThisTopic").stringValue = topic;
            so.FindProperty("m_onlyRespondToThisPayload").stringValue = payload;
            so.ApplyModifiedPropertiesWithoutUndo();
        }


        private void Enable()
        {
            // OnEnable does not run for plain components in Edit mode.
            typeof(MQTTFirstResponder).GetMethod("OnEnable", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(_responder, null);
        }


        private void Disable()
        {
            typeof(MQTTFirstResponder).GetMethod("OnDisable", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(_responder, null);
        }


        [Test]
        public void EmptyTopic_SubscribesToHash()
        {
            Configure("", "");
            Enable();

            Assert.That(_responder.EffectiveTopic, Is.EqualTo("#"));
            Assert.That(MQTTClient.SubscribedTopics, Is.EquivalentTo(new[] {"#"}));

            MQTTClient.DispatchMessage("any/topic", Encoding.UTF8.GetBytes("hi"));

            Assert.That(_received, Is.EqualTo(new[] {("any/topic", "hi")}));
        }


        [Test]
        public void TopicAndPayloadFilter_OnlyMatchingMessagesInvoke()
        {
            Configure("lights/+", "on");
            Enable();

            MQTTClient.DispatchMessage("lights/kitchen", Encoding.UTF8.GetBytes("on"));
            MQTTClient.DispatchMessage("lights/kitchen", Encoding.UTF8.GetBytes("off"));
            MQTTClient.DispatchMessage("doors/front", Encoding.UTF8.GetBytes("on"));

            Assert.That(_received, Is.EqualTo(new[] {("lights/kitchen", "on")}));
        }


        [Test]
        public void NullPayload_IsTreatedAsEmpty()
        {
            Configure("a", "");
            Enable();

            Assert.DoesNotThrow(() => MQTTClient.DispatchMessage("a", null));
            Assert.That(_received, Is.EqualTo(new[] {("a", string.Empty)}));
        }


        [Test]
        public void Disable_UnsubscribesWithTheOriginalTopic()
        {
            Configure("a", "");
            Enable();

            Configure("b", ""); // Inspector change while enabled
            Disable();

            Assert.That(MQTTClient.SubscribedTopics, Is.Empty);
        }
    }
}
