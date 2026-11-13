using System.Collections;
using UnityEngine;
using UnityEngine.Events;


namespace SOSXR.Talkies
{
    /// <summary>
    ///     Like <see cref="PinTimer" />, but with a delay before and after the pulse and UnityEvents for each stage.
    ///     Requires a <see cref="PinController" /> on the same GameObject.
    /// </summary>
    [RequireComponent(typeof(PinController))]
    public class PinTimerWithEvents : MonoBehaviour
    {
        [SerializeField] [Range(1, 5000)] [Tooltip("ms")] private int m_toggleTime = 150;
        [SerializeField] [Range(0, 25)] [Tooltip("GPIO")] private int m_pin = 16;

        [SerializeField] [Range(0, 1000)] [Tooltip("ms")] private int m_delay;

        [SerializeField] private UnityEvent<int> m_beforeDelay;
        [SerializeField] private UnityEvent<int> m_onTogglePin;
        [SerializeField] private UnityEvent<int> m_afterDelay;


        [SerializeField] [HideInInspector] private PinController _controller;
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

            var delaySec = m_delay / 1000f;
            m_beforeDelay?.Invoke(m_delay);

            yield return new WaitForSeconds(delaySec);

            Controller.SetPin(m_pin, true);
            var toggleTimeSec = m_toggleTime / 1000f;

            m_onTogglePin?.Invoke(m_toggleTime);

            yield return new WaitForSeconds(toggleTimeSec);

            Controller.SetPin(m_pin, false);

            yield return new WaitForSeconds(delaySec);
            m_afterDelay?.Invoke(m_delay);

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
