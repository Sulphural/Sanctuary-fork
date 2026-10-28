using System.Numerics;

using Sanctuary.Core.IO;

namespace Sanctuary.Packet;

public class CinematicStartLookAtPacket : BaseUiPacket, ISerializablePacket
{
    public new const byte OpCode = 16;

    public CinematicTarget Eye = new();
    public CinematicTarget LookAt = new();

    public float PullBack;

    public bool CinematicMode;

    public CinematicStartLookAtPacket() : base(OpCode)
    {
    }

    public byte[] Serialize()
    {
        using var writer = new PacketWriter();

        Write(writer);

        Eye.Serialize(writer);
        LookAt.Serialize(writer);

        writer.Write(PullBack);

        writer.Write(CinematicMode);

        return writer.Buffer;
    }
}

public class CinematicTarget
{
    public const int CharacterGuid = 1;
    public const int Location = 2;

    public int Type;

    public Vector4 Offset;

    public ulong Guid;

    public Vector4 Position;
    public Vector4 Rotation;

    public void Serialize(PacketWriter writer)
    {
        writer.Write(Type);

        switch (Type)
        {
            case CharacterGuid:
                writer.Write(Offset);
                writer.Write(Guid);
                break;

            case Location:
                writer.Write(Offset);
                writer.Write(Position);
                writer.Write(Rotation);
                break;
        }
    }
}
