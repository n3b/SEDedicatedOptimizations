using System;
using System.Linq;
using System.Text;
using n3bOptimizations;
using n3bOptimizations.Patch.Inventory;
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
            MyModAPIHelper.MyMultiplayer.Static.RegisterMessageHandler(_channel, Handle);
        }

        static bool CheckEnabled(byte[] msg, MyClientStateBase state)
        {
            var enabled = _magicString.SequenceEqual(msg);
            if (enabled)
            {
                state.SetEnabledAPI(enabled);
                MyModAPIHelper.MyMultiplayer.Static.SendMessageTo(_channel, _magicString, state.EndpointId.Id.Value, true);
#if DEBUG
                Plugin.Log.Warn("got magic message");
#endif
            }
            else Plugin.Log.Warn("Got unexpected data from client");

            return false;
        }

        public static void Handle(byte[] msg)
        {
            var state = MyEventContext.Current.ClientState;
            if (state == null)
            {
                Plugin.Log.Warn($"o_O there is no damned client!");
                return;
            }

            if (!state.IsEnabledAPI() && !CheckEnabled(msg, state)) return;

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
                Plugin.Log.Error(e);
                return;
            }
        }

        static void SubscribeInventories(MyClientStateBase state, SubscribeInventories msg)
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
                        Plugin.Log.Warn($"Attempting to subscribe to unknown inventory entity");
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
                    Plugin.Log.Error(e);
                }
            }
        }
    }
}