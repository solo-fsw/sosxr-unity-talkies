using System.Globalization;
using System.Threading;
using NUnit.Framework;


namespace SOSXR.Talkies.Tests
{
    public class PinProtocolTests
    {
        [Test]
        public void FormatSet_UsesOneAndZero()
        {
            Assert.That(PinProtocol.FormatSet(16, true), Is.EqualTo("SET,16,1"));
            Assert.That(PinProtocol.FormatSet(3, false), Is.EqualTo("SET,3,0"));
        }


        [Test]
        public void FormatGet_FormatsPin()
        {
            Assert.That(PinProtocol.FormatGet(25), Is.EqualTo("GET,25"));
        }


        [Test]
        public void Format_IsCultureIndependent()
        {
            var original = Thread.CurrentThread.CurrentCulture;

            try
            {
                Thread.CurrentThread.CurrentCulture = new CultureInfo("ar-SA");
                Assert.That(PinProtocol.FormatSet(12, true), Is.EqualTo("SET,12,1"));
            }
            finally
            {
                Thread.CurrentThread.CurrentCulture = original;
            }
        }


        [Test]
        public void Parse_SetConfirmation()
        {
            var response = PinProtocol.Parse("OK,SET,16,1");

            Assert.That(response.Kind, Is.EqualTo(PinResponseKind.PinSet));
            Assert.That(response.Pin, Is.EqualTo(16));
            Assert.That(response.Value, Is.True);
        }


        [Test]
        public void Parse_GetAnswer_WithWhitespace()
        {
            var response = PinProtocol.Parse("  OK, GET, 25, 0 \r");

            Assert.That(response.Kind, Is.EqualTo(PinResponseKind.PinGet));
            Assert.That(response.Pin, Is.EqualTo(25));
            Assert.That(response.Value, Is.False);
        }


        [Test]
        public void Parse_Error()
        {
            Assert.That(PinProtocol.Parse("ERR,SET,99").Kind, Is.EqualTo(PinResponseKind.Error));
        }


        [TestCase("OK,SET,abc,1")]
        [TestCase("OK,GET,5,x")]
        [TestCase("OK,SET,,")]
        public void Parse_MalformedNumbers_DoNotThrow(string line)
        {
            PinResponse response = default;

            Assert.DoesNotThrow(() => response = PinProtocol.Parse(line));
            Assert.That(response.Kind, Is.EqualTo(PinResponseKind.Malformed));
        }


        [TestCase("PONG")]
        [TestCase("OK,PING")]
        [TestCase("")]
        [TestCase(null)]
        [TestCase("OK,SET,1")]
        public void Parse_Other(string line)
        {
            Assert.That(PinProtocol.Parse(line).Kind, Is.EqualTo(PinResponseKind.Other));
        }


        [Test]
        public void HighLow()
        {
            Assert.That(PinProtocol.HighLow(true), Is.EqualTo("HIGH"));
            Assert.That(PinProtocol.HighLow(false), Is.EqualTo("LOW"));
        }
    }
}
