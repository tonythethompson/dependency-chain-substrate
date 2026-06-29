using DCS.Core.Parsing;

namespace DCS.Core.Parsing;

/// <summary>
/// Language-specific static extraction from a git commit or working directory.
/// </summary>
public interface IStaticParser
{
    ParseResult ParseCommit(string repoPath, string commitSha);

    ParseResult ParseDirectory(string directoryPath);
}
