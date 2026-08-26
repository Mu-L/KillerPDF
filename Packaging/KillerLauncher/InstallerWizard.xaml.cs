using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace KillerLauncher
{
    public partial class InstallerWizard : Window
    {
        private int _page;
        private bool _installed;
        private string _installedDirectory = string.Empty;

        private InstallerWizard()
        {
            InitializeComponent();
            InstallFolder.Text = Program.DefaultInstallDirectory(false);
            ImageBrush grain = CreateGrain();
            GrainLayer.Background = grain;
            SidebarGrain.Background = grain;
            FrameGrain.Background = grain;
            RenderPage();
        }

        internal static int Run(string[] args)
        {
            var application = new System.Windows.Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            var wizard = new InstallerWizard();
            bool? result = wizard.ShowDialog();
            application.Shutdown();
            return result == true ? 0 : 1;
        }

        private void RenderPage()
        {
            bool options = _page == 1;
            Options.Visibility = options ? Visibility.Visible : Visibility.Collapsed;
            RuntimeStatus.Visibility = options ? Visibility.Visible : Visibility.Collapsed;
            BackButton.IsEnabled = _page > 0 && !_installed;
            CancelButton.Visibility = _installed ? Visibility.Collapsed : Visibility.Visible;
            if (_page == 0)
            {
                Heading.Text = "Install KillerPDF";
                Copy.Text = "Set up the standard Windows build with shortcuts, file associations, and automatic updates.";
                NextButton.Content = "Next";
            }
            else if (options)
            {
                Heading.Text = "Options";
                Copy.Text = "Choose the installation scope and shortcut.";
                SetRuntimeStatus();
                NextButton.Content = "Next";
            }
            else
            {
                Heading.Text = _installed ? "Installation complete" : "Ready to install";
                Copy.Text = _installed ? "KillerPDF is ready to use." :
                    (AllUsers.IsChecked == true ? "All users" : "Current user") +
                    (DesktopShortcut.IsChecked == true ? "  •  Desktop shortcut" : "  •  No desktop shortcut");
                SetRuntimeStatus();
                NextButton.Content = _installed ? "Launch" : "Install";
            }
        }

        private void SetRuntimeStatus()
        {
            bool ready = Program.HasDesktopRuntime10();
            RuntimeStatus.Text = ready ? ".NET 10 Desktop Runtime detected" : ".NET 10 Desktop Runtime required";
            RuntimeStatus.Foreground = new SolidColorBrush(ready
                ? Color.FromRgb(30, 165, 76) : Color.FromRgb(255, 190, 80));
        }

        private void Next_Click(object sender, RoutedEventArgs e)
        {
            if (_installed)
            {
                Process.Start(new ProcessStartInfo(Program.InstalledExecutable(_installedDirectory)) { UseShellExecute = true });
                DialogResult = true;
                return;
            }
            if (_page < 2) { _page++; RenderPage(); return; }
            string installDirectory;
            try { installDirectory = Program.ValidateInstallDirectory(InstallFolder.Text); }
            catch (Exception ex)
            {
                _page = 1;
                RenderPage();
                InstallFolder.Focus();
                InstallFolder.SelectAll();
                System.Windows.MessageBox.Show(this, ex.Message, "KillerPDF Setup", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (!Program.HasDesktopRuntime10())
            {
                Process.Start(new ProcessStartInfo("https://dotnet.microsoft.com/en-us/download/dotnet/10.0") { UseShellExecute = true });
                System.Windows.MessageBox.Show(this, "Install the .NET 10 Desktop Runtime, then return to setup.", "KillerPDF Setup", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            try
            {
                NextButton.IsEnabled = BackButton.IsEnabled = CancelButton.IsEnabled = false;
                Heading.Text = "Installing";
                Copy.Text = "Verifying and installing KillerPDF...";
                bool machine = AllUsers.IsChecked == true;
                int result = machine ? InstallForEveryone(DesktopShortcut.IsChecked == true, installDirectory)
                    : Program.Install(false, DesktopShortcut.IsChecked == true, installDirectory);
                if (result != 0) throw new InvalidOperationException("Setup returned " + result + ".");
                _installedDirectory = installDirectory;
                _installed = true;
                NextButton.IsEnabled = true;
                RenderPage();
            }
            catch (Exception ex)
            {
                NextButton.IsEnabled = BackButton.IsEnabled = CancelButton.IsEnabled = true;
                System.Windows.MessageBox.Show(this, ex.Message, "KillerPDF Setup", MessageBoxButton.OK, MessageBoxImage.Error);
                RenderPage();
            }
        }

        private static int InstallForEveryone(bool desktop, string installDirectory)
        {
            string arguments = "/silent " + (desktop ? "/desktop " : string.Empty) +
                Program.EncodeInstallDirectoryArgument(installDirectory);
            var start = new ProcessStartInfo(Process.GetCurrentProcess().MainModule.FileName,
                arguments) { UseShellExecute = true, Verb = "runas" };
            using (Process elevated = Process.Start(start))
            {
                if (elevated == null) return 1;
                elevated.WaitForExit();
                return elevated.ExitCode;
            }
        }

        private void Scope_Checked(object sender, RoutedEventArgs e)
        {
            if (InstallFolder == null) return;
            string user = Program.DefaultInstallDirectory(false);
            string machine = Program.DefaultInstallDirectory(true);
            if (string.IsNullOrWhiteSpace(InstallFolder.Text) ||
                string.Equals(InstallFolder.Text, user, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(InstallFolder.Text, machine, StringComparison.OrdinalIgnoreCase))
                InstallFolder.Text = Program.DefaultInstallDirectory(AllUsers.IsChecked == true);
        }

        private void Browse_Click(object sender, RoutedEventArgs e)
        {
            using var dialog = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "Choose where KillerPDF will be installed",
                ShowNewFolderButton = true,
                SelectedPath = Directory.Exists(InstallFolder.Text) ? InstallFolder.Text :
                    (Directory.Exists(Path.GetDirectoryName(InstallFolder.Text)) ? Path.GetDirectoryName(InstallFolder.Text) : string.Empty)
            };
            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                InstallFolder.Text = dialog.SelectedPath;
        }

        private void Back_Click(object sender, RoutedEventArgs e) { if (_page > 0) { _page--; RenderPage(); } }
        private void Cancel_Click(object sender, RoutedEventArgs e) { DialogResult = false; }
        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) { if (e.LeftButton == MouseButtonState.Pressed) DragMove(); }
        private void Website_Click(object sender, RoutedEventArgs e) =>
            Process.Start(new ProcessStartInfo("https://thekiller.net") { UseShellExecute = true });

        private static ImageBrush CreateGrain()
        {
            const int size = 128;
            var pixels = new byte[size * size * 4];
            var random = new Random(1979);
            for (int i = 0; i < pixels.Length; i += 4)
            {
                byte value = (byte)random.Next(82, 174);
                pixels[i] = pixels[i + 1] = pixels[i + 2] = value;
                pixels[i + 3] = (byte)random.Next(34, 92);
            }
            var bitmap = BitmapSource.Create(size, size, 96, 96, PixelFormats.Bgra32, null, pixels, size * 4);
            bitmap.Freeze();
            return new ImageBrush(bitmap) { TileMode = TileMode.Tile, ViewportUnits = BrushMappingMode.Absolute,
                Viewport = new Rect(0, 0, size, size), Stretch = Stretch.None };
        }
    }
}
