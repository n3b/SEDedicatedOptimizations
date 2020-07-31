using System;
using System.Reflection;
using HarmonyLib;
using ParallelTasks;
using Sandbox;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Blocks;

namespace n3bOptimizations.Patch.GasTank
{
    public class GasTankPatch : IPatch
    {
        private static bool enabled = false;
        static double threshold1 = 0.06;
        static double threshold2 = 0.03;
        static int interval = 200;
        public static int buckets = 1;

        static int bucket = 0;
        static int lastUpdate = 0;

        public static readonly MethodInfo cb = AccessTools.Method(typeof(MyGasTank), "OnFilledRatioCallback");

        private static readonly MethodInfo ChangeFilledRatio = AccessTools.Method(typeof(MyGasTank), "ChangeFilledRatio");

        public static bool ChangeFillRatioAmountPrefix(ref MyGasTank __instance, double newFilledRatio)
        {
            var oldRatio = __instance.FilledRatio;
            var res = (bool) ChangeFilledRatio.Invoke(__instance, new object[] {newFilledRatio, false});
            if (res) __instance.GetInventory(0).UpdateGasAmount();

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

        public static bool OnFilledRatioCallbackPrefix()
        {
            return false;
        }

        public bool Inject(Harmony harmony)
        {
            var config = Plugin.StaticConfig;
            enabled = config.GasTankEnabled;
            if (!enabled) return false;

            threshold1 = config.GasTankThreshold1 / 100;
            threshold2 = config.GasTankThreshold2 / 100;
            buckets = config.GasTankBatches;
            interval = config.GasTankInterval;

            var source = AccessTools.Method(typeof(MyGasTank), "ChangeFillRatioAmount");
            var patch = AccessTools.Method(typeof(GasTankPatch), "ChangeFillRatioAmountPrefix");
            harmony.Patch(source, new HarmonyMethod(patch));

            patch = AccessTools.Method(typeof(GasTankPatch), "OnFilledRatioCallbackPrefix");
            harmony.Patch(cb, new HarmonyMethod(patch));

            return true;
        }

        public static void Update()
        {
            if (!enabled) return;
            var ms = MySandboxGame.TotalTimeInMilliseconds;
            if (lastUpdate + interval < ms) return;
            lastUpdate = ms;
            bucket++;
            var data = new UpdateWork.UpdateWorkData(bucket);
            Parallel.Start(UpdateWork.DoWork, null, data);
            if (bucket == buckets) bucket = 0;
        }
    }
}