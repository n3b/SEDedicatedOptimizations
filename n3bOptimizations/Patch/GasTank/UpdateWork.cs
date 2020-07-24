using System;
using System.Collections.Concurrent;
using NLog;
using ParallelTasks;
using Sandbox.Engine.Multiplayer;
using Sandbox.Game.Entities.Blocks;

namespace n3bOptimizations.Patch.GasTank
{
    public static class UpdateWork
    {
        public static ConcurrentDictionary<MyGasTank, double> tanksUpdated = new ConcurrentDictionary<MyGasTank, double>();

        public static readonly Logger Log = LogManager.GetCurrentClassLogger();

        public static void DoWork(WorkData wd)
        {
            var bucket = (wd as UpdateWorkData).bucket;
            foreach (var tank in tanksUpdated.Keys)
            {
                if ((tank.GetHashCode() & int.MaxValue) % GasTankThrottle.buckets != 0) continue;
                tanksUpdated.TryRemove(tank, out var amount);
                Func<MyGasTank, Action<double>> fn = (MyGasTank x) =>
                    (Action<double>) GasTankThrottle.cb.CreateDelegate(typeof(Action<double>), x);
                MyMultiplayer.RaiseEvent<MyGasTank, double>(tank, fn, amount);
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