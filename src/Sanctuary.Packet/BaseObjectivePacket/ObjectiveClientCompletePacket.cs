using System;

using Sanctuary.Core.IO;

namespace Sanctuary.Packet;

public class ObjectiveClientCompletePacket : BaseObjectivePacket, IDeserializable<ObjectiveClientCompletePacket>
{
    public new const byte OpCode = 7;

    public int ObjectiveId;

    public ObjectiveClientCompletePacket() : base(OpCode)
    {
    }

    public static bool TryDeserialize(ReadOnlySpan<byte> data, out ObjectiveClientCompletePacket value)
    {
        value = new ObjectiveClientCompletePacket();

        var reader = new PacketReader(data);

        if (!value.TryRead(ref reader))
            return false;

        if (!reader.TryRead(out value.ObjectiveId))
            return false;

        return reader.RemainingLength == 0;
    }
}
