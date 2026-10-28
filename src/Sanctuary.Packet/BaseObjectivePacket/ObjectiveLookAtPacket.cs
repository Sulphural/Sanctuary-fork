using Sanctuary.Core.IO;

namespace Sanctuary.Packet;

public class ObjectiveLookAtPacket : BaseObjectivePacket, ISerializablePacket
{
    public new const byte OpCode = 6;

    public int ObjectiveId;

    public ulong TargetGuid;

    public int HeightOffset;

    public int MaxDistance;

    public int ConeAngle;

    public ObjectiveLookAtPacket() : base(OpCode)
    {
    }

    public byte[] Serialize()
    {
        using var writer = new PacketWriter();

        Write(writer);

        writer.Write(ObjectiveId);
        writer.Write(TargetGuid);
        writer.Write(HeightOffset);
        writer.Write(MaxDistance);
        writer.Write(ConeAngle);

        return writer.Buffer;
    }
}
