namespace SOSXR.MQTT.raspmarker
{
    public static class Topics
    {
        public static readonly string Main = "raspmarker";


        public static string GetTopic(string marker = "")
        {
            if (string.IsNullOrEmpty(marker))
            {
                return Main;
            }

            return Main + "/" + marker;
        }
    }
}