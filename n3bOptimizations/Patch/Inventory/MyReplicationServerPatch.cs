using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Sandbox.Game;
using Sandbox.Game.Replication;
using Sandbox.Game.Replication.StateGroups;
using VRage;
using VRage.Network;
using VRageMath;

namespace n3bOptimizations.Patch.Inventory
{
    public class MyReplicationServerPatch : IPatch
    {
        private static FieldInfo _stateInfo = AccessTools.Field(typeof(MyReplicationServer).Assembly.GetType("VRage.Network.MyClient"), "State");

        private static Type TerminalReplicableType = typeof(MyInventory).Assembly.GetType("Sandbox.Game.Replication.MyTerminalReplicable");
        private static Type InventoryReplicableType = typeof(MyInventory).Assembly.GetType("Sandbox.Game.Replication.MyInventoryReplicable");

        private static ConcurrentDictionary<MyInventory, MyExternalReplicable<MyInventory>> _replicables =
            new ConcurrentDictionary<MyInventory, MyExternalReplicable<MyInventory>>();

        private static FieldInfo invState = AccessTools.Field(InventoryReplicableType, "m_stateGroup");
        private static FieldInfo propSync = AccessTools.Field(InventoryReplicableType, "m_propertySync");

        private static MethodInfo markDirtyState = AccessTools.Method(InventoryReplicableType.Assembly.GetType("Sandbox.Game.Replication.StateGroups.MyEntityInventoryStateGroup"),
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

            source = AccessTools.Method(typeof(MyReplicationServer), "RemoveClient");
            patch = AccessTools.Method(typeof(MyReplicationServerPatch), "RemoveClientPrefix");
            harmony.Patch(source, new HarmonyMethod(patch));

            source = AccessTools.Method(typeof(MyReplicationServer), "DispatchEvent",
                new[] {typeof(IPacketData), typeof(CallSite), typeof(EndpointId), typeof(IMyNetObject), typeof(Vector3D?)});
            patch = AccessTools.Method(typeof(MyReplicationServerPatch), "DispatchEventPatch");
            harmony.Patch(source, new HarmonyMethod(patch));

            source = AccessTools.Method(InventoryReplicableType, "OnHook");
            patch = AccessTools.Method(typeof(MyReplicationServerPatch), "OnHookPatch");
            harmony.Patch(source, null, new HarmonyMethod(patch));

            // source = AccessTools.Method(typeof(MyReplicationServer), "DispatchBlockingEvent");
            // harmony.Patch(source, new HarmonyMethod(patch));
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
            if (!_replicables.TryGetValue(inventory, out var replicable)) return;
            (propSync.GetValue(replicable) as MyPropertySyncStateGroup)?.MarkDirty();
            var state = invState.GetValue(replicable);
            if (state != null) markDirtyState.Invoke(state, new[] {inventory});
        }

        public static bool ScheduleStateGroupSyncPrefix(object client, MyStateDataEntry groupEntry)
        {
            if (!(groupEntry.Owner is MyExternalReplicable<MyInventory> rep)) return true;
            var state = (MyClientStateBase) _stateInfo.GetValue(client);
            return !state.IsEnabledAPI() || state.IsSubscribedToInventory(rep.Instance);
        }

        public static bool ShouldSendEventPrefix(IMyNetObject eventInstance, object client)
        {
            if (eventInstance is MyExternalReplicable<MyInventory> rep)
            {
                var state = (MyClientStateBase) _stateInfo.GetValue(client);
                return !state.IsEnabledAPI() || state.IsSubscribedToInventory(rep.Instance);
            }

            return true;
        }

        public static void RemoveClientPrefix(Endpoint endpoint)
        {
            MyClientStateBaseExtension.RemoveEndpoint(endpoint.Id);
        }

#if DEBUG
        static Dictionary<string, int> calls = new Dictionary<string, int>();
        static Dictionary<string, string> targets = new Dictionary<string, string>();
        private static long last = 0;
#endif

        public static bool DispatchEventPatch(CallSite site, IMyNetObject eventInstance)
        {
#if DEBUG
            if (!calls.ContainsKey(site.MethodInfo.Name)) calls.Add(site.MethodInfo.Name, 0);
            calls[site.MethodInfo.Name]++;
            targets[site.MethodInfo.Name] = eventInstance?.GetType().Name ?? "x";
            if (last > DateTimeOffset.Now.ToUnixTimeSeconds() - 5) return true;
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

            return true;
        }
    }
}