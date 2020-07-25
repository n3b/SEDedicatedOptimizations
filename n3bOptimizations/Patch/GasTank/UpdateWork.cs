using System;
using System.Collections.Concurrent;
using System.Linq;
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
            foreach (var hash in tanksUpdated.Keys.ToArray())
            {
                try
                {
                    if ((hash & int.MaxValue) % data.bucket != 0) continue;
                    tanksUpdated.TryRemove(hash, out var tuple);
                    if (tuple == null || tuple.Item1 == null) continue;

                    Func<MyGasTank, Action<double>> fn = (MyGasTank x) => (Action<double>) GasTankThrottle.cb.CreateDelegate(typeof(Action<double>), x);
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

            public UpdateWorkData(int bucket)
            {
                this.bucket = bucket;
            }
        }
    }
}