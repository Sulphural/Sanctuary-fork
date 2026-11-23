using Sanctuary.Core.IO;

namespace Sanctuary.Packet;

public class ProgressiveQuestShowWindowPacket : BaseProgressiveQuestPacket, ISerializablePacket
{
    public new const int OpCode = 5;

    public bool UseSmallWindow;

    public ProgressiveQuestShowWindowPacket() : base(OpCode)
    {
    }

    public byte[] Serialize()
    {
        using var writer = new PacketWriter();

        Write(writer);

        writer.Write(UseSmallWindow);

        return writer.Buffer;
    }
}
