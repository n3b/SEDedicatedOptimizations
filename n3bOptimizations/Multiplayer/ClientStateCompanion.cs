using System;
using System.Collections.Concurrent;
using n3bOptimizations;
using n3bOptimizations.Patch.Inventory;
using Sandbox.Game;
using Sandbox.Game.Entities;
using Sandbox.Game.Replication;
using Sandbox.Game.Replication.StateGroups;
using SEClientFixes.Util;
using VRage.Network;

namespace n3b.SEMultiplayer
{
    public class ClientStateCompanion
    {
        private ConcurrentDictionary<MyStateDataEntry, double> updated = new ConcurrentDictionary<MyStateDataEntry, double>();
        private TimerUtil timer = new TimerUtil();

        public bool ScheduleInventoryUpdate(MyStateDataEntry entry)
        {
            var rep = (MyExternalReplicable<MyInventory>) entry.Owner;
            if (!(rep.Instance.Entity is MyCubeBlock block)) return true;
            // if (block?.CubeGrid.IsStatic != true) return IsSubscribedToInventory(rep.Instance);

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