using System.Diagnostics;
using System.Text;

namespace DeviceBatteryInfo.Sources.Adb;

internal interface IAdbCommandRunner
{
    Task<string> RunAsync(
        string executable,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken
    );
}

internal sealed class ProcessAdbCommandRunner : IAdbCommandRunner
{
    public async Task<string> RunAsync(
        string executable,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken
    )
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo };
        if (!process.Start())
        {
            throw new InvalidOperationException($"Could not start '{executable}'.");
        }

        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);

        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            throw;
        }

        var stdout = await stdoutTask;
        var stderr = await stderrTask;

        if (process.ExitCode != 0)
        {
            var message = new StringBuilder(
                $"'{executable} {string.Join(' ', arguments)}' exited with {process.ExitCode}."
            );
            if (!string.IsNullOrWhiteSpace(stderr))
            {
                message.Append(' ').Append(stderr.Trim());
            }

            throw new InvalidOperationException(message.ToString());
        }

        return stdout;
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception exception)
            when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            // The process is already gone.
        }
    }
}
