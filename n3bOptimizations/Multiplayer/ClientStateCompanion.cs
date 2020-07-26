using System;
using System.Collections.Concurrent;
using n3bOptimizations;
using n3bOptimizations.Patch.Inventory;
using n3bOptimizations.Util;
using Sandbox.Game;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Inventory;
using Sandbox.Game.Replication;
using Sandbox.Game.Replication.StateGroups;
using SEClientFixes.Util;
using VRage.Game.Entity;
using VRage.Network;

namespace n3b.SEMultiplayer
{
    public class ClientStateCompanion
    {
        static ConcurrentDictionary<EndpointId, ClientStateCompanion> _map = new ConcurrentDictionary<EndpointId, ClientStateCompanion>();

        public bool APIEnabled = false;

        ConcurrentHashSet<MyInventory> inventories = new ConcurrentHashSet<MyInventory>();

        public static ClientStateCompanion Get(EndpointId endpointId)
        {
            var companion = _map.GetOrAdd(endpointId, new ClientStateCompanion());
            return companion;
        }

        public static void Remove(EndpointId endpointId)
        {
            _map.TryRemove(endpointId, out var companion);
        }

        public bool IsSubscribedToInventory(MyInventory inventory)
        {
            return !APIEnabled || inventories.Contains(inventory);
        }

        public void SubscribeInventory(MyInventoryBase inventory)
        {
            try
            {
                if (inventory is MyInventoryAggregate agg)
                    foreach (MyInventory i in agg.ChildList.Reader)
                    {
                        if (i == null) continue;
                        inventories.Add(i);
                        MyReplicationServerPatch.RefreshInventory(i);
                    }
                else if (inventory is MyInventory i && i != null)
                {
                    inventories.Add(i);
                    MyReplicationServerPatch.RefreshInventory(i);
                }
            }
            catch (Exception e)
            {
                Plugin.Log.Error(e);
            }
        }

        public void UnsubscribeInventory(MyInventoryBase inventory)
        {
            try
            {
                if (inventory is MyInventoryAggregate agg)
                    foreach (MyInventory i in agg.ChildList.Reader)
                    {
                        if (i != null) inventories.TryRemove(i);
                    }
                else if (inventory is MyInventory i && i != null)
                    inventories.TryRemove(i);
            }
            catch (Exception e)
            {
                Plugin.Log.Error(e);
            }
        }

        public void ClearInventorySubscriptions()
        {
            inventories.Clear();
        }


        private ConcurrentDictionary<MyStateDataEntry, double> updated = new ConcurrentDictionary<MyStateDataEntry, double>();
        private TimerUtil timer = new TimerUtil();

        public bool ScheduleInventoryUpdate(MyStateDataEntry entry)
        {
            var rep = (MyExternalReplicable<MyInventory>) entry.Owner;
            if (!(rep.Instance.Entity is MyCubeBlock block)) return true;
            if (block?.CubeGrid.IsStatic != true) return IsSubscribedToInventory(rep.Instance);

            // when marked dirty first time, update immediately, otherwise schedule for later

            var cur = DateTimeOffset.UtcNow.TimeOfDay.TotalMilliseconds;
            if (!updated.TryGetValue(entry, out var lastUpdated))
            {
                updated.TryAdd(entry, cur);
                return true;
            }

            if (cur - lastUpdated > Plugin.StaticConfig.InventoryThrottle)
            {
                updated.TryRemove(entry, out var deleted);
                return true;
            }

            timer.Throttle(Plugin.StaticConfig.InventoryThrottle, HandleThrottledUpdate);
            return false;
        }

        void HandleThrottledUpdate(object param)
        {
            foreach (var i in updated.Keys)
            {
                try
                {
                    var group = i?.Group;
                    switch (group)
                    {
                        case null:
                            continue;
                        case MyPropertySyncStateGroup gr:
                            gr.MarkDirty();
                            break;
                        default:
                        {
                            if (i?.Owner is MyExternalReplicable<MyInventory> o && o?.Instance != null)
                                MyReplicationServerPatch.markDirtyState.Invoke(@group, new object[] {o.Instance});
                            break;
                        }
                    }
                }
                catch (Exception e)
                {
                    Plugin.Log.Error(e);
                }
            }
        }
    }
}