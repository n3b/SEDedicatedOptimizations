using System;
using System.Collections.Concurrent;
using System.Reflection;
using HarmonyLib;
using n3bOptimizations.Multiplayer;
using Sandbox.Game;
using Sandbox.Game.Replication;
using Sandbox.Game.Replication.StateGroups;
using VRage.Network;

namespace n3bOptimizations.Patch.Inventory
{
    public class MyReplicationServerPatch : IPatch
    {
        private static FieldInfo _stateInfo = AccessTools.Field(typeof(MyReplicationServer).Assembly.GetType("VRage.Network.MyClient"), "State");

        private static Type TerminalReplicableType = typeof(MyInventory).Assembly.GetType("Sandbox.Game.Replication.MyTerminalReplicable");
        private static Type InventoryReplicableType = typeof(MyInventory).Assembly.GetType("Sandbox.Game.Replication.MyInventoryReplicable");
        private static Type MyEntityInventoryStateGroup = typeof(MyInventory).Assembly.GetType("Sandbox.Game.Replication.StateGroups.MyEntityInventoryStateGroup");

        private static ConcurrentDictionary<MyInventory, MyExternalReplicable<MyInventory>> _replicables =
            new ConcurrentDictionary<MyInventory, MyExternalReplicable<MyInventory>>();

        private static FieldInfo invState = AccessTools.Field(InventoryReplicableType, "m_stateGroup");
        private static FieldInfo propSync = AccessTools.Field(InventoryReplicableType, "m_propertySync");

        public static readonly MethodInfo markDirtyState = AccessTools.Method(
            InventoryReplicableType.Assembly.GetType("Sandbox.Game.Replication.StateGroups.MyEntityInventoryStateGroup"),
            "InventoryChanged");

        public void Inject(Harmony harmony)
        {
            MyExternalReplicable.Destroyed += OnDestroy;

            var source = AccessTools.Method(typeof(MyReplicationServer), "ScheduleStateGroupSync");
            var patch = AccessTools.Method(typeof(MyReplicationServerPatch), "ScheduleStateGroupSyncPrefix");
            harmony.Patch(source, new HarmonyMethod(patch));

            source = AccessTools.Method(typeof(MyReplicationServer), "ShouldSendEvent");
            patch = AccessTools.Method(typeof(MyReplicationServerPatch), "ShouldSendEventPrefix");
            harmony.Patch(source, new HarmonyMethod(patch));

            source = AccessTools.Method(InventoryReplicableType, "OnHook");
            patch = AccessTools.Method(typeof(MyReplicationServerPatch), "OnHookPatch");
            harmony.Patch(source, null, new HarmonyMethod(patch));

#if DEBUG
            source = AccessTools.Method(typeof(MyReplicationServer), "DispatchEvent",
                new[] {typeof(IPacketData), typeof(CallSite), typeof(EndpointId), typeof(IMyNetObject), typeof(Vector3D?)});
            patch = AccessTools.Method(typeof(MyReplicationServerPatch), "DispatchEventPrefix");
            harmony.Patch(source, new HarmonyMethod(patch));

            source = AccessTools.Method(typeof(MyReplicationServer), "DispatchBlockingEvent");
            patch = AccessTools.Method(typeof(MyReplicationServerPatch), "DispatchBlockingEventPrefix");
            harmony.Patch(source, new HarmonyMethod(patch));
#endif
        }

        public static void OnHookPatch(ref MyExternalReplicable<MyInventory> __instance)
        {
            var i = __instance.Instance;
            if (i == null) return;
            _replicables.TryAdd(i, __instance);
        }

        public static void OnDestroy(MyExternalReplicable replicable)
        {
            if (!(replicable is MyExternalReplicable<MyInventory> rep)) return;
            var inv = rep.Instance;
            if (inv == null) return;
            _replicables.TryRemove(inv, out var i);
        }

        public static void RefreshInventory(MyInventory inventory)
        {
            try
            {
                if (!_replicables.TryGetValue(inventory, out var replicable)) return;
                // (propSync.GetValue(replicable) as MyPropertySyncStateGroup).MarkDirty();
                var inventoryStateGroup = invState.GetValue(replicable);
                if (inventoryStateGroup != null) markDirtyState.Invoke(inventoryStateGroup, new[] {inventory});
            }
            catch (Exception e)
            {
                Plugin.Log.Error(e);
            }
        }

        public static bool ScheduleStateGroupSyncPrefix(object client, MyStateDataEntry groupEntry)
        {
            try
            {
                if (!(groupEntry.Owner is MyExternalReplicable<MyInventory> rep)) return true;
                if (groupEntry.Group is MyPropertySyncStateGroup) return true;
                var state = (CustomClientState) _stateInfo.GetValue(client);
                return state.IsSubscribedToInventory(rep.Instance);
            }
            catch (Exception e)
            {
                Plugin.Log.Error(e);
                return true;
            }
        }

        public static bool ShouldSendEventPrefix(IMyNetObject eventInstance, object client)
        {
            try
            {
                if (!(eventInstance is MyExternalReplicable<MyInventory> rep)) return true;
                var state = (CustomClientState) _stateInfo.GetValue(client);
                return state.IsSubscribedToInventory(rep.Instance);
            }
            catch (Exception e)
            {
                Plugin.Log.Error(e);
            }

            return true;
        }

#if DEBUG
        static Dictionary<string, int> calls = new Dictionary<string, int>();
        static Dictionary<string, string> targets = new Dictionary<string, string>();
        private static long last = 0;
        private static long last2 = 0;
#endif

        public static void DispatchBlockingEventPrefix(CallSite site, IMyNetObject targetReplicable)
        {
#if DEBUG
            if (!calls.ContainsKey(site.MethodInfo.Name)) calls.Add(site.MethodInfo.Name, 0);
            calls[site.MethodInfo.Name]++;
            targets[site.MethodInfo.Name] = targetReplicable?.GetType().Name ?? "x";
            if (last2 > DateTimeOffset.Now.ToUnixTimeSeconds() - 5) return;
            last2 = DateTimeOffset.Now.ToUnixTimeSeconds();
            foreach (var k in calls.Keys)
                Plugin.Log.Info($"called method {k} {calls[k]} times");
            Plugin.Log.Info($"---------------");

            foreach (var k in targets.Keys)
                Plugin.Log.Info($"{k} {targets[k]}");

            Plugin.Log.Info($"----------------------------------------");
            calls.Clear();
            targets.Clear();
#endif
        }

        public static void DispatchEventPrefix(CallSite site, IMyNetObject eventInstance)
        {
#if DEBUG
            if (!calls.ContainsKey(site.MethodInfo.Name)) calls.Add(site.MethodInfo.Name, 0);
            calls[site.MethodInfo.Name]++;
            targets[site.MethodInfo.Name] = eventInstance?.GetType().Name ?? "x";
            if (last > DateTimeOffset.Now.ToUnixTimeSeconds() - 5) return;
            last = DateTimeOffset.Now.ToUnixTimeSeconds();
            foreach (var k in calls.Keys)
                Plugin.Log.Info($"called method {k} {calls[k]} times");
            Plugin.Log.Info($"---------------");

            foreach (var k in targets.Keys)
                Plugin.Log.Info($"{k} {targets[k]}");

            Plugin.Log.Info($"----------------------------------------");
            calls.Clear();
            targets.Clear();
#endif
        }
    }
}