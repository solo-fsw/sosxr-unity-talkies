using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;


namespace SOSXR.Talkies.Tests
{
    public class PinControllerTests
    {
        private GameObject _go;
        private FakeSerialConnector _connector;
        private PinController _controller;


        [SetUp]
        public void SetUp()
        {
            _go = new GameObject("PinControllerTests");
            _connector = new FakeSerialConnector();
            _controller = _go.AddComponent<PinController>();
            _controller.Connector = _connector;
        }


        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_go);
        }


        [Test]
        public void Connector_IsResolvedFromComponentWithoutAwakeOrOnValidate()
        {
            var go = new GameObject("PinControllerResolve");

            try
            {
                var connector = go.AddComponent<DesktopSerialConnector>();
                var controller = go.AddComponent<PinController>();

                Assert.That(controller.Connector, Is.SameAs(connector));
                Assert.That(controller.IsConnected, Is.False);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }


        [Test]
        public void Connector_InjectedFake_IsUsed()
        {
            Assert.That(_controller.Connector, Is.SameAs(_connector));
            Assert.That(_controller.IsConnected, Is.True);
        }


        [Test]
        public void SetPin_WritesCommandThroughConnector()
        {
            _controller.SetPin(3, true);

            Assert.That(_connector.WrittenText, Is.EqualTo("SET,3,1\n"));
            Assert.That(_controller.UsedPins, Does.Contain(3));
        }


        [Test]
        public void GetPin_WritesCommandThroughConnector()
        {
            _controller.GetPin(7);

            Assert.That(_connector.WrittenText, Is.EqualTo("GET,7\n"));
        }


        [Test]
        public void PollSerial_RaisesEventsFromChunkedResponses()
        {
            var sets = new List<(int, bool)>();
            var gets = new List<(int, bool)>();
            _controller.OnPinSetEvent += (pin, value) => sets.Add((pin, value));
            _controller.OnPinGetEvent += (pin, value) => gets.Add((pin, value));

            _connector.MaxBytesPerRead = 3;
            _connector.Receive("OK,SET,3,1\nOK,GET,4,0\n");

            // Each poll reads chunks until the connector has nothing more; do a few to be safe.
            for (var i = 0; i < 20; i++)
            {
                _controller.PollSerial();
            }

            Assert.That(sets, Is.EqualTo(new[] { (3, true) }));
            Assert.That(gets, Is.EqualTo(new[] { (4, false) }));
        }


        [Test]
        public void PollSerial_MalformedLineDoesNotWedgeParsing()
        {
            var sets = new List<(int, bool)>();
            _controller.OnPinSetEvent += (pin, value) => sets.Add((pin, value));

            LogAssert.Expect(LogType.Warning, new Regex("malformed"));
            _connector.Receive("OK,SET,x,1\nOK,SET,5,1\n");
            _controller.PollSerial();

            Assert.That(sets, Is.EqualTo(new[] { (5, true) }));
        }


        [Test]
        public void PollSerial_DoesNothingWhileDisconnected()
        {
            var sets = 0;
            _controller.OnPinSetEvent += (_, _) => sets++;
            _connector.Connected = false;
            _connector.Receive("OK,SET,5,1\n");

            _controller.PollSerial();

            Assert.That(sets, Is.Zero);
            LogAssert.NoUnexpectedReceived();
        }


        [Test]
        public void TogglePin_ReadsThenWritesOpposite()
        {
            _controller.TogglePin(5);
            Assert.That(_connector.WrittenText, Is.EqualTo("GET,5\n"));
            Assert.That(_controller.PendingToggleCount, Is.EqualTo(1));

            _connector.ClearWritten();
            _connector.Receive("OK,GET,5,0\n");
            _controller.PollSerial();

            Assert.That(_connector.WrittenText, Is.EqualTo("SET,5,1\n"));
            Assert.That(_controller.PendingToggleCount, Is.Zero);
        }


        [Test]
        public void TogglePin_WhileDisconnected_DoesNotPileUpHandlers()
        {
            _connector.Connected = false;

            for (var i = 0; i < 5; i++)
            {
                LogAssert.Expect(LogType.Warning, new Regex("Not connected"));
                _controller.TogglePin(25);
            }

            Assert.That(_controller.PendingToggleCount, Is.Zero);
        }


        [Test]
        public void TogglePin_Twice_KeepsOnePendingToggle()
        {
            _controller.TogglePin(5);
            _controller.TogglePin(5);

            Assert.That(_controller.PendingToggleCount, Is.EqualTo(1));

            _connector.ClearWritten();
            _connector.Receive("OK,GET,5,1\n");
            _controller.PollSerial();

            Assert.That(_connector.WrittenText, Is.EqualTo("SET,5,0\n"), "Only one SET, not one per toggle call.");
        }


        [Test]
        public void ExplicitConnector_IsUsed()
        {
            var other = new FakeSerialConnector();

            _controller.Connector = other;
            _controller.SetPin(1, false);

            Assert.That(other.WrittenText, Is.EqualTo("SET,1,0\n"));
            Assert.That(_connector.WrittenText, Is.Empty);
        }
    }
}
