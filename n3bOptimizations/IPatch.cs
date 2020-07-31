using HarmonyLib;

namespace n3bOptimizations
{
    public interface IPatch
    {
        public bool Inject(Harmony harmony);
    }
}