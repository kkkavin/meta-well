using System.Collections.Generic;
using Convai.Runtime.DynamicContext;
using NUnit.Framework;

namespace Convai.Tests.EditMode.Runtime
{
    [TestFixture]
    public class ConvaiDynamicContextTrackerTests
    {
        [Test]
        public void BuildCanonicalContext_EmptyTracker_ReturnsEmptyString()
        {
            var tracker = new ConvaiDynamicContextTracker();

            Assert.AreEqual(string.Empty, tracker.BuildCanonicalContext());
            Assert.IsFalse(tracker.HasTrackedContent);
        }

        [Test]
        public void SetState_PreservesInsertionOrder()
        {
            var tracker = new ConvaiDynamicContextTracker();

            tracker.SetState("Health", "100");
            tracker.SetState("Ammo", "6");

            Assert.AreEqual("Health is 100\nAmmo is 6", tracker.BuildCanonicalContext());
        }

        [Test]
        public void SetState_UpdatingExistingState_DoesNotDuplicateKey()
        {
            var tracker = new ConvaiDynamicContextTracker();

            tracker.SetState("Health", "100");
            tracker.SetState("Ammo", "6");
            tracker.SetState("Health", "50");

            Assert.AreEqual("Health is 50\nAmmo is 6", tracker.BuildCanonicalContext());
        }

        [Test]
        public void SetState_SameValue_ReturnsNoChange()
        {
            var tracker = new ConvaiDynamicContextTracker();

            tracker.SetState("Health", "100");
            ConvaiDynamicContextStateChangeResult result = tracker.SetState("Health", "100");

            Assert.IsFalse(result.HasChanged);
            Assert.AreEqual("Health is 100", tracker.BuildCanonicalContext());
        }

        [Test]
        public void BuildCanonicalContext_StatesAppearBeforeEvents()
        {
            var tracker = new ConvaiDynamicContextTracker();

            tracker.SetState("Health", "100");
            tracker.AddEvent("Door opened");
            tracker.SetState("Ammo", "6");
            tracker.AddEvent("Enemy spotted");

            Assert.AreEqual("Health is 100\nAmmo is 6\nDoor opened\nEnemy spotted", tracker.BuildCanonicalContext());
        }

        [Test]
        public void BuildCanonicalContext_ExcludedStates_AreOmittedButEventsRemain()
        {
            var tracker = new ConvaiDynamicContextTracker();

            tracker.SetState("Health", "100");
            tracker.SetState("Ammo", "6");
            tracker.AddEvent("Door opened");

            Assert.AreEqual(
                "Health is 100\nDoor opened",
                tracker.BuildCanonicalContext(new HashSet<string> { "Ammo" }));
        }

        [Test]
        public void RemoveState_RemovesTrackedLine()
        {
            var tracker = new ConvaiDynamicContextTracker();

            tracker.SetState("Health", "100");
            tracker.SetState("Ammo", "6");
            bool removed = tracker.RemoveState("Health");

            Assert.IsTrue(removed);
            Assert.AreEqual("Ammo is 6", tracker.BuildCanonicalContext());
        }

        [Test]
        public void Reset_ClearsAllTrackedContent()
        {
            var tracker = new ConvaiDynamicContextTracker();

            tracker.SetState("Health", "100");
            tracker.AddEvent("Door opened");
            tracker.Reset();

            Assert.AreEqual(string.Empty, tracker.BuildCanonicalContext());
            Assert.IsFalse(tracker.HasTrackedContent);
        }
    }
}
