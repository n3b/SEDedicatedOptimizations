using System;
using System.Reflection;
using HarmonyLib;
using n3bOptimizations.Multiplayer;
using n3bOptimizations.Replication.Inventory;
using Sandbox.Game;
using VRage.Network;

namespace n3bOptimizations.Patch.Inventory
{
    public class MyReplicationServerPatch : IPatch
    {
        private static FieldInfo _stateInfo = AccessTools.Field(typeof(MyReplicationServer).Assembly.GetType("VRage.Network.MyClient"), "State");

        public bool Inject(Harmony harmony)
        {
            MethodInfo source;
            MethodInfo patch;
#if DEBUG
            source = AccessTools.Method(typeof(MyReplicationServer), "DispatchEvent",
                new[] {typeof(IPacketData), typeof(CallSite), typeof(EndpointId), typeof(IMyNetObject), typeof(Vector3D?)});
            patch = AccessTools.Method(typeof(MyReplicationServerPatch), "DispatchEventPrefix");
            harmony.Patch(source, new HarmonyMethod(patch));

            source = AccessTools.Method(typeof(MyReplicationServer), "DispatchBlockingEvent");
            patch = AccessTools.Method(typeof(MyReplicationServerPatch), "DispatchBlockingEventPrefix");
            harmony.Patch(source, new HarmonyMethod(patch));
#endif
            if (!Plugin.StaticConfig.InventoryEnabled) return false;

            source = AccessTools.Method(typeof(MyReplicationServer), "ScheduleStateGroupSync");
            patch = AccessTools.Method(typeof(MyReplicationServerPatch), "ScheduleStateGroupSyncPrefix");
            harmony.Patch(source, new HarmonyMethod(patch));

            source = AccessTools.Method(typeof(MyReplicationServer), "ShouldSendEvent");
            patch = AccessTools.Method(typeof(MyReplicationServerPatch), "ShouldSendEventPrefix");
            harmony.Patch(source, new HarmonyMethod(patch));

            return true;
        }

        public static bool ScheduleStateGroupSyncPrefix(object client, MyStateDataEntry groupEntry)
        {
            try
            {
                if (!(groupEntry.Group is ItemsStateGroup group)) return true;
                var state = (CustomClientState) _stateInfo.GetValue(client);
                if (!group.HasRights(state.EndpointId)) return false;
                return state.IsSubscribedToInventory(group.Inventory);
            }
            catch (Exception e)
            {
                Plugin.Error("", e);
                return true;
            }
        }

        public static bool ShouldSendEventPrefix(IMyNetObject eventInstance, object client)
        {
            try
            {
                if (!(eventInstance is IMyProxyTarget proxyTarget && proxyTarget.Target is MyInventory inventory)) return true;
                var state = (CustomClientState) _stateInfo.GetValue(client);
                return state.IsSubscribedToInventory(inventory);
            }
            catch (Exception e)
            {
                Plugin.Error("", e);
            }

            return true;
        }


#if DEBUG
        static Dictionary<string, int> calls = new Dictionary<string, int>();
        static Dictionary<string, string> targets = new Dictionary<string, string>();
        private static long last = 0;
        private static long last2 = 0;


        public static void DispatchBlockingEventPrefix(CallSite site, IMyNetObject targetReplicable)
        {
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
        }

        public static void DispatchEventPrefix(CallSite site, IMyNetObject eventInstance)
        {
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
        }
#endif
    }
}