using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Reflection;
using ProtoBuf;
using Sandbox.ModAPI;

namespace n3b.SEMultiplayer
{
    class ProtoId : System.Attribute
    {
        public ushort id;

        public ProtoId(ushort id)
        {
            this.id = id;
        }
    }

    public interface IProtoSerializable
    {
    }

    public abstract class ProtoMessage
    {
        public static Dictionary<ushort, MethodInfo> unserializers = new Dictionary<ushort, MethodInfo>();

        static ProtoMessage()
        {
            foreach (var t in Assembly.GetExecutingAssembly().GetTypes().Where(t => Enumerable.Contains(t.GetInterfaces(), typeof(IProtoSerializable))))
            {
                var id = (ProtoId) t.GetCustomAttribute(typeof(ProtoId));
                if (id == null) continue;
                unserializers[id.id] = typeof(ProtoMessage).GetMethod("Unserialize", BindingFlags.Static | BindingFlags.NonPublic).MakeGenericMethod(t);
            }
        }

        public static byte[] Serialize(IProtoSerializable o)
        {
            var type = o.GetType();
            var attr = (ProtoId) type.GetCustomAttribute(typeof(ProtoId));
            var msg = Serialize(o);
            return BitConverter.GetBytes(attr.id).Concat(msg).ToArray();
        }

        public static IProtoSerializable Unserialize(byte[] msg)
        {
            var id = BitConverter.ToUInt16(msg, 0);
            if (!unserializers.ContainsKey(id)) throw new ConstraintException($"Type with id {id} doesn't exist");
            return (IProtoSerializable) unserializers[id].Invoke(null, new object[] {msg.Skip(2).ToArray()});
        }

        static byte[] Serialize<T>(T o)
        {
            return MyAPIGateway.Utilities.SerializeToBinary(o);
        }

        static T Unserialize<T>(byte[] data)
        {
            return MyAPIGateway.Utilities.SerializeFromBinary<T>(data);
        }
    }

    [ProtoContract]
    [ProtoId(1)]
    public class SubscribeInventories : IProtoSerializable
    {
        [ProtoMember(1)] public long[] EntityId;

        public SubscribeInventories()
        {
        }

        public SubscribeInventories(long[] ids)
        {
            EntityId = ids;
        }
    }
}