using System.Collections.Generic;

using Sanctuary.Core.IO;
using Sanctuary.Packet.Common;

namespace Sanctuary.Packet;

public class QuestAddPacket : BaseQuestPacket, ISerializablePacket
{
    public new const int OpCode = 3;

    public int QuestId;
    public int TitleId;
    public int DescriptionId;
    public int HelperTextId;
    public bool MembersOnly;
    public long TimeStarted;
    public int ProfileId;
    public bool SuppressStartBanner;
    public float CompletedPercentage;

    public RewardBundleBase RewardBundle { get; set; } = new()
    {
        Success = false,
        Unknown3 = 0,
        Multiplier = 0f
    };

    public int Unknown;

    public Dictionary<int, QuestObjectiveInfo> Objectives { get; set; } = [];

    public int IconId;
    public bool SystemQuest;
    public bool Unknown2;

    public bool Unknown3;

    public QuestAddPacket() : base(OpCode)
    {
    }

    public byte[] Serialize()
    {
        using var writer = new PacketWriter();

        Write(writer);

        writer.Write(QuestId);
        writer.Write(TitleId);
        writer.Write(DescriptionId);
        writer.Write(HelperTextId);
        writer.Write(MembersOnly);
        writer.Write(TimeStarted);
        writer.Write(ProfileId);
        writer.Write(SuppressStartBanner);
        writer.Write(CompletedPercentage);

        RewardBundle.Serialize(writer);
        writer.Write(Unknown);

        writer.Write(Objectives);

        writer.Write(IconId);
        writer.Write(SystemQuest);
        writer.Write(Unknown2);

        writer.Write(Unknown3);

        return writer.Buffer;
    }
}
