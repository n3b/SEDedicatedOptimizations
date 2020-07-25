using System;
using System.Collections.Concurrent;
using n3bOptimizations.Util;
using Sandbox.Game;
using Sandbox.Game.Entities.Inventory;
using VRage.Game.Entity;
using VRage.Network;

namespace n3bOptimizations.Patch.Inventory
{
    public static class MyClientStateBaseExtension
    {
        static ConcurrentDictionary<EndpointId, ConcurrentHashSet<MyInventory>>
            subscriptions = new ConcurrentDictionary<EndpointId, ConcurrentHashSet<MyInventory>>();

        static ConcurrentHashSet<EndpointId> enabled = new ConcurrentHashSet<EndpointId>();

        public static bool IsSubscribedToInventory(this MyClientStateBase state, MyInventory inventory)
        {
            if (!subscriptions.TryGetValue(state.EndpointId.Id, out var inventories)) return false;
            return inventories.Contains(inventory);
        }

        public static void SubscribeInventory(this MyClientStateBase state, MyInventoryBase inventory)
        {
            try
            {
                var inventories = subscriptions.GetOrAdd(state.EndpointId.Id, new ConcurrentHashSet<MyInventory>());
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

        public static void UnsubscribeInventory(this MyClientStateBase state, MyInventoryBase inventory)
        {
            try
            {
                if (!subscriptions.TryGetValue(state.EndpointId.Id, out var inventories)) return;
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

        public static void ClearInventorySubscriptions(this MyClientStateBase state)
        {
            subscriptions.TryRemove(state.EndpointId.Id, out var inventories);
        }

        public static void RemoveEndpoint(EndpointId endpointId)
        {
            subscriptions.TryRemove(endpointId, out var v);
            enabled.TryRemove(endpointId);
        }

        public static void SetEnabledAPI(this MyClientStateBase state, bool enable)
        {
            if (enable) enabled.Add(state.EndpointId.Id);
            else enabled.TryRemove(state.EndpointId.Id);
        }

        public static bool IsEnabledAPI(this MyClientStateBase state)
        {
            return enabled.Contains(state.EndpointId.Id);
        }
    }
}