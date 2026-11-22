using System.Collections.Generic;

using Sanctuary.Core.IO;

namespace Sanctuary.Packet;

public class ClaimItem
{
    public List<ulong> ClaimInstanceIds = [];

    public int NameId;
    public int DescriptionId;
    public int ImageSetId;
    public int TintId;

    public List<ClaimItemContent> Contents = [];

    public ulong SenderGuid;

    public void Write(PacketWriter writer)
    {
        writer.Write(ClaimInstanceIds.Count);
        foreach (var id in ClaimInstanceIds)
            writer.Write(id);

        writer.Write(NameId);
        writer.Write(DescriptionId);
        writer.Write(ImageSetId);
        writer.Write(TintId);

        writer.Write(Contents.Count);
        foreach (var content in Contents)
        {
            writer.Write(content.ItemDefinitionId);
            writer.Write(content.Unknown);
            writer.Write(content.Quantity);
        }

        writer.Write(SenderGuid);
    }
}

public class ClaimItemContent
{
    public int ItemDefinitionId;
    public int Unknown;
    public int Quantity;
}
