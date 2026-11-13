using System.Collections;
using UnityEngine;


namespace SOSXR.Talkies
{
    /// <summary>
    ///     Drives a GPIO pin HIGH for a short configurable duration, then LOW — a single pulse.
    ///     Requires a <see cref="PinController" /> on the same GameObject.
    ///     If a pulse is already in progress when <c>ToggleOnOff</c> is called again, the
    ///     running coroutine is cancelled and the pin is driven LOW.
    /// </summary>
    [RequireComponent(typeof(PinController))]
    public class PinTimer : MonoBehaviour
    {
        [SerializeField] [HideInInspector] private PinController _controller;
        [SerializeField] [Range(0.15f, 5f)] private float m_toggleTime = 0.15f;
        [SerializeField] [Range(0, 25)] private int m_pin = 16;

        private Coroutine _toggleCoroutine;


        /// <summary><c>true</c> while a pulse is running.</summary>
        public bool IsPulsing => _toggleCoroutine != null;


        private PinController Controller
        {
            get
            {
                if (_controller == null)
                {
                    _controller = GetComponent<PinController>();
                }

                return _controller;
            }
        }


        private void OnValidate()
        {
            if (_controller == null)
            {
                _controller = GetComponent<PinController>();
            }
        }


        private void Awake()
        {
            _ = Controller;
        }


        /// <summary>
        ///     Starts a one-shot pulse on the configured pin: HIGH for <c>m_toggleTime</c> seconds,
        ///     then LOW. If a pulse is already active, stops it and drives the pin LOW instead.
        /// </summary>
        [Button]
        private void ToggleOnOff()
        {
            if (_toggleCoroutine != null)
            {
                StopCoroutine(_toggleCoroutine);

                Controller.SetPin(m_pin, false);

                Debug.LogWarning("We already had a toogler running. Will turn that toggler off, but not continue from here. Toggle again to resume functionality.");

                _toggleCoroutine = null;

                return;
            }

            _toggleCoroutine = StartCoroutine(ToggleOnOffCR());
        }


        private IEnumerator ToggleOnOffCR()
        {
            if (!Application.isPlaying)
            {
                _toggleCoroutine = null;

                yield break;
            }

            Controller.SetPin(m_pin, true);

            yield return new WaitForSeconds(m_toggleTime);

            Controller.SetPin(m_pin, false);

            _toggleCoroutine = null;
        }


        private void OnDisable()
        {
            StopAllCoroutines();
            _toggleCoroutine = null;

            if (Controller != null && Controller.IsConnected)
            {
                Controller.SetPin(m_pin, false);
            }
        }
    }
}
