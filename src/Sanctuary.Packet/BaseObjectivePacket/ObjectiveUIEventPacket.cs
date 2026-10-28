using Sanctuary.Core.IO;

namespace Sanctuary.Packet;

public class ObjectiveUIEventPacket : BaseObjectivePacket, ISerializablePacket
{
    public new const byte OpCode = 10;

    public int ObjectiveId;

    public string Name = string.Empty;

    public ObjectiveUIEventPacket() : base(OpCode)
    {
    }

    public byte[] Serialize()
    {
        using var writer = new PacketWriter();

        Write(writer);

        writer.Write(ObjectiveId);
        writer.Write(Name);

        return writer.Buffer;
    }
}
