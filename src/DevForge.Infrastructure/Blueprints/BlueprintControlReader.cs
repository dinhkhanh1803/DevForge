using System.Collections.Immutable;
using System.Text;
using DevForge.Application.Contracts;

namespace DevForge.Infrastructure.Blueprints;

internal static class BlueprintControlLimits
{
    internal const int MaximumControlFileBytes = 256 * 1024;
    internal const int MaximumScalarCharacters = 16384;
    internal const int MaximumDepth = 128;
}

internal interface IBlueprintControlReader<T>
    where T : class
{
    ValueTask<BlueprintLoadResult<T>> ReadAsync(
        Stream content,
        CancellationToken cancellationToken);
}

internal sealed record BlueprintLoadResult<T>(
    T? Value,
    ImmutableArray<BlueprintInspectionIssue> Issues)
    where T : class
{
    public bool IsValid => Value is not null && Issues.IsEmpty;
}

internal static class BlueprintControlReadSupport
{
    private static readonly UTF8Encoding _strictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    internal static async ValueTask<BoundedControlText> ReadTextAsync(
        Stream content,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);
        var buffer = new byte[8192];
        using var collected = new MemoryStream();
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var read = await content.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            if (collected.Length + read > BlueprintControlLimits.MaximumControlFileBytes)
            {
                return BoundedControlText.Failure(BoundsIssue());
            }

            collected.Write(buffer, 0, read);
        }

        try
        {
            return BoundedControlText.Success(_strictUtf8.GetString(collected.GetBuffer(), 0, checked((int)collected.Length)));
        }
        catch (DecoderFallbackException)
        {
            return BoundedControlText.Failure(MalformedIssue());
        }
    }

    internal static BlueprintInspectionIssue MalformedIssue()
    {
        return BlueprintInspectionIssue.Create(
            "DF-BP-001",
            "The blueprint control file is malformed or uses unsupported structure.").Value;
    }

    internal static BlueprintInspectionIssue BoundsIssue()
    {
        return BlueprintInspectionIssue.Create(
            "DF-BP-004",
            "The blueprint control file exceeds a supported bound.").Value;
    }
}

internal sealed record BoundedControlText(
    string? Text,
    BlueprintInspectionIssue? Issue)
{
    internal bool IsValid => Text is not null && Issue is null;

    internal static BoundedControlText Success(string text)
    {
        return new BoundedControlText(text, null);
    }

    internal static BoundedControlText Failure(BlueprintInspectionIssue issue)
    {
        return new BoundedControlText(null, issue);
    }
}

internal enum BlueprintControlValueKind
{
    Scalar = 1,
    Sequence = 2,
    Mapping = 3,
}

internal sealed class BlueprintControlValue
{
    private BlueprintControlValue(
        BlueprintControlValueKind kind,
        string? scalar,
        ImmutableArray<BlueprintControlValue> sequence,
        ImmutableDictionary<string, BlueprintControlValue> mapping)
    {
        Kind = kind;
        Scalar = scalar;
        Sequence = sequence;
        Mapping = mapping;
    }

    internal BlueprintControlValueKind Kind { get; }

    internal string? Scalar { get; }

    internal ImmutableArray<BlueprintControlValue> Sequence { get; }

    internal ImmutableDictionary<string, BlueprintControlValue> Mapping { get; }

    internal static BlueprintControlValue FromScalar(string value)
    {
        return new BlueprintControlValue(
            BlueprintControlValueKind.Scalar,
            value,
            [],
            ImmutableDictionary<string, BlueprintControlValue>.Empty.WithComparers(StringComparer.Ordinal));
    }

    internal static BlueprintControlValue FromSequence(IEnumerable<BlueprintControlValue> values)
    {
        return new BlueprintControlValue(
            BlueprintControlValueKind.Sequence,
            null,
            [.. values],
            ImmutableDictionary<string, BlueprintControlValue>.Empty.WithComparers(StringComparer.Ordinal));
    }

    internal static BlueprintControlValue FromMapping(
        IEnumerable<KeyValuePair<string, BlueprintControlValue>> values)
    {
        return new BlueprintControlValue(
            BlueprintControlValueKind.Mapping,
            null,
            [],
            values.ToImmutableDictionary(StringComparer.Ordinal));
    }
}
