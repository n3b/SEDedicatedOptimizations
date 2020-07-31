using System;
using System.Collections.Generic;
using HarmonyLib;
using Sandbox.Game;
using Sandbox.Game.Replication;
using VRage.Network;

namespace n3bOptimizations.Replication.Inventory
{
    public class InventoryReplicablePatch : IPatch
    {
        static Type InventoryReplicableType = typeof(MyInventory).Assembly.GetType("Sandbox.Game.Replication.MyInventoryReplicable");

        public bool Inject(Harmony harmony)
        {
            if (!Plugin.StaticConfig.InventoryEnabled) return false;

            var source = AccessTools.Method(typeof(MyReplicableFactory), "FindTypeFor");
            var patch = AccessTools.Method(typeof(InventoryReplicablePatch), "FindTypeForPrefix");
            harmony.Patch(source, new HarmonyMethod(patch));

            source = AccessTools.Method(typeof(MyTypeTable), "Register");
            patch = AccessTools.Method(typeof(InventoryReplicablePatch), "RegisterPostfix");
            harmony.Patch(source, null, new HarmonyMethod(patch));

            return true;
        }

        public static bool FindTypeForPrefix(object obj, ref Type __result)
        {
            if (!(obj is MyInventory inventory)) return true;
            __result = typeof(InventoryReplicable);
            return false;
        }

        public static void RegisterPostfix(Type type, ref MySynchronizedTypeInfo __result, ref Dictionary<Type, MySynchronizedTypeInfo> ___m_typeLookup)
        {
            if (type != InventoryReplicableType) return;
            var info = AccessTools.Field(typeof(MySynchronizedTypeInfo), "Type");
            info.SetValue(__result, typeof(InventoryReplicable));
            ___m_typeLookup.Add(typeof(InventoryReplicable), __result);
        }
    }
}