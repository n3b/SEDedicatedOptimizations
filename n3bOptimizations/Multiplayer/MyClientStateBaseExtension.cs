using n3b.SEMultiplayer;
using VRage.Network;

namespace n3bOptimizations.Patch.Inventory
{
    public static class MyClientStateBaseExtension
    {
        public static ClientStateCompanion Companion(this MyClientStateBase state)
        {
            return ClientStateCompanion.Get(state.EndpointId.Id);
        }
    }
}