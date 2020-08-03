using System.Collections.Generic;
using Sandbox;
using Sandbox.Common.ObjectBuilders.Definitions;
using Sandbox.Engine.Multiplayer;
using Sandbox.Game;
using VRage;
using VRage.Game.Entity;
using VRage.Library.Collections;
using VRage.Library.Utils;
using VRage.Network;
using VRage.ObjectBuilders;
using VRage.Serialization;

namespace n3bOptimizations.Replication.Inventory
{
    public class ItemsStateGroup : IMyStateGroup, IMyNetObject, IMyEventOwner, IMarkDirty
    {
        public bool IsHighPriority => false;

        public MyInventory Inventory { get; }
        public IMyReplicable Owner { get; }

        public bool IsValid => Owner != null && Owner.IsValid;

        public bool IsStreaming => false;

        public bool NeedsUpdate => false;

        private MyReplicationServer _server;

        private ulong _lastFrame = 0;

        public bool Scheduled { get; set; }

        public int Batch { get; }

        public ItemsStateGroup(MyInventory entity, IMyReplicable owner, int batch)
        {
            Inventory = entity;
            Batch = batch;
            m_clientInventoryUpdate = new Dictionary<Endpoint, InventoryClientData>();
            Inventory.ContentsChanged += InventoryChanged;
            Owner = owner;
            _server = (MyReplicationServer) MyMultiplayer.Static.ReplicationLayer;
        }

        private void InventoryChanged(MyInventoryBase obj)
        {
            var counter = MySandboxGame.Static.SimulationFrameCounter;
            if (_lastFrame + (uint) InventoryReplicableUpdate.ReplicableInterval > counter) InventoryReplicableUpdate.Schedule(this);
            else
            {
                InventoryReplicableUpdate.Reset(this);
                MarkDirty();
            }
        }

        public void MarkDirty()
        {
            var counter = MySandboxGame.Static.SimulationFrameCounter;
            if (_lastFrame == counter) return;
            _lastFrame = counter;

            foreach (KeyValuePair<Endpoint, InventoryClientData> keyValuePair in m_clientInventoryUpdate)
            {
                m_clientInventoryUpdate[keyValuePair.Key].Dirty = true;
            }

            _server.AddToDirtyGroups(this);
        }

        public void CreateClientData(MyClientStateBase forClient)
        {
            CreateClientData(forClient.EndpointId);
        }

        public void RefreshClientData(Endpoint clientEndpoint)
        {
            m_clientInventoryUpdate.Remove(clientEndpoint);
            CreateClientData(clientEndpoint);
        }

        private void CreateClientData(Endpoint clientEndpoint)
        {
            if (!m_clientInventoryUpdate.TryGetValue(clientEndpoint, out var inventoryClientData))
            {
                inventoryClientData = new InventoryClientData();
                m_clientInventoryUpdate[clientEndpoint] = inventoryClientData;
            }

            inventoryClientData.Dirty = false;
            foreach (MyPhysicalInventoryItem myPhysicalInventoryItem in Inventory.GetItems())
            {
                MyFixedPoint amount = myPhysicalInventoryItem.Amount;
                if (myPhysicalInventoryItem.Content is MyObjectBuilder_GasContainerObject gas)
                {
                    amount = (MyFixedPoint) gas.GasLevel;
                }

                ClientInvetoryData clientInvetoryData = new ClientInvetoryData
                {
                    Item = myPhysicalInventoryItem,
                    Amount = amount
                };
                inventoryClientData.ClientItemsSorted[myPhysicalInventoryItem.ItemId] = clientInvetoryData;
                inventoryClientData.ClientItems.Add(clientInvetoryData);
            }
        }

        public void DestroyClientData(MyClientStateBase forClient)
        {
            m_clientInventoryUpdate.Remove(forClient.EndpointId);
        }

        public void ClientUpdate(MyTimeSpan clientTimestamp)
        {
        }

        public void Destroy()
        {
            Inventory.ContentsChanged -= InventoryChanged;
            _server = null;
        }

        public void Serialize(BitStream stream, Endpoint forClient, MyTimeSpan serverTimestamp, MyTimeSpan lastClientTimestamp, byte packetId, int maxBitPosition,
            HashSet<string> cachedData)
        {
            if (!stream.Writing)
            {
                ReadInventory(stream);
                return;
            }

            InventoryClientData inventoryClientData;
            if (m_clientInventoryUpdate == null || !m_clientInventoryUpdate.TryGetValue(forClient, out inventoryClientData))
            {
                stream.WriteBool(false);
                stream.WriteUInt32(0U, 32);
                return;
            }

            bool flag = false;
            if (inventoryClientData.FailedIncompletePackets.Count > 0)
            {
                InventoryDeltaInformation inventoryDeltaInformation = inventoryClientData.FailedIncompletePackets[0];
                inventoryClientData.FailedIncompletePackets.RemoveAtFast(0);
                InventoryDeltaInformation value = WriteInventory(ref inventoryDeltaInformation, stream, packetId, maxBitPosition, out flag);
                value.MessageId = inventoryDeltaInformation.MessageId;
                if (flag)
                {
                    inventoryClientData.FailedIncompletePackets.Add(CreateSplit(ref inventoryDeltaInformation, ref value));
                }

                inventoryClientData.SendPackets[packetId] = value;
                return;
            }

            InventoryDeltaInformation inventoryDeltaInformation2 = CalculateInventoryDiff(ref inventoryClientData);
            inventoryDeltaInformation2.MessageId = inventoryClientData.CurrentMessageId;
            inventoryClientData.MainSendingInfo = WriteInventory(ref inventoryDeltaInformation2, stream, packetId, maxBitPosition, out flag);
            inventoryClientData.SendPackets[packetId] = inventoryClientData.MainSendingInfo;
            inventoryClientData.CurrentMessageId += 1U;
            if (flag)
            {
                InventoryDeltaInformation item = CreateSplit(ref inventoryDeltaInformation2, ref inventoryClientData.MainSendingInfo);
                item.MessageId = inventoryClientData.CurrentMessageId;
                inventoryClientData.FailedIncompletePackets.Add(item);
                inventoryClientData.CurrentMessageId += 1U;
            }

            inventoryClientData.Dirty = false;
        }

        private void ReadInventory(BitStream stream)
        {
            bool flag = stream.ReadBool();
            uint num = stream.ReadUInt32(32);
            bool flag2 = true;
            bool flag3 = false;
            InventoryDeltaInformation inventoryDeltaInformation = default(InventoryDeltaInformation);
            if (num == m_nextExpectedPacketId)
            {
                m_nextExpectedPacketId += 1U;
                if (!flag)
                {
                    FlushBuffer();
                    return;
                }
            }
            else if (num > m_nextExpectedPacketId && !m_buffer.ContainsKey(num))
            {
                flag3 = true;
                inventoryDeltaInformation.MessageId = num;
            }
            else
            {
                flag2 = false;
            }

            if (!flag)
            {
                if (flag3)
                {
                    m_buffer.Add(num, inventoryDeltaInformation);
                }

                return;
            }

            if (stream.ReadBool())
            {
                int num2 = stream.ReadInt32(32);
                for (int i = 0; i < num2; i++)
                {
                    uint num3 = stream.ReadUInt32(32);
                    MyFixedPoint myFixedPoint = default(MyFixedPoint);
                    myFixedPoint.RawValue = stream.ReadInt64(64);
                    if (flag2)
                    {
                        if (flag3)
                        {
                            if (inventoryDeltaInformation.ChangedItems == null)
                            {
                                inventoryDeltaInformation.ChangedItems = new Dictionary<uint, MyFixedPoint>();
                            }

                            inventoryDeltaInformation.ChangedItems.Add(num3, myFixedPoint);
                        }
                        else
                        {
                            Inventory.UpdateItemAmoutClient(num3, myFixedPoint);
                        }
                    }
                }
            }

            if (stream.ReadBool())
            {
                int num4 = stream.ReadInt32(32);
                for (int j = 0; j < num4; j++)
                {
                    uint num5 = stream.ReadUInt32(32);
                    if (flag2)
                    {
                        if (flag3)
                        {
                            if (inventoryDeltaInformation.RemovedItems == null)
                            {
                                inventoryDeltaInformation.RemovedItems = new List<uint>();
                            }

                            inventoryDeltaInformation.RemovedItems.Add(num5);
                        }
                        else
                        {
                            Inventory.RemoveItemClient(num5);
                        }
                    }
                }
            }

            if (stream.ReadBool())
            {
                int num6 = stream.ReadInt32(32);
                for (int k = 0; k < num6; k++)
                {
                    int num7 = stream.ReadInt32(32);
                    MyPhysicalInventoryItem myPhysicalInventoryItem;
                    MySerializer.CreateAndRead<MyPhysicalInventoryItem>(stream, out myPhysicalInventoryItem, MyObjectBuilderSerializer.Dynamic);
                    if (flag2)
                    {
                        if (flag3)
                        {
                            if (inventoryDeltaInformation.NewItems == null)
                            {
                                inventoryDeltaInformation.NewItems = new SortedDictionary<int, MyPhysicalInventoryItem>();
                            }

                            inventoryDeltaInformation.NewItems.Add(num7, myPhysicalInventoryItem);
                        }
                        else
                        {
                            Inventory.AddItemClient(num7, myPhysicalInventoryItem);
                        }
                    }
                }
            }

            if (stream.ReadBool())
            {
                if (m_tmpSwappingList == null)
                {
                    m_tmpSwappingList = new Dictionary<int, MyPhysicalInventoryItem>();
                }

                int num8 = stream.ReadInt32(32);
                for (int l = 0; l < num8; l++)
                {
                    uint num9 = stream.ReadUInt32(32);
                    int num10 = stream.ReadInt32(32);
                    if (flag2)
                    {
                        if (flag3)
                        {
                            if (inventoryDeltaInformation.SwappedItems == null)
                            {
                                inventoryDeltaInformation.SwappedItems = new Dictionary<uint, int>();
                            }

                            inventoryDeltaInformation.SwappedItems.Add(num9, num10);
                        }
                        else
                        {
                            MyPhysicalInventoryItem? itemByID = Inventory.GetItemByID(num9);
                            if (itemByID != null)
                            {
                                m_tmpSwappingList.Add(num10, itemByID.Value);
                            }
                        }
                    }
                }

                foreach (KeyValuePair<int, MyPhysicalInventoryItem> keyValuePair in m_tmpSwappingList)
                {
                    Inventory.ChangeItemClient(keyValuePair.Value, keyValuePair.Key);
                }

                m_tmpSwappingList.Clear();
            }

            if (flag3)
            {
                m_buffer.Add(num, inventoryDeltaInformation);
            }
            else if (flag2)
            {
                FlushBuffer();
            }

            Inventory.Refresh();
        }

        private void FlushBuffer()
        {
            while (m_buffer.Count > 0)
            {
                InventoryDeltaInformation inventoryDeltaInformation = m_buffer.Values[0];
                if (inventoryDeltaInformation.MessageId != m_nextExpectedPacketId) break;
                m_nextExpectedPacketId += 1U;
                ApplyChangesOnClient(inventoryDeltaInformation);
                m_buffer.RemoveAt(0);
            }
        }

        private void ApplyChangesOnClient(InventoryDeltaInformation changes)
        {
            if (changes.ChangedItems != null)
            {
                foreach (KeyValuePair<uint, MyFixedPoint> keyValuePair in changes.ChangedItems)
                {
                    Inventory.UpdateItemAmoutClient(keyValuePair.Key, keyValuePair.Value);
                }
            }

            if (changes.RemovedItems != null)
            {
                foreach (uint itemId in changes.RemovedItems)
                {
                    Inventory.RemoveItemClient(itemId);
                }
            }

            if (changes.NewItems != null)
            {
                foreach (KeyValuePair<int, MyPhysicalInventoryItem> keyValuePair2 in changes.NewItems)
                {
                    Inventory.AddItemClient(keyValuePair2.Key, keyValuePair2.Value);
                }
            }

            if (changes.SwappedItems != null)
            {
                if (m_tmpSwappingList == null)
                {
                    m_tmpSwappingList = new Dictionary<int, MyPhysicalInventoryItem>();
                }

                foreach (KeyValuePair<uint, int> keyValuePair3 in changes.SwappedItems)
                {
                    MyPhysicalInventoryItem? itemByID = Inventory.GetItemByID(keyValuePair3.Key);
                    if (itemByID != null)
                    {
                        m_tmpSwappingList.Add(keyValuePair3.Value, itemByID.Value);
                    }
                }

                foreach (KeyValuePair<int, MyPhysicalInventoryItem> keyValuePair4 in m_tmpSwappingList)
                {
                    Inventory.ChangeItemClient(keyValuePair4.Value, keyValuePair4.Key);
                }

                m_tmpSwappingList.Clear();
            }
        }

        private InventoryDeltaInformation CalculateInventoryDiff(ref InventoryClientData clientData)
        {
            if (m_itemsToSend == null)
            {
                m_itemsToSend = new List<MyPhysicalInventoryItem>();
            }

            if (m_foundDeltaItems == null)
            {
                m_foundDeltaItems = new HashSet<uint>();
            }

            m_foundDeltaItems.Clear();
            List<MyPhysicalInventoryItem> items = Inventory.GetItems();
            InventoryDeltaInformation inventoryDeltaInformation;
            CalculateAddsAndRemovals(clientData, out inventoryDeltaInformation, items);
            if (inventoryDeltaInformation.HasChanges)
            {
                ApplyChangesToClientItems(clientData, ref inventoryDeltaInformation);
            }

            for (int i = 0; i < items.Count; i++)
            {
                if (i < clientData.ClientItems.Count)
                {
                    uint itemId = clientData.ClientItems[i].Item.ItemId;
                    if (itemId != items[i].ItemId)
                    {
                        if (inventoryDeltaInformation.SwappedItems == null)
                        {
                            inventoryDeltaInformation.SwappedItems = new Dictionary<uint, int>();
                        }

                        for (int j = 0; j < items.Count; j++)
                        {
                            if (itemId == items[j].ItemId)
                            {
                                inventoryDeltaInformation.SwappedItems[itemId] = j;
                            }
                        }
                    }
                }
            }

            clientData.ClientItemsSorted.Clear();
            clientData.ClientItems.Clear();
            foreach (MyPhysicalInventoryItem myPhysicalInventoryItem in items)
            {
                MyFixedPoint amount = myPhysicalInventoryItem.Amount;
                MyObjectBuilder_GasContainerObject myObjectBuilder_GasContainerObject = myPhysicalInventoryItem.Content as MyObjectBuilder_GasContainerObject;
                if (myObjectBuilder_GasContainerObject != null)
                {
                    amount = (MyFixedPoint) myObjectBuilder_GasContainerObject.GasLevel;
                }

                ClientInvetoryData clientInvetoryData = new ClientInvetoryData
                {
                    Item = myPhysicalInventoryItem,
                    Amount = amount
                };
                clientData.ClientItemsSorted[myPhysicalInventoryItem.ItemId] = clientInvetoryData;
                clientData.ClientItems.Add(clientInvetoryData);
            }

            return inventoryDeltaInformation;
        }

        private static void ApplyChangesToClientItems(InventoryClientData clientData, ref InventoryDeltaInformation delta)
        {
            if (delta.RemovedItems != null)
            {
                foreach (uint num in delta.RemovedItems)
                {
                    int num2 = -1;
                    for (int i = 0; i < clientData.ClientItems.Count; i++)
                    {
                        if (clientData.ClientItems[i].Item.ItemId == num)
                        {
                            num2 = i;
                            break;
                        }
                    }

                    if (num2 != -1)
                    {
                        clientData.ClientItems.RemoveAt(num2);
                    }
                }
            }

            if (delta.NewItems != null)
            {
                foreach (KeyValuePair<int, MyPhysicalInventoryItem> keyValuePair in delta.NewItems)
                {
                    ClientInvetoryData item = new ClientInvetoryData
                    {
                        Item = keyValuePair.Value,
                        Amount = keyValuePair.Value.Amount
                    };
                    if (keyValuePair.Key >= clientData.ClientItems.Count)
                    {
                        clientData.ClientItems.Add(item);
                    }
                    else
                    {
                        clientData.ClientItems.Insert(keyValuePair.Key, item);
                    }
                }
            }
        }

        private void CalculateAddsAndRemovals(InventoryClientData clientData, out InventoryDeltaInformation delta, List<MyPhysicalInventoryItem> items)
        {
            delta = new InventoryDeltaInformation
            {
                HasChanges = false
            };
            int num = 0;
            foreach (MyPhysicalInventoryItem myPhysicalInventoryItem in items)
            {
                ClientInvetoryData clientInvetoryData;
                if (clientData.ClientItemsSorted.TryGetValue(myPhysicalInventoryItem.ItemId, out clientInvetoryData))
                {
                    if (clientInvetoryData.Item.Content.TypeId == myPhysicalInventoryItem.Content.TypeId &&
                        clientInvetoryData.Item.Content.SubtypeId == myPhysicalInventoryItem.Content.SubtypeId)
                    {
                        m_foundDeltaItems.Add(myPhysicalInventoryItem.ItemId);
                        MyFixedPoint myFixedPoint = myPhysicalInventoryItem.Amount;
                        MyObjectBuilder_GasContainerObject myObjectBuilder_GasContainerObject = myPhysicalInventoryItem.Content as MyObjectBuilder_GasContainerObject;
                        if (myObjectBuilder_GasContainerObject != null)
                        {
                            myFixedPoint = (MyFixedPoint) myObjectBuilder_GasContainerObject.GasLevel;
                        }

                        if (clientInvetoryData.Amount != myFixedPoint)
                        {
                            MyFixedPoint value = myFixedPoint - clientInvetoryData.Amount;
                            if (delta.ChangedItems == null)
                            {
                                delta.ChangedItems = new Dictionary<uint, MyFixedPoint>();
                            }

                            delta.ChangedItems[myPhysicalInventoryItem.ItemId] = value;
                            delta.HasChanges = true;
                        }
                    }
                }
                else
                {
                    if (delta.NewItems == null)
                    {
                        delta.NewItems = new SortedDictionary<int, MyPhysicalInventoryItem>();
                    }

                    delta.NewItems[num] = myPhysicalInventoryItem;
                    delta.HasChanges = true;
                }

                num++;
            }

            foreach (KeyValuePair<uint, ClientInvetoryData> keyValuePair in clientData.ClientItemsSorted)
            {
                if (delta.RemovedItems == null)
                {
                    delta.RemovedItems = new List<uint>();
                }

                if (!m_foundDeltaItems.Contains(keyValuePair.Key))
                {
                    delta.RemovedItems.Add(keyValuePair.Key);
                    delta.HasChanges = true;
                }
            }
        }

        private InventoryDeltaInformation WriteInventory(ref InventoryDeltaInformation packetInfo, BitStream stream, byte packetId, int maxBitPosition, out bool needsSplit)
        {
            InventoryDeltaInformation inventoryDeltaInformation = PrepareSendData(ref packetInfo, stream, maxBitPosition, out needsSplit);
            inventoryDeltaInformation.MessageId = packetInfo.MessageId;
            stream.WriteBool(inventoryDeltaInformation.HasChanges);
            stream.WriteUInt32(inventoryDeltaInformation.MessageId, 32);
            if (!inventoryDeltaInformation.HasChanges)
            {
                return inventoryDeltaInformation;
            }

            stream.WriteBool(inventoryDeltaInformation.ChangedItems != null);
            if (inventoryDeltaInformation.ChangedItems != null)
            {
                stream.WriteInt32(inventoryDeltaInformation.ChangedItems.Count, 32);
                foreach (KeyValuePair<uint, MyFixedPoint> keyValuePair in inventoryDeltaInformation.ChangedItems)
                {
                    stream.WriteUInt32(keyValuePair.Key, 32);
                    stream.WriteInt64(keyValuePair.Value.RawValue, 64);
                }
            }

            stream.WriteBool(inventoryDeltaInformation.RemovedItems != null);
            if (inventoryDeltaInformation.RemovedItems != null)
            {
                stream.WriteInt32(inventoryDeltaInformation.RemovedItems.Count, 32);
                foreach (uint value in inventoryDeltaInformation.RemovedItems)
                {
                    stream.WriteUInt32(value, 32);
                }
            }

            stream.WriteBool(inventoryDeltaInformation.NewItems != null);
            if (inventoryDeltaInformation.NewItems != null)
            {
                stream.WriteInt32(inventoryDeltaInformation.NewItems.Count, 32);
                foreach (KeyValuePair<int, MyPhysicalInventoryItem> keyValuePair2 in inventoryDeltaInformation.NewItems)
                {
                    stream.WriteInt32(keyValuePair2.Key, 32);
                    MyPhysicalInventoryItem value2 = keyValuePair2.Value;
                    MySerializer.Write<MyPhysicalInventoryItem>(stream, ref value2, MyObjectBuilderSerializer.Dynamic);
                }
            }

            stream.WriteBool(inventoryDeltaInformation.SwappedItems != null);
            if (inventoryDeltaInformation.SwappedItems != null)
            {
                stream.WriteInt32(inventoryDeltaInformation.SwappedItems.Count, 32);
                foreach (KeyValuePair<uint, int> keyValuePair3 in inventoryDeltaInformation.SwappedItems)
                {
                    stream.WriteUInt32(keyValuePair3.Key, 32);
                    stream.WriteInt32(keyValuePair3.Value, 32);
                }
            }

            return inventoryDeltaInformation;
        }

        private InventoryDeltaInformation PrepareSendData(ref InventoryDeltaInformation packetInfo, BitStream stream, int maxBitPosition, out bool needsSplit)
        {
            needsSplit = false;
            int bitPosition = stream.BitPosition;
            InventoryDeltaInformation inventoryDeltaInformation = new InventoryDeltaInformation
            {
                HasChanges = false
            };
            stream.WriteBool(false);
            stream.WriteUInt32(packetInfo.MessageId, 32);
            stream.WriteBool(packetInfo.ChangedItems != null);
            if (packetInfo.ChangedItems != null)
            {
                stream.WriteInt32(packetInfo.ChangedItems.Count, 32);
                if (stream.BitPosition > maxBitPosition)
                {
                    needsSplit = true;
                }
                else
                {
                    inventoryDeltaInformation.ChangedItems = new Dictionary<uint, MyFixedPoint>();
                    foreach (KeyValuePair<uint, MyFixedPoint> keyValuePair in packetInfo.ChangedItems)
                    {
                        stream.WriteUInt32(keyValuePair.Key, 32);
                        stream.WriteInt64(keyValuePair.Value.RawValue, 64);
                        if (stream.BitPosition <= maxBitPosition)
                        {
                            inventoryDeltaInformation.ChangedItems[keyValuePair.Key] = keyValuePair.Value;
                            inventoryDeltaInformation.HasChanges = true;
                        }
                        else
                        {
                            needsSplit = true;
                        }
                    }
                }
            }

            stream.WriteBool(packetInfo.RemovedItems != null);
            if (packetInfo.RemovedItems != null)
            {
                stream.WriteInt32(packetInfo.RemovedItems.Count, 32);
                if (stream.BitPosition > maxBitPosition)
                {
                    needsSplit = true;
                }
                else
                {
                    inventoryDeltaInformation.RemovedItems = new List<uint>();
                    foreach (uint num in packetInfo.RemovedItems)
                    {
                        stream.WriteUInt32(num, 32);
                        if (stream.BitPosition <= maxBitPosition)
                        {
                            inventoryDeltaInformation.RemovedItems.Add(num);
                            inventoryDeltaInformation.HasChanges = true;
                        }
                        else
                        {
                            needsSplit = true;
                        }
                    }
                }
            }

            stream.WriteBool(packetInfo.NewItems != null);
            if (packetInfo.NewItems != null)
            {
                stream.WriteInt32(packetInfo.NewItems.Count, 32);
                if (stream.BitPosition > maxBitPosition)
                {
                    needsSplit = true;
                }
                else
                {
                    inventoryDeltaInformation.NewItems = new SortedDictionary<int, MyPhysicalInventoryItem>();
                    foreach (KeyValuePair<int, MyPhysicalInventoryItem> keyValuePair2 in packetInfo.NewItems)
                    {
                        MyPhysicalInventoryItem value = keyValuePair2.Value;
                        stream.WriteInt32(keyValuePair2.Key, 32);
                        int bitPosition2 = stream.BitPosition;
                        MySerializer.Write<MyPhysicalInventoryItem>(stream, ref value, MyObjectBuilderSerializer.Dynamic);
                        int bitPosition3 = stream.BitPosition;
                        if (stream.BitPosition <= maxBitPosition)
                        {
                            inventoryDeltaInformation.NewItems[keyValuePair2.Key] = value;
                            inventoryDeltaInformation.HasChanges = true;
                        }
                        else
                        {
                            needsSplit = true;
                        }
                    }
                }
            }

            stream.WriteBool(packetInfo.SwappedItems != null);
            if (packetInfo.SwappedItems != null)
            {
                stream.WriteInt32(packetInfo.SwappedItems.Count, 32);
                if (stream.BitPosition > maxBitPosition)
                {
                    needsSplit = true;
                }
                else
                {
                    inventoryDeltaInformation.SwappedItems = new Dictionary<uint, int>();
                    foreach (KeyValuePair<uint, int> keyValuePair3 in packetInfo.SwappedItems)
                    {
                        stream.WriteUInt32(keyValuePair3.Key, 32);
                        stream.WriteInt32(keyValuePair3.Value, 32);
                        if (stream.BitPosition <= maxBitPosition)
                        {
                            inventoryDeltaInformation.SwappedItems[keyValuePair3.Key] = keyValuePair3.Value;
                            inventoryDeltaInformation.HasChanges = true;
                        }
                        else
                        {
                            needsSplit = true;
                        }
                    }
                }
            }

            stream.SetBitPositionWrite(bitPosition);
            return inventoryDeltaInformation;
        }

        private InventoryDeltaInformation CreateSplit(ref InventoryDeltaInformation originalData, ref InventoryDeltaInformation sentData)
        {
            InventoryDeltaInformation inventoryDeltaInformation = new InventoryDeltaInformation
            {
                MessageId = sentData.MessageId
            };
            if (originalData.ChangedItems != null)
            {
                if (sentData.ChangedItems == null)
                {
                    inventoryDeltaInformation.ChangedItems = new Dictionary<uint, MyFixedPoint>();
                    using (Dictionary<uint, MyFixedPoint>.Enumerator enumerator = originalData.ChangedItems.GetEnumerator())
                    {
                        while (enumerator.MoveNext())
                        {
                            KeyValuePair<uint, MyFixedPoint> keyValuePair = enumerator.Current;
                            inventoryDeltaInformation.ChangedItems[keyValuePair.Key] = keyValuePair.Value;
                        }

                        goto IL_102;
                    }
                }

                if (originalData.ChangedItems.Count != sentData.ChangedItems.Count)
                {
                    inventoryDeltaInformation.ChangedItems = new Dictionary<uint, MyFixedPoint>();
                    foreach (KeyValuePair<uint, MyFixedPoint> keyValuePair2 in originalData.ChangedItems)
                    {
                        if (!sentData.ChangedItems.ContainsKey(keyValuePair2.Key))
                        {
                            inventoryDeltaInformation.ChangedItems[keyValuePair2.Key] = keyValuePair2.Value;
                        }
                    }
                }
            }

            IL_102:
            if (originalData.RemovedItems != null)
            {
                if (sentData.RemovedItems == null)
                {
                    inventoryDeltaInformation.RemovedItems = new List<uint>();
                    using (List<uint>.Enumerator enumerator2 = originalData.RemovedItems.GetEnumerator())
                    {
                        while (enumerator2.MoveNext())
                        {
                            uint item = enumerator2.Current;
                            inventoryDeltaInformation.RemovedItems.Add(item);
                        }

                        goto IL_1D0;
                    }
                }

                if (originalData.RemovedItems.Count != sentData.RemovedItems.Count)
                {
                    inventoryDeltaInformation.RemovedItems = new List<uint>();
                    foreach (uint item2 in originalData.RemovedItems)
                    {
                        if (!sentData.RemovedItems.Contains(item2))
                        {
                            inventoryDeltaInformation.RemovedItems.Add(item2);
                        }
                    }
                }
            }

            IL_1D0:
            if (originalData.NewItems != null)
            {
                if (sentData.NewItems == null)
                {
                    inventoryDeltaInformation.NewItems = new SortedDictionary<int, MyPhysicalInventoryItem>();
                    using (SortedDictionary<int, MyPhysicalInventoryItem>.Enumerator enumerator3 = originalData.NewItems.GetEnumerator())
                    {
                        while (enumerator3.MoveNext())
                        {
                            KeyValuePair<int, MyPhysicalInventoryItem> keyValuePair3 = enumerator3.Current;
                            inventoryDeltaInformation.NewItems[keyValuePair3.Key] = keyValuePair3.Value;
                        }

                        goto IL_2BE;
                    }
                }

                if (originalData.NewItems.Count != sentData.NewItems.Count)
                {
                    inventoryDeltaInformation.NewItems = new SortedDictionary<int, MyPhysicalInventoryItem>();
                    foreach (KeyValuePair<int, MyPhysicalInventoryItem> keyValuePair4 in originalData.NewItems)
                    {
                        if (!sentData.NewItems.ContainsKey(keyValuePair4.Key))
                        {
                            inventoryDeltaInformation.NewItems[keyValuePair4.Key] = keyValuePair4.Value;
                        }
                    }
                }
            }

            IL_2BE:
            if (originalData.SwappedItems != null)
            {
                if (sentData.SwappedItems == null)
                {
                    inventoryDeltaInformation.SwappedItems = new Dictionary<uint, int>();
                    using (Dictionary<uint, int>.Enumerator enumerator4 = originalData.SwappedItems.GetEnumerator())
                    {
                        while (enumerator4.MoveNext())
                        {
                            KeyValuePair<uint, int> keyValuePair5 = enumerator4.Current;
                            inventoryDeltaInformation.SwappedItems[keyValuePair5.Key] = keyValuePair5.Value;
                        }

                        return inventoryDeltaInformation;
                    }
                }

                if (originalData.SwappedItems.Count != sentData.SwappedItems.Count)
                {
                    inventoryDeltaInformation.SwappedItems = new Dictionary<uint, int>();
                    foreach (KeyValuePair<uint, int> keyValuePair6 in originalData.SwappedItems)
                    {
                        if (!sentData.SwappedItems.ContainsKey(keyValuePair6.Key))
                        {
                            inventoryDeltaInformation.SwappedItems[keyValuePair6.Key] = keyValuePair6.Value;
                        }
                    }
                }
            }

            return inventoryDeltaInformation;
        }

        public void OnAck(MyClientStateBase forClient, byte packetId, bool delivered)
        {
            InventoryClientData inventoryClientData;
            InventoryDeltaInformation item;
            if (m_clientInventoryUpdate != null && m_clientInventoryUpdate.TryGetValue(forClient.EndpointId, out inventoryClientData) &&
                inventoryClientData.SendPackets.TryGetValue(packetId, out item))
            {
                if (!delivered)
                {
                    inventoryClientData.FailedIncompletePackets.Add(item);
                    _server.AddToDirtyGroups(this);
                }

                inventoryClientData.SendPackets.Remove(packetId);
            }
        }

        public void ForceSend(MyClientStateBase clientData)
        {
        }

        public void Reset(bool reinit, MyTimeSpan clientTimestamp)
        {
        }

        public bool IsStillDirty(Endpoint forClient)
        {
            return false;
            InventoryClientData inventoryClientData;
            return m_clientInventoryUpdate == null || !m_clientInventoryUpdate.TryGetValue(forClient, out inventoryClientData) || inventoryClientData.Dirty ||
                   inventoryClientData.FailedIncompletePackets.Count != 0;
        }

        public MyStreamProcessingState IsProcessingForClient(Endpoint forClient)
        {
            return MyStreamProcessingState.None;
        }

        private readonly int m_inventoryIndex;

        private Dictionary<Endpoint, InventoryClientData> m_clientInventoryUpdate;

        private List<MyPhysicalInventoryItem> m_itemsToSend;

        private HashSet<uint> m_foundDeltaItems;

        private uint m_nextExpectedPacketId;

        private readonly SortedList<uint, InventoryDeltaInformation> m_buffer;

        private Dictionary<int, MyPhysicalInventoryItem> m_tmpSwappingList;

        private struct InventoryDeltaInformation
        {
            public bool HasChanges;

            public uint MessageId;

            public List<uint> RemovedItems;

            public Dictionary<uint, MyFixedPoint> ChangedItems;

            public SortedDictionary<int, MyPhysicalInventoryItem> NewItems;

            public Dictionary<uint, int> SwappedItems;
        }

        private struct ClientInvetoryData
        {
            public MyPhysicalInventoryItem Item;

            public MyFixedPoint Amount;
        }

        private class InventoryClientData
        {
            public uint CurrentMessageId;

            public InventoryDeltaInformation MainSendingInfo;

            public bool Dirty;

            public readonly Dictionary<byte, InventoryDeltaInformation> SendPackets = new Dictionary<byte, InventoryDeltaInformation>();

            public readonly List<InventoryDeltaInformation> FailedIncompletePackets = new List<InventoryDeltaInformation>();

            public readonly SortedDictionary<uint, ClientInvetoryData> ClientItemsSorted = new SortedDictionary<uint, ClientInvetoryData>();

            public readonly List<ClientInvetoryData> ClientItems = new List<ClientInvetoryData>();
        }
    }
}