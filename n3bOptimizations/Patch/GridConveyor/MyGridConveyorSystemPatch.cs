using System;
using System.Runtime.CompilerServices;
using HarmonyLib;
using n3bOptimizations.Util;
using Sandbox.Game.Entities;
using Sandbox.Game.GameSystems;
using Sandbox.Game.GameSystems.Conveyors;
using SEClientFixes.Util;

namespace n3bOptimizations.Patch.GridConveyor
{
    public class MyGridConveyorSystemPatch : IPatch
    {
        static readonly ConditionalWeakTable<MyGridConveyorSystem, Companion> Conveyors = new ConditionalWeakTable<MyGridConveyorSystem, Companion>();

        static TimerUtil _timer = new TimerUtil();

        public bool Inject(Harmony harmony)
        {
            if (!Plugin.StaticConfig.ConveyorCachingEnabled) return false;

            var ctor = typeof(MyGridConveyorSystem).GetConstructor(new[] {typeof(MyCubeGrid)});
            var patch = AccessTools.Method(typeof(MyGridConveyorSystemPatch), "CtorPostfix");
            harmony.Patch(ctor, null, new HarmonyMethod(patch));

            var source = AccessTools.Method(typeof(MyGridConveyorSystem), "Reachable", new[] {typeof(IMyConveyorEndpoint), typeof(IMyConveyorEndpoint)});
            patch = AccessTools.Method(typeof(MyGridConveyorSystemPatch), "ReachablePrefix");
            harmony.Patch(source, new HarmonyMethod(patch));

            patch = AccessTools.Method(typeof(MyGridConveyorSystemPatch), "ReachablePostfix");
            harmony.Patch(source, null, new HarmonyMethod(patch));

            source = AccessTools.Method(typeof(MyGridConveyorSystem), "StartRecomputationThread");
            patch = AccessTools.Method(typeof(MyGridConveyorSystemPatch), "StartRecomputationThreadPrefix");
            harmony.Patch(source, new HarmonyMethod(patch));

            return true;
        }

        public static void CtorPostfix(ref MyGridConveyorSystem __instance)
        {
            Conveyors.Add(__instance, new Companion(__instance));
        }

        public static bool ReachablePrefix(IMyConveyorEndpoint from, IMyConveyorEndpoint to, ref bool __result)
        {
            if (!(from is MyMultilineConveyorEndpoint ep)) return true;
            if (!Conveyors.TryGetValue(ep.CubeBlock.CubeGrid.GridSystems.ConveyorSystem, out var companion)) return true;
            var k = new Tuple<IMyConveyorEndpoint, IMyConveyorEndpoint>(from, to);
            if (!companion.Reachables.Contains(k)) return true;
            __result = true;
            return false;
        }

        public static void ReachablePostfix(IMyConveyorEndpoint from, IMyConveyorEndpoint to, ref bool __result)
        {
            if (!(from is MyMultilineConveyorEndpoint ep)) return;
            if (!Conveyors.TryGetValue(ep.CubeBlock.CubeGrid.GridSystems.ConveyorSystem, out var companion)) return;
            if (__result) companion.Reachables.Add(new Tuple<IMyConveyorEndpoint, IMyConveyorEndpoint>(from, to));
        }

        public static void StartRecomputationThreadPrefix(ref MyGridConveyorSystem __instance)
        {
            if (!Conveyors.TryGetValue(__instance, out var companion)) return;
            companion.Reachables.Clear();
        }

        class Companion
        {
            public readonly ConcurrentHashSet<Tuple<IMyConveyorEndpoint, IMyConveyorEndpoint>>
                Reachables = new ConcurrentHashSet<Tuple<IMyConveyorEndpoint, IMyConveyorEndpoint>>();

            public Companion(MyGridConveyorSystem conveyor)
            {
            }
        }
    }
}