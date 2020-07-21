using HarmonyLib;
using Sandbox.Game;

namespace n3bOptimizations.Patch.Inventory
{
    public class Patch : IPatch
    {
        public void Inject(Harmony harmony)
        {
            harmony.Patch(AccessTools.Constructor(typeof(MyInventory)), null, new HarmonyMethod(AccessTools.Method(typeof(InventoryStorage), "ctor")));
        }
    }
}