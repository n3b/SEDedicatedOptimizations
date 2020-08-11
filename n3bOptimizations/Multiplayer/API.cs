using System;
using System.Data;
using System.Linq;
using System.Text;
using n3bOptimizations;
using n3bOptimizations.Multiplayer;
using Sandbox.Game;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VRage.Network;

namespace n3b.SEMultiplayer
{
    public class API
    {
        private static ushort _channel = 59700;
        private static byte[] _magicString = UnicodeEncoding.ASCII.GetBytes("MaGiC sTrInG :)");

        public static void Register()
        {
            if (!Plugin.StaticConfig.InventoryEnabled) return;
            MyPerGameSettings.ClientStateType = typeof(CustomClientState);
            MyModAPIHelper.MyMultiplayer.Static.RegisterMessageHandler(_channel, Handle);
        }

        static bool CheckEnabled(byte[] msg, CustomClientState state)
        {
            var enabled = _magicString.SequenceEqual(msg);
            if (enabled)
            {
                state.APIEnabled = true;
                MyModAPIHelper.MyMultiplayer.Static.SendMessageTo(_channel, _magicString, state.EndpointId.Id.Value, true);
#if DEBUG
                Plugin.Log.Warn("got magic message");
#endif
            }
            else Plugin.Warn("Got unexpected data from client");

            return false;
        }

        public static void Handle(byte[] msg)
        {
            if (!(MyEventContext.Current.ClientState is CustomClientState state))
            {
                Plugin.Error($"Invalid client state!");
                throw new ConstraintException($"Invalid client state!");
            }

            if (!state.APIEnabled && !CheckEnabled(msg, state)) return;

            IProtoSerializable o;
            try
            {
                switch (ProtoMessage.Unserialize(msg))
                {
                    case SubscribeInventories s:
                        SubscribeInventories(state, s);
                        break;
                }
            }
            catch (Exception e)
            {
                Plugin.Error("Error reading proto msg", e);
            }
        }

        static void SubscribeInventories(CustomClientState state, SubscribeInventories msg)
        {
#if DEBUG
            Plugin.Log.Warn($"Got inventories subscribe {msg.EntityId?.Length ?? 0}");
#endif
            state.ClearInventorySubscriptions();
            if (msg.EntityId == null) return;

            foreach (var entityId in msg.EntityId)
            {
                try
                {
                    var entity = MyEntities.GetEntityById(entityId);

                    if (entity == null)
                    {
                        Plugin.Warn($"Attempting to subscribe to unknown inventory entity");
                        continue;
                    }

                    for (int i2 = 0; i2 < entity.InventoryCount; i2++)
                    {
                        var inv = entity.GetInventory(i2);
                        if (inv != null) state.SubscribeInventory(inv);
                    }
                }
                catch (Exception e)
                {
                    Plugin.Error("Inventory subscription error", e);
                }
            }
        }
    }
}