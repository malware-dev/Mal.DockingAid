using NUnit.Framework;

namespace Mal.DockingAid.Tests.Tests
{
    [TestFixture]
    public class DockingTargetingComponentTests
    {
        // Priority ladder used by TryGetCurrent to pick the dominant report
        // across all source connectors on the same construct as the LCD.
        // Locked beats Tracking beats NoTargetInRange beats NoSourceAntenna.

        [Test]
        public void Locked_outranks_tracking()
        {
            Assert.That(
                DockingTargetingComponent.PriorityOf(DockingDisplayState.Locked),
                Is.GreaterThan(DockingTargetingComponent.PriorityOf(DockingDisplayState.Tracking)));
        }

        [Test]
        public void Tracking_outranks_no_target_in_range()
        {
            Assert.That(
                DockingTargetingComponent.PriorityOf(DockingDisplayState.Tracking),
                Is.GreaterThan(DockingTargetingComponent.PriorityOf(DockingDisplayState.NoTargetInRange)));
        }

        [Test]
        public void No_target_in_range_outranks_no_source_antenna()
        {
            Assert.That(
                DockingTargetingComponent.PriorityOf(DockingDisplayState.NoTargetInRange),
                Is.GreaterThan(DockingTargetingComponent.PriorityOf(DockingDisplayState.NoSourceAntenna)));
        }

        [Test]
        public void Priority_ladder_is_strictly_increasing_no_states_tie()
        {
            // Each rung must be a distinct integer so TryGetCurrent's
            // tiebreak (later tick wins) only fires for genuine same-state
            // duplicates, never accidentally between adjacent states.
            int locked = DockingTargetingComponent.PriorityOf(DockingDisplayState.Locked);
            int tracking = DockingTargetingComponent.PriorityOf(DockingDisplayState.Tracking);
            int noTarget = DockingTargetingComponent.PriorityOf(DockingDisplayState.NoTargetInRange);
            int noAnt = DockingTargetingComponent.PriorityOf(DockingDisplayState.NoSourceAntenna);

            Assert.That(new[] { locked, tracking, noTarget, noAnt },
                Is.Unique, "priorities must not collide");
            Assert.That(locked, Is.GreaterThan(tracking));
            Assert.That(tracking, Is.GreaterThan(noTarget));
            Assert.That(noTarget, Is.GreaterThan(noAnt));
        }

        [TestCase(DockingDisplayState.Locked)]
        [TestCase(DockingDisplayState.Tracking)]
        [TestCase(DockingDisplayState.NoTargetInRange)]
        [TestCase(DockingDisplayState.NoSourceAntenna)]
        public void Priority_is_positive_for_every_known_state(DockingDisplayState s)
        {
            // Non-positive priorities would fall below the int.MinValue
            // sentinel TryGetCurrent uses for "no winner yet" — defending
            // the contract that any reported state always beats no-state.
            Assert.That(DockingTargetingComponent.PriorityOf(s), Is.GreaterThan(0));
        }
    }
}
