using System;
using System.Collections.Generic;
using System.Linq;
using Sandbox;

namespace n3bOptimizations.Replication.Inventory
{
    public class InventoryReplicableUpdate
    {
        private static bool _enabled = false;
        private static int _count = 0;
        private static int _batches = 1;
        private static int _current = 0;
        private static int _interval = 10;
        private static ulong _lastFrame = 0;

        private static List<HashSet<IMarkDirty>> _dirty;

        public static int ReplicableInterval => _batches * _interval;

        public static void Init()
        {
            var config = Plugin.StaticConfig;
            _enabled = config.InventoryEnabled;
            _batches = config.InventoryBatches;
            _interval = config.InventoryInterval;
            _dirty = Enumerable.Range(0, (int) _batches).Select(x => new HashSet<IMarkDirty>()).ToList();

#if DEBUG
            Plugin.Log.Warn($"Configured {_batches} batches with {_interval} frames");
#endif
        }

        public static int GetNextBatch()
        {
            _count++;
            return _count % _batches;
        }

        public static void Schedule(IMarkDirty group)
        {
            try
            {
                if (group.Scheduled) return;
                _dirty[group.Batch].Add(group);
                group.Scheduled = true;
            }
            catch (Exception e)
            {
                Plugin.Log.Error(e);
            }
        }

        public static void Reset(IMarkDirty group)
        {
            try
            {
                if (!group.Scheduled) return;
                _dirty[group.Batch].Remove(group);
                group.Scheduled = false;
            }
            catch (Exception e)
            {
                Plugin.Log.Error(e);
            }
        }

        public static void Update()
        {
            try
            {
                if (!_enabled) return;
                var counter = MySandboxGame.Static.SimulationFrameCounter;
                if (_lastFrame + (uint) _interval > counter) return;
                _lastFrame = counter;

                var dirty = _dirty[_current];
                _dirty[_current] = new HashSet<IMarkDirty>();
#if DEBUG
                Plugin.Log.Warn($"Updating inventories, frame {counter} batch {_current}, {dirty.Count} inventories");
#endif
                foreach (var group in dirty)
                {
                    group.Scheduled = false;
                    group.MarkDirty();
                }

                _current++;
                if (_current == _batches) _current = 0;
            }
            catch (Exception e)
            {
                Plugin.Log.Error("Unable to process inventories update");
                Plugin.Log.Error(e);
            }
        }
    }

    public interface IMarkDirty
    {
        public int Batch { get; }
        public bool Scheduled { get; set; }
        public void MarkDirty();
    }
}