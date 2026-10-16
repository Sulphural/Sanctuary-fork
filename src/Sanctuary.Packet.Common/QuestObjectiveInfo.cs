using Sanctuary.Core.IO;

namespace Sanctuary.Packet.Common;

public class QuestObjectiveInfo : ISerializableType
{
    public int ObjectiveId { get; set; }
    public int NameId { get; set; }
    public int DescriptionId { get; set; }

    public bool Unknown { get; set; }

    public RewardBundleBase RewardBundle { get; set; } = new()
    {
        Success = false,
        Unknown3 = 0,
        Multiplier = 0f
    };

    public int Unknown2 { get; set; }
    public int Unknown3 { get; set; }
    public int Unknown4 { get; set; }
    public int Unknown5 { get; set; }
    public int Unknown6 { get; set; }
    public bool Unknown7 { get; set; }
    public int Unknown8 { get; set; }

    public void Serialize(PacketWriter writer)
    {
        writer.Write(ObjectiveId);
        writer.Write(NameId);
        writer.Write(DescriptionId);
        writer.Write(Unknown);

        RewardBundle.Serialize(writer);
        writer.Write(Unknown2);

        writer.Write(Unknown3);
        writer.Write(Unknown4);
        writer.Write(Unknown5);
        writer.Write(Unknown6);
        writer.Write(Unknown7);
        writer.Write(Unknown8);
    }
}
