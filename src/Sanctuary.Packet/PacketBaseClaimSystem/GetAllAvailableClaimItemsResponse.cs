using System.Collections.Generic;

using Sanctuary.Core.IO;

namespace Sanctuary.Packet;

public class GetAllAvailableClaimItemsResponse : PacketBaseClaimSystem, ISerializablePacket
{
    public new const short OpCode = 2;

    public bool Success = true;

    public List<ClaimItem> Items = [];

    public GetAllAvailableClaimItemsResponse() : base(OpCode)
    {
    }

    public byte[] Serialize()
    {
        using var writer = new PacketWriter();

        Write(writer);

        writer.Write(Success);

        if (Success)
        {
            writer.Write(Items.Count);
            foreach (var item in Items)
                item.Write(writer);
        }

        return writer.Buffer;
    }
}
