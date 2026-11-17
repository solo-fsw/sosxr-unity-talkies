using System;
using System.Text;
using MQTTUnity;
using UnityEngine;


namespace SOSXR.MQTT.raspmarker
{
    /// <summary>
    ///     This restricts sending payloads to a byte value (0-255), which is the range that the raspmarker supports.
    ///     Additionally, it stores each byte value as an index in the RaspmarkerStringList, where we can store information on what the marker actually means.
    ///     It also sends messages over a default topic, to ensure that the other device can receive them.
    ///     This means that the raspmarker should be subscribed to the same topic.
    /// </summary>
    public class RaspmarkerMQTTHandler : MonoBehaviour
    {
        [SerializeField] private RaspmarkerStringList m_stringList;

        [SerializeField] private string m_publishTopic = "from-Unity";

        public Action<int, string, long> OnReceived;
        public Action<int, string, long> OnSent;


        private void OnEnable()
        {
            MQTTClient.Subscribe(OnPayloadReceived, Topics.Main);
        }


        private void OnPayloadReceived(string topic, byte[] payload)
        {
            var index = int.TryParse(Encoding.UTF8.GetString(payload), out var result) ? result : -1;

            if (!string.Equals(topic, Topics.Main))
            {
                Debug.LogWarning($"Topic received from raspmarker ({topic}) is not equal to the expected default topic ({Topics.Main}). Please check."); // Moot check since this cannot happen
            }

            if (index is < 0 or > 255)
            {
                Debug.LogWarning("Received invalid payload: " + index + ". It needs to be between 0 and 255. Please check"); // Moot check since this cannot happen
            }

            if (!m_stringList.TryGetMarker(index, out var marker))
            {
                Debug.LogWarning("Received payload of: " + index + ". This is not registered in our stringList as a valid marker, so we cannot log it in a meaningful way.");
            }

            var unixTimeMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            OnReceived?.Invoke(index, marker, unixTimeMs);

            // Debug.Log("Payload received: " + index);
        }


        /// <summary>
        /// </summary>
        /// <param name="marker"></param>
        public void SendPayload(string marker)
        {
            var index = m_stringList.GetOrAdd(marker);
            SendPayload(index);
        }


        /// <summary>
        /// </summary>
        /// <param name="index"></param>
        public void SendPayload(int index)
        {
            if (index is < 0 or > 255)
            {
                Debug.LogError("Trying to send an invalid payload: " + index + ". Please check");

                return;
            }

            SendPayload((byte) index);
        }


        private void SendPayload(byte index)
        {
            if (!m_stringList.TryGetMarker(index, out var marker))
            {
                Debug.LogWarning($"Marker-index {index} is not registered in the stringlist with a corresponding string marker. This is not bad per se (the raspmarker will still received the message), but this doesn't allow Unity to log what this index {index} actually means.");
            }

            var unixTimeMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            OnSent?.Invoke(index, marker, unixTimeMs);

            // Debug.Log("Payload sent: " + index);

            MQTTClient.Publish(Topics.GetTopic(m_publishTopic), index.ToString());
        }


        private void OnDisable()
        {
            MQTTClient.Unsubscribe(OnPayloadReceived, Topics.Main);
        }
    }
}
