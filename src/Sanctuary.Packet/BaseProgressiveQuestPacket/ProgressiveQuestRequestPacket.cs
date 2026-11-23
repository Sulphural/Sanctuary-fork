using System;

using Sanctuary.Core.IO;

namespace Sanctuary.Packet;

public class ProgressiveQuestRequestPacket : BaseProgressiveQuestPacket, IDeserializable<ProgressiveQuestRequestPacket>
{
    public const int StartQuestOpCode = 2;
    public const int ClaimSlotOpCode = 3;
    public const int ClaimPrizeOpCode = 4;

    public int QuestId;
    public int SlotId;

    public ProgressiveQuestRequestPacket(int subOpCode) : base(subOpCode)
    {
    }

    public static bool TryDeserialize(ReadOnlySpan<byte> data, out ProgressiveQuestRequestPacket value)
    {
        var reader = new PacketReader(data);

        value = new ProgressiveQuestRequestPacket(0);

        if (!reader.TryRead(out short _) || !reader.TryRead(out int subOpCode))
            return false;

        value = new ProgressiveQuestRequestPacket(subOpCode);

        if (!reader.TryRead(out value.QuestId))
            return false;

        if (!reader.TryRead(out value.SlotId))
            return false;

        return reader.RemainingLength == 0;
    }
}
