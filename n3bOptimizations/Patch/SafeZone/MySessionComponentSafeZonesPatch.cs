using System;
using HarmonyLib;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Character;
using VRage.Collections;
using VRage.Game.Entity;
using VRage.Game.ObjectBuilders.Components;

namespace n3bOptimizations.Patch.SafeZone
{
    public class MySessionComponentSafeZonesPatch : IPatch
    {
        public static bool IsActionAllowedPrefix(ref bool __result, MyEntity entity)
        {
            if (entity.GetSafeZone() != null) return true;
            __result = true;
            return false;
        }

        public static void InsertEntityInternalPostfix(MyEntity entity, ref MySafeZone __instance, ref bool __result)
        {
            if (!__result) return;
            var top = entity.GetTopMostParent();
            top.SetSafeZone(__instance);
        }

        public static void RemoveEntityInternalPostfix(MyEntity entity, ref bool __result)
        {
            if (!__result) return;
            entity.SetSafeZone(null);
        }

        public static void InsertEntity_ImplementationPostfix(long entityId, ref MyConcurrentHashSet<long> ___m_containedEntities, ref MySafeZone __instance)
        {
            if (___m_containedEntities.Contains(entityId)) return;
            if (MyEntities.TryGetEntityById(entityId, out var top)) top.SetSafeZone(__instance);
        }

        public bool Inject(Harmony harmony)
        {
            if (!Plugin.StaticConfig.SafeZoneCachingEnabled) return false;

            var source = AccessTools.Method(typeof(MySessionComponentSafeZones), "IsActionAllowed",
                new[] {typeof(MyEntity), typeof(MySafeZoneAction), typeof(long), typeof(ulong)});
            var patch = AccessTools.Method(typeof(MySessionComponentSafeZonesPatch), "IsActionAllowedPrefix");
            harmony.Patch(source, new HarmonyMethod(patch));

            source = AccessTools.Method(typeof(MySafeZone), "InsertEntityInternal");
            patch = AccessTools.Method(typeof(MySessionComponentSafeZonesPatch), "InsertEntityInternalPostfix");
            harmony.Patch(source, null, new HarmonyMethod(patch));

            source = AccessTools.Method(typeof(MySafeZone), "RemoveEntityInternal");
            patch = AccessTools.Method(typeof(MySessionComponentSafeZonesPatch), "RemoveEntityInternalPostfix");
            harmony.Patch(source, null, new HarmonyMethod(patch));

            source = AccessTools.Method(typeof(MySafeZone), "InsertEntity_Implementation");
            patch = AccessTools.Method(typeof(MySessionComponentSafeZonesPatch), "InsertEntity_ImplementationPostfix");
            harmony.Patch(source, null, new HarmonyMethod(patch));

            var ctor = typeof(MyEntity).GetConstructor(new Type[] { });
            patch = AccessTools.Method(typeof(MySessionComponentSafeZonesPatch), "EntityCtor");
            harmony.Patch(ctor, null, new HarmonyMethod(patch));


            return true;
        }

        public static void EntityCtor(MyEntity __instance)
        {
            if (__instance is MyCubeGrid || __instance is MyCharacter) __instance.InitSafeZoneCompanion();
        }
    }
}