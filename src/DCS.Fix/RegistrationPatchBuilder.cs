namespace DCS.Fix;

internal static class RegistrationPatchBuilder
{
    internal static Dictionary<string, FilePatch> BuildRemovalPatches(
        FixFileContext files,
        IEnumerable<(string RelativePath, int Line, string Token)> removals,
        string fixKindLabel)
    {
        var patches = new Dictionary<string, FilePatch>(StringComparer.OrdinalIgnoreCase);

        foreach (var group in removals.GroupBy(r => r.RelativePath, StringComparer.OrdinalIgnoreCase))
        {
            var relativePath = group.Key;

            string original;
            try
            {
                original = files.Read(relativePath);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"Registration file not found for {fixKindLabel}: {relativePath}. {ex.Message}", ex);
            }

            var requests = group
                .Select(g => new RegistrationRemovalRequest(g.Line, g.Token))
                .ToList();

            var updated = RegistrationStatementRemover.TryRemoveMany(original, requests);
            if (updated == null)
            {
                var failed = requests.OrderByDescending(r => r.Line).First();
                throw new InvalidOperationException(
                    $"Could not locate registration statement for {failed.TokenName} " +
                    $"at {relativePath}:{failed.Line}.");
            }

            patches[relativePath] = new FilePatch(relativePath, original, updated);
        }

        return patches;
    }
}
