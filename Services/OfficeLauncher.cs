using System.Diagnostics;
using Microsoft.Win32;

namespace Aras.SreHub.Services;

public static class OfficeLauncher
{
    // Starts the Office app directly because the file association for .docx may point at a program that cannot open it.
    public static void Open(string filePath)
    {
        var executable = FindExecutable(filePath);

        var info = executable is null
            ? new ProcessStartInfo(filePath) { UseShellExecute = true }
            : new ProcessStartInfo(executable) { UseShellExecute = false, ArgumentList = { filePath } };

        using var process = Process.Start(info);
    }

    public static string? ExecutableNameFor(string filePath) => Path.GetExtension(filePath).ToLowerInvariant() switch
    {
        ".docx" => "WINWORD.EXE",
        ".xlsx" => "EXCEL.EXE",
        ".pptx" => "POWERPNT.EXE",
        _ => null
    };

    private static string? FindExecutable(string filePath)
    {
        var name = ExecutableNameFor(filePath);

        if (name is null || !OperatingSystem.IsWindows())
        {
            return null;
        }

        var key = $@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\{name}";

        foreach (var hive in new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser })
        {
            foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            {
                using var baseKey = RegistryKey.OpenBaseKey(hive, view);
                using var subKey = baseKey.OpenSubKey(key);
                var path = (subKey?.GetValue(null) as string)?.Trim().Trim('"');

                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                {
                    return path;
                }
            }
        }

        return null;
    }
}
