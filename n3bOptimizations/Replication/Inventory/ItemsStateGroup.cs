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
        readonly SortedList<uint, InventoryDeltaInformation> m_buffer;

        readonly int m_inventoryIndex;

        ulong _lastFrame;

        MyReplicationServer _server;

        readonly Dictionary<Endpoint, InventoryClientData> _serverData;

        HashSet<uint> m_foundDeltaItems;

        List<MyPhysicalInventoryItem> m_itemsToSend;

        uint m_nextExpectedPacketId;

        Dictionary<int, MyPhysicalInventoryItem> m_tmpSwappingList;

        public ItemsStateGroup(MyInventory entity, IMyReplicable owner, int batch)
        {
            Inventory = entity;
            Batch = batch;
            _serverData = new Dictionary<Endpoint, InventoryClientData>();
            Inventory.ContentsChanged += InventoryChanged;
            Owner = owner;
            _server = (MyReplicationServer) MyMultiplayer.Static.ReplicationLayer;
        }

        public MyInventory Inventory { get; }

        public bool Scheduled { get; set; }

        public int Batch { get; }

        public void MarkDirty()
        {
            var counter = MySandboxGame.Static.SimulationFrameCounter;
            if (_lastFrame == counter) return;
            _lastFrame = counter;

            foreach (var keyValuePair in _serverData) _serverData[keyValuePair.Key].Dirty = true;

            _server.AddToDirtyGroups(this);
        }

        public void UpdateOwnership()
        {
            foreach (var data in _serverData)
                data.Value.HasRights = (Owner as InventoryReplicable).HasRights(data.Key.Id, ValidationType.Access | ValidationType.Ownership) == ValidationResult.Passed;
        }

        public bool IsHighPriority => false;
        public IMyReplicable Owner { get; }

        public bool IsValid => Owner != null && Owner.IsValid;

        public bool IsStreaming => false;

        public bool NeedsUpdate => false;

        public void CreateClientData(MyClientStateBase forClient)
        {
            CreateClientData(forClient.EndpointId);
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

        public void Serialize(BitStream stream, MyClientInfo forClient, MyTimeSpan serverTimestamp, MyTimeSpan lastClientTimestamp, byte packetId, int maxBitPosition,
            HashSet<string> cachedData)
        {
            var endpoint = forClient.EndpointId;

            if (!stream.Writing) return;

            if (!_serverData.TryGetValue(endpoint, out var data) || !data.HasRights)
            {
                stream.WriteBool(false);
                stream.WriteUInt32(0U);
                return;
            }

            var flag = false;
            if (data.FailedIncompletePackets.Count > 0)
            {
                var delta = data.FailedIncompletePackets[0];
                data.FailedIncompletePackets.RemoveAtFast(0);
                var value = WriteInventory(ref delta, stream, packetId, maxBitPosition, out flag);
                value.MessageId = delta.MessageId;
                if (flag) data.FailedIncompletePackets.Add(CreateSplit(ref delta, ref value));
                data.SendPackets[packetId] = value;
                return;
            }

            var delta2 = CalculateInventoryDiff(ref data);
            delta2.MessageId = data.CurrentMessageId;
            data.MainSendingInfo = WriteInventory(ref delta2, stream, packetId, maxBitPosition, out flag);
            data.SendPackets[packetId] = data.MainSendingInfo;
            data.CurrentMessageId += 1U;
            if (flag)
            {
                var item = CreateSplit(ref delta2, ref data.MainSendingInfo);
                item.MessageId = data.CurrentMessageId;
                data.FailedIncompletePackets.Add(item);
                data.CurrentMessageId += 1U;
            }

            data.Dirty = false;
        }

        public void OnAck(MyClientStateBase forClient, byte packetId, bool delivered)
        {
            if (_serverData == null || !_serverData.TryGetValue(forClient.EndpointId, out var data) ||
                !data.SendPackets.TryGetValue(packetId, out var item)) return;
            if (!delivered)
            {
                data.FailedIncompletePackets.Add(item);
                _server.AddToDirtyGroups(this);
            }

            data.SendPackets.Remove(packetId);
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

        void InventoryChanged(MyInventoryBase obj)
        {
            var counter = MySandboxGame.Static.SimulationFrameCounter;
            if (_lastFrame + (uint) InventoryReplicableUpdate.ReplicableInterval > counter)
            {
                InventoryReplicableUpdate.Schedule(this);
            }
            else
            {
                InventoryReplicableUpdate.Reset(this);
                MarkDirty();
            }
        }

        public void RefreshClientData(Endpoint clientEndpoint)
        {
            _serverData.Remove(clientEndpoint);
            CreateClientData(clientEndpoint);
        }

        void CreateClientData(Endpoint clientEndpoint)
        {
            if (!_serverData.TryGetValue(clientEndpoint, out var clientData))
            {
                clientData = new InventoryClientData();
                _serverData[clientEndpoint] = clientData;
            }

            clientData.Dirty = false;
            clientData.HasRights = !Plugin.StaticConfig.InventoryPreventSharing ||
                                   (Owner as InventoryReplicable).HasRights(clientEndpoint.Id, ValidationType.Access | ValidationType.Ownership) ==
                                   ValidationResult.Passed;

            foreach (var item in Inventory.GetItems())
            {
                var amount = item.Amount;
                if (item.Content is MyObjectBuilder_GasContainerObject gas) amount = (MyFixedPoint) gas.GasLevel;

                var data = new ClientInvetoryData
                {
                    Item = item,
                    Amount = amount
                };
                clientData.ClientItemsSorted[item.ItemId] = data;
                clientData.ClientItems.Add(data);
            }
        }

        InventoryDeltaInformation CalculateInventoryDiff(ref InventoryClientData clientData)
        {
            if (m_itemsToSend == null) m_itemsToSend = new List<MyPhysicalInventoryItem>();

            if (m_foundDeltaItems == null) m_foundDeltaItems = new HashSet<uint>();

            m_foundDeltaItems.Clear();
            var items = Inventory.GetItems();
            CalculateAddsAndRemovals(clientData, out var delta, items);
            if (delta.HasChanges) ApplyChangesToClientItems(clientData, ref delta);

            for (var i = 0; i < items.Count; i++)
                if (i < clientData.ClientItems.Count)
                {
                    var itemId = clientData.ClientItems[i].Item.ItemId;
                    if (itemId != items[i].ItemId)
                    {
                        if (delta.SwappedItems == null) delta.SwappedItems = new Dictionary<uint, int>();

                        for (var j = 0; j < items.Count; j++)
                            if (itemId == items[j].ItemId)
                                delta.SwappedItems[itemId] = j;
                    }
                }

            clientData.ClientItemsSorted.Clear();
            clientData.ClientItems.Clear();
            foreach (var item in items)
            {
                var amount = item.Amount;
                var obj = item.Content as MyObjectBuilder_GasContainerObject;
                if (obj != null) amount = (MyFixedPoint) obj.GasLevel;

                var data = new ClientInvetoryData
                {
                    Item = item,
                    Amount = amount
                };
                clientData.ClientItemsSorted[item.ItemId] = data;
                clientData.ClientItems.Add(data);
            }

            return delta;
        }

        static void ApplyChangesToClientItems(InventoryClientData clientData, ref InventoryDeltaInformation delta)
        {
            if (delta.RemovedItems != null)
                foreach (var num in delta.RemovedItems)
                {
                    var num2 = -1;
                    for (var i = 0; i < clientData.ClientItems.Count; i++)
                        if (clientData.ClientItems[i].Item.ItemId == num)
                        {
                            num2 = i;
                            break;
                        }

                    if (num2 != -1) clientData.ClientItems.RemoveAt(num2);
                }

            if (delta.NewItems != null)
                foreach (var keyValuePair in delta.NewItems)
                {
                    var item = new ClientInvetoryData
                    {
                        Item = keyValuePair.Value,
                        Amount = keyValuePair.Value.Amount
                    };
                    if (keyValuePair.Key >= clientData.ClientItems.Count)
                        clientData.ClientItems.Add(item);
                    else
                        clientData.ClientItems.Insert(keyValuePair.Key, item);
                }
        }

        void CalculateAddsAndRemovals(InventoryClientData clientData, out InventoryDeltaInformation delta, List<MyPhysicalInventoryItem> items)
        {
            delta = new InventoryDeltaInformation
            {
                HasChanges = false
            };
            var num = 0;
            foreach (var myPhysicalInventoryItem in items)
            {
                if (clientData.ClientItemsSorted.TryGetValue(myPhysicalInventoryItem.ItemId, out var data))
                {
                    if (data.Item.Content.TypeId == myPhysicalInventoryItem.Content.TypeId &&
                        data.Item.Content.SubtypeId == myPhysicalInventoryItem.Content.SubtypeId)
                    {
                        m_foundDeltaItems.Add(myPhysicalInventoryItem.ItemId);
                        var myFixedPoint = myPhysicalInventoryItem.Amount;
                        var obj = myPhysicalInventoryItem.Content as MyObjectBuilder_GasContainerObject;
                        if (obj != null) myFixedPoint = (MyFixedPoint) obj.GasLevel;

                        if (data.Amount != myFixedPoint)
                        {
                            var value = myFixedPoint - data.Amount;
                            if (delta.ChangedItems == null) delta.ChangedItems = new Dictionary<uint, MyFixedPoint>();

                            delta.ChangedItems[myPhysicalInventoryItem.ItemId] = value;
                            delta.HasChanges = true;
                        }
                    }
                }
                else
                {
                    delta.NewItems ??= new SortedDictionary<int, MyPhysicalInventoryItem>();
                    delta.NewItems[num] = myPhysicalInventoryItem;
                    delta.HasChanges = true;
                }

                num++;
            }

            foreach (var keyValuePair in clientData.ClientItemsSorted)
            {
                if (delta.RemovedItems == null) delta.RemovedItems = new List<uint>();

                if (m_foundDeltaItems.Contains(keyValuePair.Key)) continue;
                delta.RemovedItems.Add(keyValuePair.Key);
                delta.HasChanges = true;
            }
        }

        InventoryDeltaInformation WriteInventory(ref InventoryDeltaInformation packetInfo, BitStream stream, byte packetId, int maxBitPosition, out bool needsSplit)
        {
            var delta = PrepareSendData(ref packetInfo, stream, maxBitPosition, out needsSplit);
            delta.MessageId = packetInfo.MessageId;
            stream.WriteBool(delta.HasChanges);
            stream.WriteUInt32(delta.MessageId);
            if (!delta.HasChanges) return delta;

            stream.WriteBool(delta.ChangedItems != null);
            if (delta.ChangedItems != null)
            {
                stream.WriteInt32(delta.ChangedItems.Count);
                foreach (var keyValuePair in delta.ChangedItems)
                {
                    stream.WriteUInt32(keyValuePair.Key);
                    stream.WriteInt64(keyValuePair.Value.RawValue);
                }
            }

            stream.WriteBool(delta.RemovedItems != null);
            if (delta.RemovedItems != null)
            {
                stream.WriteInt32(delta.RemovedItems.Count);
                foreach (var value in delta.RemovedItems) stream.WriteUInt32(value);
            }

            stream.WriteBool(delta.NewItems != null);
            if (delta.NewItems != null)
            {
                stream.WriteInt32(delta.NewItems.Count);
                foreach (var keyValuePair2 in delta.NewItems)
                {
                    stream.WriteInt32(keyValuePair2.Key);
                    var value2 = keyValuePair2.Value;
                    MySerializer.Write(stream, ref value2, MyObjectBuilderSerializer.Dynamic);
                }
            }

            stream.WriteBool(delta.SwappedItems != null);
            if (delta.SwappedItems != null)
            {
                stream.WriteInt32(delta.SwappedItems.Count);
                foreach (var keyValuePair3 in delta.SwappedItems)
                {
                    stream.WriteUInt32(keyValuePair3.Key);
                    stream.WriteInt32(keyValuePair3.Value);
                }
            }

            return delta;
        }

        InventoryDeltaInformation PrepareSendData(ref InventoryDeltaInformation packetInfo, BitStream stream, int maxBitPosition, out bool needsSplit)
        {
            needsSplit = false;
            var bitPosition = stream.BitPosition;
            var delta = new InventoryDeltaInformation
            {
                HasChanges = false
            };
            stream.WriteBool(false);
            stream.WriteUInt32(packetInfo.MessageId);
            stream.WriteBool(packetInfo.ChangedItems != null);
            if (packetInfo.ChangedItems != null)
            {
                stream.WriteInt32(packetInfo.ChangedItems.Count);
                if (stream.BitPosition > maxBitPosition)
                {
                    needsSplit = true;
                }
                else
                {
                    delta.ChangedItems = new Dictionary<uint, MyFixedPoint>();
                    foreach (var keyValuePair in packetInfo.ChangedItems)
                    {
                        stream.WriteUInt32(keyValuePair.Key);
                        stream.WriteInt64(keyValuePair.Value.RawValue);
                        if (stream.BitPosition <= maxBitPosition)
                        {
                            delta.ChangedItems[keyValuePair.Key] = keyValuePair.Value;
                            delta.HasChanges = true;
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
                stream.WriteInt32(packetInfo.RemovedItems.Count);
                if (stream.BitPosition > maxBitPosition)
                {
                    needsSplit = true;
                }
                else
                {
                    delta.RemovedItems = new List<uint>();
                    foreach (var num in packetInfo.RemovedItems)
                    {
                        stream.WriteUInt32(num);
                        if (stream.BitPosition <= maxBitPosition)
                        {
                            delta.RemovedItems.Add(num);
                            delta.HasChanges = true;
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
                stream.WriteInt32(packetInfo.NewItems.Count);
                if (stream.BitPosition > maxBitPosition)
                {
                    needsSplit = true;
                }
                else
                {
                    delta.NewItems = new SortedDictionary<int, MyPhysicalInventoryItem>();
                    foreach (var keyValuePair2 in packetInfo.NewItems)
                    {
                        var value = keyValuePair2.Value;
                        stream.WriteInt32(keyValuePair2.Key);
                        MySerializer.Write(stream, ref value, MyObjectBuilderSerializer.Dynamic);
                        if (stream.BitPosition <= maxBitPosition)
                        {
                            delta.NewItems[keyValuePair2.Key] = value;
                            delta.HasChanges = true;
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
                stream.WriteInt32(packetInfo.SwappedItems.Count);
                if (stream.BitPosition > maxBitPosition)
                {
                    needsSplit = true;
                }
                else
                {
                    delta.SwappedItems = new Dictionary<uint, int>();
                    foreach (var keyValuePair3 in packetInfo.SwappedItems)
                    {
                        stream.WriteUInt32(keyValuePair3.Key);
                        stream.WriteInt32(keyValuePair3.Value);
                        if (stream.BitPosition <= maxBitPosition)
                        {
                            delta.SwappedItems[keyValuePair3.Key] = keyValuePair3.Value;
                            delta.HasChanges = true;
                        }
                        else
                        {
                            needsSplit = true;
                        }
                    }
                }
            }

            stream.SetBitPositionWrite(bitPosition);
            return delta;
        }

        InventoryDeltaInformation CreateSplit(ref InventoryDeltaInformation originalData, ref InventoryDeltaInformation sentData)
        {
            var delta = new InventoryDeltaInformation
            {
                MessageId = sentData.MessageId
            };
            if (originalData.ChangedItems != null)
            {
                if (sentData.ChangedItems == null)
                {
                    delta.ChangedItems = new Dictionary<uint, MyFixedPoint>();
                    using (var enumerator = originalData.ChangedItems.GetEnumerator())
                    {
                        while (enumerator.MoveNext())
                        {
                            var keyValuePair = enumerator.Current;
                            delta.ChangedItems[keyValuePair.Key] = keyValuePair.Value;
                        }

                        goto IL_102;
                    }
                }

                if (originalData.ChangedItems.Count != sentData.ChangedItems.Count)
                {
                    delta.ChangedItems = new Dictionary<uint, MyFixedPoint>();
                    foreach (var keyValuePair2 in originalData.ChangedItems)
                        if (!sentData.ChangedItems.ContainsKey(keyValuePair2.Key))
                            delta.ChangedItems[keyValuePair2.Key] = keyValuePair2.Value;
                }
            }

            IL_102:
            if (originalData.RemovedItems != null)
            {
                if (sentData.RemovedItems == null)
                {
                    delta.RemovedItems = new List<uint>();
                    using (var enumerator2 = originalData.RemovedItems.GetEnumerator())
                    {
                        while (enumerator2.MoveNext())
                        {
                            var item = enumerator2.Current;
                            delta.RemovedItems.Add(item);
                        }

                        goto IL_1D0;
                    }
                }

                if (originalData.RemovedItems.Count != sentData.RemovedItems.Count)
                {
                    delta.RemovedItems = new List<uint>();
                    foreach (var item2 in originalData.RemovedItems)
                        if (!sentData.RemovedItems.Contains(item2))
                            delta.RemovedItems.Add(item2);
                }
            }

            IL_1D0:
            if (originalData.NewItems != null)
            {
                if (sentData.NewItems == null)
                {
                    delta.NewItems = new SortedDictionary<int, MyPhysicalInventoryItem>();
                    using (var enumerator3 = originalData.NewItems.GetEnumerator())
                    {
                        while (enumerator3.MoveNext())
                        {
                            var keyValuePair3 = enumerator3.Current;
                            delta.NewItems[keyValuePair3.Key] = keyValuePair3.Value;
                        }

                        goto IL_2BE;
                    }
                }

                if (originalData.NewItems.Count != sentData.NewItems.Count)
                {
                    delta.NewItems = new SortedDictionary<int, MyPhysicalInventoryItem>();
                    foreach (var keyValuePair4 in originalData.NewItems)
                        if (!sentData.NewItems.ContainsKey(keyValuePair4.Key))
                            delta.NewItems[keyValuePair4.Key] = keyValuePair4.Value;
                }
            }

            IL_2BE:
            if (originalData.SwappedItems == null) return delta;
            if (sentData.SwappedItems == null)
            {
                delta.SwappedItems = new Dictionary<uint, int>();
                using (var enumerator4 = originalData.SwappedItems.GetEnumerator())
                {
                    while (enumerator4.MoveNext())
                    {
                        var keyValuePair5 = enumerator4.Current;
                        delta.SwappedItems[keyValuePair5.Key] = keyValuePair5.Value;
                    }

                    return delta;
                }
            }

            if (originalData.SwappedItems.Count != sentData.SwappedItems.Count)
            {
                delta.SwappedItems = new Dictionary<uint, int>();
                foreach (var keyValuePair6 in originalData.SwappedItems)
                    if (!sentData.SwappedItems.ContainsKey(keyValuePair6.Key))
                        delta.SwappedItems[keyValuePair6.Key] = keyValuePair6.Value;
            }

            return delta;
        }

        public bool HasRights(Endpoint endpoint)
        {
            return _serverData.TryGetValue(endpoint, out var data) && data.HasRights;
        }

        struct InventoryDeltaInformation
        {
            public bool HasChanges;

            public uint MessageId;

            public List<uint> RemovedItems;

            public Dictionary<uint, MyFixedPoint> ChangedItems;

            public SortedDictionary<int, MyPhysicalInventoryItem> NewItems;

            public Dictionary<uint, int> SwappedItems;
        }

        struct ClientInvetoryData
        {
            public MyPhysicalInventoryItem Item;

            public MyFixedPoint Amount;
        }

        class InventoryClientData
        {
            public readonly List<ClientInvetoryData> ClientItems = new List<ClientInvetoryData>();

            public readonly SortedDictionary<uint, ClientInvetoryData> ClientItemsSorted = new SortedDictionary<uint, ClientInvetoryData>();

            public readonly List<InventoryDeltaInformation> FailedIncompletePackets = new List<InventoryDeltaInformation>();

            public readonly Dictionary<byte, InventoryDeltaInformation> SendPackets = new Dictionary<byte, InventoryDeltaInformation>();
            public uint CurrentMessageId;

            public bool Dirty;

            public bool HasRights = true;

            public InventoryDeltaInformation MainSendingInfo;
        }
    }
}