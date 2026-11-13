using System;
using System.Collections.Generic;
using UnityEngine;


namespace SOSXR.MQTT.raspmarker
{
    [CreateAssetMenu(fileName = "FileName", menuName = "SOSXR/MQTT/raspmarker", order = 0)]
    public class RaspmarkerStringList : ScriptableObject
    {
        [SerializeField] private List<string> m_markers = new();
        public List<string> Markers => m_markers;


        public int GetOrAdd(string value)
        {
            if (value.Contains(",") || value.Contains("\"") || value.Contains(" "))
            {
                Debug.LogError("Values cannot contain commas, spaces, or quotes.");

                return -1;
            }

            var index = m_markers.IndexOf(value);

            if (index >= 0 && index < m_markers.Count)
            {
                return index;
            }

            if (m_markers.Count > 255)
            {
                throw new InvalidOperationException("Maximum of 256 items reached");
            }

            m_markers.Add(value);
            index = m_markers.Count - 1;

            Debug.Log($"Adding marker {value} with index {index} to list, since it was not yet included.");

            return index;
        }


        public bool TryGetMarker(int index, out string value)
        {
            if (index < m_markers.Count && !string.IsNullOrEmpty(m_markers[index]))
            {
                value = m_markers[index];

                return true;
            }

            value = null;

            return false;
        }
    }
}
