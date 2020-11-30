using System;
using System.Collections.Generic;
using Sandbox.Engine.Multiplayer;
using Sandbox.Game;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Cube;
using Sandbox.Game.Replication;
using VRage.Game.Components;
using VRage.Library.Collections;
using VRage.Network;
using VRage.Serialization;
using VRageMath;

namespace n3bOptimizations.Replication.Inventory
{
    public class InventoryReplicable : MyExternalReplicableEvent<MyInventory>
    {
        ItemsStateGroup itemsGroup;

        long m_entityId;

        int m_inventoryId;
        PropsStateGroup propsGroup;

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
                if (block is MyTerminalBlock tb && Plugin.StaticConfig.InventoryPreventSharing) tb.OwnershipChanged += OnOwnershipChanged;
            }
            else
            {
                m_parent = FindByObject(Instance.Owner);
            }
        }

        void OnBlockCubeGridChanged(MySlimBlock slimBlock, MyCubeGrid grid)
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
            var entityId = Instance.Owner.EntityId;
            MySerializer.Write(stream, ref entityId);
            var num = 0;
            for (var i = 0; i < Instance.Owner.InventoryCount; i++)
                if (Instance == Instance.Owner.GetInventory(i))
                {
                    num = i;
                    break;
                }

            MySerializer.Write(stream, ref num);
            return true;
        }

        protected override void OnLoad(BitStream stream, Action<MyInventory> loadingDoneHandler)
        {
            if (stream != null)
            {
                MySerializer.CreateAndRead(stream, out m_entityId);
                MySerializer.CreateAndRead(stream, out m_inventoryId);
            }

            MyEntities.CallAsync(delegate { LoadAsync(loadingDoneHandler); });
        }

        void LoadAsync(Action<MyInventory> loadingDoneHandler)
        {
            MyEntities.TryGetEntityById(m_entityId, out var entity);
            MyInventory obj = null;
            var entity2 = entity != null && entity.HasInventory ? entity : null;
            if (entity2 != null && !entity2.GetTopMostParent().MarkedForClose) obj = entity2.GetInventory(m_inventoryId);

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
            string str = Instance != null ? Instance.Owner != null ? Instance.Owner.EntityId.ToString() : "<owner null>" : "<inventory null>";
            return string.Format("MyInventoryReplicable, Owner id: " + str, Array.Empty<object>());
        }

        void OnRemovedFromContainer(MyEntityComponentBase obj)
        {
            InventoryReplicableUpdate.Reset(itemsGroup);
            InventoryReplicableUpdate.Reset(propsGroup);
            if (Instance?.Owner is MyCubeBlock block)
            {
                block.SlimBlock.CubeGridChanged -= OnBlockCubeGridChanged;
                if (block is MyTerminalBlock tb)
                {
                    tb.OwnershipChanged -= OnOwnershipChanged;
                    InventoryReplicableUpdate.ResetChangedOwnership(itemsGroup);
                    InventoryReplicableUpdate.ResetChangedOwnership(propsGroup);
                }
            }

            RaiseDestroyed();
        }

        public override BoundingBoxD GetAABB()
        {
            return BoundingBoxD.CreateInvalid();
        }

        public override ValidationResult HasRights(EndpointId endpointId, ValidationType validationFlags)
        {
            MyExternalReplicable myExternalReplicable = FindByObject(Instance.Owner);
            return myExternalReplicable?.HasRights(endpointId, validationFlags) ?? base.HasRights(endpointId, validationFlags);
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

        void OnOwnershipChanged(MyTerminalBlock block)
        {
            InventoryReplicableUpdate.OnChangedOwnership(itemsGroup);
            InventoryReplicableUpdate.OnChangedOwnership(propsGroup);
        }
    }
}