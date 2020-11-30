using System;
using System.Runtime.CompilerServices;
using Sandbox.Game.Entities;
using VRage.Game.Entity;
using VRage.Utils;

namespace n3bOptimizations.Patch.SafeZone
{
    public static class MyEntityExtension
    {
        static ConditionalWeakTable<MyEntity, Companion> _comps = new ConditionalWeakTable<MyEntity, Companion>();

        public static MySafeZone? GetSafeZone(this MyEntity @this)
        {
            try
            {
                _comps.TryGetValue(@this, out var companion);
                companion.ZoneRef.TryGetTarget(out var zone);
                return zone;
            }
            catch (Exception e)
            {
                MyLog.Default.WriteLine(e);
                return null;
            }
        }

        public static void SetSafeZone(this MyEntity @this, MySafeZone? zone)
        {
            try
            {
                _comps.TryGetValue(@this, out var companion);
                companion.ZoneRef = new WeakReference<MySafeZone>(zone);
            }
            catch (Exception e)
            {
                MyLog.Default.WriteLine(e);
            }
        }

        public static void InitSafeZoneCompanion(this MyEntity @this)
        {
            _comps.Add(@this, new Companion());
        }

        class Companion
        {
            public WeakReference<MySafeZone> ZoneRef = new WeakReference<MySafeZone>(null);
        }
    }
}