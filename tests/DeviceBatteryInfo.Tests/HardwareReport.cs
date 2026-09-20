using NUnit.Framework;

namespace DeviceBatteryInfo.Tests;

internal static class HardwareReport
{
    public static void Line(string subject, string result) =>
        TestContext.Out.WriteLine($"{subject,-44} {result}");
}
