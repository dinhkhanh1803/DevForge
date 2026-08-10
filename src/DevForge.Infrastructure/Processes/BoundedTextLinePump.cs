using System.Buffers;
using System.Text;
using DevForge.Application.Contracts;

namespace DevForge.Infrastructure.Processes;

internal static class BoundedTextLinePump
{
    private const int BufferLength = 1_024;
    private const int MaxRawLineLength = ProcessOutputLine.MaxTextLength * 2;

    public static async Task PumpAsync(
        StreamReader reader,
        ProcessOutputChannel channel,
        BoundedProcessOutput output)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(output);

        var buffer = ArrayPool<char>.Shared.Rent(BufferLength);
        var line = new StringBuilder(Math.Min(MaxRawLineLength, BufferLength));
        var lineExceededLimit = false;
        try
        {
            while (true)
            {
                var charactersRead = await reader.ReadAsync(
                    buffer.AsMemory(0, BufferLength)).ConfigureAwait(false);
                if (charactersRead == 0)
                {
                    break;
                }

                for (var index = 0; index < charactersRead; index++)
                {
                    var character = buffer[index];
                    if (character == '\n')
                    {
                        FlushLine(channel, output, line, lineExceededLimit);
                        line.Clear();
                        lineExceededLimit = false;
                        continue;
                    }

                    if (character == '\r')
                    {
                        continue;
                    }

                    if (lineExceededLimit)
                    {
                        continue;
                    }

                    if (line.Length == MaxRawLineLength)
                    {
                        line.Clear();
                        lineExceededLimit = true;
                        continue;
                    }

                    line.Append(character);
                }
            }

            if (line.Length > 0 || lineExceededLimit)
            {
                FlushLine(channel, output, line, lineExceededLimit);
            }
        }
        finally
        {
            Array.Clear(buffer, 0, buffer.Length);
            ArrayPool<char>.Shared.Return(buffer);
        }
    }

    private static void FlushLine(
        ProcessOutputChannel channel,
        BoundedProcessOutput output,
        StringBuilder line,
        bool lineExceededLimit)
    {
        if (lineExceededLimit)
        {
            output.MarkTruncated();
            output.Observe(channel, "[OUTPUT LINE TRUNCATED]");
            return;
        }

        output.Observe(channel, line.ToString());
    }
}
