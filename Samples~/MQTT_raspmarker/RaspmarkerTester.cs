using SOSXR.Talkies;
using UnityEngine;


namespace SOSXR.MQTT.raspmarker
{
    public class RaspmarkerTester : MonoBehaviour
    {
        [SerializeField] [HideInInspector] private RaspmarkerMQTTHandler _handler;

        [Header("Testing sending markers")]
        [Tooltip("The index is restricted to the byte-range, since that is what the markerbox / raspmarker can send or receive. \n" +
                 "In this context the index refers to an index in the string list, where we can store the meaning of each index (=marker)")]
        [SerializeField] [Range(0, 256)] private int m_testIndex = 64;

        [SerializeField] private string m_testMarker = "test";


        private void OnValidate()
        {
            if (_handler == null)
            {
                _handler = FindAnyObjectByType<RaspmarkerMQTTHandler>();
            }
        }


        private void OnEnable()
        {
            _handler.OnReceived += TestReceivingPayload;
        }


        private void TestReceivingPayload(int index, string marker, long time)
        {
            Debug.Log($"Received payload {index} ({marker}) at {time}");
        }


        [Button]
        public void TestSendingIndex()
        {
            _handler.SendPayload(m_testIndex);
        }


        [Button]
        public void TestSendingMarker()
        {
            _handler.SendPayload(m_testMarker);
        }


        private void OnDisable()
        {
            _handler.OnReceived -= TestReceivingPayload;
        }
    }
}
