using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;


namespace SOSXR.Talkies.Tests
{
    public class ByteSizeTests
    {
        private GameObject _go;
        private FakeSerialConnector _connector;
        private ByteSize _byteSize;


        [SetUp]
        public void SetUp()
        {
            _go = new GameObject("ByteSizeTests");
            _connector = new FakeSerialConnector();
            _byteSize = _go.AddComponent<ByteSize>();
            _byteSize.Connector = _connector;
        }


        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_go);
        }


        [Test]
        public void Connector_IsResolvedFromComponentWithoutAwake()
        {
            var go = new GameObject("ByteSizeResolve");

            try
            {
                var connector = go.AddComponent<AndroidSerialConnector>();
                var byteSize = go.AddComponent<ByteSize>();

                Assert.That(byteSize.Connector, Is.SameAs(connector));
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }


        [Test]
        public void SingleByteCommand_GoesThroughConnector()
        {
            _byteSize.Unlock();

            Assert.That(_connector.WrittenText, Is.EqualTo("u"));
        }


        [Test]
        public void TwoByteCommand_GoesThroughConnector()
        {
            _byteSize.SendCommand("ab");
            _byteSize.SendCommand("c", "d");

            Assert.That(_connector.WrittenText, Is.EqualTo("abcd"));
        }


        [Test]
        public void InvalidCommandLength_SendsNothing()
        {
            LogAssert.Expect(LogType.Warning, new Regex("2 characters"));
            _byteSize.SendCommand("abc");

            LogAssert.Expect(LogType.Warning, new Regex("single character"));
            _byteSize.SendCommand("ab", "c");

            Assert.That(_connector.WrittenText, Is.Empty);
        }


        [Test]
        public void Disconnected_SendReturnsFalse()
        {
            _connector.Connected = false;

            LogAssert.Expect(LogType.Warning, new Regex("Not connected"));

            Assert.That(_byteSize.SendCommand((byte) 'x'), Is.False);
            Assert.That(_connector.WrittenText, Is.Empty);
        }


        [Test]
        public void ReadBuffer_ReadsOneByteAtATime()
        {
            _connector.Receive("xy");

            _byteSize.ReadBuffer();
            Assert.That(_byteSize.LastReceivedByte, Is.EqualTo((int) 'x'));

            Assert.That(_byteSize.TryReadByte(out var second), Is.True);
            Assert.That(second, Is.EqualTo((byte) 'y'));

            Assert.That(_byteSize.TryReadByte(out _), Is.False, "Nothing left to read.");
        }


        [Test]
        public void NoConnector_WarnsOnceAndDoesNotThrow()
        {
            var go = new GameObject("NoConnector");

            try
            {
                var byteSize = go.AddComponent<ByteSize>();

                LogAssert.Expect(LogType.Warning, new Regex("has no ISerialConnect"));
                LogAssert.Expect(LogType.Warning, new Regex("Not connected"));
                Assert.DoesNotThrow(() => byteSize.Unlock());

                LogAssert.Expect(LogType.Warning, new Regex("Not connected"));
                Assert.DoesNotThrow(() => byteSize.Info());
                Assert.That(byteSize.TryReadByte(out _), Is.False);
                LogAssert.NoUnexpectedReceived();
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }
    }
}
