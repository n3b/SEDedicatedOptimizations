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
        public static Dictionary<ushort, Tuple<MethodInfo, MethodInfo>> map = new Dictionary<ushort, Tuple<MethodInfo, MethodInfo>>();

        static ProtoMessage()
        {
            foreach (var t in Assembly.GetExecutingAssembly().GetTypes().Where(t => Enumerable.Contains(t.GetInterfaces(), typeof(IProtoSerializable))))
            {
                var id = (ProtoId) t.GetCustomAttribute(typeof(ProtoId));
                if (id == null) continue;
                map[id.id] = new Tuple<MethodInfo, MethodInfo>(typeof(ProtoMessage).GetMethod("Serialize", BindingFlags.Static | BindingFlags.NonPublic).MakeGenericMethod(t),
                    typeof(ProtoMessage).GetMethod("Unserialize", BindingFlags.Static | BindingFlags.NonPublic).MakeGenericMethod(t));
            }
        }

        public static byte[] Serialize(IProtoSerializable o)
        {
            var type = o.GetType();
            var id = (ProtoId) type.GetCustomAttribute(typeof(ProtoId));
            if (!map.ContainsKey(id.id)) throw new ConstraintException($"Type with id {id} doesn't exist");
            var msg = (byte[]) map[id.id].Item1.Invoke(null, new object[] {o});
            return BitConverter.GetBytes(id.id).Concat(msg).ToArray();
        }

        public static IProtoSerializable Unserialize(byte[] msg)
        {
            var id = BitConverter.ToUInt16(msg, 0);
            if (!map.ContainsKey(id)) throw new ConstraintException($"Type with id {id} doesn't exist");
            return (IProtoSerializable) map[id].Item2.Invoke(null, new object[] {msg.Skip(2).ToArray()});
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

    [ProtoContract]
    [ProtoId(2)]
    public class UnsubscribeInventories : IProtoSerializable
    {
        public ushort typeId { get; }
    }
}