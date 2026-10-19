using TerrariaModder.Core.Config;

namespace QuickRecall
{
    // ReSharper disable once ClassNeverInstantiated.Global
    public sealed class QuickRecallConfig : ModConfig
    {
        public override int Version => 1;

        [Client, Label("Use Recall Potions"),
         Description("Use recall potions when no reusable recall item is available.")]
        public bool ShouldUsePotions { get; set; } = false;
    }
}