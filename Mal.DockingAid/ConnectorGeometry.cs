using System;
using Sandbox.Definitions;
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
        // Default mating direction in local block space: -Z, matching SE's
        // unspecified ConnectDirection on the standard Connector. The vanilla
        // Structural Platform Connector is the only stock block that overrides
        // this (to (0, -1, 0), mating through its underside); modded connectors
        // can do the same.
        static readonly Vector3D DefaultConnectDirection = new Vector3D(0, 0, -1);

        public static Vector3D MatingPosition(IMyShipConnector c)
        {
            // Mating face is offset from the block centre along the mate axis
            // by roughly half the cube depth (0.25 m small, 1.25 m large) -
            // close enough for the alignment readouts.
            var halfDepth = c.CubeGrid.GridSize * 0.5;
            return c.WorldMatrix.Translation + MateAxis(c) * halfDepth;
        }

        /// <summary>
        ///     World-space unit vector pointing outward through the mating face.
        ///     Falls back to local -Z (= WorldMatrix.Forward) when the
        ///     definition isn't a <see cref="MyShipConnectorDefinition"/>.
        ///
        ///     SE composes its own mating axis in MyShipConnector.CreateConstraint as
        ///         a = CD.X * WorldMatrix.Left + CD.Y * WorldMatrix.Up + CD.Z * WorldMatrix.Forward
        ///     (Left not Right for X; Forward not Backward for Z - a quirky
        ///     basis that only applies to ConnectDirection). That `a` points
        ///     INWARD from the mating face into the block; the outward face
        ///     normal is its negation, which is what every consumer here wants.
        /// </summary>
        public static Vector3D MateAxis(IMyShipConnector c)
        {
            var cd = GetLocalConnectDirection(c);
            var m = c.WorldMatrix;
            // -a = -CD.X*Left - CD.Y*Up - CD.Z*Forward
            //    = +CD.X*Right - CD.Y*Up - CD.Z*Forward
            // The zero-guards keep us NaN-clean against the test fixture's
            // degenerate Right/Up rows; runtime matrices never trip them.
            var result = Vector3D.Zero;
            if (cd.X != 0.0) result += cd.X * m.Right;
            if (cd.Y != 0.0) result -= cd.Y * m.Up;
            if (cd.Z != 0.0) result -= cd.Z * m.Forward;
            return result;
        }

        /// <summary>
        ///     World-space "mating roll reference" axis, perpendicular to
        ///     <see cref="MateAxis"/>. The 4-fold mating-roll fold in
        ///     DockingAlignment means any perpendicular choice gives the same
        ///     status; we pick the local cardinal axis most perpendicular to
        ///     ConnectDirection, mapped to its standard world axis (so for the
        ///     usual CD along ±Z we get local +Y = WorldMatrix.Up, matching
        ///     the pre-ConnectDirection-aware behaviour).
        /// </summary>
        public static Vector3D MateUp(IMyShipConnector c)
        {
            var cd = GetLocalConnectDirection(c);
            var m = c.WorldMatrix;
            double ax = Math.Abs(cd.X);
            double ay = Math.Abs(cd.Y);
            double az = Math.Abs(cd.Z);
            if (ay <= ax && ay <= az) return m.Up;
            if (az <= ax) return m.Backward;
            return m.Right;
        }

        static Vector3D GetLocalConnectDirection(IMyShipConnector c)
        {
            var slim = c.SlimBlock;
            if (slim == null) return DefaultConnectDirection;
            var def = slim.BlockDefinition as MyShipConnectorDefinition;
            if (def == null) return DefaultConnectDirection;
            var d = def.ConnectDirection;
            if (d.LengthSquared() < 1e-6f) return DefaultConnectDirection;
            return new Vector3D(d.X, d.Y, d.Z);
        }
    }
}
