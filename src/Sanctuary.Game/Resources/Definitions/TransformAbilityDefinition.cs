namespace Sanctuary.Game.Resources.Definitions;

public class TransformAbilityDefinition
{
    public int AbilityId { get; set; }
    public int ModelId { get; set; }
    public int DurationMs { get; set; }
    public int CooldownMs { get; set; }
    public int CompositeEffectId { get; set; }
    public int RepeatCompositeEffectId { get; set; }
    public int RepeatAnimationId { get; set; }
}
