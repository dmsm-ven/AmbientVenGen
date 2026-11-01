using AmbientVenGen.Core.Projects;
using Microsoft.Win32;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace AmbientVenGen.Desktop.Services;

/// <summary>
/// Makes Windows open .afproj files with this program (double click in Explorer). Registered for the current user
/// only (HKCU\Software\Classes), so no administrator rights are needed; it is re-written when the program is moved.
/// </summary>
public sealed class FileAssociationService
{
    private const string ProgId = "AmbientForge.Project";

    public void EnsureRegistered()
    {
        try
        {
            string? exe = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exe) || exe.EndsWith("dotnet.exe", StringComparison.OrdinalIgnoreCase))
                return; // started through "dotnet run": there is no stable exe to register

            string command = $"\"{exe}\" \"%1\"";

            using RegistryKey classes = Registry.CurrentUser.CreateSubKey(@"Software\Classes")!;

            using (RegistryKey open = classes.CreateSubKey($@"{ProgId}\shell\open\command")!)
            {
                if ((open.GetValue(null) as string) == command
                    && (classes.OpenSubKey(ProjectFileService.Extension)?.GetValue(null) as string) == ProgId)
                {
                    return;
                }

                open.SetValue(null, command);
            }

            using (RegistryKey id = classes.OpenSubKey(ProgId, writable: true)!)
            {
                id.SetValue(null, "Ambient Forge project");
                using RegistryKey icon = id.CreateSubKey("DefaultIcon")!;
                icon.SetValue(null, $"\"{exe}\",0");
            }

            using (RegistryKey extension = classes.CreateSubKey(ProjectFileService.Extension)!)
                extension.SetValue(null, ProgId);

            // Tell Explorer that file associations changed (SHCNE_ASSOCCHANGED).
            SHChangeNotify(0x08000000, 0, IntPtr.Zero, IntPtr.Zero);
        }
        catch (Exception ex)
        {
            // Not being able to register is not worth stopping the program.
            Debug.WriteLine($"File association failed: {ex.Message}");
        }
    }

    [DllImport("shell32.dll")]
    private static extern void SHChangeNotify(int eventId, uint flags, IntPtr item1, IntPtr item2);
}
