using PKHeX.Core;

namespace SysBot.Pokemon.Discord.Helpers.TradeModule
{
    public static class NaturePipeline
    {
        public static void ProcessNatures(PKM pkm, Nature? setNature, Nature? statNature, bool shinyRequested)
        {
            bool hasSetNature = setNature.HasValue && setNature.Value != Nature.Random;
            bool hasStatNature = statNature.HasValue && statNature.Value != Nature.Random;

            // CASE 1 — Stat Nature only

            {
                if (statNature.HasValue) // Ensure statNature is not null
                {
                    pkm.StatNature = statNature.Value;
                }
                pkm.RefreshChecksum();
                return;
            }

            // CASE 2 — Both SetNature and StatNature
            if (hasSetNature && hasStatNature)
            {
                if (setNature.HasValue) // Ensure setNature is not null
                {
                    ForceNatureHelper.ForceNature(pkm, setNature.Value, shinyRequested);
                }
                if (statNature.HasValue) // Ensure statNature is not null
                {
                    pkm.StatNature = statNature.Value;
                }
                pkm.RefreshChecksum();
                return;
            }

            // CASE 3 — SetNature only
            if (hasSetNature && !hasStatNature)
            {
                if (setNature.HasValue) // Ensure setNature is not null
                {
                    ForceNatureHelper.ForceNature(pkm, setNature.Value, shinyRequested);
                }
                return;
            }

            // CASE 4 — Neither → ALM handles it
        }
    }
}
