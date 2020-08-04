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
            _serverData = new Dictionary<Endpoint, InventoryClientData>();
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

            foreach (KeyValuePair<Endpoint, InventoryClientData> keyValuePair in _serverData)
            {
                _serverData[keyValuePair.Key].Dirty = true;
            }

            _server.AddToDirtyGroups(this);
        }

        public void CreateClientData(MyClientStateBase forClient)
        {
            CreateClientData(forClient.EndpointId);
        }

        public void RefreshClientData(Endpoint clientEndpoint)
        {
            _serverData.Remove(clientEndpoint);
            CreateClientData(clientEndpoint);
        }

        private void CreateClientData(Endpoint clientEndpoint)
        {
            if (!_serverData.TryGetValue(clientEndpoint, out var inventoryClientData))
            {
                inventoryClientData = new InventoryClientData();
                _serverData[clientEndpoint] = inventoryClientData;
            }

            inventoryClientData.Dirty = false;
            inventoryClientData.HasRights = !Plugin.StaticConfig.InventoryPreventSharing ||
                                            (Owner as InventoryReplicable).HasRights(clientEndpoint.Id, ValidationType.Access | ValidationType.Ownership) ==
                                            ValidationResult.Passed;

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
            _serverData.Remove(forClient.EndpointId);
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
            if (!stream.Writing) return;

            if (!_serverData.TryGetValue(forClient, out var data) || !data.HasRights)
            {
                stream.WriteBool(false);
                stream.WriteUInt32(0U, 32);
                return;
            }

            bool flag = false;
            if (data.FailedIncompletePackets.Count > 0)
            {
                InventoryDeltaInformation inventoryDeltaInformation = data.FailedIncompletePackets[0];
                data.FailedIncompletePackets.RemoveAtFast(0);
                InventoryDeltaInformation value = WriteInventory(ref inventoryDeltaInformation, stream, packetId, maxBitPosition, out flag);
                value.MessageId = inventoryDeltaInformation.MessageId;
                if (flag)
                {
                    data.FailedIncompletePackets.Add(CreateSplit(ref inventoryDeltaInformation, ref value));
                }

                data.SendPackets[packetId] = value;
                return;
            }

            InventoryDeltaInformation inventoryDeltaInformation2 = CalculateInventoryDiff(ref data);
            inventoryDeltaInformation2.MessageId = data.CurrentMessageId;
            data.MainSendingInfo = WriteInventory(ref inventoryDeltaInformation2, stream, packetId, maxBitPosition, out flag);
            data.SendPackets[packetId] = data.MainSendingInfo;
            data.CurrentMessageId += 1U;
            if (flag)
            {
                InventoryDeltaInformation item = CreateSplit(ref inventoryDeltaInformation2, ref data.MainSendingInfo);
                item.MessageId = data.CurrentMessageId;
                data.FailedIncompletePackets.Add(item);
                data.CurrentMessageId += 1U;
            }

            data.Dirty = false;
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
            if (_serverData != null && _serverData.TryGetValue(forClient.EndpointId, out inventoryClientData) &&
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
            return _serverData.TryGetValue(forClient, out var data) && data.HasRights && (data.Dirty || data.FailedIncompletePackets.Count != 0);
        }

        public MyStreamProcessingState IsProcessingForClient(Endpoint forClient)
        {
            return MyStreamProcessingState.None;
        }

        private readonly int m_inventoryIndex;

        private Dictionary<Endpoint, InventoryClientData> _serverData;

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

            public bool HasRights = true;
        }

        public void UpdateOwnership()
        {
            foreach (var data in _serverData)
            {
                data.Value.HasRights = (Owner as InventoryReplicable).HasRights(data.Key.Id, ValidationType.Access | ValidationType.Ownership) == ValidationResult.Passed;
            }
        }

        public bool HasRights(Endpoint endpoint)
        {
            return _serverData.TryGetValue(endpoint, out var data) && data.HasRights;
        }
    }
}