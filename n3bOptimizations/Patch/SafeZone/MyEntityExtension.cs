using System;
using System.Runtime.CompilerServices;
using Sandbox.Game.Entities;
using VRage.Game.Entity;

namespace n3bOptimizations.Patch.SafeZone
{
    public static class MyEntityExtension
    {
        static ConditionalWeakTable<MyEntity, WeakReference<MySafeZone>> Zones = new ConditionalWeakTable<MyEntity, WeakReference<MySafeZone>>();

        public static MySafeZone? GetSafeZone(this MyEntity @this)
        {
            if (!Zones.TryGetValue(@this, out var reference)) return null;
            return !reference.TryGetTarget(out var zone) ? null : zone;
        }

        public static void SetSafeZone(this MyEntity @this, MySafeZone zone = null)
        {
            Zones.Remove(@this);
            if (zone != null) Zones.Add(@this, new WeakReference<MySafeZone>(zone));
        }
    }
}