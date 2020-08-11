using System;
using HarmonyLib;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Character;
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
            var top = entity.GetTopMostParent(null);
            if (top is MyCubeGrid || top is MyCharacter) top.SetSafeZone(__instance);
        }

        public static void RemoveEntityInternalPostfix(MyEntity entity, ref bool __result)
        {
            if (!__result) return;
            if (entity is MyCubeGrid || entity is MyCharacter) entity.SetSafeZone();
        }

        public bool Inject(Harmony harmony)
        {
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