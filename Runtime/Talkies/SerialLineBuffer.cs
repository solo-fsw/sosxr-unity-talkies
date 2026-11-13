using System.Text;


namespace SOSXR.Talkies
{
    /// <summary>
    ///     Collects raw bytes from a serial connection and splits them into newline-terminated ASCII lines.
    ///     Partial lines are kept until the rest arrives. Empty lines are skipped.
    ///     If more than <see cref="MaxBufferedChars" /> characters arrive without a newline, the buffer is
    ///     dropped, so a device that never sends a newline cannot grow memory without bound.
    ///     Pure C#, no Unity dependencies, so it can be unit tested.
    /// </summary>
    public sealed class SerialLineBuffer
    {
        private readonly StringBuilder _buffer = new();


        /// <summary>Creates an empty buffer. <paramref name="maxBufferedChars" /> caps how much may wait for a newline.</summary>
        public SerialLineBuffer(int maxBufferedChars = 4096)
        {
            MaxBufferedChars = maxBufferedChars;
        }


        /// <summary>Upper limit for characters kept while waiting for a newline.</summary>
        public int MaxBufferedChars { get; }

        /// <summary>Number of characters currently waiting for a newline.</summary>
        public int PendingLength => _buffer.Length;


        /// <summary>Appends <paramref name="count" /> bytes from <paramref name="data" />, starting at <paramref name="offset" />.</summary>
        public void Append(byte[] data, int offset, int count)
        {
            if (data == null || count <= 0)
            {
                return;
            }

            _buffer.Append(Encoding.ASCII.GetString(data, offset, count));

            if (_buffer.Length > MaxBufferedChars && _buffer.ToString().IndexOf('\n') < 0)
            {
                _buffer.Clear();
            }
        }


        /// <summary>Appends text directly. Mostly useful for tests.</summary>
        public void Append(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            var bytes = Encoding.ASCII.GetBytes(text);
            Append(bytes, 0, bytes.Length);
        }


        /// <summary>Takes the next complete, non-empty line (trimmed, without the newline) from the buffer.</summary>
        /// <returns><c>false</c> when no complete line is available yet.</returns>
        public bool TryReadLine(out string line)
        {
            while (true)
            {
                var text = _buffer.ToString();
                var newlineIndex = text.IndexOf('\n');

                if (newlineIndex < 0)
                {
                    line = null;

                    return false;
                }

                line = text.Substring(0, newlineIndex).Trim();
                _buffer.Remove(0, newlineIndex + 1);

                if (line.Length > 0)
                {
                    return true;
                }
            }
        }


        /// <summary>Drops everything that is buffered.</summary>
        public void Clear()
        {
            _buffer.Clear();
        }
    }
}
