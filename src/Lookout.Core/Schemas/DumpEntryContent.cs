namespace Lookout.Core.Schemas;

/// <summary>Payload schema for entries of type <c>dump</c>.</summary>
public sealed record DumpEntryContent(
    string? Label,
    string Json,
    bool JsonTruncated,
    string ValueType,
    string CallerFile,
    int CallerLine,
    string CallerMember);
