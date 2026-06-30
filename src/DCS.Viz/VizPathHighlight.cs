using DCS.Analysis;

namespace DCS.Viz;

public sealed record VizPathHighlight(
    IReadOnlyList<string> NodeIds,
    IReadOnlyList<string> EdgeKeys)
{
    public static VizPathHighlight? FromResult(GraphPathResult result)
    {
        if (!result.Success || result.Nodes.Count == 0)
            return null;

        return new VizPathHighlight(
            result.Nodes.Select(n => n.Id).ToList(),
            result.Edges.Select(e => EdgeKey(e.From, e.To)).ToList());
    }

    public static string EdgeKey(string from, string to) => $"{from}|{to}";
}
