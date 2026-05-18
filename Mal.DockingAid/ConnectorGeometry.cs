using VRageMath;
using IMyShipConnector = Sandbox.ModAPI.IMyShipConnector;

namespace Mal.DockingAid
{
    /// <summary>
    ///     Pure geometry helpers for ship connectors. Lives outside ConnectorLogic
    ///     so the LCD app can consume mating-face math without reaching across the
    ///     per-block-logic boundary.
    /// </summary>
    public static class ConnectorGeometry
    {
        public static Vector3D MatingPosition(IMyShipConnector c)
        {
            // Mating face is forward of the block centre by roughly half the cube depth.
            // Approximate as 0.5 × grid size (0.25 m small, 1.25 m large) — close enough
            // for the alignment readouts; precise mating math can refine later.
            var halfDepth = c.CubeGrid.GridSize * 0.5;
            return c.WorldMatrix.Translation + c.WorldMatrix.Forward * halfDepth;
        }
    }
}
