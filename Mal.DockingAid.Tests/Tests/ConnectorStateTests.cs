using NUnit.Framework;

namespace Mal.DockingAid.Tests.Tests
{
    [TestFixture]
    public class ConnectorStateTests
    {
        [Test]
        public void Clamp_pulls_below_minimum_up_to_minimum()
        {
            Assert.That(ConnectorState.ClampDetectionRange(0.5f),
                Is.EqualTo(ConnectorState.MinDetectionRange));
            Assert.That(ConnectorState.ClampDetectionRange(-1000f),
                Is.EqualTo(ConnectorState.MinDetectionRange));
        }

        [Test]
        public void Clamp_pulls_above_maximum_down_to_maximum()
        {
            Assert.That(ConnectorState.ClampDetectionRange(100f),
                Is.EqualTo(ConnectorState.MaxDetectionRange));
            Assert.That(ConnectorState.ClampDetectionRange(float.MaxValue),
                Is.EqualTo(ConnectorState.MaxDetectionRange));
        }

        [Test]
        public void Clamp_passes_in_band_values_through_unchanged()
        {
            Assert.That(ConnectorState.ClampDetectionRange(20f), Is.EqualTo(20f));
            Assert.That(ConnectorState.ClampDetectionRange(ConnectorState.DefaultDetectionRange),
                Is.EqualTo(ConnectorState.DefaultDetectionRange));
        }

        [Test]
        public void Clamp_holds_at_exact_bounds()
        {
            Assert.That(ConnectorState.ClampDetectionRange(ConnectorState.MinDetectionRange),
                Is.EqualTo(ConnectorState.MinDetectionRange));
            Assert.That(ConnectorState.ClampDetectionRange(ConnectorState.MaxDetectionRange),
                Is.EqualTo(ConnectorState.MaxDetectionRange));
        }

        [Test]
        public void Default_detection_range_sits_inside_the_clamp_band()
        {
            Assert.That(ConnectorState.DefaultDetectionRange,
                Is.InRange(ConnectorState.MinDetectionRange, ConnectorState.MaxDetectionRange));
        }
    }
}
