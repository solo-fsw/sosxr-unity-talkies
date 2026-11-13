using System.Collections.Generic;


namespace SOSXR.Talkies
{
    /// <summary>
    ///     Keeps track of which pins have been driven HIGH and when they must be driven LOW again.
    ///     Used by <see cref="SafetyPin" />. The deadline is set once, when a pin first goes HIGH;
    ///     setting it HIGH again does not extend the deadline. Setting it LOW clears the deadline.
    ///     Negative pin numbers are ignored.
    ///     Pure C#, no Unity dependencies, so it can be unit tested.
    /// </summary>
    public sealed class PinDeadlineTracker
    {
        private readonly Dictionary<int, float> _deadlines = new();
        private readonly HashSet<int> _knownPins = new();


        /// <summary>Every pin that was ever set through <see cref="OnPinSet" />.</summary>
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

                return;
            }

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


        /// <summary>Returns, and forgets, every pin whose deadline is at or before <paramref name="now" />.</summary>
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

            foreach (var pin in expired)
            {
                _deadlines.Remove(pin);
            }

            expired.Sort();

            return expired;
        }


        /// <summary>Forgets all pins and deadlines.</summary>
        public void Clear()
        {
            _deadlines.Clear();
            _knownPins.Clear();
        }
    }
}
