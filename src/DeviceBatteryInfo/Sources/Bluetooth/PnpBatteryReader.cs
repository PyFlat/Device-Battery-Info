using System.Diagnostics;

namespace DeviceBatteryInfo.Sources.Bluetooth;

// Interface so this can be tested without shelling out to PowerShell
internal interface IPnpBatteryReader
{
    Task<string?> ReadRawAsync(string friendlyName, CancellationToken cancellationToken);

    Task<IReadOnlyList<(string Name, string? RawBattery)>> ListDevicesAsync(
        CancellationToken cancellationToken
    );
}

internal sealed class PowerShellPnpBatteryReader : IPnpBatteryReader
{
    // DEVPKEY_Bluetooth_Battery: the well-known key Windows fills for HFP/A2DP audio devices
    private const string BatteryPropertyKey = "{104EA319-6EE2-4701-BD47-8DDBF425BBE5} 2";

    private static readonly string PowerShellExecutablePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.System),
        "WindowsPowerShell",
        "v1.0",
        "powershell.exe"
    );

    private const string FriendlyNameEnvironmentVariable = "DEVICE_BATTERY_INFO_PNP_FRIENDLY_NAME";

    public async Task<string?> ReadRawAsync(
        string friendlyName,
        CancellationToken cancellationToken
    )
    {
        var script = $$"""
            $batteryPropertyKey = '{{BatteryPropertyKey}}'
            $targetName = $env:{{FriendlyNameEnvironmentVariable}}
            $matchingDevices = @(Get-PnpDevice -FriendlyName $targetName -PresentOnly -ErrorAction SilentlyContinue)

            $addresses = @(
                $matchingDevices.InstanceId |
                    Select-String -Pattern '(?:DEV_|&0&)([0-9A-Fa-f]{12})' -AllMatches |
                    ForEach-Object { $_.Matches } |
                    ForEach-Object { $_.Groups[1].Value } |
                    Sort-Object -Unique
            )

            $candidateIds = @($matchingDevices.InstanceId)
            if ($addresses.Count -gt 0) {
                $candidateIds += Get-PnpDevice -PresentOnly -ErrorAction SilentlyContinue |
                    Where-Object {
                        $instanceId = $_.InstanceId
                        $addresses | Where-Object { $instanceId -like "*$_*" }
                    } |
                    ForEach-Object { $_.InstanceId }
            }

            $bleIds = @($candidateIds | Where-Object { $_ -like 'BTHLE*' } | Sort-Object -Unique)
            $otherIds = @($candidateIds | Where-Object { $_ -notlike 'BTHLE*' } | Sort-Object -Unique)
            ($bleIds + $otherIds) |
                ForEach-Object { (Get-PnpDeviceProperty -InstanceId $_ -KeyName $batteryPropertyKey -ErrorAction SilentlyContinue).Data } |
                Where-Object { $_ -ne $null -and "$_" -ne '' } |
                Select-Object -First 1
            """;

        var output = await RunAsync(
            script,
            cancellationToken,
            new Dictionary<string, string?> { [FriendlyNameEnvironmentVariable] = friendlyName }
        );
        return output.Length == 0 ? null : output;
    }

    public async Task<IReadOnlyList<(string Name, string? RawBattery)>> ListDevicesAsync(
        CancellationToken cancellationToken
    )
    {
        var script = $$"""
            $batteryPropertyKey = '{{BatteryPropertyKey}}'

            $bluetoothDevices = @(
                Get-PnpDevice -PresentOnly -ErrorAction SilentlyContinue |
                    Where-Object {
                        $_.InstanceId -like 'BTHENUM*' -or
                        $_.InstanceId -like 'BTHHFENUM*' -or
                        $_.InstanceId -like 'BTHLE*'
                    }
            )

            $batteryByInstanceId = @{}
            $properties = Get-PnpDeviceProperty -InstanceId $bluetoothDevices.InstanceId -KeyName $batteryPropertyKey -ErrorAction SilentlyContinue
            foreach ($property in @($properties)) {
                if ($property.Data -ne $null -and "$($property.Data)" -ne '') {
                    $batteryByInstanceId[$property.InstanceId] = $property.Data
                }
            }

            $rootDevices = $bluetoothDevices | Where-Object {
                $_.InstanceId -match '^(BTHENUM|BTHLE)\\DEV_' -and $_.FriendlyName
            }

            $batteryByName = [ordered]@{}
            foreach ($root in $rootDevices) {
                if ($root.InstanceId -notmatch 'DEV_([0-9A-Fa-f]{12})') {
                    continue
                }
                $address = $matches[1]
                $siblings = @($bluetoothDevices | Where-Object { $_.InstanceId -like "*$address*" })

                $supportsBattery = $siblings | Where-Object {
                    $_.InstanceId -like '*0000111E*' -or
                    $_.InstanceId -like '*0000111F*' -or
                    $_.InstanceId -like '*0000180F*'
                }
                if (-not $supportsBattery) {
                    continue
                }

                $value = $null
                foreach ($id in $siblings.InstanceId) {
                    if ($batteryByInstanceId.Contains($id)) {
                        $value = $batteryByInstanceId[$id]
                        break
                    }
                }

                $alreadyHasValue = $batteryByName.Contains($root.FriendlyName) -and $null -ne $batteryByName[$root.FriendlyName]
                $bleOverrides = $root.InstanceId -like 'BTHLE*' -and $null -ne $value
                if (-not $alreadyHasValue -or $bleOverrides) {
                    $batteryByName[$root.FriendlyName] = $value
                }
            }

            $batteryByName.Keys | Sort-Object | ForEach-Object { "$_`t$($batteryByName[$_])" }
            """;

        var output = await RunAsync(script, cancellationToken);
        return output
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line =>
            {
                var separator = line.IndexOf('\t');
                return separator < 0
                    ? (Name: line, RawBattery: null)
                    : (Name: line[..separator], RawBattery: line[(separator + 1)..]);
            })
            .DistinctBy(d => d.Name, StringComparer.Ordinal)
            .ToArray();
    }

    private static async Task<string> RunAsync(
        string script,
        CancellationToken cancellationToken,
        IReadOnlyDictionary<string, string?>? environment = null
    )
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = PowerShellExecutablePath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            ArgumentList = { "-NoProfile", "-NonInteractive", "-Command", script },
        };
        if (environment is not null)
        {
            foreach (var (key, value) in environment)
            {
                startInfo.Environment[key] = value;
            }
        }

        using var process = new Process { StartInfo = startInfo };
        if (!process.Start())
        {
            throw new InvalidOperationException("Could not start powershell.");
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
            throw new InvalidOperationException(
                $"PowerShell exited with {process.ExitCode}: {stderr.Trim()}"
            );
        }

        return stdout.Trim();
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
            // Already gone.
        }
    }
}
