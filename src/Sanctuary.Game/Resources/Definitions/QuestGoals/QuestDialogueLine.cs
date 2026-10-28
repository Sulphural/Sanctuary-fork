using System.Collections.Generic;

namespace Sanctuary.Game.Resources.Definitions.QuestGoals;

public sealed class QuestDialogueLine
{
    public int TextId { get; set; }

    // 0 = the generic caption.
    public int ResponseTextId { get; set; }

    public bool Closable { get; set; } = true;

    public int SoundId { get; set; }

    public int AnimationId { get; set; }

    public List<QuestDialogueChoice> Choices { get; set; } = [];
}
