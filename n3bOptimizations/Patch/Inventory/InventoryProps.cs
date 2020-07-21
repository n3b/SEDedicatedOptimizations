using Sandbox.Game;
using VRage.Sync;

namespace n3bOptimizations.Patch.Inventory
{
    public class InventoryProps
    {
        public Sync<bool, SyncDirection.BothWays> subscribed;

        private MyInventory _inventory;

        public InventoryProps(MyInventory inventory)
        {
            _inventory = inventory;
        }
    }
}