using System;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Sanctuary.Core.Configuration;
using Sanctuary.Core.Helpers;
using Sanctuary.Database;
using Sanctuary.Game;
using Sanctuary.Packet;
using Sanctuary.Packet.Common;
using Sanctuary.Packet.Common.Attributes;

namespace Sanctuary.Login.Handlers;

[PacketHandler]
public static class CharacterCreateRequestHandler
{
    private static ILogger _logger = null!;
    private static LoginServerOptions _options = null!;
    private static IResourceManager _resourceManager = null!;
    private static IDbContextFactory<DatabaseContext> _dbContextFactory = null!;

    public static void ConfigureServices(IServiceProvider serviceProvider)
    {
        var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
        _logger = loggerFactory.CreateLogger(nameof(CharacterCreateRequestHandler));

        _resourceManager = serviceProvider.GetRequiredService<IResourceManager>();
        _dbContextFactory = serviceProvider.GetRequiredService<IDbContextFactory<DatabaseContext>>();

        var options = serviceProvider.GetRequiredService<IOptionsMonitor<LoginServerOptions>>();
        _options = options.CurrentValue;
        options.OnChange(o => _options = o);
    }

    public static bool HandlePacket(LoginConnection connection, Span<byte> data)
    {
        if (!CharacterCreateRequest.TryDeserialize(data, out var packet))
        {
            _logger.LogError("Failed to deserialize {packet}.", nameof(CharacterCreateRequest));
            return false;
        }

        _logger.LogTrace("Received {name} packet. ( {packet} )", nameof(CharacterCreateRequest), packet);

        var characterCreateReply = new CharacterCreateReply();

        if (!CharacterData.TryDeserialize(packet.Payload, out var characterData))
        {
            _logger.LogError("Failed to create character, invalid character data.");

            characterCreateReply.Result = 3;

            connection.Send(characterCreateReply);

            return true;
        }

        if (connection.UserId == 0)
        {
            _logger.LogError("Failed to create character, unknown user id.");

            characterCreateReply.Result = 3;

            connection.Send(characterCreateReply);

            return true;
        }

        var settings = new CharacterCreationSettings
        {
            DefaultTitleId = _options.DefaultTitleId,
            DefaultProfileId = _options.DefaultProfileId,
            StartingCoins = _options.StartingCoins,
            StartingStationCash = _options.StartingStationCash,
            UnlockAllTitles = _options.UnlockAllTitles,
            UnlockAllProfiles = _options.UnlockAllProfiles
        };

        using var dbContext = _dbContextFactory.CreateDbContext();

        var result = CharacterCreator.Create(_resourceManager, dbContext, settings, connection.UserId, characterData, _logger, out var characterId);

        characterCreateReply.Result = (int)result;

        if (result == CharacterCreateResult.Success)
            characterCreateReply.Guid = GuidHelper.GetPlayerGuid(characterId);

        connection.Send(characterCreateReply);

        return true;
    }
}
