using Sanctuary.Core.IO;

namespace Sanctuary.Packet;

public class OneTimeSessionResponsePacket : ISerializablePacket
{
    public const short OpCode = 118;

    public bool Success;
    public string? SessionId;
    public int Type;
    public string? Unknown;

    public byte[] Serialize()
    {
        using var writer = new PacketWriter();

        writer.Write(OpCode);

        writer.Write(Success);
        writer.Write(SessionId);
        writer.Write(Type);
        writer.Write(Unknown);

        return writer.Buffer;
    }
}
