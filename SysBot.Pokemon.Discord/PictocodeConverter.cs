using System;
using System.Collections.Generic;
using System.Linq;

namespace SysBot.Pokemon.Discord
{
    public static class PictocodeConverter
    {
        public static int ConvertToInt(List<Pictocodes> pictocodes)
        {
            int result = 0;
            foreach (var pictocode in pictocodes)
            {
                result |= 1 << (int)pictocode;
            }
            return result;
        }

        public static List<Pictocodes> ConvertToList(int code)
        {
            var result = new List<Pictocodes>();
            foreach (Pictocodes pictocode in Enum.GetValues(typeof(Pictocodes)))
            {
                if ((code & (1 << (int)pictocode)) != 0)
                {
                    result.Add(pictocode);
                }
            }
            return result;
        }

        public static List<Pictocodes> ConvertFromStrings(List<string> pokemonNames)
        {
            return [.. pokemonNames
                .Select(name => Enum.TryParse<Pictocodes>(name, true, out var pictocode) ? pictocode : (Pictocodes?)null)
                .Where(p => p.HasValue)
                .Select(p => p!.Value)];
        }
    }
}
