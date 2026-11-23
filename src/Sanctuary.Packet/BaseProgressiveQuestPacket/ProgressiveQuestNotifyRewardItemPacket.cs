using Sanctuary.Core.IO;

namespace Sanctuary.Packet;

public class ProgressiveQuestNotifyRewardItemPacket : BaseProgressiveQuestPacket, ISerializablePacket
{
    public new const int OpCode = 7;

    public int IconId;
    public int TintId;
    public int TitleId;
    public int BodyId;

    public ProgressiveQuestNotifyRewardItemPacket() : base(OpCode)
    {
    }

    public byte[] Serialize()
    {
        using var writer = new PacketWriter();

        Write(writer);

        writer.Write(IconId);
        writer.Write(TintId);
        writer.Write(TitleId);
        writer.Write(BodyId);

        return writer.Buffer;
    }
}
