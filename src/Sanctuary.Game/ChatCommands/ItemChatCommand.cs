using Sanctuary.Game.Entities;
using Sanctuary.Game.Helpers;

namespace Sanctuary.Game.ChatCommands;

public class ItemChatCommand : IChatCommand
{
    private const int MaxQuantity = 100;

    private readonly IChatCommandManager _chatCommandManager;
    private readonly IRewardManager _rewardManager;
    private readonly IResourceManager _resourceManager;

    public string KeyWord => "item";
    public string Usage => "<itemDefinitionId> [quantity]";
    public string Description => "Gives you an item, for testing.";
    public ChatCommandRole RequiredRole => ChatCommandRole.Admin;

    public ItemChatCommand(IChatCommandManager chatCommandManager, IRewardManager rewardManager, IResourceManager resourceManager)
    {
        _chatCommandManager = chatCommandManager;
        _rewardManager = rewardManager;
        _resourceManager = resourceManager;
    }

    public bool Handle(Player invoker, string[] args)
    {
        if (args.Length is < 1 or > 2 || !int.TryParse(args[0], out var itemDefinitionId))
            return false;

        var quantity = 1;
        if (args.Length == 2 && (!int.TryParse(args[1], out quantity) || quantity < 1 || quantity > MaxQuantity))
        {
            ChatHelper.SendSystemMessage(invoker, $"Quantity must be between 1 and {MaxQuantity}.");
            return true;
        }

        if (!_resourceManager.ClientItemDefinitions.TryGetValue(itemDefinitionId, out var itemDefinition))
        {
            ChatHelper.SendSystemMessage(invoker, $"Unknown item {itemDefinitionId}.");
            return true;
        }

        var tint = itemDefinition.IsTintable ? 0 : itemDefinition.Icon.TintId;

        if (!_rewardManager.TryGrantItem(invoker, itemDefinitionId, tint, quantity))
        {
            ChatHelper.SendSystemMessage(invoker, $"Failed to give item {itemDefinitionId}.");
            return true;
        }

        _chatCommandManager.LogAction(this, invoker, "Item grant", null, $"item={itemDefinitionId}, quantity={quantity}");
        ChatHelper.SendSystemMessage(invoker, $"Gave you {quantity} of item {itemDefinitionId}.");

        return true;
    }
}
