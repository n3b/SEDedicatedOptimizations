using System;
using System.Collections.Concurrent;
using ParallelTasks;
using Sandbox.Engine.Multiplayer;
using Sandbox.Game.Entities.Blocks;

namespace n3bOptimizations.Patch.GasTank
{
    public static class UpdateWork
    {
        public static ConcurrentDictionary<int, Tuple<MyGasTank, double>> tanksUpdated = new ConcurrentDictionary<int, Tuple<MyGasTank, double>>();

        public static void DoWork(WorkData wd)
        {
            var data = (UpdateWorkData) wd;
            if (data == null) return;
            foreach (var hash in tanksUpdated.Keys)
            {
                try
                {
                    if ((hash & int.MaxValue) % data.totalBuckets != data.bucket) continue;
                    tanksUpdated.TryRemove(hash, out var tuple);
                    if (tuple == null || tuple.Item1 == null) continue;

                    Func<MyGasTank, Action<double>> fn = (MyGasTank x) => (Action<double>) GasTankPatch.cb.CreateDelegate(typeof(Action<double>), x);
                    MyMultiplayer.RaiseEvent<MyGasTank, double>(tuple.Item1, fn, tuple.Item2);
                }
                catch (Exception e)
                {
                    Plugin.Log.Error(e);
                }
            }
        }

        public class UpdateWorkData : WorkData
        {
            public int bucket;
            public int totalBuckets;

            public UpdateWorkData(int bucket, int totalBuckets)
            {
                this.bucket = bucket;
                this.totalBuckets = totalBuckets;
            }
        }
    }
}