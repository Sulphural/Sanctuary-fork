using System.Collections.Generic;

using Sanctuary.Core.IO;

namespace Sanctuary.Packet;

public class ProgressiveQuestClientDataPacket : BaseProgressiveQuestPacket, ISerializablePacket
{
    public new const int OpCode = 1;

    public class Slot
    {
        public int QuestId;
        public int SlotId;
        public int IconId;
        public bool HasSlotItem;
        public bool HasKeyItem;
        public bool CanDoQuest;
        public bool CanPurchaseKeyItem;
        public int KeyBundleId;
        public int KeyBundlePrice;
        public bool CanClaimSlotItem;
        public int TooltipId;
        public int NameId;
    }

    public class PrizeSlot
    {
        public int QuestId;
        public int PrizeSlotId;
        public int NameId;
        public int IconId;
        public int TooltipId;
        public int ProgressPercent;
        public bool CanClaimPrizeItem;
    }

    public int QuestId;
    public int NameId;
    public int SecondsRemaining;
    public int ActiveSlotId;
    public int IconId;
    public int ObjectiveStringId1;
    public int ObjectiveStringId2;
    public int ObjectiveStringId3;

    public List<Slot> Slots = [];
    public List<PrizeSlot> PrizeSlots = [];

    public bool UseSmallWindow;
    public int ButtonStringIdOpen;
    public int ButtonStringIdOpened;
    public int ButtonStringIdBuy;

    public ProgressiveQuestClientDataPacket() : base(OpCode)
    {
    }

    public byte[] Serialize()
    {
        using var writer = new PacketWriter();

        Write(writer);

        writer.Write(QuestId);
        writer.Write(NameId);
        writer.Write(SecondsRemaining);
        writer.Write(ActiveSlotId);
        writer.Write(IconId);
        writer.Write(ObjectiveStringId1);
        writer.Write(ObjectiveStringId2);
        writer.Write(ObjectiveStringId3);

        writer.Write(Slots.Count);

        foreach (var slot in Slots)
        {
            writer.Write(slot.QuestId);
            writer.Write(slot.SlotId);
            writer.Write(slot.IconId);
            writer.Write(slot.HasSlotItem);
            writer.Write(slot.HasKeyItem);
            writer.Write(slot.CanDoQuest);
            writer.Write(slot.CanPurchaseKeyItem);
            writer.Write(slot.KeyBundleId);
            writer.Write(slot.KeyBundlePrice);
            writer.Write(slot.CanClaimSlotItem);
            writer.Write(slot.TooltipId);
            writer.Write(slot.NameId);
        }

        writer.Write(PrizeSlots.Count);

        foreach (var prize in PrizeSlots)
        {
            writer.Write(prize.QuestId);
            writer.Write(prize.PrizeSlotId);
            writer.Write(prize.NameId);
            writer.Write(prize.IconId);
            writer.Write(prize.TooltipId);
            writer.Write(prize.ProgressPercent);
            writer.Write(prize.CanClaimPrizeItem);
        }

        writer.Write(UseSmallWindow);
        writer.Write(ButtonStringIdOpen);
        writer.Write(ButtonStringIdOpened);
        writer.Write(ButtonStringIdBuy);

        return writer.Buffer;
    }
}
