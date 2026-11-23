using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Sanctuary.Core.Helpers;
using Sanctuary.Database;
using Sanctuary.Game;
using Sanctuary.Game.Resources.Definitions.Rewards;
using Sanctuary.Packet;

namespace Sanctuary.Gateway.Uram;

public sealed class UramServer : BackgroundService
{
    private const int MaxRequestBytes = 32 * 1024;
    private const int MaxConcurrentRequests = 32;

    private readonly ILogger<UramServer> _logger;
    private readonly UramOptions _options;
    private readonly IDbContextFactory<DatabaseContext> _dbContextFactory;
    private readonly IRewardManager _rewardManager;
    private readonly ItemNames _itemNames;
    private readonly SemaphoreSlim _slots = new(MaxConcurrentRequests);

    private X509Certificate2 _leaf = null!;
    private X509Certificate2 _authority = null!;

    public UramServer(ILogger<UramServer> logger, IOptions<UramOptions> options, IDbContextFactory<DatabaseContext> dbContextFactory, IRewardManager rewardManager, ItemNames itemNames)
    {
        _logger = logger;
        _options = options.Value;
        _dbContextFactory = dbContextFactory;
        _rewardManager = rewardManager;
        _itemNames = itemNames;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
            return;

        (_leaf, _authority) = UramCertificates.Load(_options.CertificateFolder, _options.CertificateName);

        var listener = new TcpListener(IPAddress.Any, _options.Port);
        listener.Start();

        _logger.LogInformation("In-game purchase server listening on port {port} (certificate name \"{name}\", authority in {folder}).", _options.Port, _options.CertificateName, _options.CertificateFolder);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var client = await listener.AcceptTcpClientAsync(stoppingToken);

                if (!await _slots.WaitAsync(0, stoppingToken))
                {
                    client.Dispose();
                    continue;
                }

                _ = Task.Run(() =>
                {
                    try
                    {
                        Handle(client);
                    }
                    finally
                    {
                        _slots.Release();
                    }
                }, stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            listener.Stop();
        }
    }

    private void Handle(TcpClient client)
    {
        var remote = client.Client.RemoteEndPoint;

        using (client)
        {
            client.ReceiveTimeout = 15000;
            client.SendTimeout = 15000;

            using var stream = client.GetStream();
            using var tls = new Ssl3ServerConnection(stream, _leaf, [_authority]);

            try
            {
                tls.Handshake();

                var (path, body) = ReadRequest(tls);
                var form = ParseForm(body);
                var reply = Route(path, form);

                var bytes = Encoding.UTF8.GetBytes(reply);
                var header = "HTTP/1.1 200 OK\r\nContent-Type: text/xml; charset=UTF-8\r\nContent-Length: " + bytes.Length + "\r\nConnection: close\r\n\r\n";

                tls.Write(Encoding.ASCII.GetBytes(header));
                tls.Write(bytes);
                tls.Close();
            }
            catch (Exception ex) when (ex is Ssl3Exception or System.IO.IOException or CryptographicException or FormatException)
            {
                _logger.LogWarning("In-game purchase request from {remote} failed: {message}", remote, ex.Message);
            }
        }
    }

    private static (string Path, string Body) ReadRequest(Ssl3ServerConnection tls)
    {
        var data = new List<byte>();
        var buffer = new byte[4096];
        var headerEnd = -1;

        while (headerEnd < 0)
        {
            var read = tls.Read(buffer, 0, buffer.Length);
            if (read == 0)
                throw new FormatException("Connection closed before the request was complete.");

            data.AddRange(buffer.Take(read));
            if (data.Count > MaxRequestBytes)
                throw new FormatException("Request too large.");

            headerEnd = IndexOf(data, "\r\n\r\n"u8.ToArray());
        }

        var headerText = Encoding.ASCII.GetString(data.Take(headerEnd).ToArray());
        var lines = headerText.Split("\r\n");
        var requestLine = lines[0].Split(' ');

        if (requestLine.Length < 2)
            throw new FormatException("Bad request line.");

        var contentLength = 0;
        foreach (var line in lines.Skip(1))
        {
            var colon = line.IndexOf(':');
            if (colon > 0 && line[..colon].Trim().Equals("Content-Length", StringComparison.OrdinalIgnoreCase))
                contentLength = int.Parse(line[(colon + 1)..].Trim());
        }

        if (contentLength < 0 || contentLength > MaxRequestBytes)
            throw new FormatException("Bad content length.");

        var bodyStart = headerEnd + 4;
        while (data.Count - bodyStart < contentLength)
        {
            var read = tls.Read(buffer, 0, buffer.Length);
            if (read == 0)
                break;
            data.AddRange(buffer.Take(read));
        }

        var body = Encoding.UTF8.GetString(data.Skip(bodyStart).Take(contentLength).ToArray());
        return (requestLine[1], body);
    }

    private static int IndexOf(List<byte> data, byte[] pattern)
    {
        for (var i = 0; i <= data.Count - pattern.Length; i++)
        {
            var match = true;
            for (var j = 0; j < pattern.Length && match; j++)
                match = data[i + j] == pattern[j];

            if (match)
                return i;
        }

        return -1;
    }

    private static Dictionary<string, string> ParseForm(string body)
    {
        var form = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var pair in body.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var equals = pair.IndexOf('=');
            var key = equals < 0 ? pair : pair[..equals];
            var value = equals < 0 ? string.Empty : pair[(equals + 1)..];
            form[WebUtility.UrlDecode(key)] = WebUtility.UrlDecode(value);
        }

        return form;
    }

    private string Route(string path, Dictionary<string, string> form)
    {
        var action = path.Split('?')[0];
        action = action[(action.LastIndexOf('/') + 1)..].ToLowerInvariant();

        _logger.LogInformation("In-game purchase request: {action} ( sku: {sku} )", action, form.GetValueOrDefault("sku"));

        return action switch
        {
            "purchasepreview.action" => Preview(form),
            "purchase.action" or "purchaseorder.action" => Purchase(form),
            "redeemcode.action" => Redeem(form),
            _ => Failure("purchaseresponse", "This is not available on this server.")
        };
    }

    private string Preview(Dictionary<string, string> form)
    {
        const string root = "purchasepreviewresponse";

        if (!TryFindBuyer(form, out var connection, out var plan, out var error))
            return Failure(root, error);

        using var dbContext = _dbContextFactory.CreateDbContext();

        var characterId = GuidHelper.GetPlayerId(connection.Player.Guid);
        var character = dbContext.Characters.AsNoTracking().Where(x => x.Id == characterId).Select(x => new { x.UserId, x.StationCash }).SingleOrDefault();

        if (character is null)
            return Failure(root, "Your character could not be found.");

        var user = dbContext.Users.AsNoTracking().SingleOrDefault(x => x.Id == character.UserId);

        if (user is null)
            return Failure(root, "Your account could not be found.");

        if (Membership.StatusFor(user) == Membership.MemberStatus && user.MemberUntil is null)
            return Failure(root, "You already have a Lifetime Membership.");

        if (character.StationCash < plan.PriceStationCash)
            return Failure(root, "You don't have enough Station Cash for this membership.");

        var price = plan.PriceStationCash.ToString();
        var name = Escape(plan.IsLifetime ? "Lifetime Membership" : plan.Name + " Membership");

        return $"<?xml version=\"1.0\" encoding=\"UTF-8\"?><{root}><version>11</version><status>success</status>"
            + $"<oid>{RandomNumberGenerator.GetInt32(100000, int.MaxValue)}</oid>"
            + $"<usedPaymentSourceId>{Escape(form.GetValueOrDefault("paymentSourceId") ?? string.Empty)}</usedPaymentSourceId>"
            + "<tax>0</tax><vatTax>0</vatTax><discountAmount>0</discountAmount>"
            + $"<orderTotal>{price}</orderTotal>"
            + "<previewLegalText>Your Station Cash is spent right away. Membership is added to your account.</previewLegalText>"
            + "<subscriptionproducts><subscriptionproductdiscountaware>"
            + $"<productname>{name}</productname><price>{price}</price><isDiscounted>false</isDiscounted>"
            + $"<originalPrice>{price}</originalPrice><discountedPrice>{price}</discountedPrice>"
            + $"<currentCharge>{price}</currentCharge><nextCharge>0</nextCharge><nextDetails></nextDetails>"
            + $"</subscriptionproductdiscountaware></subscriptionproducts></{root}>";
    }

    private string Purchase(Dictionary<string, string> form)
    {
        const string root = "purchaseresponse";

        if (!TryFindBuyer(form, out var connection, out var plan, out var error))
            return Failure(root, error);

        using var dbContext = _dbContextFactory.CreateDbContext();

        var characterId = GuidHelper.GetPlayerId(connection.Player.Guid);
        var userId = dbContext.Characters.Where(x => x.Id == characterId).Select(x => (ulong?)x.UserId).SingleOrDefault();
        var user = userId is null ? null : dbContext.Users.SingleOrDefault(x => x.Id == userId);

        if (user is null)
            return Failure(root, "Your account could not be found.");

        Membership.ExpireIfDue(dbContext, user);

        if (Membership.IsLifetime(user))
            return Failure(root, "You already have a Lifetime Membership.");

        using var transaction = dbContext.Database.BeginTransaction();

        var price = plan.PriceStationCash;
        var paid = dbContext.Characters
            .Where(x => x.Id == characterId && x.StationCash >= price)
            .ExecuteUpdate(x => x.SetProperty(c => c.StationCash, c => c.StationCash - price));

        if (paid == 0)
            return Failure(root, "You don't have enough Station Cash for this membership.");

        Membership.Extend(dbContext, user, plan);
        dbContext.SaveChanges();
        transaction.Commit();

        var balance = dbContext.Characters.AsNoTracking().Where(x => x.Id == characterId).Select(x => x.StationCash).Single();

        _logger.LogInformation("{username} bought {plan} membership in game for {price} Station Cash.", user.Username, plan.Name, price);

        connection.QueueWork(() => connection.Player.StationCash = balance);
        MembershipLive.Apply(connection, Membership.MemberStatus, Membership.IsLifetime(user));

        return $"<?xml version=\"1.0\" encoding=\"UTF-8\"?><{root}><version>11</version><status>success</status>"
            + $"<orderid>{RandomNumberGenerator.GetInt32(100000, int.MaxValue)}</orderid>"
            + "<subscriptionproducts><subscriptionproduct>"
            + $"<productname>{Escape(plan.IsLifetime ? "Lifetime Membership" : plan.Name + " Membership")}</productname><price>{price}</price>"
            + "</subscriptionproduct></subscriptionproducts>"
            + $"<discountamount>0</discountamount><vattax>0</vattax><tax>0</tax><ordertotal>{price}</ordertotal></{root}>";
    }

    private string Redeem(Dictionary<string, string> form)
    {
        const string root = "redeemcoderesponse";

        var connection = UramSessions.Find(form.GetValueOrDefault("sessionID"));
        if (connection?.Player is null)
            return Failure(root, "Your session has expired. Please close the window and try again.");

        using var dbContext = _dbContextFactory.CreateDbContext();

        var characterId = GuidHelper.GetPlayerId(connection.Player.Guid);
        var userId = PendingRewards.UserIdOf(dbContext, connection);

        if (userId is null)
            return Failure(root, "Your account could not be found.");

        var outcome = CodeRedeemer.Redeem(dbContext, userId.Value, characterId, form.GetValueOrDefault("activationCode"));

        if (!outcome.Success)
        {
            return Failure(root, outcome.Error switch
            {
                CodeRedeemer.Expired => "That code has expired.",
                CodeRedeemer.AlreadyRedeemed => "You already used this code.",
                CodeRedeemer.UsedUp => "That code has already been used up.",
                _ => "That code isn't valid. Check it and try again."
            });
        }

        var code = outcome.Code!;

        _logger.LogInformation("{connection} redeemed code {code} in game.", connection, code.Code);

        int? stationCashBalance = null;

        if (code.StationCash > 0)
        {
            var stationCash = code.StationCash;

            dbContext.Characters
                .Where(x => x.Id == characterId)
                .ExecuteUpdate(x => x.SetProperty(c => c.StationCash, c => c.StationCash > int.MaxValue - stationCash ? int.MaxValue : c.StationCash + stationCash));

            stationCashBalance = dbContext.Characters.AsNoTracking().Where(x => x.Id == characterId).Select(x => x.StationCash).Single();
        }

        connection.QueueWork(() =>
        {
            if (connection.Player is null)
                return;

            if (code.Coins > 0)
                _rewardManager.TryGrantCurrency(connection.Player, CurrencyType.Coins, code.Coins);

            if (stationCashBalance is { } balance)
                connection.Player.StationCash = balance;

            if (outcome.PendingReward is not null)
                Handlers.PacketBaseClaimSystemHandler.SendList(connection);
        });

        var features = new StringBuilder();

        if (outcome.PendingReward is { } pending)
        {
            var name = _itemNames.NameOf(pending.ItemDefinition);
            features.Append(Feature(pending.Quantity > 1 ? $"{pending.Quantity} x {name} (claim it in the Claim tab)" : $"{name} (claim it in the Claim tab)"));
        }

        if (code.Coins > 0)
            features.Append(Feature($"{code.Coins:N0} Coins"));

        return $"<?xml version=\"1.0\" encoding=\"UTF-8\"?><{root}><version>11</version><status>success</status>"
            + $"<grantedfeatures><nonrecurringfeatures>{features}</nonrecurringfeatures>"
            + $"<stationcashgranted>{(code.StationCash > 0 ? code.StationCash : 0)}</stationcashgranted></grantedfeatures>"
            + $"<termsandconditionsdescription>{Escape(code.Description)}</termsandconditionsdescription><termsandconditionslink></termsandconditionslink></{root}>";
    }

    private static string Feature(string description)
    {
        return $"<feature><gamecode>FR</gamecode><id>1</id><value>1</value><description>{Escape(description)}</description></feature>";
    }

    private static bool TryFindBuyer(Dictionary<string, string> form, out GatewayConnection connection, out MembershipPlan plan, out string error)
    {
        connection = null!;
        plan = null!;
        error = string.Empty;

        var found = UramSessions.Find(form.GetValueOrDefault("sessionID"));
        if (found?.Player is null)
        {
            error = "Your purchase session has expired. Please close the store and try again.";
            return false;
        }

        var foundPlan = Membership.FindPlan(form.GetValueOrDefault("sku"));
        if (foundPlan is null)
        {
            error = "That membership is not available.";
            return false;
        }

        connection = found;
        plan = foundPlan;
        return true;
    }

    private static string Failure(string root, string message)
    {
        return $"<?xml version=\"1.0\" encoding=\"UTF-8\"?><{root}><version>11</version><status>failure</status>"
            + $"<errorreasons><errorreason>{Escape(message)}</errorreason></errorreasons><errors><error>{Escape(message)}</error></errors></{root}>";
    }

    private static string Escape(string value) => SecurityElement.Escape(value) ?? string.Empty;
}
