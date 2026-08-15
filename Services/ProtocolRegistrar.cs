using System;
using System.Diagnostics;
using Microsoft.Win32;

namespace KillerPDF.Services
{
    internal static class ProtocolRegistrar
    {
        internal const string Scheme = "killerpdf";
        private const string RegistryPath = @"Software\Classes\killerpdf";

        internal static void Register() => Register(Registry.CurrentUser, null);

        // #183: machine-wide installs must register under HKLM so every user gets the handler,
        // not just whoever ran the installer. Root is chosen by the caller; appPath defaults to
        // the running executable (the per-user refresh path) but the elevated installer passes
        // the Program Files copy explicitly since IT is the source exe at that moment.
        internal static void Register(RegistryKey root, string? appPath)
        {
            try
            {
                appPath ??= Process.GetCurrentProcess().MainModule!.FileName;
                using var protocol = root.CreateSubKey(RegistryPath);
                if (protocol == null) return;
                protocol.SetValue("", "URL:KillerPDF Protocol");
                protocol.SetValue("URL Protocol", "");
                using (var icon = protocol.CreateSubKey("DefaultIcon"))
                    icon?.SetValue("", $"\"{appPath}\",0");
                using (var command = protocol.CreateSubKey(@"shell\open\command"))
                    command?.SetValue("", $"\"{appPath}\" \"%1\"");
            }
            catch (Exception ex) { Debug.WriteLine($"Failed to register KillerPDF protocol: {ex.Message}"); }
        }

        internal static void Unregister() => Unregister(Registry.CurrentUser);

        internal static void Unregister(RegistryKey root)
        {
            try { root.DeleteSubKeyTree(RegistryPath, false); } catch { }
        }

        internal static bool TryGetTargetUrl(string? protocolUrl, out Uri? target)
        {
            target = null;
            if (!Uri.TryCreate(protocolUrl, UriKind.Absolute, out var launch) ||
                !launch.Scheme.Equals(Scheme, StringComparison.OrdinalIgnoreCase) ||
                !launch.Host.Equals("open", StringComparison.OrdinalIgnoreCase)) return false;

            string query = launch.Query.TrimStart('?');
            foreach (string pair in query.Split('&'))
            {
                int equals = pair.IndexOf('=');
                if (equals < 0) continue;
                string name = Uri.UnescapeDataString(pair.Substring(0, equals).Replace("+", " "));
                if (!name.Equals("url", StringComparison.OrdinalIgnoreCase)) continue;
                string value = Uri.UnescapeDataString(pair.Substring(equals + 1).Replace("+", " "));
                if (!Uri.TryCreate(value, UriKind.Absolute, out var parsed)) return false;
                if (!parsed.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)) return false;
                target = parsed;
                return true;
            }
            return false;
        }
    }
}
