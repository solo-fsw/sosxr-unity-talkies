using NUnit.Framework;


namespace SOSXR.Talkies.Tests
{
    public class PinDeadlineTrackerTests
    {
        [Test]
        public void HighStartsDeadline_ExpiresAfterTimeout()
        {
            var tracker = new PinDeadlineTracker();
            tracker.OnPinSet(16, true, 10f, 30f);

            Assert.That(tracker.CollectExpired(39f), Is.Empty);
            Assert.That(tracker.CollectExpired(40f), Is.EqualTo(new[] { 16 }));
            Assert.That(tracker.HasDeadline(16), Is.False, "An expired pin is forgotten until it goes HIGH again.");
        }


        [Test]
        public void HighAgain_DoesNotExtendDeadline()
        {
            var tracker = new PinDeadlineTracker();
            tracker.OnPinSet(5, true, 0f, 10f);
            tracker.OnPinSet(5, true, 8f, 10f);

            Assert.That(tracker.CollectExpired(10f), Is.EqualTo(new[] { 5 }));
        }


        [Test]
        public void Low_ClearsDeadline()
        {
            var tracker = new PinDeadlineTracker();
            tracker.OnPinSet(5, true, 0f, 10f);
            tracker.OnPinSet(5, false, 1f, 10f);

            Assert.That(tracker.CollectExpired(100f), Is.Empty);
        }


        [Test]
        public void KnownPins_OnlyContainsPinsThatWereSet()
        {
            var tracker = new PinDeadlineTracker();
            tracker.OnPinSet(20, true, 0f, 10f);
            tracker.OnPinSet(3, false, 0f, 10f);

            Assert.That(tracker.KnownPins, Is.EquivalentTo(new[] { 3, 20 }), "Pins 0..19 must not be touched just because pin 20 was used.");
        }


        [Test]
        public void NegativePins_AreIgnored()
        {
            var tracker = new PinDeadlineTracker();

            Assert.DoesNotThrow(() => tracker.OnPinSet(-1, true, 0f, 1f));
            Assert.That(tracker.KnownPins, Is.Empty);
        }


        [Test]
        public void CollectExpired_IsSorted()
        {
            var tracker = new PinDeadlineTracker();
            tracker.OnPinSet(9, true, 0f, 1f);
            tracker.OnPinSet(2, true, 0f, 1f);

            Assert.That(tracker.CollectExpired(5f), Is.EqualTo(new[] { 2, 9 }));
        }
    }
}
