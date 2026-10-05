using System;

namespace ReelWalk.Services;
internal static class FolderOrder
{
    internal const string Random = "Random";
    internal const string Sequential = "Sequential";
    internal const string SizeAsc = "SizeAsc";
    internal const string SizeDesc = "SizeDesc";
    internal const string LengthAsc = "LengthAsc";
    internal const string LengthDesc = "LengthDesc";

    // Maps a folder order name to one this app stores.
    // mode is raw text. Returns Random when it is blank or unknown.
    internal static string Normalize(string mode)
    {
        if (string.IsNullOrWhiteSpace(mode))
            return Random;
        if (mode.Equals(Random, StringComparison.OrdinalIgnoreCase) ||
            mode.Equals("Shuffle", StringComparison.OrdinalIgnoreCase))
            return Random;
        if (mode.Equals(Sequential, StringComparison.OrdinalIgnoreCase) ||
            mode.Equals("Ordered", StringComparison.OrdinalIgnoreCase) ||
            mode.Equals("Name", StringComparison.OrdinalIgnoreCase))
            return Sequential;
        if (mode.Equals(SizeAsc, StringComparison.OrdinalIgnoreCase) ||
            mode.Equals("Smallest", StringComparison.OrdinalIgnoreCase))
            return SizeAsc;
        if (mode.Equals(SizeDesc, StringComparison.OrdinalIgnoreCase) ||
            mode.Equals("Largest", StringComparison.OrdinalIgnoreCase))
            return SizeDesc;
        if (mode.Equals(LengthAsc, StringComparison.OrdinalIgnoreCase) ||
            mode.Equals("Shortest", StringComparison.OrdinalIgnoreCase))
            return LengthAsc;
        if (mode.Equals(LengthDesc, StringComparison.OrdinalIgnoreCase) ||
            mode.Equals("Longest", StringComparison.OrdinalIgnoreCase))
            return LengthDesc;
        return Random;
    }

    // Short label for toasts and the info overlay.
    // mode is a stored folder order. Returns Random when the name is blank.
    internal static string Label(string mode)
    {
        switch (Normalize(mode))
        {
            case SizeAsc: return "Smallest";
            case SizeDesc: return "Largest";
            case LengthAsc: return "Shortest";
            case LengthDesc: return "Longest";
            case Sequential: return "Ordered";
            default: return "Random";
        }
    }

    // True when mode is Random.
    // mode is a stored folder order. Returns false for the sorts.
    internal static bool IsRandom(string mode)
    {
        return Normalize(mode) == Random;
    }
}
