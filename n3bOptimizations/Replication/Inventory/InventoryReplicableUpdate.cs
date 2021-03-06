﻿using System;
using System.Collections.Generic;
using System.Linq;
using n3bOptimizations.Util;
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

        private static ConcurrentHashSet<IMarkDirty> _changedOwnership = new ConcurrentHashSet<IMarkDirty>();

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
                Plugin.Error("", e);
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
                Plugin.Error("", e);
            }
        }

        public static void Update()
        {
            try
            {
                ApplyChangedOwnership();

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
                Plugin.Error("Unable to process inventories update", e);
            }
        }

        static void ApplyChangedOwnership()
        {
            while (_changedOwnership.Count > 0)
            {
                var item = _changedOwnership.First();
                if (!_changedOwnership.TryRemove(item)) continue;
                item.UpdateOwnership();
                item.MarkDirty();
            }
        }

        public static void OnChangedOwnership(IMarkDirty group)
        {
            _changedOwnership.Add(group);
        }

        public static void ResetChangedOwnership(IMarkDirty group)
        {
            _changedOwnership.TryRemove(group);
        }
    }

    public interface IMarkDirty
    {
        public int Batch { get; }
        public bool Scheduled { get; set; }
        public void MarkDirty();
        public void UpdateOwnership();
    }
}