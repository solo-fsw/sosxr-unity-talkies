/*
The MIT License (MIT)

Copyright (c) 2018 Giovanni Paolo Vigano' && 2025 SOSXR

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
*/


using System.Text;
using UnityEngine;
using UnityEngine.UI;


namespace MQTTUnity
{
    /// <summary>
    ///     Showing a simple user interface for the MQTT client of the M2MQTT library (https://github.com/eclipse/paho.mqtt.m2mqtt)
    /// </summary>
    public class MQTTUI : MonoBehaviour
    {
        [SerializeField] private MQTTClient m_MQTTClient;

        [Header("User Interface")] [SerializeField]
        private InputField m_consoleInputField;

        [SerializeField] private Toggle m_encryptedToggle;
        [SerializeField] private InputField m_addressInputField;
        [SerializeField] private InputField m_portInputField;
        [SerializeField] private Button m_connectButton;
        [SerializeField] private Button m_disconnectButton;
        [SerializeField] private Button m_testPublishButton;
        [SerializeField] private Button m_clearButton;


        private void OnValidate()
        {
            if (m_MQTTClient == null)
            {
                m_MQTTClient = FindAnyObjectByType<MQTTClient>();
            }

            if (m_MQTTClient == null)
            {
                // Debug.Log("MQTTUI: MQTTClient component is not assigned.");
            }
        }


        private void Awake()
        {
            if (m_MQTTClient == null)
            {
                m_MQTTClient = MQTTClient.Instance;
            }
        }


        private void Start()
        {
            SetUIMessage("Connecting... \n");
        }


        private void OnEnable()
        {
            MQTTClient.OnConnected += HandleConnectedUI;
            MQTTClient.OnDisconnected += HandleDisconnectedUI;
        }


        private void HandleConnectedUI()
        {
            SetUIMessage("Ready... \n");
            MQTTClient.Subscribe(AddUIMessage);
        }


        private void HandleDisconnectedUI()
        {
            SetUIMessage("Disconnected from broker.\n");
        }


        public void SetUIMessage(string msg = "")
        {
            if (m_consoleInputField == null)
            {
                return;
            }

            m_consoleInputField.text = msg;

            // Debug.Log("MQTTUI: SetUIMessage: " + msg);
            UpdateUI();
        }


        private void AddUIMessage(string topic, byte[] payload)
        {
            if (m_consoleInputField == null)
            {
                return;
            }

            var msg = Encoding.UTF8.GetString(payload);

            m_consoleInputField.text += topic + " : " + msg + "\n";

            // Debug.Log("MQTTUI: AddUIMessage: " + msg);

            UpdateUI();
        }


        private void UpdateUI()
        {
            if (m_disconnectButton != null)
            {
                m_disconnectButton.interactable = MQTTClient.IsConnected;
            }

            if (m_connectButton != null)
            {
                m_connectButton.interactable = !MQTTClient.IsConnected;
            }

            if (m_testPublishButton != null)
            {
                m_testPublishButton.interactable = MQTTClient.IsConnected;
            }

            if (m_addressInputField != null && m_connectButton != null)
            {
                m_addressInputField.interactable = m_connectButton.interactable;
                m_addressInputField.text = m_MQTTClient.BrokerAddress;
            }

            if (m_portInputField != null && m_connectButton != null)
            {
                m_portInputField.interactable = m_connectButton.interactable;
                m_portInputField.text = m_MQTTClient.BrokerPort.ToString();
            }

            if (m_encryptedToggle != null && m_connectButton != null)
            {
                m_encryptedToggle.interactable = m_connectButton.interactable;
                m_encryptedToggle.isOn = m_MQTTClient.IsEncrypted;
            }

            if (m_clearButton != null && m_connectButton != null)
            {
                m_clearButton.interactable = m_connectButton.interactable;
            }
        }


        private void OnDisable()
        {
            MQTTClient.Unsubscribe(AddUIMessage);

            SetUIMessage();

            MQTTClient.OnConnected -= HandleConnectedUI;
            MQTTClient.OnDisconnected -= HandleDisconnectedUI;
        }
    }
}
