using System.Collections.Concurrent;
using Sandbox.Game;
using VRage.Game.Components;

namespace n3bOptimizations.Patch.Inventory
{
    public static class InventoryStorage
    {
        public static ConcurrentDictionary<MyInventory, InventoryProps> props = new ConcurrentDictionary<MyInventory, InventoryProps>();
        
        public static void ctor(ref MyInventory __instance)
        {
            var p = new InventoryProps(__instance);
            props.TryAdd(__instance, p);
            __instance.SyncType.Append(p);
            
            __instance.BeforeRemovedFromContainer += delegate(MyEntityComponentBase x)
            {
                var i = x as MyInventory;
                props.TryRemove(i, out var v);
            };
        }
    }
}