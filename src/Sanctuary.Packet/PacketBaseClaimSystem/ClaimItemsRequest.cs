using System;
using System.Collections.Generic;

using Sanctuary.Core.IO;

namespace Sanctuary.Packet;

public class ClaimItemsRequest : PacketBaseClaimSystem, IDeserializable<ClaimItemsRequest>
{
    public new const short OpCode = 3;

    public uint RequestId;

    public List<ulong> ClaimInstanceIds = [];

    public ClaimItemsRequest() : base(OpCode)
    {
    }

    public static bool TryDeserialize(ReadOnlySpan<byte> data, out ClaimItemsRequest value)
    {
        value = new ClaimItemsRequest();

        var reader = new PacketReader(data);

        if (!value.TryRead(ref reader))
            return false;

        if (!reader.TryRead(out value.RequestId))
            return false;

        if (!reader.TryRead(out int count) || count < 0 || count > 1000)
            return false;

        for (var i = 0; i < count; i++)
        {
            if (!reader.TryRead(out ulong id))
                return false;

            value.ClaimInstanceIds.Add(id);
        }

        return reader.RemainingLength == 0;
    }
}
