using NUnit.Framework;

namespace DreamCodeVRPlus.EditorTests
{
    /// <summary>
    /// EditMode tests for the PURE Phase-6/7 security logic (confirmation state machine,
    /// disclosure feed, notice encoder). Device-free and deterministic. Run inside
    /// Unity's Test Runner — they are NOT part of the Rust `cargo test` gate, and the
    /// MonoBehaviour/compile/HUD integration remains on-device pending.
    /// </summary>
    public sealed class Phase67SecurityTests
    {
        // --- Phase 7: compile confirmation state machine ---

        [Test]
        public void Confirmation_DefaultsIdle_AndConfirmReturnsNullWhenNothingPending()
        {
            var s = new CompileConfirmationState(30000);
            Assert.AreEqual(CompileConfirmationState.Phase.Idle, s.Current);
            Assert.IsFalse(s.HasPending);
            Assert.IsNull(s.Confirm());
        }

        [Test]
        public void Confirmation_SubmitThenConfirm_ReturnsCode_ThenIdle()
        {
            var s = new CompileConfirmationState(30000);
            s.Submit("CODE_A", 1000);
            Assert.IsTrue(s.HasPending);
            Assert.AreEqual("CODE_A", s.Confirm());
            Assert.IsFalse(s.HasPending);
            Assert.IsNull(s.Confirm(), "second confirm has nothing to run");
        }

        [Test]
        public void Confirmation_NewerSubmitReplacesOlderPending()
        {
            var s = new CompileConfirmationState(30000);
            s.Submit("OLD", 1000);
            s.Submit("NEW", 1200);
            Assert.AreEqual("NEW", s.Confirm());
        }

        [Test]
        public void Confirmation_ExpiresStalePending_FailClosed()
        {
            var s = new CompileConfirmationState(30000);
            s.Submit("CODE", 1000);
            Assert.IsFalse(s.ExpireIfStale(1000 + 29999), "not yet stale");
            Assert.IsTrue(s.ExpireIfStale(1000 + 30000), "stale at ttl");
            Assert.IsFalse(s.HasPending);
            Assert.IsNull(s.Confirm(), "expired code must never compile");
        }

        [Test]
        public void Confirmation_ResetClearsPending()
        {
            var s = new CompileConfirmationState(30000);
            s.Submit("CODE", 1000);
            s.Reset();
            Assert.IsFalse(s.HasPending);
            Assert.IsNull(s.Confirm());
        }

        // --- Phase 6: disclosure feed ---

        [Test]
        public void Feed_CoalescesRapidRepeatsOfSameDetector()
        {
            var f = new DisclosureFeed(16, 750);
            f.Push("drift", "head drift", 0.1f, 0);
            f.Push("drift", "head drift", 0.2f, 100);
            f.Push("drift", "head drift", 0.3f, 200);
            Assert.AreEqual(1, f.Count, "same detector within window coalesces to one entry");
            var recent = f.Recent();
            Assert.AreEqual(3, recent[0].count);
            Assert.AreEqual(0.3f, recent[0].metric, 1e-6f, "keeps the latest metric");
        }

        [Test]
        public void Feed_DistinctDetectorsAreSeparateEntries_NewestFirst()
        {
            var f = new DisclosureFeed(16, 750);
            f.Push("drift", "a", 0.1f, 0);
            f.Push("vection", "b", 0.2f, 100);
            Assert.AreEqual(2, f.Count);
            Assert.AreEqual("vection", f.Recent()[0].detector, "newest first");
        }

        [Test]
        public void Feed_SameDetectorAfterWindowIsANewEntry()
        {
            var f = new DisclosureFeed(16, 750);
            f.Push("drift", "a", 0.1f, 0);
            f.Push("drift", "a", 0.2f, 1000); // > 750 ms later
            Assert.AreEqual(2, f.Count);
        }

        [Test]
        public void Feed_RingBufferIsBounded()
        {
            var f = new DisclosureFeed(4, 0); // no coalescing (window 0)
            for (int i = 0; i < 10; i++)
            {
                f.Push("d" + i, "r", i, i * 1000);
            }

            Assert.AreEqual(4, f.Count, "capacity bounds the buffer");
            Assert.AreEqual("d9", f.Recent()[0].detector, "newest retained");
        }

        // --- Phase 6: notice JSON encoder (safety-log forwarding) ---

        [Test]
        public void Encoder_ProducesValidJsonAndEscapes()
        {
            string json = DisclosureBackendForwarder.EncodeNotice("drift", "he said \"hi\"\n", 0.25f);
            StringAssert.Contains("\"type\":\"disclosure\"", json);
            StringAssert.Contains("\"detector\":\"drift\"", json);
            StringAssert.Contains("\\\"hi\\\"", json, "quotes escaped");
            StringAssert.Contains("\\n", json, "newline escaped");
            StringAssert.Contains("\"metric\":0.25", json);
        }

        [Test]
        public void Encoder_HandlesNullReasonWithoutThrowing()
        {
            string json = DisclosureBackendForwarder.EncodeNotice("d", null, 0f);
            StringAssert.Contains("\"reason\":\"\"", json);
        }
    }
}
