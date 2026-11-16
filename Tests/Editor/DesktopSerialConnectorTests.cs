using NUnit.Framework;


namespace SOSXR.Talkies.Tests
{
    /// <summary>EditMode tests for <see cref="DesktopSerialConnector.FilterPorts" />. No hardware needed.</summary>
    public class DesktopSerialConnectorTests
    {
        [Test]
        public void FilterPorts_RemovesMacSystemPorts_AndListsUsbSerialFirst()
        {
            var ports = new[]
            {
                "/dev/cu.wlan-debug",
                "/dev/cu.debug-console",
                "/dev/cu.Bluetooth-Incoming-Port",
                "/dev/cu.usbmodem1101",
                "/dev/cu.another"
            };

            var result = DesktopSerialConnector.FilterPorts(ports, true);

            Assert.That(result, Is.EqualTo(new[] { "/dev/cu.usbmodem1101", "/dev/cu.another" }));
        }


        [Test]
        public void FilterPorts_AllUsbSerialKinds_ComeBeforeOtherPorts()
        {
            var ports = new[] { "/dev/cu.aaa", "/dev/cu.wchusbserial10", "/dev/cu.usbserial-A1", "/dev/cu.usbmodem2" };

            var result = DesktopSerialConnector.FilterPorts(ports, true);

            Assert.That(result, Is.EqualTo(new[] { "/dev/cu.usbmodem2", "/dev/cu.usbserial-A1", "/dev/cu.wchusbserial10", "/dev/cu.aaa" }));
        }


        [Test]
        public void FilterPorts_KeepsBluetooth_WhenNotStripping()
        {
            var result = DesktopSerialConnector.FilterPorts(new[] { "/dev/cu.Bluetooth-Incoming-Port", "/dev/cu.debug-console" }, false);

            Assert.That(result, Is.EqualTo(new[] { "/dev/cu.Bluetooth-Incoming-Port" }));
        }


        [Test]
        public void FilterPorts_WindowsComPorts_AreSortedOrdinally()
        {
            var result = DesktopSerialConnector.FilterPorts(new[] { "COM4", null, "", "COM3" }, true);

            Assert.That(result, Is.EqualTo(new[] { "COM3", "COM4" }));
        }
    }
}
