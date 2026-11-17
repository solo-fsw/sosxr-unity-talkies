using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;


namespace SOSXR.MQTT.raspmarker
{
    public class MQTTLogger : MonoBehaviour
    {
        [SerializeField] [HideInInspector] private RaspmarkerMQTTHandler m_handler;
        [SerializeField] private RaspmarkerStringList m_stringList;
        [SerializeField] private List<MarkerLog> m_sentLog = new();
        [SerializeField] private List<MarkerLog> m_receivedLog = new();


        private void OnValidate()
        {
            if (m_handler == null)
            {
                m_handler = FindAnyObjectByType<RaspmarkerMQTTHandler>();
            }
        }


        private void OnEnable()
        {
            m_handler.OnSent += LogSent;
            m_handler.OnReceived += LogReceived;
        }


        private void LogSent(int index, string marker, long time)
        {
            var log = new MarkerLog
            {
                Index = index,
                Marker = marker,
                Time = time
            };

            m_sentLog.Add(log);
        }


        private void LogReceived(int index, string marker, long time)
        {
            var log = new MarkerLog
            {
                Index = index,
                Marker = marker,
                Time = time
            };

            m_receivedLog.Add(log);
        }


        private static void SaveCsv(List<MarkerLog> markerlog, string label)
        {
            var dateTimeString = DateTime.UtcNow.ToString("yyyy-MM-dd_HH-mm-ss");
            var path = Path.Combine(Application.persistentDataPath, $"{label}_{dateTimeString}.csv");

            var sb = new StringBuilder();
            sb.AppendLine("Index,Marker,UnixTimeMs");

            foreach (var log in markerlog)
            {
                sb.AppendLine($"{log.Index},{log.Marker},{log.Time}");
            }

            File.WriteAllText(path, sb.ToString());
            Debug.Log($"Saved {label} logs to: {path}");
        }


        private static void SaveCsv(List<string> markers, string label)
        {
            var dateTimeString = DateTime.UtcNow.ToString("yyyy-MM-dd_HH-mm-ss");
            var path = Path.Combine(Application.persistentDataPath, $"{label}_{dateTimeString}.csv");

            var sb = new StringBuilder();
            sb.AppendLine("Index,Marker");

            for (var i = 0; i < markers.Count; i++)
            {
                sb.AppendLine($"{i},{markers[i]}");
            }

            File.WriteAllText(path, sb.ToString());
            Debug.Log($"Saved {label} list to: {path}");
        }


        private void OnDisable()
        {
            m_handler.OnSent -= LogSent;
            m_handler.OnReceived -= LogReceived;

            SaveCsv(m_sentLog, "Sent");
            SaveCsv(m_receivedLog, "Received");
            SaveCsv(m_stringList.Markers, "Markers");
        }
    }
}