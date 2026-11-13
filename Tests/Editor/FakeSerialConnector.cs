using System;
using System.Collections.Generic;
using System.Text;


namespace SOSXR.Talkies.Tests
{
    /// <summary>
    ///     In-memory <see cref="ISerialConnect" /> for tests: records writes and serves queued reads.
    ///     A plain class, not a MonoBehaviour: Unity cannot add components defined in an Editor-only (test) assembly.
    ///     Inject it through the <c>Connector</c> property.
    /// </summary>
    public class FakeSerialConnector : ISerialConnect
    {
        private readonly Queue<byte> _incoming = new();
        private readonly List<byte> _written = new();

        public bool Connected = true;

        /// <summary>Limits how many bytes one <see cref="Read" /> call returns, to simulate chunked arrival.</summary>
        public int MaxBytesPerRead = int.MaxValue;

        public bool IsConnected => Connected;

        public string WrittenText => Encoding.ASCII.GetString(_written.ToArray());


        public void Connect()
        {
            Connected = true;
        }


        public void Disconnect()
        {
            Connected = false;
        }


        public int Write(byte[] data, int offset, int count)
        {
            if (!Connected)
            {
                return 0;
            }

            for (var i = offset; i < offset + count; i++)
            {
                _written.Add(data[i]);
            }

            return count;
        }


        public int Read(byte[] buffer, int offset, int count)
        {
            if (!Connected)
            {
                return 0;
            }

            var n = 0;
            var limit = Math.Min(count, MaxBytesPerRead);

            while (n < limit && _incoming.Count > 0)
            {
                buffer[offset + n] = _incoming.Dequeue();
                n++;
            }

            return n;
        }


        public void Receive(string text)
        {
            foreach (var b in Encoding.ASCII.GetBytes(text))
            {
                _incoming.Enqueue(b);
            }
        }


        public void ClearWritten()
        {
            _written.Clear();
        }
    }
}
