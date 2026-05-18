using Mal.Mods.Utilities;
using Mal.Mods.Utilities.Notifications;
using VRage.Game.Components;

namespace Mal.DockingAid
{
    [MySessionComponentDescriptor(MyUpdateOrder.NoUpdate)]
    public class DockingAidSession : ModSession
    {
        static DockingAidSession _instance;

        public static bool TryGet(out DockingAidSession instance)
        {
            instance = _instance;
            return instance != null;
        }

        protected override void Configure(Builder builder)
        {
            builder.UseNotifications()
                .Using<DockingTargetingComponent>();
        }

        protected override void OnLoadData()
        {
            _instance = this;
        }

        protected override void OnBeforeStart()
        {
            NotificationsDriverComponent.EnsureChannelBuilt();
        }

        protected override void OnUnloadData()
        {
            _instance = null;
        }
    }
}
