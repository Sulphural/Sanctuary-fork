using Sanctuary.Core.IO;

namespace Sanctuary.Packet;

public class EncounterPlayerEnterPacket : BaseEncounterPacket, ISerializablePacket
{
    public new const short OpCode = 2;

    public ulong Guid;

    public bool NpxTutorial;

    public EncounterPlayerEnterPacket() : base(OpCode)
    {
    }

    public byte[] Serialize()
    {
        using var writer = new PacketWriter();

        Write(writer);

        writer.Write(Unknown);
        writer.Write(Unknown2);

        writer.Write(Guid);

        writer.Write(NpxTutorial);

        return writer.Buffer;
    }
}
