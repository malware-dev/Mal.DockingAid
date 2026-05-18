using Mal.DockingAid.Tests.TestUtilities;
using NUnit.Framework;
using VRageMath;

namespace Mal.DockingAid.Tests.Tests
{
    [TestFixture]
    public class ConnectorGeometryTests
    {
        [Test]
        public void Mating_position_is_offset_forward_by_half_grid_size()
        {
            var c = FakeConnector.At(
                position: new Vector3D(10, 20, 30),
                forward: Vector3D.Forward, // -Z
                up: Vector3D.Up,
                gridSize: FakeConnector.LargeGridSize); // 2.5 m → half = 1.25

            var mate = ConnectorGeometry.MatingPosition(c);

            // Vector3D.Forward = (0,0,-1) in SE conventions; mate sits 1.25 m
            // along forward from the connector centre.
            var expected = new Vector3D(10, 20, 30) + Vector3D.Forward * 1.25;
            Assert.That((mate - expected).Length(), Is.LessThan(1e-9));
        }

        [Test]
        public void Mating_position_uses_small_grid_offset_for_small_grid()
        {
            var c = FakeConnector.At(
                position: Vector3D.Zero,
                forward: Vector3D.Forward,
                up: Vector3D.Up,
                gridSize: FakeConnector.SmallGridSize); // 0.5 m → half = 0.25

            var mate = ConnectorGeometry.MatingPosition(c);
            Assert.That((mate - Vector3D.Forward * 0.25).Length(), Is.LessThan(1e-9));
        }

        [Test]
        public void Mating_position_follows_arbitrary_orientation()
        {
            // Connector pointed +X (right) instead of -Z. Mate should walk
            // along the connector's local forward, not along world -Z.
            var c = FakeConnector.At(
                position: Vector3D.Zero,
                forward: new Vector3D(1, 0, 0),
                up: Vector3D.Up,
                gridSize: FakeConnector.LargeGridSize);

            var mate = ConnectorGeometry.MatingPosition(c);
            Assert.That((mate - new Vector3D(1.25, 0, 0)).Length(), Is.LessThan(1e-9));
        }
    }
}
