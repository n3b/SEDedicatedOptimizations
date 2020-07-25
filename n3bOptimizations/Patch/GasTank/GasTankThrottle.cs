using System;
using System.Reflection;
using HarmonyLib;
using n3bOptimizations.Patch.GasTank;
using ParallelTasks;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Blocks;

namespace n3bOptimizations
{
    public static class GasTankThrottle
    {
        static double threshold1 = 0.06;
        static double threshold2 = 0.03;
        static int perTicks = 13;
        public static int buckets = 1;

        static int bucket = 0;
        static long ticks = 0;

        public static readonly MethodInfo cb = AccessTools.Method(typeof(MyGasTank), "OnFilledRatioCallback");

        private static readonly MethodInfo ChangeFilledRatio = AccessTools.Method(typeof(MyGasTank), "ChangeFilledRatio");

        public static bool ChangeFillRatioAmountPatch(ref MyGasTank __instance, double newFilledRatio)
        {
            var oldRatio = __instance.FilledRatio;
            __instance.ApplyAmount(newFilledRatio);

            // dispatch immediately
            if (oldRatio > newFilledRatio && newFilledRatio < threshold1 && newFilledRatio * 2 - oldRatio < threshold2)
            {
                UpdateWork.tanksUpdated.TryRemove(__instance.GetHashCode(), out var tupleDispose);
                return true;
            }

            var tuple = new Tuple<MyGasTank, double>(__instance, newFilledRatio);
            UpdateWork.tanksUpdated.AddOrUpdate(__instance.GetHashCode(), tuple, (key, old) => tuple);
            return false;
        }

        public static bool OnFilledRatioCallbackPatch()
        {
            return false;
        }

        public static void Init(Harmony harmony, PluginConfig config)
        {
            threshold1 = config.Threshold1 / 100;
            threshold2 = config.Threshold2 / 100;
            buckets = config.Batches;
            perTicks = config.PerTicks;

            var source = AccessTools.Method(typeof(MyGasTank), "ChangeFillRatioAmount");
            var patch = AccessTools.Method(typeof(GasTankThrottle), "ChangeFillRatioAmountPatch");
            harmony.Patch(source, new HarmonyMethod(patch));

            patch = AccessTools.Method(typeof(GasTankThrottle), "OnFilledRatioCallbackPatch");
            harmony.Patch(cb, new HarmonyMethod(patch));
        }

        public static void Update()
        {
            if (++ticks < perTicks) return;
            ticks = 0;
            bucket++;
            var data = new UpdateWork.UpdateWorkData(bucket);
            Parallel.Start(UpdateWork.DoWork, null, data);
            if (bucket == buckets) bucket = 0;
        }

        public static void ApplyAmount(this MyGasTank tank, double amount)
        {
            var res = (bool) ChangeFilledRatio.Invoke(tank, new object[] {amount, false});
            if (res) tank.GetInventory(0).UpdateGasAmount();
        }
    }
}