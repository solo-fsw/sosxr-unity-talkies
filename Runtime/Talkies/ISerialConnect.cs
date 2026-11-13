namespace SOSXR.Talkies
{
    /// <summary>
    ///     Contract for serial connection implementations. Provides a unified API for
    ///     opening, closing, reading from and writing to a serial device across platforms (Android, Desktop).
    ///     <see cref="PinController" /> talks to the device only through this interface.
    /// </summary>
    public interface ISerialConnect
    {
        /// <summary>Gets a value indicating whether the serial device is currently connected.</summary>
        bool IsConnected { get; }

        /// <summary>Opens the serial connection to the device.</summary>
        void Connect();

        /// <summary>Closes the serial connection and releases the device.</summary>
        void Disconnect();

        /// <summary>Writes <paramref name="count" /> bytes from <paramref name="data" />, starting at <paramref name="offset" />.</summary>
        /// <returns>The number of bytes written. 0 when not connected or on error.</returns>
        int Write(byte[] data, int offset, int count);

        /// <summary>
        ///     Reads up to <paramref name="count" /> bytes that are already available into <paramref name="buffer" />,
        ///     starting at <paramref name="offset" />. Must not block.
        /// </summary>
        /// <returns>The number of bytes read. 0 when nothing is available, when not connected, or on error.</returns>
        int Read(byte[] buffer, int offset, int count);
    }
}
