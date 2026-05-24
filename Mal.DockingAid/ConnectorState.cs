using System;
using Sandbox.Game.EntityComponents;
using Sandbox.ModAPI;
using VRage.ModAPI;
using VRage.Utils;
using MyIni = VRage.Game.ModAPI.Ingame.Utilities.MyIni;

namespace Mal.DockingAid
{
    /// <summary>
    ///     Per-connector state persisted in the entity's <see cref="MyModStorageComponent"/>.
    ///     A single Guid keys a <see cref="MyIni"/> blob; each piece of state is its own
    ///     key under the <see cref="IniSection"/> section. Adding a new field is just a
    ///     new public Get/Set pair — the SBC and storage layout don't change.
    /// </summary>
    public static class ConnectorState
    {
        // Registered in Content/Data/EntityComponents.sbc — keep them in sync.
        static readonly Guid StorageKey = new Guid("8b1c6e8a-5d2f-4f0a-9a4b-9c2f0e6d1a01");

        const string IniSection = "DockingAid";
        const string KeyUsedForDocking = "UsedForDocking";
        const string KeyDetectionRange = "DetectionRange";

        public const float MinDetectionRange = 1f;
        public const float MaxDetectionRange = 50f;
        public const float DefaultDetectionRange = 20f;

        public static bool GetUsedForDocking(IMyTerminalBlock block)
        {
            var ini = LoadIni(block as IMyEntity);
            // Default ON to match vanilla "Use for parking": every connector is
            // a docking candidate until the player opts out.
            return ini.Get(IniSection, KeyUsedForDocking).ToBoolean(true);
        }

        public static void SetUsedForDocking(IMyTerminalBlock block, bool value)
        {
            var entity = block as IMyEntity;
            if (entity == null) return;
            var ini = LoadIni(entity);
            ini.Set(IniSection, KeyUsedForDocking, value);
            SaveIni(entity, ini);
        }

        public static float GetDetectionRange(IMyTerminalBlock block)
        {
            var ini = LoadIni(block as IMyEntity);
            return ClampDetectionRange(ini.Get(IniSection, KeyDetectionRange).ToSingle(DefaultDetectionRange));
        }

        public static void SetDetectionRange(IMyTerminalBlock block, float value)
        {
            value = ClampDetectionRange(value);
            var entity = block as IMyEntity;
            if (entity == null) return;
            var ini = LoadIni(entity);
            ini.Set(IniSection, KeyDetectionRange, value);
            SaveIni(entity, ini);
        }

        // Pulled out so tests can pin the bounds without an SE block.
        public static float ClampDetectionRange(float value)
        {
            if (value < MinDetectionRange) return MinDetectionRange;
            if (value > MaxDetectionRange) return MaxDetectionRange;
            return value;
        }

        // ── Internals ───────────────────────────────────────────────────────

        static MyIni LoadIni(IMyEntity entity)
        {
            var ini = new MyIni();
            if (entity == null || entity.Storage == null) return ini;
            string data;
            if (entity.Storage.TryGetValue(StorageKey, out data) && !string.IsNullOrEmpty(data))
            {
                if (!ini.TryParse(data))
                {
                    // Corrupt blob: fall back to defaults rather than throw, but
                    // log so the player has a breadcrumb instead of silently
                    // losing per-connector settings.
                    MyLog.Default.WriteLineAndConsole(
                        "[Mal.DockingAid] corrupt connector storage on entity " +
                        entity.EntityId + " — falling back to defaults");
                }
            }
            return ini;
        }

        static void SaveIni(IMyEntity entity, MyIni ini)
        {
            if (entity == null) return;
            if (entity.Storage == null)
                entity.Storage = new MyModStorageComponent();
            entity.Storage[StorageKey] = ini.ToString();
        }
    }
}
