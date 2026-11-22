using System;

using Sanctuary.Core.IO;

namespace Sanctuary.Packet;

public class GetAllAvailableClaimItemsRequest : PacketBaseClaimSystem, IDeserializable<GetAllAvailableClaimItemsRequest>
{
    public new const short OpCode = 1;

    public GetAllAvailableClaimItemsRequest() : base(OpCode)
    {
    }

    public static bool TryDeserialize(ReadOnlySpan<byte> data, out GetAllAvailableClaimItemsRequest value)
    {
        value = new GetAllAvailableClaimItemsRequest();

        var reader = new PacketReader(data);

        if (!value.TryRead(ref reader))
            return false;

        return reader.RemainingLength == 0;
    }
}
