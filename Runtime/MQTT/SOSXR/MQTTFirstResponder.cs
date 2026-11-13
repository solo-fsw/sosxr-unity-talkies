using System.Text;
using MQTTUnity;
using SOSXR.Talkies;
using UnityEngine;
using UnityEngine.Events;


/// <summary>
///     Invokes a UnityEvent when an MQTT message arrives, optionally only for one topic and/or one payload.
///     An empty topic subscribes to all topics (<c>#</c>). Wildcards (<c>+</c>, <c>#</c>) are supported.
/// </summary>
public class MQTTFirstResponder : MonoBehaviour
{
    [SerializeField] [HideInInspector] private bool m_toggleTopic;
    [SerializeField] [HideInInspector] private bool m_togglePayload;

    [Tooltip("Leave this blank (or #) for responding to all topics")]
    [EnableIf(nameof(m_toggleTopic))]
    [SerializeField] private string m_onlyRespondToThisTopic;

    [Tooltip("Leave this blank for responding to all payloads")]
    [EnableIf(nameof(m_togglePayload))]
    [SerializeField] private string m_onlyRespondToThisPayload;

    [SerializeField] private UnityEvent<string, string> m_onMessageReceived;

    private string _subscribedTopic;


    /// <summary>The topic filter actually used: <c>#</c> when the configured topic is empty.</summary>
    public string EffectiveTopic => string.IsNullOrEmpty(m_onlyRespondToThisTopic) ? "#" : m_onlyRespondToThisTopic;


    private void OnValidate()
    {
        if (!m_toggleTopic)
        {
            m_onlyRespondToThisTopic = string.Empty;
        }

        if (!m_togglePayload)
        {
            m_onlyRespondToThisPayload = string.Empty;
        }
    }


    private void OnEnable()
    {
        _subscribedTopic = EffectiveTopic;
        MQTTClient.Subscribe(MessageReceived, _subscribedTopic);
    }


    private void MessageReceived(string topic, byte[] payload)
    {
        if (!MQTTClient.TopicMatches(EffectiveTopic, topic))
        {
            return;
        }

        var payloadString = payload == null ? string.Empty : Encoding.UTF8.GetString(payload);

        if (payloadString != m_onlyRespondToThisPayload && m_onlyRespondToThisPayload != "#" && !string.IsNullOrEmpty(m_onlyRespondToThisPayload))
        {
            return;
        }

        Debug.Log($"Payload received: {payloadString} on topic {topic}");

        m_onMessageReceived?.Invoke(topic, payloadString);
    }


    private void OnDisable()
    {
        // Use the topic we subscribed with, even if the Inspector value changed since.
        MQTTClient.Unsubscribe(MessageReceived, _subscribedTopic);
    }
}
