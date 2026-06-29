namespace DCS.Parser.Java;

[Flags]
public enum SourceSetFilter
{
    None = 0,
    Main = 1,
    Test = 2,
    Generated = 4,
    Unknown = 8,
    MainOnly = Main,
    All = Main | Test | Generated | Unknown
}
