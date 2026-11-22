using System.Collections.Generic;

using Sanctuary.Core.IO;

namespace Sanctuary.Packet;

public class ClaimItemsResponse : PacketBaseClaimSystem, ISerializablePacket
{
    public new const short OpCode = 4;

    public uint RequestId;

    public bool Success;

    public List<ClaimItem> Items = [];

    public ClaimItemsResponse() : base(OpCode)
    {
    }

    public byte[] Serialize()
    {
        using var writer = new PacketWriter();

        Write(writer);

        writer.Write(RequestId);
        writer.Write(Success);

        writer.Write(Items.Count);
        foreach (var item in Items)
            item.Write(writer);

        return writer.Buffer;
    }
}
