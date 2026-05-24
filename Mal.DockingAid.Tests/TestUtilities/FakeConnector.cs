using FakeItEasy;
using Sandbox.ModAPI;
using VRage.Game.ModAPI;
using VRageMath;

namespace Mal.DockingAid.Tests.TestUtilities
{
    /// <summary>
    ///     Builds an <see cref="IMyShipConnector"/> mock backed by FakeItEasy
    ///     with just enough surface to drive ConnectorGeometry, DockingAlignment,
    ///     and DockingProjection: a world matrix, a cube grid with a configured
    ///     GridSize, and an entity id for tests that distinguish source vs
    ///     target. Anything else returns FakeItEasy defaults.
    /// </summary>
    public static class FakeConnector
    {
        public const float LargeGridSize = 2.5f;
        public const float SmallGridSize = 0.5f;

        public static IMyShipConnector At(Vector3D position, Vector3D forward, Vector3D up,
            float gridSize = LargeGridSize, long entityId = 0)
        {
            var connector = A.Fake<IMyShipConnector>();
            var grid = A.Fake<IMyCubeGrid>();
            A.CallTo(() => grid.GridSize).Returns(gridSize);

            // Build a right-handed orthonormal matrix from forward + up. SE /
            // VRageMath follows the DirectX RH convention: Right = Forward × Up
            // (you can verify from the Vector3D.Right/Up/Forward constants:
            // Forward = (0,0,-1), Up = (0,1,0), Right = (1,0,0); and indeed
            // Cross(Forward, Up) = (1,0,0)). The earlier formula here was
            // Cross(Up, Forward) which is -Right, and it accidentally agreed
            // with a matching sign-flip in ScreenBasis — together they formed
            // a self-consistent bubble that passed every offline test but was
            // mirrored in-game. Fixed in both places.
            var fwd = Vector3D.Normalize(forward);
            var u = Vector3D.Normalize(up - Vector3D.Dot(up, fwd) * fwd);
            var right = Vector3D.Cross(fwd, u);
            var matrix = MatrixD.Identity;
            matrix.Right = right;
            matrix.Up = u;
            matrix.Forward = fwd;
            matrix.Translation = position;

            A.CallTo(() => connector.WorldMatrix).Returns(matrix);
            A.CallTo(() => connector.CubeGrid).Returns(grid);
            A.CallTo(() => connector.EntityId).Returns(entityId);
            return connector;
        }

        // Quick-construction helper for the common "looking down +Z, +Y up"
        // alignment tests don't care about absolute positioning of.
        public static IMyShipConnector Forward(Vector3D position, float gridSize = LargeGridSize)
        {
            return At(position, Vector3D.Forward, Vector3D.Up, gridSize);
        }
    }
}
