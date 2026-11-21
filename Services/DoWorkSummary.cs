namespace Aras.SreHub.Services;

// The line the Duty Log writes into a Micro-mgmt daily work summary when a DO handover is completed.
public static class DoWorkSummary
{
    public const string Prefix = "Worked as DO";
    public const int MaxLength = 500;

    private const string Separator = "; ";

    public static string LineFor(string shift) => shift.Trim() switch
    {
        "" => Prefix,
        var both when both.Contains('+') => $"{Prefix} in {string.Join(" and ", both.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))} shifts",
        var single => $"{Prefix} in {single} shift"
    };

    public static string ExistingLine(string summary) =>
        string.Join(Separator, Segments(summary).Where(IsDoSegment));

    // Replaces any earlier DO line with `line`; with an empty `line` only the "Worked as DO" line written here is taken out and your own wording stays.
    public static string Apply(string summary, string line)
    {
        var others = string.Join(Separator, Segments(summary).Where(segment => line.Length > 0 ? !IsDoSegment(segment) : !segment.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase)));

        if (line.Length == 0)
        {
            return Fit(others);
        }

        if (others.Length == 0)
        {
            return Fit(line);
        }

        var room = MaxLength - line.Length - Separator.Length;
        others = room <= 3 ? string.Empty : others.Length <= room ? others : others[..(room - 3)] + "...";

        return others.Length == 0 ? Fit(line) : others + Separator + line;
    }

    private static string Fit(string text) => text.Length <= MaxLength ? text : text[..(MaxLength - 3)] + "...";

    private static IEnumerable<string> Segments(string summary) =>
        summary.Split(Separator, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

    private static bool IsDoSegment(string segment) =>
        segment.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase) || segment.StartsWith("Working as DO", StringComparison.OrdinalIgnoreCase);

    // Adds one finished item to the summary unless it is already there; the DO line always stays last and within the limit.
    public static string AddSegment(string summary, string text)
    {
        text = text.Trim();

        if (text.Length == 0 || Segments(summary).Contains(text, StringComparer.OrdinalIgnoreCase))
        {
            return summary;
        }

        var joined = summary.Length == 0 ? text : summary + Separator + text;
        return joined.Length <= MaxLength ? joined : Apply(joined, ExistingLine(joined));
    }

    public static string RemoveSegment(string summary, string text) =>
        string.Join(Separator, Segments(summary).Where(segment => !segment.Equals(text.Trim(), StringComparison.OrdinalIgnoreCase)));
}
