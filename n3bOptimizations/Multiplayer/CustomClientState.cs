using System;
using System.Collections.Generic;
using n3bOptimizations.Replication.Inventory;
using Sandbox.Engine.Multiplayer;
using Sandbox.Game;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Inventory;
using Sandbox.Game.Gui;
using Sandbox.Game.Replication;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRage.Library.Collections;

namespace n3bOptimizations.Multiplayer
{
    public class CustomClientState : MyClientState
    {
        public bool APIEnabled = false;

        HashSet<MyInventory> inventories = new HashSet<MyInventory>();

        public bool IsSubscribedToInventory(MyInventory inventory)
        {
            return !APIEnabled || inventories.Contains(inventory);
        }

        public void SubscribeInventory(MyInventoryBase inventory)
        {
            try
            {
                if (inventory is MyInventoryAggregate agg)
                    foreach (MyInventory i in agg.ChildList.Reader)
                    {
                        if (i == null) continue;
                        inventories.Add(i);
                        if (MyExternalReplicable.FindByObject(i) is InventoryReplicable replicable) replicable.Refresh();
                    }
                else if (inventory is MyInventory i && i != null)
                {
                    inventories.Add(i);
                    if (MyExternalReplicable.FindByObject(i) is InventoryReplicable replicable) replicable.Refresh();
                }
            }
            catch (Exception e)
            {
                Plugin.Log.Error(e);
            }
        }

        public void ClearInventorySubscriptions()
        {
            inventories.Clear();
        }

        private static MyContextKind GetContextByPage(MyTerminalPageEnum page)
        {
            switch (page)
            {
                case MyTerminalPageEnum.Inventory:
                    return MyContextKind.Inventory;
                case MyTerminalPageEnum.ControlPanel:
                    return MyContextKind.Terminal;
                case MyTerminalPageEnum.Production:
                    return MyContextKind.Production;
                default:
                    return MyContextKind.None;
            }
        }

        protected override void WriteInternal(BitStream stream, MyEntity controlledEntity)
        {
            MyContextKind contextByPage = GetContextByPage(MyGuiScreenTerminal.GetCurrentScreen());
            stream.WriteInt32((int) contextByPage, 2);
            if (contextByPage != MyContextKind.None)
            {
                long value = (MyGuiScreenTerminal.InteractedEntity != null) ? MyGuiScreenTerminal.InteractedEntity.EntityId : 0L;
                stream.WriteInt64(value, 64);
            }
        }

        protected override void ReadInternal(BitStream stream, MyEntity controlledEntity)
        {
            Context = (MyContextKind) stream.ReadInt32(2);
            if (Context != MyContextKind.None)
            {
                long entityId = stream.ReadInt64(64);
                ContextEntity = MyEntities.GetEntityByIdOrDefault(entityId, null, true);
                if (ContextEntity != null && ContextEntity.GetTopMostParent().MarkedForClose)
                {
                    ContextEntity = null;
                }
            }
            else
            {
                ContextEntity = null;
            }
        }
    }
}