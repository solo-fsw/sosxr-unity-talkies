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
            Assert.That(tracker.HasDeadline(16), Is.True, "An expired pin is kept until the device confirms LOW.");
        }


        [Test]
        public void ExpiredPin_IsReturnedAgainUntilLowConfirmation()
        {
            var tracker = new PinDeadlineTracker();
            tracker.OnPinSet(16, true, 0f, 10f);

            Assert.That(tracker.CollectExpired(10f), Is.EqualTo(new[] { 16 }));

            // The LOW write failed, so no confirmation arrived: the next check must retry.
            Assert.That(tracker.CollectExpired(11f), Is.EqualTo(new[] { 16 }));

            tracker.OnPinSet(16, false, 12f, 10f);

            Assert.That(tracker.CollectExpired(13f), Is.Empty);
            Assert.That(tracker.HasDeadline(16), Is.False);
        }


        [Test]
        public void MarkExpiryReported_IsTrueOnceUntilLowConfirmation()
        {
            var tracker = new PinDeadlineTracker();
            tracker.OnPinSet(4, true, 0f, 1f);

            Assert.That(tracker.MarkExpiryReported(4), Is.True);
            Assert.That(tracker.MarkExpiryReported(4), Is.False, "Retries must not log again.");

            tracker.OnPinSet(4, false, 2f, 1f);
            tracker.OnPinSet(4, true, 3f, 1f);

            Assert.That(tracker.MarkExpiryReported(4), Is.True, "A new HIGH period is reported again.");
        }


        [Test]
        public void HighRequest_StartsDeadlineWithoutConfirmation()
        {
            var tracker = new PinDeadlineTracker();
            tracker.OnPinSetRequested(16, true, 10f, 30f);

            Assert.That(tracker.HasDeadline(16), Is.True);
            Assert.That(tracker.KnownPins, Does.Contain(16));
            Assert.That(tracker.CollectExpired(40f), Is.EqualTo(new[] { 16 }));
        }


        [Test]
        public void ConfirmationAfterHighRequest_DoesNotExtendDeadline()
        {
            var tracker = new PinDeadlineTracker();
            tracker.OnPinSetRequested(5, true, 0f, 10f);
            tracker.OnPinSet(5, true, 2f, 10f);

            Assert.That(tracker.CollectExpired(10f), Is.EqualTo(new[] { 5 }));
        }


        [Test]
        public void HighRequestAgain_DoesNotExtendDeadline()
        {
            var tracker = new PinDeadlineTracker();
            tracker.OnPinSetRequested(5, true, 0f, 10f);
            tracker.OnPinSetRequested(5, true, 8f, 10f);

            Assert.That(tracker.CollectExpired(10f), Is.EqualTo(new[] { 5 }));
        }


        [Test]
        public void LowRequest_DoesNotClearDeadline()
        {
            var tracker = new PinDeadlineTracker();
            tracker.OnPinSet(5, true, 0f, 10f);
            tracker.OnPinSetRequested(5, false, 1f, 10f);

            Assert.That(tracker.HasDeadline(5), Is.True, "A LOW command can be lost; only the LOW confirmation clears the deadline.");
            Assert.That(tracker.CollectExpired(10f), Is.EqualTo(new[] { 5 }));
        }


        [Test]
        public void NegativePins_AreIgnoredForRequests()
        {
            var tracker = new PinDeadlineTracker();

            Assert.DoesNotThrow(() => tracker.OnPinSetRequested(-1, true, 0f, 1f));
            Assert.That(tracker.KnownPins, Is.Empty);
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
