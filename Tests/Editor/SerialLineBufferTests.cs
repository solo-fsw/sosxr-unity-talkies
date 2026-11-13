using NUnit.Framework;


namespace SOSXR.Talkies.Tests
{
    public class SerialLineBufferTests
    {
        [Test]
        public void SplitsLinesAcrossChunks()
        {
            var buffer = new SerialLineBuffer();

            buffer.Append("OK,SE");
            Assert.That(buffer.TryReadLine(out _), Is.False);

            buffer.Append("T,1,1\nOK,GET,2,0\nPAR");

            Assert.That(buffer.TryReadLine(out var first), Is.True);
            Assert.That(first, Is.EqualTo("OK,SET,1,1"));
            Assert.That(buffer.TryReadLine(out var second), Is.True);
            Assert.That(second, Is.EqualTo("OK,GET,2,0"));
            Assert.That(buffer.TryReadLine(out _), Is.False);
            Assert.That(buffer.PendingLength, Is.EqualTo(3));
        }


        [Test]
        public void TrimsCarriageReturnsAndSkipsEmptyLines()
        {
            var buffer = new SerialLineBuffer();
            buffer.Append("\r\n\nPONG\r\n");

            Assert.That(buffer.TryReadLine(out var line), Is.True);
            Assert.That(line, Is.EqualTo("PONG"));
            Assert.That(buffer.TryReadLine(out _), Is.False);
        }


        [Test]
        public void AppendBytes_RespectsOffsetAndCount()
        {
            var buffer = new SerialLineBuffer();
            var bytes = System.Text.Encoding.ASCII.GetBytes("xxPONG\nyy");

            buffer.Append(bytes, 2, 5);

            Assert.That(buffer.TryReadLine(out var line), Is.True);
            Assert.That(line, Is.EqualTo("PONG"));
            Assert.That(buffer.PendingLength, Is.EqualTo(0));
        }


        [Test]
        public void DropsBufferWhenNoNewlineArrivesForTooLong()
        {
            var buffer = new SerialLineBuffer(8);

            buffer.Append("0123456789");

            Assert.That(buffer.PendingLength, Is.EqualTo(0));

            buffer.Append("OK\n");
            Assert.That(buffer.TryReadLine(out var line), Is.True);
            Assert.That(line, Is.EqualTo("OK"));
        }
    }
}
