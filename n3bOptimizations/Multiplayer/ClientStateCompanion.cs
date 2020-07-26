using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using n3bOptimizations;
using n3bOptimizations.Patch.Inventory;
using n3bOptimizations.Util;
using Sandbox.Game;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Inventory;
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


        private HashSet<MyInventory> _scheduledForUpdate = new HashSet<MyInventory>();
        private HashSet<MyInventory> _markedForUpdate = new HashSet<MyInventory>();

        // TODO timer doesn't work, check it later
        private DebounceDispatcher _inventoryUpdates = new DebounceDispatcher();
        private double _lastTrigger = DateTime.UtcNow.TimeOfDay.TotalMilliseconds;

        public bool ScheduleInventoryUpdate(MyInventory inventory)
        {
            if (!(inventory.Entity is MyCubeBlock block)) return true;
            if (block?.CubeGrid.IsStatic != true) return IsSubscribedToInventory(inventory);
            if (_markedForUpdate.Remove(inventory)) return true;
            var ms = DateTime.UtcNow.TimeOfDay.TotalMilliseconds;
            if (ms - _lastTrigger < Plugin.StaticConfig.InventoryThrottle)
            {
                _scheduledForUpdate.Add(inventory);
                return false;
            }

            _lastTrigger = ms;
            var old = _scheduledForUpdate;
            _scheduledForUpdate = new HashSet<MyInventory>();

            foreach (var i in old)
            {
                _markedForUpdate.Add(i);
                MyReplicationServerPatch.RefreshInventory(i);
            }

            return false;
        }
    }
}