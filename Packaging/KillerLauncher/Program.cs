using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Forms;

namespace KillerLauncher
{
    internal static class Program
    {
        private const string ProductName = "KillerPDF";
        private const string InnerExeName = "KillerPDF.App.exe";
        private const string PayloadResourceName = "KillerLauncher.payload.zip";
        private const string ManifestName = "payload.manifest";
        private const string PortableMarkerName = ".killerpdf-portable";
        private const string TestInstallRootEnvironmentVariable = "KILLERPDF_TEST_INSTALL_ROOT";
        private const string SkipRegistrationEnvironmentVariable = "KILLERPDF_SKIP_REGISTRATION";

        private static readonly string UserInstallDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", ProductName);
        private static readonly string MachineInstallDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), ProductName);
        private static readonly string PortableRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), ProductName, "Portable");

        [STAThread]
        private static int Main(string[] args)
        {
            try
            {
                if (args.Any(a => string.Equals(a, "/install-user", StringComparison.OrdinalIgnoreCase)))
                    return Install(machine: false, desktop: args.Any(a => string.Equals(a, "/desktop", StringComparison.OrdinalIgnoreCase)));

                if (args.Any(a => string.Equals(a, "/silent", StringComparison.OrdinalIgnoreCase)))
                    return Install(machine: true, desktop: false);

                return RunPortable(args);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    ProductName + " could not start.\n\n" + ex.Message,
                    ProductName,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                return 1;
            }
        }

        private static int RunPortable(string[] args)
        {
            SweepAbandonedPortableDirectories();
            Directory.CreateDirectory(PortableRoot);

            string version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "unknown";
            string directory = Path.Combine(PortableRoot,
                version + "-" + Process.GetCurrentProcess().Id.ToString(CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString("N"));

            try
            {
                ExtractAndVerify(directory);
                WritePortableMarker(directory, version, Process.GetCurrentProcess().Id, null);

                var start = new ProcessStartInfo(Path.Combine(directory, InnerExeName), QuoteArguments(args))
                {
                    UseShellExecute = false,
                    WorkingDirectory = directory
                };
                start.EnvironmentVariables["KILLERPDF_LAUNCHER_PATH"] = CurrentExecutablePath();
                start.EnvironmentVariables["KILLERPDF_LAUNCHER_PID"] =
                    Process.GetCurrentProcess().Id.ToString(CultureInfo.InvariantCulture);
                start.EnvironmentVariables["KILLERPDF_PORTABLE_ROOT"] = directory;

                using (var child = Process.Start(start))
                {
                    if (child == null) throw new InvalidOperationException("The application process could not be created.");
                    WritePortableMarker(directory, version, Process.GetCurrentProcess().Id, child.Id);
                    child.WaitForExit();
                    return child.ExitCode;
                }
            }
            finally
            {
                DeleteDirectoryWithRetries(directory);
            }
        }

        private static int Install(bool machine, bool desktop)
        {
            if (!IsTrustedForInstall(CurrentExecutablePath()))
                throw new InvalidOperationException(
                    "Installation was refused because this download does not have a valid KillerPDF digital signature.");

            string? testRoot = Environment.GetEnvironmentVariable(TestInstallRootEnvironmentVariable);
            if (string.IsNullOrWhiteSpace(testRoot) && !machine &&
                (File.Exists(Path.Combine(MachineInstallDirectory, InnerExeName)) ||
                 File.Exists(Path.Combine(MachineInstallDirectory, "KillerPDF.exe"))))
                throw new InvalidOperationException(
                    "KillerPDF is already installed for everyone on this computer. Update that installation, " +
                    "or uninstall it before choosing a per-user install. KillerPDF will not create two installed copies.");

            string destination = !string.IsNullOrWhiteSpace(testRoot)
                ? Path.GetFullPath(testRoot)
                : (machine ? MachineInstallDirectory : UserInstallDirectory);
            string parent = Path.GetDirectoryName(destination) ?? throw new InvalidOperationException("Invalid install directory.");
            Directory.CreateDirectory(parent);

            string staging = destination + ".staging-" + Guid.NewGuid().ToString("N");
            string backup = destination + ".previous-" + Guid.NewGuid().ToString("N");
            bool movedExisting = false;
            try
            {
                ExtractAndVerify(staging);

                if (Directory.Exists(destination))
                {
                    Directory.Move(destination, backup);
                    movedExisting = true;
                }
                Directory.Move(staging, destination);

                int registrationExit = string.Equals(
                    Environment.GetEnvironmentVariable(SkipRegistrationEnvironmentVariable), "1", StringComparison.Ordinal)
                    ? 0
                    : RunRegistration(destination, machine, desktop);
                if (registrationExit != 0)
                    throw new InvalidOperationException("Windows integration could not be registered (exit " + registrationExit + ").");

                // A machine-wide install supersedes the current account's per-user copy. This
                // also covers unattended /silent installs that do not return through the portable
                // app's InstallAndRelaunch cleanup path.
                if (machine && string.IsNullOrWhiteSpace(testRoot))
                    RunMaintenance(destination, "/remove-user-install");

                if (movedExisting) DeleteDirectoryWithRetries(backup);
                return 0;
            }
            catch
            {
                DeleteDirectoryWithRetries(staging);
                if (movedExisting && Directory.Exists(backup))
                {
                    DeleteDirectoryWithRetries(destination);
                    if (!Directory.Exists(destination)) Directory.Move(backup, destination);
                }
                throw;
            }
        }

        private static int RunRegistration(string directory, bool machine, bool desktop)
        {
            var arguments = new List<string> { machine ? "/register-machine" : "/register-user" };
            if (desktop) arguments.Add("/desktop");
            var start = new ProcessStartInfo(Path.Combine(directory, InnerExeName), QuoteArguments(arguments.ToArray()))
            {
                UseShellExecute = false,
                WorkingDirectory = directory
            };
            using (var process = Process.Start(start))
            {
                if (process == null) return 1;
                process.WaitForExit();
                return process.ExitCode;
            }
        }

        private static int RunMaintenance(string directory, string argument)
        {
            var start = new ProcessStartInfo(Path.Combine(directory, InnerExeName), argument)
            {
                UseShellExecute = false,
                WorkingDirectory = directory
            };
            using (var process = Process.Start(start))
            {
                if (process == null) return 1;
                process.WaitForExit();
                return process.ExitCode;
            }
        }

        private static void ExtractAndVerify(string destination)
        {
            Directory.CreateDirectory(destination);
            using (var payload = Assembly.GetExecutingAssembly().GetManifestResourceStream(PayloadResourceName))
            {
                if (payload == null) throw new InvalidOperationException("The application payload is missing.");
                using (var archive = new ZipArchive(payload, ZipArchiveMode.Read, leaveOpen: false))
                {
                    var manifestEntry = archive.GetEntry(ManifestName)
                        ?? throw new InvalidDataException("The payload manifest is missing.");
                    Dictionary<string, ManifestFile> manifest;
                    using (var reader = new StreamReader(manifestEntry.Open(), Encoding.UTF8, true))
                        manifest = ReadManifest(reader);

                    var payloadEntries = archive.Entries
                        .Where(e => !string.IsNullOrEmpty(e.Name) && !string.Equals(e.FullName, ManifestName, StringComparison.Ordinal))
                        .ToDictionary(e => NormalizeRelativePath(e.FullName), StringComparer.OrdinalIgnoreCase);

                    if (payloadEntries.Count != manifest.Count || manifest.Keys.Any(k => !payloadEntries.ContainsKey(k)))
                        throw new InvalidDataException("The payload contents do not match its manifest.");

                    string destinationRoot = EnsureTrailingSeparator(Path.GetFullPath(destination));
                    foreach (var item in manifest.OrderBy(p => p.Key, StringComparer.Ordinal))
                    {
                        string outputPath = Path.GetFullPath(Path.Combine(destination, item.Key.Replace('/', Path.DirectorySeparatorChar)));
                        if (!outputPath.StartsWith(destinationRoot, StringComparison.OrdinalIgnoreCase))
                            throw new InvalidDataException("The payload contains an unsafe path.");

                        Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? destination);
                        using (var input = payloadEntries[item.Key].Open())
                        using (var output = new FileStream(outputPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                            input.CopyTo(output);

                        var info = new FileInfo(outputPath);
                        if (info.Length != item.Value.Size || !string.Equals(HashFile(outputPath), item.Value.Sha256, StringComparison.OrdinalIgnoreCase))
                            throw new InvalidDataException("Payload verification failed for " + item.Key + ".");
                    }

                    File.WriteAllLines(Path.Combine(destination, ManifestName),
                        manifest.OrderBy(p => p.Key, StringComparer.Ordinal)
                            .Select(p => p.Value.Sha256 + "\t" +
                                p.Value.Size.ToString(CultureInfo.InvariantCulture) + "\t" + p.Key),
                        new UTF8Encoding(false));
                }
            }

            if (!File.Exists(Path.Combine(destination, InnerExeName)))
                throw new InvalidDataException("The payload does not contain " + InnerExeName + ".");
        }

        private static Dictionary<string, ManifestFile> ReadManifest(TextReader reader)
        {
            var result = new Dictionary<string, ManifestFile>(StringComparer.OrdinalIgnoreCase);
            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#", StringComparison.Ordinal)) continue;
                var parts = line.Split(new[] { '\t' }, 3);
                if (parts.Length != 3 || parts[0].Length != 64 || !long.TryParse(parts[1], NumberStyles.None,
                        CultureInfo.InvariantCulture, out long size) || size < 0)
                    throw new InvalidDataException("The payload manifest is invalid.");
                string path = NormalizeRelativePath(parts[2]);
                if (result.ContainsKey(path))
                    throw new InvalidDataException("The payload manifest contains a duplicate path.");
                result.Add(path, new ManifestFile(parts[0], size));
            }
            return result;
        }

        private static string NormalizeRelativePath(string path)
        {
            string normalized = path.Replace('\\', '/').TrimStart('/');
            if (string.IsNullOrWhiteSpace(normalized) || Path.IsPathRooted(path) || normalized.Contains(":") ||
                normalized.Split('/').Any(p => p.Length == 0 || p == "." || p == ".."))
                throw new InvalidDataException("The payload contains an unsafe path: " + path);
            return normalized;
        }

        private static void SweepAbandonedPortableDirectories()
        {
            if (!Directory.Exists(PortableRoot)) return;
            foreach (string directory in Directory.GetDirectories(PortableRoot))
            {
                string marker = Path.Combine(directory, PortableMarkerName);
                if (!File.Exists(marker)) continue;
                try
                {
                    var lines = File.ReadAllLines(marker);
                    if (lines.Length > 0 && string.Equals(lines[0], ProductName, StringComparison.Ordinal) &&
                        !MarkerHasLiveProcess(lines))
                        DeleteDirectoryWithRetries(directory);
                }
                catch { }
            }
        }

        private static void WritePortableMarker(string directory, string version, int launcherPid, int? childPid)
        {
            File.WriteAllLines(Path.Combine(directory, PortableMarkerName), new[]
            {
                ProductName,
                version,
                launcherPid.ToString(CultureInfo.InvariantCulture),
                childPid?.ToString(CultureInfo.InvariantCulture) ?? string.Empty
            }, new UTF8Encoding(false));
        }

        private static bool MarkerHasLiveProcess(string[] lines)
        {
            foreach (string text in lines.Skip(2).Take(2))
            {
                if (!int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out int pid)) continue;
                try
                {
                    using (var process = Process.GetProcessById(pid))
                        if (!process.HasExited) return true;
                }
                catch { }
            }
            return false;
        }

        private static void DeleteDirectoryWithRetries(string directory)
        {
            if (!Directory.Exists(directory)) return;
            for (int attempt = 0; attempt < 5; attempt++)
            {
                try
                {
                    foreach (string file in Directory.GetFiles(directory, "*", SearchOption.AllDirectories))
                        try { File.SetAttributes(file, FileAttributes.Normal); } catch { }
                    Directory.Delete(directory, recursive: true);
                    return;
                }
                catch when (attempt < 4)
                {
                    System.Threading.Thread.Sleep(150 * (attempt + 1));
                }
            }
        }

        private static bool IsTrustedForInstall(string path)
        {
#if ALLOW_UNSIGNED_INSTALL
            // Local packages produced by build-portable.ps1 are intentionally unsigned and must
            // remain installable for end-to-end testing. release.ps1 omits this compile-time flag;
            // its public launcher has no environment-variable or command-line bypass.
            return true;
#else
            return AuthenticodeTrust.IsValid(path);
#endif
        }

        private static string HashFile(string path)
        {
            using (var stream = File.OpenRead(path))
            using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", string.Empty);
        }

        private static string CurrentExecutablePath() => Process.GetCurrentProcess().MainModule?.FileName
            ?? Assembly.GetExecutingAssembly().Location;

        private static string EnsureTrailingSeparator(string path) =>
            path.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
                ? path
                : path + Path.DirectorySeparatorChar;

        private static string QuoteArguments(IEnumerable<string> arguments) =>
            string.Join(" ", arguments.Select(QuoteArgument));

        private static string QuoteArgument(string argument)
        {
            if (argument.Length > 0 && argument.All(c => !char.IsWhiteSpace(c) && c != '"')) return argument;
            var sb = new StringBuilder("\"");
            int slashes = 0;
            foreach (char c in argument)
            {
                if (c == '\\') { slashes++; continue; }
                if (c == '"') sb.Append('\\', slashes * 2 + 1).Append('"');
                else { sb.Append('\\', slashes).Append(c); }
                slashes = 0;
            }
            sb.Append('\\', slashes * 2).Append('"');
            return sb.ToString();
        }

        private sealed class ManifestFile
        {
            internal ManifestFile(string sha256, long size) { Sha256 = sha256; Size = size; }
            internal string Sha256 { get; }
            internal long Size { get; }
        }
    }

    internal static class AuthenticodeTrust
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct WinTrustFileInfo
        {
            internal uint Size;
            internal IntPtr FilePath;
            internal IntPtr File;
            internal IntPtr KnownSubject;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct WinTrustData
        {
            internal uint Size;
            internal IntPtr PolicyCallbackData;
            internal IntPtr SipClientData;
            internal uint UiChoice;
            internal uint RevocationChecks;
            internal uint UnionChoice;
            internal IntPtr Union;
            internal uint StateAction;
            internal IntPtr StateData;
            internal IntPtr UrlReference;
            internal uint ProviderFlags;
            internal uint UiContext;
            internal IntPtr SignatureSettings;
        }

        private static readonly Guid VerifyGeneric = new Guid("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");

        [DllImport("wintrust.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
        private static extern uint WinVerifyTrust(IntPtr window, ref Guid action, IntPtr trustData);

        internal static bool IsValid(string path)
        {
            IntPtr pathPointer = Marshal.StringToHGlobalUni(path);
            IntPtr filePointer = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(WinTrustFileInfo)));
            IntPtr dataPointer = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(WinTrustData)));
            try
            {
                Marshal.StructureToPtr(new WinTrustFileInfo
                {
                    Size = (uint)Marshal.SizeOf(typeof(WinTrustFileInfo)),
                    FilePath = pathPointer
                }, filePointer, false);
                Marshal.StructureToPtr(new WinTrustData
                {
                    Size = (uint)Marshal.SizeOf(typeof(WinTrustData)),
                    UiChoice = 2,
                    UnionChoice = 1,
                    Union = filePointer,
                    ProviderFlags = 0x1000
                }, dataPointer, false);
                var action = VerifyGeneric;
                return WinVerifyTrust(IntPtr.Zero, ref action, dataPointer) == 0;
            }
            finally
            {
                Marshal.FreeHGlobal(dataPointer);
                Marshal.FreeHGlobal(filePointer);
                Marshal.FreeHGlobal(pathPointer);
            }
        }
    }
}
