using System.Diagnostics;
using System.Globalization;
using System.Reflection;

if (args.Length == 0)
{
    return 64;
}

switch (args[0])
{
    case "echo-args":
        for (var index = 1; index < args.Length; index++)
        {
            Console.WriteLine($"ARG[{index - 1}]={args[index]}");
        }

        return 0;

    case "write-streams":
        Console.Out.WriteLine("stdout-line");
        Console.Error.WriteLine("stderr-line");
        return 0;

    case "echo-env":
        if (args.Length != 2)
        {
            return 64;
        }

        Console.WriteLine(Environment.GetEnvironmentVariable(args[1]) ?? "<missing>");
        return 0;

    case "large-output":
        if (args.Length != 3
            || !int.TryParse(args[1], NumberStyles.None, CultureInfo.InvariantCulture, out var lineCount)
            || !int.TryParse(args[2], NumberStyles.None, CultureInfo.InvariantCulture, out var lineLength))
        {
            return 64;
        }

        for (var index = 0; index < lineCount; index++)
        {
            Console.WriteLine(new string('x', lineLength));
        }

        return 0;

    case "sleep":
        if (args.Length != 2
            || !int.TryParse(args[1], NumberStyles.None, CultureInfo.InvariantCulture, out var delayMilliseconds))
        {
            return 64;
        }

        await Task.Delay(delayMilliseconds);
        return 0;

    case "write-until-killed":
        var outputIndex = 0;
        while (true)
        {
            Console.WriteLine($"stream-line-{outputIndex++}");
            await Console.Out.FlushAsync();
            await Task.Delay(5);
        }

    case "spawn-child-and-wait":
        var hostPath = Environment.ProcessPath ?? throw new InvalidOperationException("Host path is unavailable.");
        var assemblyPath = Assembly.GetExecutingAssembly().Location;
        using (var child = Process.Start(new ProcessStartInfo(hostPath)
        {
            UseShellExecute = false,
            ArgumentList =
                   {
                       assemblyPath,
                       "sleep",
                       "60000",
                   },
        }) ?? throw new InvalidOperationException("Child process did not start."))
        {
            Console.WriteLine($"CHILD_PID={child.Id}");
            await Console.Out.FlushAsync();
            await child.WaitForExitAsync();
        }

        return 0;

    default:
        return 64;
}
