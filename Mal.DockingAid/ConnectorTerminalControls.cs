using System.Collections.Generic;
using System.Text;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces.Terminal;
using VRage.Utils;

namespace Mal.DockingAid
{
    public static class ConnectorTerminalControls
    {
        public const string CheckboxId = "MalDockingAid_UsedForDocking";
        public const string SliderId = "MalDockingAid_DetectionRange";

        const string ParkingAnchorId = "EnableParking";

        static bool _done;

        public static void DoOnce()
        {
            if (_done) return;
            _done = true;

            CreateCheckbox();
            CreateSlider();

            MyAPIGateway.TerminalControls.CustomControlGetter += OnCustomControls;
        }

        static void CreateCheckbox()
        {
            var checkbox = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlCheckbox, IMyShipConnector>(CheckboxId);
            checkbox.Title = MyStringId.GetOrCompute("Used for docking");
            checkbox.Tooltip = MyStringId.GetOrCompute(
                "When set, this connector is eligible to drive the docking-aid LCD app target detection.");
            checkbox.OnText = MyStringId.GetOrCompute("On");
            checkbox.OffText = MyStringId.GetOrCompute("Off");
            checkbox.Getter = ConnectorState.GetUsedForDocking;
            checkbox.Setter = ConnectorState.SetUsedForDocking;
            checkbox.SupportsMultipleBlocks = true;

            MyAPIGateway.TerminalControls.AddControl<IMyShipConnector>(checkbox);
        }

        static void CreateSlider()
        {
            var slider = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlSlider, IMyShipConnector>(SliderId);
            slider.Title = MyStringId.GetOrCompute("Docking detection range");
            slider.Tooltip = MyStringId.GetOrCompute(
                "Maximum distance to scan for valid docking targets, in metres.");
            slider.SetLimits(ConnectorState.MinDetectionRange, ConnectorState.MaxDetectionRange);
            slider.Getter = ConnectorState.GetDetectionRange;
            slider.Setter = ConnectorState.SetDetectionRange;
            slider.Writer = WriteRange;
            slider.SupportsMultipleBlocks = true;

            MyAPIGateway.TerminalControls.AddControl<IMyShipConnector>(slider);
        }

        static void WriteRange(IMyTerminalBlock block, StringBuilder sb)
        {
            sb.Append(ConnectorState.GetDetectionRange(block).ToString("F1")).Append(" m");
        }

        static void OnCustomControls(IMyTerminalBlock block, List<IMyTerminalControl> controls)
        {
            if (!(block is IMyShipConnector)) return;

            // Place "Used for docking" right after vanilla "Use for parking",
            // and "Docking detection range" right after that.
            InsertAfter(controls, CheckboxId, ParkingAnchorId);
            InsertAfter(controls, SliderId, CheckboxId);
        }

        static void InsertAfter(List<IMyTerminalControl> controls, string controlId, string anchorId)
        {
            int controlIdx = -1;
            int anchorIdx = -1;

            for (int i = 0; i < controls.Count; i++)
            {
                var id = controls[i].Id;
                if (id == controlId) controlIdx = i;
                else if (id == anchorId) anchorIdx = i;
            }

            if (controlIdx < 0 || anchorIdx < 0) return;
            int targetIdx = anchorIdx + 1;
            if (controlIdx == targetIdx) return;

            var ctrl = controls[controlIdx];
            controls.RemoveAt(controlIdx);
            if (controlIdx < anchorIdx) anchorIdx--;
            controls.Insert(anchorIdx + 1, ctrl);
        }
    }
}
