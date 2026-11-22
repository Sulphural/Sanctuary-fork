using System;

using Sanctuary.Core.IO;

namespace Sanctuary.Packet;

public class OneTimeSessionRequestPacket : IDeserializable<OneTimeSessionRequestPacket>
{
    public const short OpCode = 117;

    public int Type;

    public static bool TryDeserialize(ReadOnlySpan<byte> data, out OneTimeSessionRequestPacket value)
    {
        value = new OneTimeSessionRequestPacket();

        var reader = new PacketReader(data);

        if (!reader.TryRead(out short opCode) || opCode != OpCode)
            return false;

        if (!reader.TryRead(out value.Type))
            return false;

        return reader.RemainingLength == 0;
    }
}
