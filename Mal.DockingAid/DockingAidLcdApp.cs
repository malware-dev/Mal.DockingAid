using System;
using Sandbox.Game.GameSystems.TextSurfaceScripts;
using Sandbox.ModAPI;
using VRage.Game.GUI.TextPanel;
using VRage.Utils;
using VRageMath;
using IMyTextSurface = Sandbox.ModAPI.Ingame.IMyTextSurface;
using IMyCubeBlock = VRage.Game.ModAPI.Ingame.IMyCubeBlock;
using IMyShipConnector = Sandbox.ModAPI.IMyShipConnector;
using MyShipConnectorStatus = Sandbox.ModAPI.Ingame.MyShipConnectorStatus;

namespace Mal.DockingAid
{
    [MyTextSurfaceScript("Mal_DockingAid", "Docking Aid")]
    public class DockingAidLcdApp : MyTextSurfaceScriptBase
    {
        public override ScriptUpdate NeedsUpdate
        {
            get { return ScriptUpdate.Update10; }
        }

        // SE default font. Smaller intrinsic glyphs than Monospace, which leaves
        // more room for sprites on small LCDs.
        const string FontId = "White";

        // Sprite rotation helpers (radians, CW because screen Y grows down).
        const float HalfPi = (float)(Math.PI / 2.0);
        const float Pi = (float)Math.PI;

        // All sprite pixel sizes AND text scales derive from reticleRadius via
        // ratios in BuildLayout, so the whole indicator scales with the
        // surface. SE's text scale is in pixels, NOT visual size — fixed text
        // scales would render fixed-pixel text that takes proportionally too
        // much of a small LCD or too little of a large one. Layout calibrated
        // for the smallest realistic case: a 256 × 256 small text panel
        // (radius ~90 with the 0.35 multiplier below).
        const float ReticleMultiplier = 0.35f;
        const float ReferenceRadius = 90f;

        // SE's "White" font is ~28 px tall at scale 1.0. Line-height /
        // text-centring offsets need to scale with the text scale, NOT with
        // reticleRadius — using a radius-fraction makes lines overlap on
        // small LCDs because the actual text height grows with the scale.
        const float FontHeightPx = 28f;
        const float FontLineSpacedPx = 32f; // ≈ 14% line gap
        const float FontHalfHeightPx = 14f;

        readonly ClosureTracker _closure = new ClosureTracker();

        // TEMP diagnostic throttle — PER INSTANCE (not static: each text
        // surface throttles independently, otherwise one surface starves the
        // others and we sample the wrong one). Removed once resolved.
        int _lastDiagFrame = -100000;

        DockingAidPalette _palette = DockingAidPalette.Default;
        Color _lastForeground;
        Color _lastBackground;

        public DockingAidLcdApp(IMyTextSurface surface, IMyCubeBlock block, Vector2 size)
            : base(surface, block, size)
        {
        }

        public override void Run()
        {
            try
            {
                base.Run();
                RunCore();
            }
            catch (Exception e)
            {
                MyLog.Default.WriteLineAndConsole("[Mal.DockingAid] LCD app error: " + e.Message + "\n" + e.StackTrace);
            }
        }

        // Surface-derived sizes for one frame. Everything that was a magic
        // pixel constant is a ratio of ReticleRadius here.
        struct Layout
        {
            public Vector2 ViewportTopLeft;
            public Vector2 SurfaceSize;
            public Vector2 Center;
            public float ReticleRadius;
            public float BoreSize;
            public float TipGap;
            public float RailThickness;
            public float NumericTextScale;
            public float StatusTextScale;
            public float BigTextScale;
            public float Margin;
            public float LineHeight;
            public float CenteredTextNudge;
            public float BigTextNudge;
            public float NumericTextHeight;
            public float RollVerticalNudge;
        }

        static Layout BuildLayout(Vector2 surfaceSize, Vector2 textureSize)
        {
            var viewportTopLeft = (textureSize - surfaceSize) * 0.5f;
            float reticleRadius = Math.Min(surfaceSize.X, surfaceSize.Y) * ReticleMultiplier;
            // Text-scale multiplier — 1.0 on a 256-LCD, 2.0 on a 512-LCD, etc.
            float textScale = reticleRadius / ReferenceRadius;

            float numericTextScale = 0.55f * textScale;
            float statusTextScale = 0.95f * textScale;
            float bigTextScale = 1.7f * textScale;

            return new Layout
            {
                ViewportTopLeft = viewportTopLeft,
                SurfaceSize = surfaceSize,
                Center = viewportTopLeft + surfaceSize * 0.5f,
                ReticleRadius = reticleRadius,
                // Sprite pixel sizes — ratios calibrated so a 256-LCD looks
                // right; on a 512-LCD everything is naturally double.
                BoreSize = reticleRadius * 0.16f,
                TipGap = Math.Max(1f, reticleRadius * 0.015f),
                RailThickness = Math.Max(1f, reticleRadius * 0.02f),
                Margin = Math.Max(2f, reticleRadius * 0.05f),
                // Text-related dimensions track the text scale × font glyph
                // metrics, NOT the reticle — line gap must match real text
                // height or numeric lines overlap.
                NumericTextScale = numericTextScale,
                StatusTextScale = statusTextScale,
                BigTextScale = bigTextScale,
                LineHeight = numericTextScale * FontLineSpacedPx,
                CenteredTextNudge = statusTextScale * FontHalfHeightPx,
                BigTextNudge = bigTextScale * FontHalfHeightPx,
                NumericTextHeight = numericTextScale * FontHeightPx,
                // 4 virtual px at the 256-LCD baseline; scales with surface.
                RollVerticalNudge = 4f * textScale,
            };
        }

        void RunCore()
        {
            RefreshPaletteIfNeeded();

            var layout = BuildLayout(Surface.SurfaceSize, Surface.TextureSize);

            using (var frame = Surface.DrawFrame())
            {
                // background
                frame.Add(new MySprite
                {
                    Type = SpriteType.TEXTURE,
                    Data = "SquareSimple",
                    Position = layout.Center,
                    Size = layout.SurfaceSize,
                    Color = _palette.Background,
                    Alignment = TextAlignment.CENTER
                });

                DockingAidSession session;
                if (!DockingAidSession.TryGet(out session))
                {
                    DrawCenteredText(frame, layout, "session not ready", _palette.Faint, layout.StatusTextScale);
                    return;
                }

                var comp = session.Get<DockingTargetingComponent>();
                long lcdGridId = Block.CubeGrid.EntityId;
                DockingDisplayState state;
                IMyShipConnector src, tgt;
                if (comp == null || !comp.TryGetCurrent(lcdGridId, out state, out src, out tgt))
                {
                    // No connector on this construct has been configured for
                    // docking — surface the setup fault so the player knows.
                    ResetTrackingState();
                    DrawCenteredText(frame, layout, "NO DOCKING CONNECTOR", _palette.Faint, layout.StatusTextScale);
                    return;
                }

                if (state == DockingDisplayState.NoSourceAntenna)
                {
                    ResetTrackingState();
                    DrawCenteredText(frame, layout, "NO ANTENNA", _palette.Faint, layout.StatusTextScale);
                    return;
                }

                if (state == DockingDisplayState.NoTargetInRange || tgt == null)
                {
                    ResetTrackingState();
                    DrawCenteredText(frame, layout, "NO TARGET", _palette.Faint, layout.StatusTextScale);
                    return;
                }

                if (state == DockingDisplayState.Locked)
                {
                    DrawLocked(frame, layout, src);
                    return;
                }

                // Tracking — render the indicator.
                //
                // Camera = source connector forward (the mating axis). The
                // image's "up" is the SHIP's up, taken from the controller the
                // local player is ACTIVELY piloting on this construct
                // (ControlledEntity + CanControlShip — automatic, per-client,
                // no config). ScreenBasis projects that into the plane ⊥ the
                // connector forward, so the picture is independent of the
                // connector's build-roll and of where the screen is mounted.
                // No one piloting this construct on this client ⇒ orientation
                // is genuinely undefined ⇒ explicit no-reference state instead
                // of a silently wrong frame. (Cross still uses the host matrix
                // for now — ring correctness first; cross consistency is the
                // next step once this is confirmed in-game.)
                var host = ((VRage.ModAPI.IMyEntity)Block).WorldMatrix;
                var srcMtx = src.WorldMatrix;
                var pilot = ResolvePilotController(src);

                int diagFrame = MyAPIGateway.Session.GameplayFrameCounter;
                bool diagDue =
                    (((src.CustomName ?? "").IndexOf("THIS ONE", System.StringComparison.OrdinalIgnoreCase) >= 0) ||
                     ((tgt.CustomName ?? "").IndexOf("THIS ONE", System.StringComparison.OrdinalIgnoreCase) >= 0))
                    && diagFrame - _lastDiagFrame >= 120;

                if (pilot == null)
                {
                    if (diagDue)
                    {
                        _lastDiagFrame = diagFrame;
                        MyLog.Default.WriteLineAndConsole(
                            "[Mal.DockingAid][DIAG] no pilot controlling src construct ('" +
                            (src.CustomName ?? "") + "') — orientation unavailable");
                        LogConstructControllers(src, null);
                    }
                    DrawNoOrientationRef(frame, layout, src, tgt);
                    return;
                }

                var pilotMtx = pilot.WorldMatrix;
                Vector3D screenRight, screenUp;
                DockingProjection.ScreenBasis(srcMtx.Forward, pilotMtx.Up, pilotMtx.Forward,
                    out screenRight, out screenUp);
                var alignment = DockingAlignment.Compute(src, tgt, screenRight, screenUp);

                if (diagDue)
                {
                    _lastDiagFrame = diagFrame;
                    LogDiag(Block, host, screenRight, screenUp, src, tgt, alignment);
                    MyLog.Default.WriteLineAndConsole(
                        "[Mal.DockingAid][DIAG3] pilot='" + (pilot.CustomName ?? "") +
                        "' pilotUp=" + V(pilotMtx.Up) + " pilotFwd=" + V(pilotMtx.Forward) +
                        " srcFwd=" + V(srcMtx.Forward) +
                        " => sR=" + V(screenRight) + " sU=" + V(screenUp));
                    LogConstructControllers(src, pilot);
                }

                var closure = _closure.Update(
                    tgt.EntityId, alignment.Range, MyAPIGateway.Session.GameplayFrameCounter);
                bool isReady = src.Status == MyShipConnectorStatus.Connectable;
                var indicatorColor = isReady ? _palette.Accent : DockingAlignment.ColorFor(alignment, _palette);

                // Brightness follows attention: anything the pilot actively
                // flies toward gets the bright status colour; pure static
                // reference recedes to dim Chrome. So the projected TARGET
                // ring, the pitch/yaw cross, and the live roll chevrons are
                // bright; only the fixed reticle and the static reference
                // chevrons + rails are Chrome. (Earlier this dimmed the target
                // ring too — wrong: it's the single most important thing to
                // see, especially on small LCDs.)
                DrawReticle(frame, layout, _palette.Chrome);
                DrawPitchYawCross(frame, layout, alignment, indicatorColor, _palette.Chrome);
                DrawTargetRing(frame, layout, src, tgt, screenRight, screenUp, indicatorColor);
                DrawRollChevronPair(frame, layout, 0.0, _palette.Chrome);
                DrawRollChevronPair(frame, layout, alignment.RollRadians, indicatorColor);
                DrawNumerics(frame, layout, alignment, closure, src, _palette);
                if (isReady)
                    DrawCenteredBigText(frame, layout, "READY", _palette.Accent);
            }
        }

        // TEMP diagnostic: dumps the real host/connector frames, the derived
        // screen frame, and the projected lateral/roll terms for the tracked
        // pair (throttled ~2 s). Lets us read the actual cockpit surface
        // numbers from the SE log instead of guessing the convention. Removed
        // once the frame derivation is fixed from this data.
        static void LogDiag(IMyCubeBlock block, MatrixD host,
            Vector3D sR, Vector3D sU, IMyShipConnector src, IMyShipConnector tgt,
            AlignmentData a)
        {
            try
            {
                var sM = src.WorldMatrix;
                var tM = tgt.WorldMatrix;
                var rel = ConnectorGeometry.MatingPosition(tgt) - ConnectorGeometry.MatingPosition(src);
                double depth = Vector3D.Dot(rel, sM.Forward);
                double sx = Vector3D.Dot(rel, sR);
                double sy = Vector3D.Dot(rel, sU);
                double tUR = Vector3D.Dot(tM.Up, sM.Right);
                double tUU = Vector3D.Dot(tM.Up, sM.Up);
                MyLog.Default.WriteLineAndConsole(
                    "[Mal.DockingAid][DIAG] host=" + block.GetType().Name +
                    " | hF=" + V(host.Forward) + " hR=" + V(host.Right) + " hU=" + V(host.Up) +
                    " | sR=" + V(sR) + " sU=" + V(sU) +
                    " | srcF=" + V(sM.Forward) + " srcR=" + V(sM.Right) + " srcU=" + V(sM.Up) +
                    " | tgtF=" + V(tM.Forward) + " tgtU=" + V(tM.Up) +
                    " | rel=" + V(rel) + " depth=" + depth.ToString("F2") +
                    " sx=" + sx.ToString("F3") + " sy=" + sy.ToString("F3") +
                    " | tUR=" + tUR.ToString("F3") + " tUU=" + tUU.ToString("F3") +
                    " roll=" + a.RollRadians.ToString("F3"));

                // Identity + physics: proves whether the tracked src/tgt are
                // on the grids we expect, and whether src's grid is actually
                // rotating while the player rolls. A frozen src world-matrix
                // during a real roll => the aid is locked to a connector that
                // isn't on the maneuvering ship (not a roll-math bug).
                string srcGn = "?"; long srcGid = 0;
                VRageMath.Vector3 srcAng = VRageMath.Vector3.Zero;
                VRageMath.Vector3 srcLin = VRageMath.Vector3.Zero;
                string tgtGn = "?"; long tgtGid = 0;
                string lcdGn = "?"; long lcdGid = 0;
                try
                {
                    var sg = src.CubeGrid;
                    if (sg != null) srcGn = sg.CustomName;
                    var sge = sg as VRage.ModAPI.IMyEntity;
                    if (sge != null)
                    {
                        srcGid = sge.EntityId;
                        if (sge.Physics != null)
                        {
                            srcAng = sge.Physics.AngularVelocity;
                            srcLin = sge.Physics.LinearVelocity;
                        }
                    }
                }
                catch { }
                try
                {
                    var tg = tgt.CubeGrid;
                    if (tg != null) tgtGn = tg.CustomName;
                    var tge = tg as VRage.ModAPI.IMyEntity;
                    if (tge != null) tgtGid = tge.EntityId;
                }
                catch { }
                try
                {
                    var lgn = block.CubeGrid as VRage.Game.ModAPI.IMyCubeGrid;
                    if (lgn != null) lcdGn = lgn.CustomName;
                    var lge = block.CubeGrid as VRage.ModAPI.IMyEntity;
                    if (lge != null) lcdGid = lge.EntityId;
                }
                catch { }
                MyLog.Default.WriteLineAndConsole(
                    "[Mal.DockingAid][DIAG2] src='" + (src.CustomName ?? "") + "'#" + src.EntityId +
                    " srcGrid='" + srcGn + "'#" + srcGid +
                    " srcAngVel=" + V(srcAng) + " srcLinVel=" + V(srcLin) +
                    " | tgt='" + (tgt.CustomName ?? "") + "'#" + tgt.EntityId +
                    " tgtGrid='" + tgtGn + "'#" + tgtGid +
                    " | lcdGrid='" + lcdGn + "'#" + lcdGid);
            }
            catch (Exception e)
            {
                MyLog.Default.WriteLineAndConsole("[Mal.DockingAid][DIAG] error " + e.Message);
            }
        }

        static string V(Vector3D v)
        {
            return v.X.ToString("F2") + "," + v.Y.ToString("F2") + "," + v.Z.ToString("F2");
        }

        static string V(VRageMath.Vector3 v)
        {
            return v.X.ToString("F2") + "," + v.Y.ToString("F2") + "," + v.Z.ToString("F2");
        }

        // Single seam for the "leaving Tracking" reset: the closure smoother
        // only means anything while a target is locked in. Every non-Tracking
        // exit branch in RunCore calls this.
        void ResetTrackingState()
        {
            _closure.Reset();
        }

        void RefreshPaletteIfNeeded()
        {
            var fg = Surface.ScriptForegroundColor;
            var bg = Surface.ScriptBackgroundColor;
            if (fg != _lastForeground || bg != _lastBackground)
            {
                _palette = DockingAidPalette.From(fg, bg);
                _lastForeground = fg;
                _lastBackground = bg;
            }
        }

        // ── Drawing ─────────────────────────────────────────────────────────

        static void DrawReticle(MySpriteDrawFrame frame, Layout layout, Color color)
        {
            frame.Add(new MySprite
            {
                Type = SpriteType.TEXTURE,
                Data = "CircleHollow",
                Position = layout.Center,
                Size = new Vector2(layout.ReticleRadius * 2f, layout.ReticleRadius * 2f),
                Color = color,
                Alignment = TextAlignment.CENTER
            });
        }

        static void DrawCenteredBigText(MySpriteDrawFrame frame, Layout layout, string text, Color color)
        {
            // Text origin sits roughly on the baseline; nudge up so it
            // visually centres on the reticle middle.
            frame.Add(new MySprite
            {
                Type = SpriteType.TEXT,
                Data = text,
                Position = layout.Center - new Vector2(0f, layout.BigTextNudge),
                RotationOrScale = layout.BigTextScale,
                Color = color,
                Alignment = TextAlignment.CENTER,
                FontId = FontId
            });
        }

        void DrawLocked(MySpriteDrawFrame frame, Layout layout, IMyShipConnector src)
        {
            DrawReticle(frame, layout, _palette.Accent);
            DrawCenteredBigText(frame, layout, "LOCKED", _palette.Accent);
            DrawConnectorLabel(frame, layout, NameOf(src), _palette.Body);
        }

        // The controller the LOCAL player is actively piloting, but only if
        // it's a real pilot seat (CanControlShip — no passenger seats) AND on
        // the same mechanical construct as the source connector (sitting in a
        // different ship's cockpit must not hijack this aid's orientation).
        // Same pattern WarpVisualComponent uses; per-client by design.
        static Sandbox.ModAPI.IMyShipController ResolvePilotController(IMyShipConnector src)
        {
            if (src == null) return null;
            var session = MyAPIGateway.Session;
            if (session == null || session.Player == null) return null;
            var ctrl = session.Player.Controller;
            if (ctrl == null) return null;
            var controlled = ctrl.ControlledEntity;
            if (controlled == null) return null;
            var cockpit = controlled.Entity as Sandbox.ModAPI.IMyShipController;
            if (cockpit == null || !cockpit.CanControlShip) return null;
            var cg = cockpit.CubeGrid;
            var sg = src.CubeGrid;
            if (cg == null || sg == null || !cg.IsSameConstructAs(sg)) return null;
            return cockpit;
        }

        // TEMP diagnostic: dumps every ship controller on the source
        // construct with the SE flags that actually disambiguate "who is
        // commanding the ship" (IsUnderControl) vs. our current local-seat
        // pick. Reveals SE's real behaviour with multiple seated players so
        // the resolution rule comes from data, not assumption. Removed once
        // the rule is settled.
        static void LogConstructControllers(IMyShipConnector src,
            Sandbox.ModAPI.IMyShipController picked)
        {
            try
            {
                var grid = src.CubeGrid;
                if (grid == null) return;
                var grids = new System.Collections.Generic.List<VRage.Game.ModAPI.IMyCubeGrid>();
                var group = grid.GetGridGroup(VRage.Game.ModAPI.GridLinkTypeEnum.Mechanical);
                if (group != null) group.GetGrids(grids); else grids.Add(grid);

                var blocks = new System.Collections.Generic.List<VRage.Game.ModAPI.IMySlimBlock>();
                for (int gi = 0; gi < grids.Count; gi++)
                {
                    blocks.Clear();
                    grids[gi].GetBlocks(blocks, b => b.FatBlock is Sandbox.ModAPI.IMyShipController);
                    for (int bi = 0; bi < blocks.Count; bi++)
                    {
                        var c = blocks[bi].FatBlock as Sandbox.ModAPI.IMyShipController;
                        if (c == null) continue;
                        MyLog.Default.WriteLineAndConsole(
                            "[Mal.DockingAid][DIAGC] '" + (c.CustomName ?? "") +
                            "' grid='" + (c.CubeGrid != null ? c.CubeGrid.CustomName : "?") +
                            "' underControl=" + c.IsUnderControl +
                            " canControl=" + c.CanControlShip +
                            " main=" + c.IsMainCockpit +
                            (object.ReferenceEquals(c, picked) ? "  <== PICKED(local-seat)" : ""));
                    }
                }
            }
            catch (System.Exception e)
            {
                MyLog.Default.WriteLineAndConsole("[Mal.DockingAid][DIAGC] error " + e.Message);
            }
        }

        // Nobody on this client is piloting the source construct, so "up" is
        // undefined — show the still-valid range/closure/target instead of a
        // ring drawn in a meaningless orientation.
        void DrawNoOrientationRef(MySpriteDrawFrame frame, Layout layout,
            IMyShipConnector src, IMyShipConnector tgt)
        {
            double range = (ConnectorGeometry.MatingPosition(tgt)
                            - ConnectorGeometry.MatingPosition(src)).Length();
            float closure = _closure.Update(
                tgt.EntityId, range, MyAPIGateway.Session.GameplayFrameCounter);

            DrawReticle(frame, layout, _palette.Chrome);
            DrawText(frame,
                layout.ViewportTopLeft + new Vector2(layout.Margin, layout.Margin),
                "RNG " + range.ToString("F2") + " m",
                _palette.Body, layout.NumericTextScale, TextAlignment.LEFT);
            DrawText(frame,
                layout.ViewportTopLeft + new Vector2(layout.SurfaceSize.X - layout.Margin, layout.Margin),
                "CLO " + closure.ToString("+0.00;-0.00; 0.00") + " m/s",
                _palette.Body, layout.NumericTextScale, TextAlignment.RIGHT);
            DrawCenteredText(frame, layout, "NO PILOT REFERENCE", _palette.Faint, layout.StatusTextScale);
            DrawConnectorLabel(frame, layout, NameOf(src), _palette.Body);
        }

        static string NameOf(IMyShipConnector c)
        {
            return c.CustomName ?? c.DisplayNameText ?? "(unnamed)";
        }

        // Single line at the bottom-centre of the surface — shows which of the
        // ship's docking connectors is currently driving the indicator.
        static void DrawConnectorLabel(MySpriteDrawFrame frame, Layout layout, string name, Color color)
        {
            var pos = layout.ViewportTopLeft + new Vector2(
                layout.SurfaceSize.X * 0.5f,
                layout.SurfaceSize.Y - layout.Margin - layout.NumericTextHeight);
            DrawText(frame, pos, name, color, layout.NumericTextScale, TextAlignment.CENTER);
        }

        static void DrawPitchYawCross(MySpriteDrawFrame frame, Layout layout,
            AlignmentData a, Color indicatorColor, Color railColor)
        {
            float diameter = layout.ReticleRadius * 2f;

            // Vertical pitch rail through centre
            frame.Add(new MySprite
            {
                Type = SpriteType.TEXTURE,
                Data = "SquareSimple",
                Position = layout.Center,
                Size = new Vector2(layout.RailThickness, diameter),
                Color = railColor,
                Alignment = TextAlignment.CENTER
            });
            // Horizontal yaw rail through centre
            frame.Add(new MySprite
            {
                Type = SpriteType.TEXTURE,
                Data = "SquareSimple",
                Position = layout.Center,
                Size = new Vector2(diameter, layout.RailThickness),
                Color = railColor,
                Alignment = TextAlignment.CENTER
            });

            // Pitch notch — pair of AH_BoreSight chevrons flanking the vertical
            // rail (> <), tips pointing at the rail at the pitch position.
            // Positive pitch component = target axis is above source forward
            // (in cockpit frame) → notch above centre.
            float notchY = layout.Center.Y - (float)(a.PitchComponent * layout.ReticleRadius);
            notchY = MathHelper.Clamp(notchY,
                layout.Center.Y - layout.ReticleRadius,
                layout.Center.Y + layout.ReticleRadius);
            float boreOff = layout.BoreSize * 0.5f + layout.TipGap;
            // Left chevron — default ">" points right at the rail
            frame.Add(new MySprite
            {
                Type = SpriteType.TEXTURE,
                Data = "AH_BoreSight",
                Position = new Vector2(layout.Center.X - boreOff, notchY),
                Size = new Vector2(layout.BoreSize, layout.BoreSize),
                Color = indicatorColor,
                Alignment = TextAlignment.CENTER
            });
            // Right chevron — rotated 180° → "<" points left at the rail
            frame.Add(new MySprite
            {
                Type = SpriteType.TEXTURE,
                Data = "AH_BoreSight",
                Position = new Vector2(layout.Center.X + boreOff, notchY),
                Size = new Vector2(layout.BoreSize, layout.BoreSize),
                Color = indicatorColor,
                RotationOrScale = Pi,
                Alignment = TextAlignment.CENTER
            });

            // Yaw notch — pair of AH_BoreSight chevrons flanking the horizontal
            // rail (v above, ^ below), tips pointing at the rail at the yaw
            // position.
            float notchX = layout.Center.X + (float)(a.YawComponent * layout.ReticleRadius);
            notchX = MathHelper.Clamp(notchX,
                layout.Center.X - layout.ReticleRadius,
                layout.Center.X + layout.ReticleRadius);
            // Top chevron — rotated 90° CW → "v" points down at the rail
            frame.Add(new MySprite
            {
                Type = SpriteType.TEXTURE,
                Data = "AH_BoreSight",
                Position = new Vector2(notchX, layout.Center.Y - boreOff),
                Size = new Vector2(layout.BoreSize, layout.BoreSize),
                Color = indicatorColor,
                RotationOrScale = HalfPi,
                Alignment = TextAlignment.CENTER
            });
            // Bottom chevron — rotated 270° CW (-90°) → "^" points up at the rail
            frame.Add(new MySprite
            {
                Type = SpriteType.TEXTURE,
                Data = "AH_BoreSight",
                Position = new Vector2(notchX, layout.Center.Y + boreOff),
                Size = new Vector2(layout.BoreSize, layout.BoreSize),
                Color = indicatorColor,
                RotationOrScale = -HalfPi,
                Alignment = TextAlignment.CENTER
            });
        }

        // Renders the target connector's mating ring as a single CircleHollow
        // sprite, scaled into an ellipse (and rotated) to mimic perspective —
        // see DockingProjection for the math. Same sprite type as the reticle,
        // so the two rings read as visually related: concentric and equal-
        // sized means aligned.
        void DrawTargetRing(MySpriteDrawFrame frame, Layout layout,
            IMyShipConnector src, IMyShipConnector tgt,
            Vector3D screenRight, Vector3D screenUp, Color color)
        {
            // Lay the ring out in the caller-resolved screen frame — the same
            // frame the pitch/yaw cross uses — so neither connector build-roll
            // nor cockpit-vs-LCD host can flip it.
            var ring = DockingProjection.Project(
                src, tgt, screenRight, screenUp,
                layout.Center, layout.ReticleRadius, layout.SurfaceSize.Length());

            // If the projected ring sits outside the viewport, swap to an
            // edge-of-screen arrow pointing at it. Same code path catches the
            // behind-camera fallback DockingProjection encodes as a far
            // off-viewport ScreenCenter.
            if (IsOutsideViewport(layout, ring.ScreenCenter))
            {
                DrawOffScreenArrow(frame, layout, ring.ScreenCenter, color);
                return;
            }

            frame.Add(new MySprite
            {
                Type = SpriteType.TEXTURE,
                Data = "CircleHollow",
                Position = ring.ScreenCenter,
                Size = new Vector2(ring.MajorDiameterPx, ring.MinorDiameterPx),
                Color = color,
                RotationOrScale = ring.RotationRadians,
                Alignment = TextAlignment.CENTER
            });
        }

        // Inset around the viewport edge that the off-screen arrow lives in;
        // also doubles as the "is the ring outside the visible area" test.
        // Uses the boresight half-size + a bit of margin so the arrow sits
        // fully inside the screen with its tip pointing at the rim.
        static float OffScreenInset(Layout layout)
        {
            return layout.BoreSize * 0.75f + layout.Margin;
        }

        static bool IsOutsideViewport(Layout layout, Vector2 ringCenter)
        {
            var min = layout.ViewportTopLeft;
            var max = min + layout.SurfaceSize;
            float inset = OffScreenInset(layout);
            return ringCenter.X < min.X + inset
                || ringCenter.X > max.X - inset
                || ringCenter.Y < min.Y + inset
                || ringCenter.Y > max.Y - inset;
        }

        // Draws an AH_PullUp arrow pinned to the viewport edge, pointing
        // toward the off-screen ring. Position = clamp(ringCenter) inside the
        // inset rectangle, so it slides along the edge with the target.
        static void DrawOffScreenArrow(MySpriteDrawFrame frame, Layout layout,
            Vector2 ringCenter, Color color)
        {
            var dir = ringCenter - layout.Center;
            if (dir.LengthSquared() < 1e-4f) return;

            var min = layout.ViewportTopLeft;
            var max = min + layout.SurfaceSize;
            float inset = OffScreenInset(layout);

            float x = MathHelper.Clamp(ringCenter.X, min.X + inset, max.X - inset);
            float y = MathHelper.Clamp(ringCenter.Y, min.Y + inset, max.Y - inset);

            // AH_PullUp default points UP (-Y). atan2(dy, dx) on screen coords
            // gives the CW rotation that aims +X at dir; add π/2 to rotate the
            // up-pointing default to point along dir instead.
            float angle = (float)Math.Atan2(dir.Y, dir.X) + HalfPi;
            float size = layout.BoreSize * 1.5f;

            frame.Add(new MySprite
            {
                Type = SpriteType.TEXTURE,
                Data = "AH_PullUp",
                Position = new Vector2(x, y),
                Size = new Vector2(size, size),
                Color = color,
                RotationOrScale = angle,
                Alignment = TextAlignment.CENTER
            });
        }

        // Draws two AH_BoreSight chevrons flanking the rim at the given roll
        // angle (0 = top, +ve CW): one just inside the rim pointing OUTWARD,
        // one just outside the rim pointing INWARD. Their tips align radially
        // through the rim itself.
        //
        // RollVerticalNudge shifts the pair down a few px to compensate for
        // the visual offset between the AH_BoreSight sprite's bounding box
        // and its drawn chevron content.
        static void DrawRollChevronPair(MySpriteDrawFrame frame, Layout layout,
            double angle, Color color)
        {
            float boreOff = layout.BoreSize * 0.5f + layout.TipGap;
            float rIn = layout.ReticleRadius - boreOff;
            float rOut = layout.ReticleRadius + boreOff;

            float dirX = (float)Math.Sin(angle);
            float dirY = -(float)Math.Cos(angle);

            var nudge = new Vector2(0f, layout.RollVerticalNudge);

            // Inside chevron — sits inside the rim, points outward.
            // Default ">" rotated by (angle - π/2) points along (sinθ, -cosθ).
            frame.Add(new MySprite
            {
                Type = SpriteType.TEXTURE,
                Data = "AH_BoreSight",
                Position = layout.Center + new Vector2(dirX * rIn, dirY * rIn) + nudge,
                Size = new Vector2(layout.BoreSize, layout.BoreSize),
                Color = color,
                RotationOrScale = (float)(angle - Math.PI / 2.0),
                Alignment = TextAlignment.CENTER
            });

            // Outside chevron — sits outside the rim, points inward.
            // Default ">" rotated by (angle + π/2) points along (-sinθ, cosθ).
            frame.Add(new MySprite
            {
                Type = SpriteType.TEXTURE,
                Data = "AH_BoreSight",
                Position = layout.Center + new Vector2(dirX * rOut, dirY * rOut) + nudge,
                Size = new Vector2(layout.BoreSize, layout.BoreSize),
                Color = color,
                RotationOrScale = (float)(angle + Math.PI / 2.0),
                Alignment = TextAlignment.CENTER
            });
        }

        static void DrawNumerics(MySpriteDrawFrame frame, Layout layout,
            AlignmentData a, float closureMps, IMyShipConnector src,
            DockingAidPalette palette)
        {
            // Top-left: range
            DrawText(frame,
                layout.ViewportTopLeft + new Vector2(layout.Margin, layout.Margin),
                "RNG " + a.Range.ToString("F2") + " m",
                palette.Foreground, layout.NumericTextScale, TextAlignment.LEFT);

            // Top-right: closure rate (positive = approaching)
            DrawText(frame,
                layout.ViewportTopLeft + new Vector2(layout.SurfaceSize.X - layout.Margin, layout.Margin),
                "CLO " + closureMps.ToString("+0.00;-0.00; 0.00") + " m/s",
                palette.Foreground, layout.NumericTextScale, TextAlignment.RIGHT);

            // Bottom-centre: source connector name (which one is driving us).
            DrawConnectorLabel(frame, layout, NameOf(src), palette.Body);
        }

        static void DrawText(MySpriteDrawFrame frame, Vector2 position, string text,
            Color color, float scale, TextAlignment alignment)
        {
            frame.Add(new MySprite
            {
                Type = SpriteType.TEXT,
                Data = text,
                Position = position,
                RotationOrScale = scale,
                Color = color,
                Alignment = alignment,
                FontId = FontId
            });
        }

        static void DrawCenteredText(MySpriteDrawFrame frame, Layout layout, string text,
            Color color, float scale)
        {
            // Text origin sits roughly on the baseline; shift up slightly so it
            // visually centres on the reticle middle.
            frame.Add(new MySprite
            {
                Type = SpriteType.TEXT,
                Data = text,
                Position = layout.Center - new Vector2(0f, layout.CenteredTextNudge),
                RotationOrScale = scale,
                Color = color,
                Alignment = TextAlignment.CENTER,
                FontId = FontId
            });
        }
    }
}
