using System;
using System.Collections.Generic;
using Sandbox.Engine.Multiplayer;
using Sandbox.Game;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Cube;
using Sandbox.Game.Replication;
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

        protected override void OnHook()
        {
            base.OnHook();
            if (Instance == null) return;

            var batch = InventoryReplicableUpdate.GetNextBatch();
            itemsGroup = new ItemsStateGroup(Instance, this, batch);
            propsGroup = new PropsStateGroup(this, Instance.SyncType, batch);
            Instance.BeforeRemovedFromContainer += OnRemovedFromContainer;

            if (Instance.Owner is MyCubeBlock block)
            {
                block.SlimBlock.CubeGridChanged += OnBlockCubeGridChanged;
                m_parent = FindByObject(block.CubeGrid);
            }
            else
            {
                m_parent = FindByObject(Instance.Owner);
            }
        }

        private void OnBlockCubeGridChanged(MySlimBlock slimBlock, MyCubeGrid grid)
        {
            m_parent = FindByObject((Instance.Owner as MyCubeBlock)?.CubeGrid);
            (MyMultiplayer.ReplicationLayer as MyReplicationLayer).RefreshReplicableHierarchy(this);
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
            InventoryReplicableUpdate.Reset(itemsGroup);
            InventoryReplicableUpdate.Reset(propsGroup);
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
            InventoryReplicableUpdate.Reset(itemsGroup);
            InventoryReplicableUpdate.Reset(propsGroup);
            if (Instance?.Owner is MyCubeBlock block) block.SlimBlock.CubeGridChanged -= OnBlockCubeGridChanged;
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
}