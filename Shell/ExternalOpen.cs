using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using KillerPDF.Services;

namespace KillerPDF
{
    /// <summary>
    /// Single-instance entry points: App forwards a second launch's file path here rather than
    /// starting another process.
    ///
    /// These stay on the window: RestoreAndActivate drives WindowState, Activate() and Topmost,
    /// which a UserControl does not have. The OpenInNewTab call routes through ActiveViewer so the
    /// forwarded file lands in whichever pane has focus.
    /// </summary>
    public partial class MainWindow
    {
        /// <summary>A forwarded path that arrived before the panes existed, replayed from Loaded.</summary>
        private string? _pendingExternalPath;

        public async void OpenFromExternal(string? path)
        {
            // A second launch is forwarded here off the pipe thread, and Application.MainWindow is
            // already set while this window's constructor is still running - so ActiveViewer can
            // still be null (it is assigned by InitSplitPanes). Hold the path and let Loaded
            // replay it rather than dereferencing null and losing the file the user double-clicked.
            if (ActiveViewer == null)
            {
                _pendingExternalPath = path;
                return;
            }

            if (!string.IsNullOrEmpty(path) && File.Exists(path))
            {
                ActiveViewer.OpenInNewTabExt(path!);
                return;
            }
            if (ProtocolRegistrar.TryGetTargetUrl(path, out var target) && target != null)
                await OpenProtocolUrlAsync(target);
        }

        /// <summary>Replay whatever arrived during startup. No-op in the normal case.</summary>
        internal void FlushPendingExternalOpen()
        {
            if (_pendingExternalPath == null) return;
            var path = _pendingExternalPath;
            _pendingExternalPath = null;
            OpenFromExternal(path);
        }

        private async Task OpenProtocolUrlAsync(System.Uri target)
        {
            string temp = App.MakeTempFile("browser");
            try
            {
                using var http = new HttpClient { Timeout = System.TimeSpan.FromSeconds(90) };
                using var response = await http.GetAsync(target, HttpCompletionOption.ResponseHeadersRead);
                response.EnsureSuccessStatusCode();
                const long MaxBytes = 256L * 1024 * 1024;
                if (response.Content.Headers.ContentLength is long length && length > MaxBytes)
                    throw new InvalidDataException("The PDF is larger than the 256 MB browser handoff limit.");

                using (var input = await response.Content.ReadAsStreamAsync())
                using (var output = File.Create(temp))
                {
                    var buffer = new byte[81920];
                    long total = 0;
                    int read;
                    while ((read = await input.ReadAsync(buffer)) > 0)
                    {
                        total += read;
                        if (total > MaxBytes) throw new InvalidDataException("The PDF is larger than the 256 MB browser handoff limit.");
                        await output.WriteAsync(buffer.AsMemory(0, read));
                    }
                }

                using (var check = File.OpenRead(temp))
                {
                    var magic = new byte[5];
                    if (check.Read(magic, 0, magic.Length) != magic.Length ||
                        System.Text.Encoding.ASCII.GetString(magic) != "%PDF-")
                        throw new InvalidDataException("The downloaded file is not a PDF.");
                }
                ActiveViewer.OpenInNewTabExt(temp);
            }
            catch (System.Exception ex)
            {
                try { File.Delete(temp); } catch { }
                KillerDialog.Show(this,
                    $"KillerPDF could not open the browser PDF.\n\n{ex.Message}",
                    "KillerPDF", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        public void RestoreAndActivate()
        {
            if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
            Activate();
            IntPtr handle = new WindowInteropHelper(this).Handle;
            if (handle != IntPtr.Zero) SetForegroundWindow(handle);
            // Briefly toggle Topmost to pull the window in front without keeping it pinned.
            Topmost = true;
            Topmost = false;
            Focus();
        }

        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool SetForegroundWindow(IntPtr hWnd);
    }
}
