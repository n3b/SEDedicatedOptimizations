using System;
using System.Collections.Generic;
using System.Threading;
using Sandbox.Engine.Multiplayer;
using Sandbox.Game;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Cube;
using Sandbox.Game.Replication;
using SEClientFixes.Util;
using VRage.Game.Components;
using VRage.Game.Entity;
using VRage.Library.Collections;
using VRage.Network;
using VRage.Serialization;
using VRageMath;

namespace n3bOptimizations.Replication.Inventory
{
    public class InventoryReplicable : MyExternalReplicableEvent<MyInventory>
    {
        private PropsStateGroup propsGroup;

        private ItemsStateGroup itemsGroup;

        private long m_entityId;

        private int m_inventoryId;

        public override bool HasToBeChild => true;

        public override bool IsValid => Instance?.Entity?.MarkedForClose == false;

        static List<TimerUtil> timers = new List<TimerUtil>
        {
            new TimerUtil()
        };

        static HashSet<IMarkDirty> dirtyGroups = new HashSet<IMarkDirty>();

        protected override void OnHook()
        {
            base.OnHook();
            if (Instance == null) return;

            itemsGroup = new ItemsStateGroup(Instance, this);
            itemsGroup.interval = Plugin.StaticConfig.InventoryInterval;
            propsGroup = new PropsStateGroup(this, Instance.SyncType);
            Instance.BeforeRemovedFromContainer += OnRemovedFromContainer;
            MyCubeBlock myCubeBlock = Instance.Owner as MyCubeBlock;
            if (myCubeBlock != null)
            {
                myCubeBlock.SlimBlock.CubeGridChanged += OnBlockCubeGridChanged;
                myCubeBlock.CubeGrid.OnStaticChanged += OnGridIsStaticChanged;
                myCubeBlock.CubeGrid.OnGridSplit += OnGridSplit;
                OnGridIsStaticChanged(myCubeBlock.CubeGrid, myCubeBlock.CubeGrid.IsStatic);
                m_parent = FindByObject(myCubeBlock.CubeGrid);
                return;
            }

            m_parent = FindByObject(Instance.Owner);
        }

        private void OnBlockCubeGridChanged(MySlimBlock slimBlock, MyCubeGrid grid)
        {
            m_parent = FindByObject((Instance.Owner as MyCubeBlock)?.CubeGrid);
            (MyMultiplayer.ReplicationLayer as MyReplicationLayer).RefreshReplicableHierarchy(this);
        }

        private void OnGridIsStaticChanged(MyCubeGrid grid, bool isStatic)
        {
            propsGroup.interval = isStatic ? Plugin.StaticConfig.InventoryInterval : 0;
        }

        private void OnGridSplit(MyCubeGrid original, MyCubeGrid newGrid)
        {
            original.OnStaticChanged -= OnGridIsStaticChanged;
            newGrid.OnStaticChanged += OnGridIsStaticChanged;
            OnGridIsStaticChanged(newGrid, newGrid.IsStatic);
        }

        public static void Schedule(IMarkDirty group, int interval)
        {
            dirtyGroups.Add(group);
            timers[0].Throttle(interval, ProcessDirtyGroups);
        }

        public static void ResetSchedule(IMarkDirty group)
        {
            dirtyGroups.Remove(group);
        }

        static void ProcessDirtyGroups(object param)
        {
            var dirty = Interlocked.Exchange(ref dirtyGroups, new HashSet<IMarkDirty>());
            foreach (var group in dirty)
            {
                group.MarkDirty();
            }
        }

        public override IMyReplicable GetParent()
        {
            if (m_parent == null) m_parent = FindByObject(Instance.Owner);
            return m_parent;
        }

        public override bool OnSave(BitStream stream, Endpoint clientEndpoint)
        {
            long entityId = Instance.Owner.EntityId;
            MySerializer.Write(stream, ref entityId, null);
            int num = 0;
            for (int i = 0; i < Instance.Owner.InventoryCount; i++)
            {
                if (Instance == Instance.Owner.GetInventory(i))
                {
                    num = i;
                    break;
                }
            }

            MySerializer.Write(stream, ref num);
            return true;
        }

        protected override void OnLoad(BitStream stream, Action<MyInventory> loadingDoneHandler)
        {
            if (stream != null)
            {
                MySerializer.CreateAndRead(stream, out m_entityId, null);
                MySerializer.CreateAndRead(stream, out m_inventoryId, null);
            }

            MyEntities.CallAsync(delegate() { LoadAsync(loadingDoneHandler); });
        }

        private void LoadAsync(Action<MyInventory> loadingDoneHandler)
        {
            MyEntity myEntity;
            MyEntities.TryGetEntityById(m_entityId, out myEntity, false);
            MyInventory obj = null;
            MyEntity myEntity2 = (myEntity != null && myEntity.HasInventory) ? myEntity : null;
            if (myEntity2 != null && !myEntity2.GetTopMostParent(null).MarkedForClose)
            {
                obj = myEntity2.GetInventory(m_inventoryId);
            }

            loadingDoneHandler(obj);
        }

        public override void OnDestroyClient()
        {
        }

        public override void GetStateGroups(List<IMyStateGroup> resultList)
        {
            resultList.Add(itemsGroup);
            resultList.Add(propsGroup);
        }

        public override string ToString()
        {
            string str = (Instance != null) ? ((Instance.Owner != null) ? Instance.Owner.EntityId.ToString() : "<owner null>") : "<inventory null>";
            return string.Format("MyInventoryReplicable, Owner id: " + str, Array.Empty<object>());
        }

        private void OnRemovedFromContainer(MyEntityComponentBase obj)
        {
            if (!(Instance?.Owner is MyCubeBlock block)) return;

            dirtyGroups.Remove(this.itemsGroup);
            dirtyGroups.Remove(this.propsGroup);

            block.SlimBlock.CubeGridChanged -= OnBlockCubeGridChanged;
            block.CubeGrid.OnStaticChanged -= OnGridIsStaticChanged;
            block.CubeGrid.OnGridSplit -= OnGridSplit;
            RaiseDestroyed();
        }

        public override BoundingBoxD GetAABB()
        {
            return BoundingBoxD.CreateInvalid();
        }

        public override ValidationResult HasRights(EndpointId endpointId, ValidationType validationFlags)
        {
            MyExternalReplicable myExternalReplicable = FindByObject(Instance.Owner);
            if (myExternalReplicable != null)
            {
                return myExternalReplicable.HasRights(endpointId, validationFlags);
            }

            return base.HasRights(endpointId, validationFlags);
        }

        public void RefreshClientData(Endpoint currentSerializationDestinationEndpoint)
        {
            itemsGroup.RefreshClientData(currentSerializationDestinationEndpoint);
        }

        public void Refresh()
        {
            itemsGroup.MarkDirty();
            propsGroup.MarkDirty();
        }
    }

    public interface IMarkDirty
    {
        public void MarkDirty();
    }
}