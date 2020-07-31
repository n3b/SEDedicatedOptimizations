using System;
using System.Collections.Generic;
using n3bOptimizations.Multiplayer;
using Sandbox;
using Sandbox.Engine.Multiplayer;
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
        public bool IsHighPriority => false;

        public IMyReplicable Owner { get; private set; }

        public bool IsStreaming => false;

        public bool IsValid => Owner != null && Owner.IsValid;

        public bool NeedsUpdate => false;

        private MyReplicationServer server;

        public int lastUpdated = 0;

        public int interval = 0;

        public PropsStateGroup(InventoryReplicable owner, SyncType syncType)
        {
            Owner = owner;
            server = (MyReplicationServer) MyMultiplayer.Static.ReplicationLayer;
            syncType.PropertyChangedNotify += Notify;
            syncType.PropertyCountChanged += OnPropertyCountChanged;
            m_properties = syncType.Properties;
            m_propertyTimestamps = new List<MyTimeSpan>(m_properties.Count);

            for (int i = 0; i < m_properties.Count; i++)
            {
                m_propertyTimestamps.Add(MyMultiplayer.Static.ReplicationLayer.GetSimulationUpdateTime());
            }
        }

        private void Notify(SyncBase sync)
        {
            m_propertyTimestamps[sync.Id] = server.GetSimulationUpdateTime();
            if (lastUpdated + interval > MySandboxGame.TotalTimeInMilliseconds) InventoryReplicable.Schedule(this, interval);
            else MarkDirty();
        }

        public void MarkDirty()
        {
            if (m_properties.Count == 0) return;
            if (lastUpdated == MySandboxGame.TotalTimeInMilliseconds) return;
            lastUpdated = MySandboxGame.TotalTimeInMilliseconds;
            InventoryReplicable.ResetSchedule(this);

            foreach (KeyValuePair<Endpoint, ServerData.DataPerClient> keyValuePair in this.m_serverData.ServerClientData)
            {
                keyValuePair.Value.DirtyProperties.Reset(true);
            }

            server.AddToDirtyGroups(this);
        }

        public void CreateClientData(MyClientStateBase forClient)
        {
            ServerData.DataPerClient dataPerClient = new ServerData.DataPerClient();
            m_serverData.ServerClientData.Add(forClient.EndpointId, dataPerClient);
            if (m_properties.Count > 0)
            {
                dataPerClient.DirtyProperties.Reset(true);
            }
        }

        public void DestroyClientData(MyClientStateBase forClient)
        {
            if (!(forClient is CustomClientState state)) return;
            m_serverData.ServerClientData.Remove(forClient.EndpointId);
        }

        public void ClientUpdate(MyTimeSpan clientTimestamp)
        {
            throw new NotImplementedException();
        }

        public void Destroy()
        {
            Owner = null;
        }

        public void Serialize(BitStream stream, Endpoint forClient, MyTimeSpan serverTimestamp, MyTimeSpan lastClientTimestamp, byte packetId, int maxBitPosition,
            HashSet<string> cachedData)
        {
            if (!stream.Writing) return;

            SmallBitField dirtyProperties;
            dirtyProperties = m_serverData.ServerClientData[forClient].DirtyProperties;
            stream.WriteUInt64(dirtyProperties.Bits, m_properties.Count);

            for (int i = 0; i < m_properties.Count; i++)
            {
                if (!dirtyProperties[i]) continue;
                double milliseconds = m_propertyTimestamps[i].Milliseconds;
                stream.WriteDouble(milliseconds);
                m_properties[i].Serialize(stream, false, true);
            }

            if (stream.BitPosition <= maxBitPosition)
            {
                var dataPerClient = m_serverData.ServerClientData[forClient];
                dataPerClient.SentProperties[packetId].Bits = dataPerClient.DirtyProperties.Bits;
                dataPerClient.DirtyProperties.Bits = 0UL;
            }
        }

        public void OnAck(MyClientStateBase forClient, byte packetId, bool delivered)
        {
            var dataPerClient = m_serverData.ServerClientData[forClient.EndpointId];
            if (delivered) return;
            var dataPerClient2 = dataPerClient;
            dataPerClient2.DirtyProperties.Bits = (dataPerClient2.DirtyProperties.Bits | dataPerClient.SentProperties[packetId].Bits);
            server.AddToDirtyGroups(this);
        }

        public void ForceSend(MyClientStateBase clientData)
        {
        }

        public void Reset(bool reinit, MyTimeSpan clientTimestamp)
        {
        }

        public bool IsStillDirty(Endpoint forClient)
        {
            return m_serverData.ServerClientData[forClient].DirtyProperties.Bits > 0UL;
        }

        public MyStreamProcessingState IsProcessingForClient(Endpoint forClient)
        {
            return MyStreamProcessingState.None;
        }

        private void OnPropertyCountChanged()
        {
            for (int i = m_propertyTimestamps.Count; i < m_properties.Count; i++)
            {
                m_propertyTimestamps.Add(MyMultiplayer.Static.ReplicationLayer.GetSimulationUpdateTime());
            }
        }

        public Func<MyEventContext, ValidationResult> GlobalValidate = (MyEventContext context) => ValidationResult.Passed;

        public MyPropertySyncStateGroup.PriorityAdjustDelegate PriorityAdjust = (int frames, MyClientStateBase state, float priority) => priority;

        private readonly ServerData m_serverData = new ServerData();

        private ListReader<SyncBase> m_properties;

        private readonly List<MyTimeSpan> m_propertyTimestamps;

        private readonly MyTimeSpan m_invalidTimestamp = MyTimeSpan.FromTicks(long.MinValue);

        private class ServerData
        {
            public readonly Dictionary<Endpoint, DataPerClient> ServerClientData =
                new Dictionary<Endpoint, DataPerClient>();

            public class DataPerClient
            {
                public SmallBitField DirtyProperties = new SmallBitField(false);

                public readonly SmallBitField[] SentProperties = new SmallBitField[256];
            }
        }

        public delegate float PriorityAdjustDelegate(int frameCountWithoutSync, MyClientStateBase clientState, float basePriority);
    }
}