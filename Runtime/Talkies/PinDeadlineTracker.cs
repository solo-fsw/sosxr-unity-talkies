using System.Collections.Generic;


namespace SOSXR.Talkies
{
    /// <summary>
    ///     Keeps track of which pins have been driven HIGH and when they must be driven LOW again.
    ///     Used by <see cref="SafetyPin" />. The deadline is set once, when a HIGH command is sent or a HIGH
    ///     confirmation arrives, whichever comes first; setting it HIGH again does not extend the deadline.
    ///     Only a LOW confirmation clears the deadline. Sending a LOW command does not, because the command can be lost.
    ///     An expired deadline is kept until the LOW confirmation arrives, so the caller can retry.
    ///     Negative pin numbers are ignored.
    ///     Pure C#, no Unity dependencies, so it can be unit tested.
    /// </summary>
    public sealed class PinDeadlineTracker
    {
        private readonly Dictionary<int, float> _deadlines = new();
        private readonly HashSet<int> _knownPins = new();
        private readonly HashSet<int> _reportedExpired = new();


        /// <summary>Every pin that was ever set through <see cref="OnPinSet" /> or <see cref="OnPinSetRequested" />.</summary>
        public IReadOnlyCollection<int> KnownPins => _knownPins;


        /// <summary>Registers a SET confirmation.</summary>
        /// <param name="pin">Pin number. Negative values are ignored.</param>
        /// <param name="high"><c>true</c> when the pin went HIGH.</param>
        /// <param name="now">Current time in seconds.</param>
        /// <param name="timeoutSeconds">How long the pin may stay HIGH.</param>
        public void OnPinSet(int pin, bool high, float now, float timeoutSeconds)
        {
            if (pin < 0)
            {
                return;
            }

            _knownPins.Add(pin);

            if (!high)
            {
                _deadlines.Remove(pin);
                _reportedExpired.Remove(pin);

                return;
            }

            StartDeadline(pin, now, timeoutSeconds);
        }


        /// <summary>
        ///     Registers a SET command that was sent, before the device confirms it.
        ///     A HIGH command starts the deadline if none is running, so a lost confirmation cannot leave the pin HIGH.
        ///     A LOW command does not clear the deadline; only the LOW confirmation (<see cref="OnPinSet" />) does.
        /// </summary>
        /// <param name="pin">Pin number. Negative values are ignored.</param>
        /// <param name="high"><c>true</c> when the command drives the pin HIGH.</param>
        /// <param name="now">Current time in seconds.</param>
        /// <param name="timeoutSeconds">How long the pin may stay HIGH.</param>
        public void OnPinSetRequested(int pin, bool high, float now, float timeoutSeconds)
        {
            if (pin < 0)
            {
                return;
            }

            _knownPins.Add(pin);

            if (high)
            {
                StartDeadline(pin, now, timeoutSeconds);
            }
        }


        private void StartDeadline(int pin, float now, float timeoutSeconds)
        {
            if (!_deadlines.ContainsKey(pin))
            {
                _deadlines[pin] = now + timeoutSeconds;
            }
        }


        /// <summary>Returns <c>true</c> if the pin currently has a pending deadline.</summary>
        public bool HasDeadline(int pin)
        {
            return _deadlines.ContainsKey(pin);
        }


        /// <summary>
        ///     Returns every pin whose deadline is at or before <paramref name="now" />.
        ///     The deadlines are kept until a LOW confirmation arrives, so the same pins are returned again on the next call.
        /// </summary>
        public List<int> CollectExpired(float now)
        {
            var expired = new List<int>();

            foreach (var pair in _deadlines)
            {
                if (pair.Value <= now)
                {
                    expired.Add(pair.Key);
                }
            }

            expired.Sort();

            return expired;
        }


        /// <summary>
        ///     Returns <c>true</c> the first time it is called for an expired pin, and <c>false</c> after that,
        ///     until the pin's LOW confirmation arrives. Lets the caller log an expiry once instead of on every retry.
        /// </summary>
        public bool MarkExpiryReported(int pin)
        {
            return _reportedExpired.Add(pin);
        }


        /// <summary>Forgets all pins and deadlines.</summary>
        public void Clear()
        {
            _deadlines.Clear();
            _knownPins.Clear();
            _reportedExpired.Clear();
        }
    }
}
