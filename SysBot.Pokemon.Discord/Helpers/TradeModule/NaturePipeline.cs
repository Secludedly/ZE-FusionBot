using PKHeX.Core;

public static class NaturePipeline
{
    public static void ProcessNatures(PKM pkm, Nature? setNature, Nature? statNature, bool shinyRequested)
    {
        bool hasSetNature = setNature.HasValue && setNature.Value != Nature.Random;
        bool hasStatNature = statNature.HasValue && statNature.Value != Nature.Random;

        // CASE 1 — StatNature ONLY, no natural Nature from set
        if (!hasSetNature && hasStatNature)
        {
            // Let AutoLegality handle Nature selection (random but legal)
            // Do NOT force nature at all.
            pkm.StatNature = statNature.Value;
            pkm.RefreshChecksum();
            return;
        }

        // CASE 2 — Both SetNature and StatNature provided
        if (hasSetNature && hasStatNature)
        {
            // First force the actual Nature like normal
            ForceNatureHelper.ForceNature(pkm, setNature.Value, shinyRequested);

            // Now override ONLY the StatNature
            pkm.StatNature = statNature.Value;
            pkm.RefreshChecksum();
            return;
        }

        // CASE 3 — Only SetNature provided, no StatNature
        if (hasSetNature && !hasStatNature)
        {
            // Standard behavior
            ForceNatureHelper.ForceNature(pkm, setNature.Value, shinyRequested);
            return;
        }

        // CASE 4 — No Nature, no StatNature → do nothing
        // Let AutoLegality handle everything
        return;
    }
}
