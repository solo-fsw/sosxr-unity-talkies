using SOSXR.Talkies;
using UnityEngine;


namespace MQTTUnity
{
    /// <summary>
    ///     Development helper for quickly verifying that the MQTT connection works.
    ///     Subscribes to a configurable topic on enable and provides Inspector buttons
    ///     to publish a test message and to subscribe/unsubscribe manually.
    /// </summary>
    public class MQTTTester : MonoBehaviour
    {
        [SerializeField] private string m_debugTopic = "SOSXR/Test";
        [SerializeField] private string m_debugPayload = "Hello world!";


        private void OnEnable()
        {
            SubscribeToDebug();
        }


        [Button]
        private void SubscribeToDebug()
        {
            MQTTClient.Subscribe(null, m_debugTopic); // Subscribe to the test topic with a null callback to ensure the topic is registered
        }


        /// <summary>Publishes the configured debug payload to the debug topic via <see cref="MQTTClient"/>.</summary>
        [Button]
        public void PublishDebug()
        {
            MQTTClient.Publish(m_debugTopic, m_debugPayload);
        }


        private void OnDisable()
        {
            UnsubscribeFromDebug();
        }


        [Button]
        private void UnsubscribeFromDebug()
        {
            MQTTClient.Unsubscribe(null, m_debugTopic);
        }
    }
}