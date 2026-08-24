#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;

using CnCNetClient.Core;
using CnCNetClient.Core.Extensions;

using Microsoft.Win32;

using CnCNetClient.Tools;

namespace CnCNetClient.Domain;

/// <summary>
/// Handles checking and fixing DirectDraw compatibility issues.
/// UI prompting (message boxes, admin restart) is delegated to the host/UI layer;
/// this class exposes <see cref="Check"/> to detect issues and <see cref="Fix"/> to apply repairs.
/// </summary>
[SupportedOSPlatform("windows")]
public static class DirectDrawCompatibilityChecker
{
    private static readonly IReadOnlyList<string> OSCompatibilityValues = [
        "WIN8RTM", "WIN7RTM", "VISTASP2", "VISTASP1", "VISTARTM", "WINXPSP3", "WINXPSP2", "WIN98", "WIN95"
    ];

    private static IEnumerable<string> GetExecutableFilePathsToCheck()
    {
        List<string> executablePaths = ClientConfiguration.Instance.GetCompatibilityCheckExecutables()
            .Select(executableName => SafePath.CombineFilePath(ProgramConstants.GamePath, executableName))
            .ToList();

        // clientdx.exe, clientogl.exe, or clientxna.exe
        string currentExePath = SafePath.GetFile(ProgramConstants.StartupExecutable).FullName;

        executablePaths.Add(currentExePath);

        Logger.Log("Checking compatibility settings for executables: " +
                   string.Join(", ", executablePaths));

        return executablePaths;
    }

    private static void Examine(out bool requireFix, out bool requireAdmin, out IEnumerable<string> problematicExeNames)
    {
        RegistryKey hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        RegistryKey hkcu = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64);

        using RegistryKey? hkcuKey = hkcu.OpenSubKey(
            @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\Layers");
        using RegistryKey? hklmKey = hklm.OpenSubKey(
            @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\Layers");

        static bool IsFixRequired(object? regValue)
            => regValue is string regValueString
               && regValueString.Split([' ']).Intersect(OSCompatibilityValues).Any();

        bool anyHkcuRequireFix = false;
        bool anyHklmRequireFix = false;

        var problematicExeNameHashSet = new HashSet<string>();
        foreach (string exeFullPath in GetExecutableFilePathsToCheck())
        {
            object? hkcuValue = hkcuKey?.GetValue(exeFullPath);
            object? hklmValue = hklmKey?.GetValue(exeFullPath);

            if (IsFixRequired(hkcuValue))
            {
                Logger.Log($"Executable '{exeFullPath}' has problematic compatibility settings in HKCU. Value: {hkcuValue}");
                anyHkcuRequireFix = true;
                problematicExeNameHashSet.Add(Path.GetFileName(exeFullPath));
            }

            if (IsFixRequired(hklmValue))
            {
                Logger.Log($"Executable '{exeFullPath}' has problematic compatibility settings in HKLM. Value: {hklmValue}");
                anyHklmRequireFix = true;
                problematicExeNameHashSet.Add(Path.GetFileName(exeFullPath));
            }
        }

        requireFix = anyHkcuRequireFix || anyHklmRequireFix;
        requireAdmin = anyHklmRequireFix;
        problematicExeNames = problematicExeNameHashSet;
    }

    private static string FixCompatLayerString(string value) => string.Join(" ",
            value
                .SplitWithCleanup(new[] { ' ' })
                .Where(v => !OSCompatibilityValues.Contains(v, StringComparer.InvariantCultureIgnoreCase)));

    private static void Fix()
    {
        void FixRegValue(object? regValue, out bool success, out string newRegValue)
        {
            if (regValue is string regValueString)
            {
                newRegValue = FixCompatLayerString(regValueString);
                success = true;
            }
            else
            {
                success = false;
                newRegValue = string.Empty;
            }
        }

        void FixRegistryKey(RegistryKey rootKey, string subKeyPath)
        {
            try
            {
                using RegistryKey? key = rootKey.OpenSubKey(subKeyPath, writable: true);
                if (key == null)
                    return;

                foreach (string exeFullPath in GetExecutableFilePathsToCheck())
                {
                    object? value = key.GetValue(exeFullPath);

                    FixRegValue(value, out bool success, out string newValue);

                    if (success)
                    {
                        if (string.IsNullOrEmpty(newValue))
                            key.DeleteValue(exeFullPath, false);
                        else
                            key.SetValue(exeFullPath, newValue, RegistryValueKind.String);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"Failed to fix registry key {rootKey.Name}\\{subKeyPath}: {ex.Message}");
            }
        }

        string subKeyPath = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\Layers";

        RegistryKey hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        RegistryKey hkcu = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64);

        FixRegistryKey(hkcu, subKeyPath);

        FixRegistryKey(hklm, subKeyPath);
    }

    /// <summary>
    /// Checks for DirectDraw compatibility issues, fixing the <c>__COMPAT_LAYER</c>
    /// environment variable inline and reporting registry-based issues for the UI layer
    /// to prompt about. Call <see cref="Fix"/> to apply registry repairs after user confirmation.
    /// </summary>
    /// <param name="requireFix">True if registry compatibility settings need fixing.</param>
    /// <param name="requireAdmin">True if HKLM changes are needed (administrator privileges required).</param>
    /// <param name="problematicExeNames">Names of executables with problematic settings.</param>
    public static void Check(out bool requireFix, out bool requireAdmin, out IEnumerable<string> problematicExeNames)
    {
        // Fix environment variable __COMPAT_LAYER first, for the client itself.
        string compatLayerEnv = Environment.GetEnvironmentVariable("__COMPAT_LAYER") ?? string.Empty;
        string fixedCompatLayerEnv = FixCompatLayerString(compatLayerEnv);
        if (compatLayerEnv != fixedCompatLayerEnv)
        {
            Logger.Log("Fixing __COMPAT_LAYER environment variable. Previous value: " +
                       $"'{compatLayerEnv}', new value: '{fixedCompatLayerEnv}'");
            Environment.SetEnvironmentVariable("__COMPAT_LAYER", fixedCompatLayerEnv);
        }

        try
        {
            Examine(out requireFix, out requireAdmin, out problematicExeNames);

            if (requireFix)
                Logger.Log("DirectDraw compatibility issue detected.");
        }
        catch (Exception ex)
        {
            Logger.Log("Error checking DirectDraw compatibility: " + ex.ToString());
            requireFix = false;
            requireAdmin = false;
            problematicExeNames = [];
        }
    }

    /// <summary>
    /// Removes problematic compatibility settings from the registry.
    /// Should be called after <see cref="Check"/> reports issues and the user confirms.
    /// </summary>
    public static void ApplyFix()
    {
        Logger.Log("Attempting to fix DirectDraw compatibility settings.");
        Fix();
        Logger.Log("DirectDraw compatibility settings fixed successfully.");
    }
}
