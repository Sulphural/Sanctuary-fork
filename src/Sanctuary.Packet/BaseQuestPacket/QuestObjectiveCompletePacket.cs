using Sanctuary.Core.IO;

namespace Sanctuary.Packet;

public class QuestObjectiveCompletePacket : BaseQuestPacket, ISerializablePacket
{
    public new const int OpCode = 10;

    public int QuestId;
    public int ObjectiveId;
    public float Percent;
    public int Unknown;
    public bool Silent;

    public QuestObjectiveCompletePacket() : base(OpCode)
    {
    }

    public byte[] Serialize()
    {
        using var writer = new PacketWriter();

        Write(writer);

        writer.Write(QuestId);
        writer.Write(ObjectiveId);
        writer.Write(Percent);
        writer.Write(Unknown);
        writer.Write(Silent);

        return writer.Buffer;
    }
}
