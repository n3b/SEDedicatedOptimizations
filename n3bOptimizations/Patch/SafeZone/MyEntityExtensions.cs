using System;
using System.Runtime.CompilerServices;
using Sandbox.Game.Entities;
using VRage.Game.Entity;

namespace n3bOptimizations.Patch.SafeZone
{
    public static class MyEntityExtension
    {
        static ConditionalWeakTable<MyEntity, Companion> _comps = new ConditionalWeakTable<MyEntity, Companion>();

        public static MySafeZone? GetSafeZone(this MyEntity @this)
        {
            if (!_comps.TryGetValue(@this, out var companion)) return null;
            return !companion.ZoneRef.TryGetTarget(out var zone) ? null : zone;
        }

        public static void SetSafeZone(this MyEntity @this, MySafeZone zone = null)
        {
            if (!_comps.TryGetValue(@this, out var companion)) return;
            companion.ZoneRef = new WeakReference<MySafeZone>(zone);
        }

        public static void InitSafeZoneCompanion(this MyEntity @this)
        {
            _comps.Remove(@this);
            _comps.Add(@this, new Companion());
        }

        class Companion
        {
            public WeakReference<MySafeZone> ZoneRef = new WeakReference<MySafeZone>(null);
        }
    }
}