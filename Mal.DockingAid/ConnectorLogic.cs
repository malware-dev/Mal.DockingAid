using System.Collections.Generic;
using Mal.Mods.Utilities;
using Sandbox.Common.ObjectBuilders;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Ingame;
using Sandbox.ModAPI.Interfaces;
using VRage.Game.Components;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.ObjectBuilders;
using VRageMath;
using IMyShipConnector = Sandbox.ModAPI.IMyShipConnector;
using IMyRadioAntenna = Sandbox.ModAPI.IMyRadioAntenna;
using IMyCubeGrid = VRage.Game.ModAPI.IMyCubeGrid;
using IMySlimBlock = VRage.Game.ModAPI.IMySlimBlock;

namespace Mal.DockingAid
{
    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_ShipConnector), useEntityUpdate: false)]
    public class ConnectorLogic : BlockLogicComponent<IMyShipConnector>
    {
        // cos(135°) — forwards within 45° of being anti-parallel.
        const double FacingCosineThreshold = -0.7071067811865475;

        // Shared across all ConnectorLogic instances. Safe because SE's update
        // loop is single-threaded and Scan() is not reentrant — every entry/exit
        // path either Clear()s the lists or returns before another scan can run.
        static readonly List<MyEntity> _scratchEntities = new List<MyEntity>();
        static readonly List<IMySlimBlock> _scratchBlocks = new List<IMySlimBlock>();
        static readonly List<IMySlimBlock> _scratchSrcAntennas = new List<IMySlimBlock>();
        static readonly List<IMySlimBlock> _scratchTgtAntennas = new List<IMySlimBlock>();

        enum ScanState { Idle, Tracking, Locked }

        ScanState _state = ScanState.Idle;
        long _currentTargetId;

        public override void Init(MyObjectBuilder_EntityBase objectBuilder)
        {
            NeedsUpdate = MyEntityUpdateEnum.BEFORE_NEXT_FRAME;
        }

        public override void UpdateOnceBeforeFrame()
        {
            // Display-only mod: a dedicated server has no LCD renderer and no
            // terminal UI, so the scan loop, the targeting reports, and the
            // terminal control registration all exist solely to feed
            // DockingAidLcdApp / the in-game terminal — neither of which runs
            // on DS. Bail out before doing any of that work.
            if (MyAPIGateway.Utilities.IsDedicated)
            {
                NeedsUpdate = MyEntityUpdateEnum.NONE;
                return;
            }
            ConnectorTerminalControls.DoOnce();
            NeedsUpdate = MyEntityUpdateEnum.EACH_100TH_FRAME;
        }

        public override void UpdateAfterSimulation100()
        {
            if (_state == ScanState.Idle || _state == ScanState.Locked)
                Scan();
        }

        public override void UpdateAfterSimulation10()
        {
            if (_state == ScanState.Tracking)
                Scan();
        }

        void Scan()
        {
            var src = Entity;
            if (src == null) { ClearAndGoIdle(); return; }

            // Player explicitly hasn't enabled this connector — don't surface
            // anything for it, just remove any prior report.
            if (!src.IsWorking || !ConnectorState.GetUsedForDocking(src))
            {
                ClearAndGoIdle();
                return;
            }

            // Locked: report the OtherConnector as the sticky target. Don't search.
            if (src.Status == MyShipConnectorStatus.Connected)
            {
                EnterLocked(src);
                return;
            }

            // From here on, the connector IS configured to dock — every exit
            // path reports a specific reason so the LCD can tell the player
            // why nothing's appearing.
            if (!HasBroadcastingAntenna(src.CubeGrid))
            {
                Report(DockingDisplayState.NoSourceAntenna, src, null);
                SetState(ScanState.Idle, MyEntityUpdateEnum.EACH_100TH_FRAME);
                return;
            }

            NoTargetReason failReason;
            var tgt = FindBestTarget(src, out failReason);
            if (tgt != null)
            {
                EnterTracking(src, tgt);
            }
            else
            {
                ReportNoTarget(src, failReason);
                SetState(ScanState.Idle, MyEntityUpdateEnum.EACH_100TH_FRAME);
            }
        }

        void Report(DockingDisplayState kind, IMyShipConnector source, IMyShipConnector target)
        {
            var comp = TryGetTargetingComponent();
            if (comp != null) comp.Report(kind, source, target);
        }

        void ReportNoTarget(IMyShipConnector source, NoTargetReason reason)
        {
            var comp = TryGetTargetingComponent();
            if (comp != null) comp.ReportNoTarget(source, reason);
        }

        void ReportIdle(IMyShipConnector source)
        {
            var comp = TryGetTargetingComponent();
            if (comp != null) comp.ReportIdle(source);
        }

        // Single seam for the session-resolution preamble both report wrappers
        // share. Returns null if either the session or the component is absent
        // (early game load, world unload mid-frame); callers no-op in that case.
        static DockingTargetingComponent TryGetTargetingComponent()
        {
            DockingAidSession session;
            if (!DockingAidSession.TryGet(out session)) return null;
            return session.Get<DockingTargetingComponent>();
        }

        void SetState(ScanState s, MyEntityUpdateEnum updates)
        {
            if (_state == s) return;
            _state = s;
            NeedsUpdate = updates;
        }

        void EnterTracking(IMyShipConnector src, IMyShipConnector tgt)
        {
            _currentTargetId = tgt.EntityId;
            Report(DockingDisplayState.Tracking, src, tgt);
            SetState(ScanState.Tracking, MyEntityUpdateEnum.EACH_10TH_FRAME);
        }

        void EnterLocked(IMyShipConnector src)
        {
            var lockedTo = src.OtherConnector;
            if (lockedTo == null) return; // shouldn't happen if Status == Connected, but guard anyway
            _currentTargetId = lockedTo.EntityId;
            Report(DockingDisplayState.Locked, src, lockedTo);
            SetState(ScanState.Locked, MyEntityUpdateEnum.EACH_100TH_FRAME);
        }

        void ClearAndGoIdle()
        {
            if (Entity != null)
                ReportIdle(Entity);
            _currentTargetId = 0;
            SetState(ScanState.Idle, MyEntityUpdateEnum.EACH_100TH_FRAME);
        }

        // ── Eligibility ─────────────────────────────────────────────────────

        // Targets are more permissive: vanilla NPC trade stations have
        // "Trading" enabled but won't have our flag, and we want to dock
        // to those too. Either flag qualifies.
        static bool IsEligibleTarget(IMyShipConnector c)
        {
            if (c == null || !c.IsWorking) return false;
            if (c.Status == MyShipConnectorStatus.Connected) return false;
            if (ConnectorState.GetUsedForDocking(c)) return true;
            return IsTradingEnabled(c);
        }

        static bool IsTradingEnabled(IMyShipConnector c)
        {
            var prop = c.GetProperty("Trading");
            if (prop == null) return false;
            var asBool = prop.As<bool>();
            return asBool != null && asBool.GetValue(c);
        }

        // ── Target search ───────────────────────────────────────────────────

        IMyShipConnector FindBestTarget(IMyShipConnector src, out NoTargetReason reason)
        {
            var srcGrid = src.CubeGrid;
            var srcVol = srcGrid.WorldVolume;
            double range = ConnectorState.GetDetectionRange(src);
            var query = new BoundingSphereD(srcVol.Center, srcVol.Radius + range);

            _scratchEntities.Clear();
            MyGamePruningStructure.GetAllTopMostEntitiesInSphere(ref query, _scratchEntities);

            var srcMate = ConnectorGeometry.MatingPosition(src);
            var srcAxis = ConnectorGeometry.MateAxis(src);

            // HasBroadcastingAntenna already verified the source has at least
            // one antenna; collect them once here so AntennasInMutualRange
            // doesn't re-scan the source grid for every candidate.
            _scratchSrcAntennas.Clear();
            srcGrid.GetBlocks(_scratchSrcAntennas, IsBroadcastingAntenna);

            IMyShipConnector best = null;
            double bestDistSq = double.MaxValue;
            double rangeSq = range * range;

            // Track the highest-rank reason any rejected candidate hit, so the
            // LCD can hint why nothing came back. Higher = closer to working;
            // we keep the max because that's the most actionable thing the
            // pilot could fix to get a target.
            NoTargetReason bestReason = NoTargetReason.Unknown;

            for (int i = 0; i < _scratchEntities.Count; i++)
            {
                var grid = _scratchEntities[i] as IMyCubeGrid;
                if (grid == null) continue;
                if (grid.IsSameConstructAs(srcGrid)) continue;
                if (!AnyAntennasInMutualRange(_scratchSrcAntennas, grid))
                {
                    if (bestReason < NoTargetReason.NoAntennaLink)
                        bestReason = NoTargetReason.NoAntennaLink;
                    continue;
                }

                _scratchBlocks.Clear();
                grid.GetBlocks(_scratchBlocks, IsConnectorBlock);

                if (_scratchBlocks.Count == 0) continue;

                bool gridHasEligible = false;
                for (int j = 0; j < _scratchBlocks.Count; j++)
                {
                    var c = _scratchBlocks[j].FatBlock as IMyShipConnector;
                    if (!IsEligibleTarget(c)) continue;
                    gridHasEligible = true;

                    var tgtMate = ConnectorGeometry.MatingPosition(c);
                    var distSq = Vector3D.DistanceSquared(srcMate, tgtMate);
                    if (distSq > rangeSq) continue;

                    var dot = Vector3D.Dot(srcAxis, ConnectorGeometry.MateAxis(c));
                    if (dot > FacingCosineThreshold)
                    {
                        if (bestReason < NoTargetReason.WrongOrientation)
                            bestReason = NoTargetReason.WrongOrientation;
                        continue;
                    }

                    if (distSq < bestDistSq)
                    {
                        bestDistSq = distSq;
                        best = c;
                    }
                }

                if (!gridHasEligible && bestReason < NoTargetReason.NotConfigured)
                    bestReason = NoTargetReason.NotConfigured;
            }

            _scratchEntities.Clear();
            _scratchBlocks.Clear();
            _scratchSrcAntennas.Clear();
            reason = bestReason;
            return best;
        }

        static bool IsConnectorBlock(IMySlimBlock b)
        {
            return b.FatBlock is IMyShipConnector;
        }

        // ── Antenna helpers ─────────────────────────────────────────────────

        static bool HasBroadcastingAntenna(IMyCubeGrid grid)
        {
            _scratchSrcAntennas.Clear();
            grid.GetBlocks(_scratchSrcAntennas, IsBroadcastingAntenna);
            bool has = _scratchSrcAntennas.Count > 0;
            _scratchSrcAntennas.Clear();
            return has;
        }

        // Source antennas are passed in pre-collected; we only scan the target
        // grid here. Avoids re-scanning the source grid for every candidate.
        static bool AnyAntennasInMutualRange(List<IMySlimBlock> srcAntennas, IMyCubeGrid tgt)
        {
            _scratchTgtAntennas.Clear();
            tgt.GetBlocks(_scratchTgtAntennas, IsBroadcastingAntenna);

            bool found = false;
            for (int i = 0; i < srcAntennas.Count && !found; i++)
            {
                var sa = (IMyRadioAntenna)srcAntennas[i].FatBlock;
                var saPos = sa.GetPosition();
                var saR = sa.Radius;
                for (int j = 0; j < _scratchTgtAntennas.Count; j++)
                {
                    var ta = (IMyRadioAntenna)_scratchTgtAntennas[j].FatBlock;
                    var d = Vector3D.Distance(saPos, ta.GetPosition());
                    if (d <= saR && d <= ta.Radius)
                    {
                        found = true;
                        break;
                    }
                }
            }

            _scratchTgtAntennas.Clear();
            return found;
        }

        static bool IsBroadcastingAntenna(IMySlimBlock b)
        {
            var a = b.FatBlock as IMyRadioAntenna;
            return a != null && a.IsWorking && a.IsBroadcasting;
        }
    }
}
