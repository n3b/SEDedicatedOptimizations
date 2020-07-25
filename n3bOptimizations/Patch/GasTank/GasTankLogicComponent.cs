using Sandbox.Common.ObjectBuilders;
using Sandbox.Game.Entities.Blocks;
using VRage.Game.Components;

namespace n3bOptimizations.Patch.GasTank
{
    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_OxygenTank), useEntityUpdate: false)]
    public class GasTankLogicComponent : MyGameLogicComponent
    {
        public override void Close()
        {
            var instance = Entity as MyGasTank;
            if (instance == null) return;
            UpdateWork.tanksUpdated.TryRemove(instance.GetHashCode(), out var tuple);
            base.Close();
        }
    }
}