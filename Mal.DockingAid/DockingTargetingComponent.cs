using System.Collections.Generic;
using Mal.Mods.Utilities;
using Sandbox.ModAPI;
using VRage.ModAPI;
using IMyShipConnector = Sandbox.ModAPI.IMyShipConnector;

namespace Mal.DockingAid
{
    /// <summary>
    ///     Tracks the most useful state to show on each construct's LCDs.
    ///     Each docking-eligible connector reports its own status; the LCD
    ///     enumerates reports for connectors on its construct and picks the
    ///     highest-priority one to display.
    ///
    ///     Priority (high → low): Locked, Tracking, NoTargetInRange,
    ///     NoSourceAntenna. Within the same priority, latest report wins.
    /// </summary>
    public enum DockingDisplayState
    {
        NoSourceAntenna,
        NoTargetInRange,
        Tracking,
        Locked,
    }

    public class DockingTargetingComponent : ModComponent
    {
        struct ReportedState
        {
            public DockingDisplayState Kind;
            public long SourceId;
            public long TargetId;   // 0 when not applicable
            public int Tick;
        }

        // Keyed by source connector EntityId, so multiple connectors on the
        // same ship don't overwrite each other's reports.
        readonly Dictionary<long, ReportedState> _byConnector = new Dictionary<long, ReportedState>();

        public override void Init(ModSession session, ModSession.Builder builder)
        {
        }

        /// <summary>
        ///     Single seam for all docking-state writes. <paramref name="target"/> is
        ///     ignored for <see cref="DockingDisplayState.NoSourceAntenna"/> and
        ///     <see cref="DockingDisplayState.NoTargetInRange"/>; required for
        ///     <see cref="DockingDisplayState.Tracking"/> and
        ///     <see cref="DockingDisplayState.Locked"/>.
        /// </summary>
        public void Report(DockingDisplayState kind, IMyShipConnector source, IMyShipConnector target)
        {
            if (source == null) return;
            bool needsTarget = kind == DockingDisplayState.Tracking || kind == DockingDisplayState.Locked;
            if (needsTarget && target == null) return;

            _byConnector[source.EntityId] = new ReportedState
            {
                Kind = kind,
                SourceId = source.EntityId,
                TargetId = target != null ? target.EntityId : 0L,
                Tick = MyAPIGateway.Session.GameplayFrameCounter,
            };
        }

        /// <summary>
        ///     Removes any prior report for <paramref name="source"/>. Named to fit
        ///     the Report* family on this component — an "idle" connector is one
        ///     with no report at all (e.g. disabled, not configured for docking).
        /// </summary>
        public void ReportIdle(IMyShipConnector source)
        {
            if (source == null) return;
            _byConnector.Remove(source.EntityId);
        }

        /// <summary>
        ///     Returns the best state to display on an LCD on the given grid.
        ///     Returns false if the LCD entity can't be resolved, isn't a grid,
        ///     or no connector on the same construct as the LCD has an active
        ///     report (i.e. nothing's configured for docking).
        /// </summary>
        public bool TryGetCurrent(
            long lcdGridId,
            out DockingDisplayState state,
            out IMyShipConnector source,
            out IMyShipConnector target)
        {
            state = DockingDisplayState.NoTargetInRange;
            source = null;
            target = null;

            IMyEntity lcdEnt;
            if (!MyAPIGateway.Entities.TryGetEntityById(lcdGridId, out lcdEnt)) return false;
            var lcdGrid = lcdEnt as VRage.Game.ModAPI.IMyCubeGrid;
            if (lcdGrid == null) return false;

            int bestPriority = -1;
            int bestTick = int.MinValue;
            ReportedState bestReport = default(ReportedState);
            IMyShipConnector bestSrc = null;

            foreach (var kv in _byConnector)
            {
                IMyEntity srcEnt;
                if (!MyAPIGateway.Entities.TryGetEntityById(kv.Value.SourceId, out srcEnt)) continue;
                var srcConn = srcEnt as IMyShipConnector;
                if (srcConn == null) continue;
                if (!srcConn.CubeGrid.IsSameConstructAs(lcdGrid)) continue;

                int p = PriorityOf(kv.Value.Kind);
                if (p > bestPriority || (p == bestPriority && kv.Value.Tick > bestTick))
                {
                    bestPriority = p;
                    bestTick = kv.Value.Tick;
                    bestReport = kv.Value;
                    bestSrc = srcConn;
                }
            }

            if (bestSrc == null) return false;

            state = bestReport.Kind;
            source = bestSrc;

            if (bestReport.TargetId != 0)
            {
                IMyEntity tgtEnt;
                if (MyAPIGateway.Entities.TryGetEntityById(bestReport.TargetId, out tgtEnt))
                    target = tgtEnt as IMyShipConnector;
            }

            return true;
        }

        // Public so tests can pin the priority ladder. Higher beats lower in
        // TryGetCurrent's selection loop; ties break on later Tick wins.
        public static int PriorityOf(DockingDisplayState s)
        {
            switch (s)
            {
                case DockingDisplayState.Locked: return 4;
                case DockingDisplayState.Tracking: return 3;
                case DockingDisplayState.NoTargetInRange: return 2;
                case DockingDisplayState.NoSourceAntenna: return 1;
                default: return 0;
            }
        }
    }
}
