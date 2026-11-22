using Sanctuary.Database;
using Sanctuary.Packet;

namespace Sanctuary.Gateway.Uram;

public static class MembershipLive
{
    public static void Apply(GatewayConnection connection, int status, bool lifetime)
    {
        connection.QueueWork(() =>
        {
            var player = connection.Player;

            if (player is null)
                return;

            var member = status != Membership.FreeStatus;

            player.MembershipStatus = status;
            player.IsLifetimeMember = member && lifetime;

            player.SendTunneled(new PacketMembershipActivation
            {
                Status = status
            });

            player.SendTunneled(new PacketMembershipSubscriptionInfo
            {
                IsMember = player.IsLifetimeMember,
                IsReferee = player.IsAdmin || player.IsMod
            });

            player.SendTunneledToVisible(new PlayerMemberStatusUpdatePacket
            {
                Guid = player.Guid,
                IsMember = member
            }, sendToSelf: true);
        });
    }
}
