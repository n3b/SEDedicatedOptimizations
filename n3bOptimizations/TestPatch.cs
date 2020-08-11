using HarmonyLib;

namespace n3bOptimizations
{
    public class TestPatch : IPatch
    {
        public bool Inject(Harmony harmony)
        {
            // var source = AccessTools.Method(typeof(MyEntityThrustComponent), "UpdateConveyorSystemChanges");
            // var patch = AccessTools.Method(typeof(TestPatch), "Test");
            // harmony.Patch(source, new HarmonyMethod(patch));
            // return true;
            return false;
        }

        public static void Test()
        {
        }
    }
}