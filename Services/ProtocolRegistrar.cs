using System;
using System.Diagnostics;
using System.IO;
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
                appPath ??= Environment.ProcessPath
                    ?? throw new InvalidOperationException("The current executable path is unavailable.");
                using var protocol = root.CreateSubKey(RegistryPath);
                if (protocol == null) return;
                protocol.SetValue("", "URL:KillerPDF Protocol");
                protocol.SetValue("URL Protocol", "");
                using (var icon = protocol.CreateSubKey("DefaultIcon"))
                    icon?.SetValue("", $"\"{appPath}\",0");
                using var command = protocol.CreateSubKey(@"shell\open\command");
                command?.SetValue("", $"\"{appPath}\" \"%1\"");
            }
            catch (Exception ex) { Debug.WriteLine($"Failed to register KillerPDF protocol: {ex.Message}"); }
        }

        /// <summary>The executable a registration points at, or null when there is none to read.</summary>
        internal static string? RegisteredAppPath(RegistryKey root)
        {
            try
            {
                using var command = root.OpenSubKey(RegistryPath + @"\shell\open\command");
                if (command?.GetValue("") is not string value) return null;
                // Register writes the command as "<appPath>" "%1", so the path is the first quoted span.
                int open = value.IndexOf('"');
                if (open < 0) return null;
                int close = value.IndexOf('"', open + 1);
                return close > open ? value[(open + 1)..close] : null;
            }
            catch { return null; }
        }

        // #246: a per-user registration outlives the copy that wrote it. The portable launcher
        // extracts to a per-run directory and deletes it on exit, so a portable session leaves the
        // handler aimed at a path that is already gone - and HKCU shadows HKLM, so it hijacks the
        // browser handoff from a working machine-wide install until something removes it. Nothing
        // did: Unregister only ran from the two install-removal paths and from uninstall.
        /// <summary>
        /// Removes a per-user registration whose executable no longer exists, leaving a valid one
        /// alone. Deleting is the safe direction here - the handler being removed is already dead,
        /// and unlike a write it can never shadow the machine-wide registration (#183).
        /// </summary>
        internal static void RemoveStaleRegistration(RegistryKey root)
        {
            string? appPath = RegisteredAppPath(root);
            if (appPath == null || File.Exists(appPath)) return;
            Unregister(root);
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
                string name = Uri.UnescapeDataString(pair[..equals].Replace("+", " "));
                if (!name.Equals("url", StringComparison.OrdinalIgnoreCase)) continue;
                string value = Uri.UnescapeDataString(pair[(equals + 1)..].Replace("+", " "));
                if (!Uri.TryCreate(value, UriKind.Absolute, out var parsed)) return false;
                if (!parsed.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)) return false;
                target = parsed;
                return true;
            }
            return false;
        }
    }
}
