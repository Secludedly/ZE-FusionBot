using PKHeX.Core;
using System.Collections.Generic;

public static class MegaStoneHelper
{
    // List of all Mega Stones' item IDs in PA9/Z-A
    private static readonly HashSet<ushort> MegaStones = new()
    {
        656, 657, 658, 659, 660, 678, 661, 662, 663, 665, 666, 667, 668, 669,
        670, 671, 672, 673, 2641, 674, 675, 676, 677, 2638, 679, 680, 681, 682,
        683, 2640, 754, 755, 756, 757, 758, 759, 760, 761, 762, 763, 764, 767,
        768, 769, 770, 2559, 2560, 2561, 2562, 2563, 2564, 2565, 2566, 2567, 2568,
        2569, 2570, 2571, 2572, 2573, 2574, 2575, 2576, 2577, 2578, 2579, 2580,
        2581, 2582, 2583, 2584, 2585, 2586, 2587, 2635, 2636, 2637, 2639, 2642,
        2643, 2644, 2645, 2646, 2647, 2648, 2649, 2650
    };

    public static bool IsMegaStone(ushort item)
    {
        return MegaStones.Contains(item);
    }
}
