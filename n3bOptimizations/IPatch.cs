using HarmonyLib;

namespace n3bOptimizations
{
    public interface IPatch
    {
        public void Inject(Harmony harmony);
    }
}