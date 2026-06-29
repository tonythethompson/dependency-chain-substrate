using DCS.Core.IR;

namespace DCS.Parser.Java.Naming;

public static class SpringBeanNameGenerator
{
  /// <summary>
  /// JavaBeans decapitalization per Spring AnnotationBeanNameGenerator.
  /// URLService -> URLService; PetService -> petService.
  /// </summary>
  public static string Decapitalize(string shortClassName)
    {
        if (string.IsNullOrEmpty(shortClassName))
            return shortClassName;

        if (shortClassName.Length > 1 &&
            char.IsUpper(shortClassName[0]) &&
            char.IsUpper(shortClassName[1]))
            return shortClassName;

        return char.ToLowerInvariant(shortClassName[0]) + shortClassName[1..];
    }

    public static (string Primary, List<string> Aliases) ParseBeanNames(
        IReadOnlyDictionary<string, string> beanAnnotationArgs,
        string methodName)
    {
        if (TryGetExplicitNames(beanAnnotationArgs, out var names) && names.Count > 0)
            return (names[0], names.Skip(1).ToList());

        return (methodName, []);
    }

    public static (string Primary, List<string> Aliases) ParseStereotypeNames(
        IReadOnlyDictionary<string, string> stereotypeArgs,
        string shortClassName)
    {
        if (TryGetExplicitNames(stereotypeArgs, out var names) && names.Count > 0)
            return (names[0], names.Skip(1).ToList());

        return (Decapitalize(shortClassName), []);
    }

    private static bool TryGetExplicitNames(IReadOnlyDictionary<string, string> args, out List<string> names)
    {
        names = [];
        if (args.TryGetValue("name", out var nameVal))
            names.AddRange(SplitNames(nameVal));
        else if (args.TryGetValue("value", out var valueVal))
            names.AddRange(SplitNames(valueVal));

        return names.Count > 0;
    }

    private static IEnumerable<string> SplitNames(string raw)
    {
        raw = raw.Trim();
        if (raw.StartsWith('{') && raw.EndsWith('}'))
        {
            raw = raw[1..^1];
            foreach (var part in raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                yield return part.Trim('"');
            yield break;
        }

        if (!string.IsNullOrWhiteSpace(raw))
            yield return raw.Trim('"');
    }
}
