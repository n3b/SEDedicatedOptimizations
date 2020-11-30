using System;
using System.Collections.Generic;
using Sandbox;
using Sandbox.Engine.Multiplayer;
using Sandbox.Game.Entities;
using Sandbox.Game.Replication.StateGroups;
using VRage.Collections;
using VRage.Library.Collections;
using VRage.Library.Utils;
using VRage.Network;
using VRage.Sync;

namespace n3bOptimizations.Replication.Inventory
{
    public class PropsStateGroup : IMyStateGroup, IMyNetObject, IMyEventOwner, IMarkDirty
    {
        public delegate float PriorityAdjustDelegate(int frameCountWithoutSync, MyClientStateBase clientState, float basePriority);

        const int VOLUME_PROP_INDEX = 1;

        readonly Dictionary<Endpoint, DataPerClient> _serverData = new Dictionary<Endpoint, DataPerClient>();

        readonly List<MyTimeSpan> m_propertyTimestamps;

        ulong _lastFrame;

        MyReplicationServer _server;

        public Func<MyEventContext, ValidationResult> GlobalValidate = context => ValidationResult.Passed;

        ListReader<SyncBase> m_properties;

        public MyPropertySyncStateGroup.PriorityAdjustDelegate PriorityAdjust = (frames, state, priority) => priority;

        public PropsStateGroup(InventoryReplicable owner, SyncType syncType, int batch)
        {
            Owner = owner;
            Batch = batch;
            _server = (MyReplicationServer) MyMultiplayer.Static.ReplicationLayer;
            syncType.PropertyChangedNotify += Notify;
            syncType.PropertyCountChanged += OnPropertyCountChanged;
            m_properties = syncType.Properties;
            m_propertyTimestamps = new List<MyTimeSpan>(m_properties.Count);

            for (var i = 0; i < m_properties.Count; i++) m_propertyTimestamps.Add(_server.GetSimulationUpdateTime());
        }

        public int Batch { get; }

        public bool Scheduled { get; set; }

        public void MarkDirty()
        {
            var counter = MySandboxGame.Static.SimulationFrameCounter;
            if (_lastFrame == counter) return;
            _lastFrame = counter;
            if (m_properties.Count == 0) return;

            foreach (var clientData in _serverData.Values) clientData.DirtyProperties.Reset(true);

            _server.AddToDirtyGroups(this);
        }

        public void UpdateOwnership()
        {
            foreach (var data in _serverData)
                data.Value.HasRights = (Owner as InventoryReplicable).HasRights(data.Key.Id, ValidationType.Access | ValidationType.Ownership) == ValidationResult.Passed;
        }

        public bool IsHighPriority => false;

        public IMyReplicable Owner { get; private set; }

        public bool IsStreaming => false;

        public bool IsValid => Owner != null && Owner.IsValid;

        public bool NeedsUpdate => false;

        public void CreateClientData(MyClientStateBase forClient)
        {
            var dataPerClient = new DataPerClient();
            _serverData.Add(forClient.EndpointId, dataPerClient);
            if (m_properties.Count > 0) dataPerClient.DirtyProperties.Reset(true);
            dataPerClient.HasRights = !Plugin.StaticConfig.InventoryPreventSharing ||
                                      (Owner as InventoryReplicable).HasRights(forClient.EndpointId.Id, ValidationType.Access | ValidationType.Ownership) ==
                                      ValidationResult.Passed;
        }

        public void DestroyClientData(MyClientStateBase forClient)
        {
            _serverData.Remove(forClient.EndpointId);
        }

        public void ClientUpdate(MyTimeSpan clientTimestamp)
        {
            throw new NotImplementedException();
        }

        public void Destroy()
        {
            Owner = null;
            _server = null;
        }

        public void OnAck(MyClientStateBase forClient, byte packetId, bool delivered)
        {
            if (delivered) return;
            var dataPerClient = _serverData[forClient.EndpointId];
            var dataPerClient2 = dataPerClient;
            dataPerClient2.DirtyProperties.Bits = dataPerClient2.DirtyProperties.Bits | dataPerClient.SentProperties[packetId].Bits;
            _server.AddToDirtyGroups(this);
        }

        public void ForceSend(MyClientStateBase clientData)
        {
        }

        public void Reset(bool reinit, MyTimeSpan clientTimestamp)
        {
        }

        public bool IsStillDirty(Endpoint forClient)
        {
            return _serverData.TryGetValue(forClient, out var data) && data.HasRights && data.DirtyProperties.Bits > 0UL;
        }

        public MyStreamProcessingState IsProcessingForClient(Endpoint forClient)
        {
            return MyStreamProcessingState.None;
        }

        void Notify(SyncBase sync)
        {
            m_propertyTimestamps[sync.Id] = _server.GetSimulationUpdateTime();
            var counter = MySandboxGame.Static.SimulationFrameCounter;
            var isStatic = (Owner as InventoryReplicable).Instance?.Entity is MyCubeBlock block && block.CubeGrid.IsStatic;
            if (isStatic && _lastFrame + (uint) InventoryReplicableUpdate.ReplicableInterval > counter)
            {
                InventoryReplicableUpdate.Schedule(this);
            }
            else
            {
                InventoryReplicableUpdate.Reset(this);
                MarkDirty();
            }
        }

        public void Serialize(BitStream stream, MyClientInfo forClient, MyTimeSpan serverTimestamp, MyTimeSpan lastClientTimestamp, byte packetId, int maxBitPosition,
            HashSet<string> cachedData)
        {
            var endpoint = forClient.EndpointId;

            if (!stream.Writing) return;

            if (!_serverData.TryGetValue(endpoint, out var data))
            {
                var bits = new SmallBitField(false);
                stream.WriteUInt64(bits.Bits, 0);
                return;
            }

            var dirtyProperties = data.DirtyProperties;
            if (!data.HasRights) dirtyProperties[VOLUME_PROP_INDEX] = false;
            stream.WriteUInt64(dirtyProperties.Bits, m_properties.Count);

            for (var i = 0; i < m_properties.Count; i++)
            {
                if (!dirtyProperties[i]) continue;
                var milliseconds = m_propertyTimestamps[i].Milliseconds;
                stream.WriteDouble(milliseconds);
                m_properties[i].Serialize(stream, false);
            }

            if (stream.BitPosition <= maxBitPosition)
            {
                var dataPerClient = _serverData[endpoint];
                dataPerClient.SentProperties[packetId].Bits = dataPerClient.DirtyProperties.Bits;
                dataPerClient.DirtyProperties.Bits = 0UL;
            }
        }

        void OnPropertyCountChanged()
        {
            for (var i = m_propertyTimestamps.Count; i < m_properties.Count; i++) m_propertyTimestamps.Add(MyMultiplayer.Static.ReplicationLayer.GetSimulationUpdateTime());
        }

        public class DataPerClient
        {
            public readonly SmallBitField[] SentProperties = new SmallBitField[256];
            public SmallBitField DirtyProperties = new SmallBitField(false);

            public bool HasRights = true;
        }
    }
}