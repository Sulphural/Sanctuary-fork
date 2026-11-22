using Sanctuary.Core.IO;

namespace Sanctuary.Packet;

public class PlayerMemberStatusUpdatePacket : BasePlayerUpdatePacket, ISerializablePacket
{
    public new const short OpCode = 66;

    public ulong Guid;

    public bool IsMember;

    public PlayerMemberStatusUpdatePacket() : base(OpCode)
    {
    }

    public byte[] Serialize()
    {
        using var writer = new PacketWriter();

        Write(writer);

        writer.Write(Guid);
        writer.Write(IsMember);

        return writer.Buffer;
    }
}
