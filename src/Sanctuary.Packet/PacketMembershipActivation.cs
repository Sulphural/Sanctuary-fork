using Sanctuary.Core.IO;

namespace Sanctuary.Packet;

public class PacketMembershipActivation : ISerializablePacket
{
    public const short OpCode = 100;

    public int Status;

    public byte[] Serialize()
    {
        using var writer = new PacketWriter();

        writer.Write(OpCode);

        writer.Write(Status);

        return writer.Buffer;
    }
}
