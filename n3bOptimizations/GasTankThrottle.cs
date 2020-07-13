﻿using System;
using System.Collections.Concurrent;
using System.Reflection;
using NLog;
using Sandbox.Common.ObjectBuilders;
using Sandbox.Engine.Multiplayer;
using Sandbox.Game.Entities.Blocks;
using VRage.Game.Components;
using VRage.Network;
using HarmonyLib;
using ParallelTasks;
using Sandbox.Game.Entities;
using Sandbox.Game.EntityComponents;

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

        private static readonly MethodInfo source = AccessTools.Method(typeof(MyGasTank), "ChangeFillRatioAmount");

        private static readonly MethodInfo patch =
            AccessTools.Method(typeof(GasTankThrottle), "ChangeFillRatioAmountPatch");

        public static readonly MethodInfo cb = AccessTools.Method(typeof(MyGasTank), "OnFilledRatioCallback");
        private static readonly MethodInfo patch2 = AccessTools.Method(typeof(GasTankThrottle), "OnFilledRatioCallbackPatch");

        private static readonly MethodInfo ChangeFilledRatio =
            AccessTools.Method(typeof(MyGasTank), "ChangeFilledRatio");

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
            
            harmony.Patch(source, new HarmonyMethod(patch));
            harmony.Patch(cb, new HarmonyMethod(patch2));
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

    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_OxygenTank), useEntityUpdate: false)]
    public class GasTankLogicComponent : MyGameLogicComponent
    {
        public override void Close()
        {
            var instance = Entity as MyGasTank;
            if (instance == null) return;
            UpdateWork.tanksUpdated.TryRemove(instance.GetHashCode(), out var tuple);
            base.Close();
        }
    }

    public static class UpdateWork
    {
        public static ConcurrentDictionary<int, Tuple<MyGasTank, double>> tanksUpdated = new ConcurrentDictionary<int, Tuple<MyGasTank, double>>();
        
        public static readonly Logger Log = LogManager.GetCurrentClassLogger();

        public static void DoWork(WorkData wd)
        {
            var bucket = (wd as UpdateWorkData).bucket;
            foreach (var hash in tanksUpdated.Keys)
            {
                if ((hash & int.MaxValue) % GasTankThrottle.buckets != 0) continue;
                tanksUpdated.TryRemove(hash, out var tuple);
                if (tuple == null || tuple.Item1 == null) continue;
                
                Func<MyGasTank, Action<double>> fn = (MyGasTank x) =>
                    (Action<double>) GasTankThrottle.cb.CreateDelegate(typeof(Action<double>), x);
                MyMultiplayer.RaiseEvent<MyGasTank, double>(tuple.Item1, fn, tuple.Item2, default(EndpointId));
            }
        }
        
        public class UpdateWorkData : WorkData
        {
            public int bucket;
            public UpdateWorkData(int bucket)
            {
                this.bucket = bucket;
            }
        }
    }
}