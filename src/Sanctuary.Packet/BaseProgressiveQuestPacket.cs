using Sanctuary.Core.IO;

namespace Sanctuary.Packet;

public class BaseProgressiveQuestPacket
{
    public const short OpCode = 207;

    public int SubOpCode { get; }

    public BaseProgressiveQuestPacket(int subOpCode)
    {
        SubOpCode = subOpCode;
    }

    public void Write(PacketWriter writer)
    {
        writer.Write(OpCode);
        writer.Write(SubOpCode);
    }

    public bool TryRead(ref PacketReader reader)
    {
        if (!reader.TryRead(out short opCode) || opCode != OpCode)
            return false;

        if (!reader.TryRead(out int subOpCode) || subOpCode != SubOpCode)
            return false;

        return true;
    }
}
