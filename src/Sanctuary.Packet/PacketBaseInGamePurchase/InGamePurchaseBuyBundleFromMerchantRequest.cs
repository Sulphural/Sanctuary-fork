using System;

using Sanctuary.Core.IO;

namespace Sanctuary.Packet;

public class InGamePurchaseBuyBundleFromMerchantRequest : PacketBaseInGamePurchase, IDeserializable<InGamePurchaseBuyBundleFromMerchantRequest>
{
    public new const short OpCode = 43;

    public int BundleId;
    public int Unknown;
    public ulong MerchantGuid;
    public int Unknown2;
    public string Locale = string.Empty;
    public int Unknown3;
    public int Quantity;
    public int StoreId;

    public InGamePurchaseBuyBundleFromMerchantRequest() : base(OpCode)
    {
    }

    public static bool TryDeserialize(ReadOnlySpan<byte> data, out InGamePurchaseBuyBundleFromMerchantRequest value)
    {
        value = new InGamePurchaseBuyBundleFromMerchantRequest();

        var reader = new PacketReader(data);

        if (!value.TryRead(ref reader))
            return false;

        if (!reader.TryRead(out value.BundleId))
            return false;

        if (!reader.TryRead(out value.Unknown))
            return false;

        if (!reader.TryRead(out value.MerchantGuid))
            return false;

        if (!reader.TryRead(out value.Unknown2))
            return false;

        if (!reader.TryRead(out value.Locale))
            return false;

        if (!reader.TryRead(out value.Unknown3))
            return false;

        if (!reader.TryRead(out value.Quantity))
            return false;

        if (!reader.TryRead(out value.StoreId))
            return false;

        return reader.RemainingLength == 0;
    }
}
