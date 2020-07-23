using System;
using System.Reflection;
using HarmonyLib;
using Sandbox.Game.Entities.Cube;
using VRage;

namespace n3bOptimizations.Patch.Inventory
{
    public class MyProductionBlockPatch : IPatch
    {
        static MethodInfo removeInfo = AccessTools.Method(typeof(MyProductionBlock), "OnRemoveQueueItem");

        // why does refinery replicate this?
        public static bool RemoveFirstQueueItemAnnouncePatch(ref MyProductionBlock __instance, MyFixedPoint amount, float progress = 0f)
        {
            if (!(__instance is MyRefinery)) return true;
            removeInfo.Invoke(__instance, new ValueType[] {0, amount, progress});
            return false;
        }

        public void Inject(Harmony harmony)
        {
            var source = AccessTools.Method(typeof(MyProductionBlock), "RemoveFirstQueueItemAnnounce");
            var patch = AccessTools.Method(typeof(MyProductionBlockPatch), "RemoveFirstQueueItemAnnouncePatch");
            harmony.Patch(source, new HarmonyMethod(patch));
        }
    }
}