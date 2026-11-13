using System;


namespace SOSXR.MQTT.raspmarker
{
    [Serializable]
    public class MarkerLog
    {
        public int Index;
        public string Marker;
        public long Time;
    }
}