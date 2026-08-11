#if WINDOWS
using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Nefarius.ViGEm.Client;

namespace EmulationScreenServer
{
    public static partial class DependencyManager
    {
        private const string VigemUrl = "https://github.com/ViGEm/ViGEmBus/releases/download/v1.22.0/ViGEmBus_1.22.0_x64_x86_arm64.exe";

        private static async Task EnsureViGEmBusAsync()
        {
            Console.WriteLine("[DependencyManager] Checking ViGEmBus driver...");
            try
            {
                var testClient = new ViGEmClient();
                testClient.Dispose();
                Console.WriteLine("[DependencyManager] ViGEmBus driver found.");
            }
            catch (Exception)
            {
                Console.WriteLine("[DependencyManager] ViGEmBus Driver not found! Initiating installation...");
                await InstallViGEmBusAsync();
            }
        }

        private static async Task InstallViGEmBusAsync()
        {
            string installerPath = Path.Combine(BinPath, "vigembus_installer.exe");

            Console.WriteLine("[DependencyManager] Downloading ViGEmBus installer...");
            await DownloadFileAsync(VigemUrl, installerPath);

            Console.WriteLine("[DependencyManager] Launching Installer to install Virtual Xbox Controller Driver.");
            Console.WriteLine("[!!!] PLEASE ACCEPT THE WINDOWS UAC PROMPT IF IT APPEARS [!!!]");

            Process installer = new Process();
            installer.StartInfo.FileName = installerPath;
            installer.StartInfo.Arguments = "/q";
            installer.StartInfo.UseShellExecute = true;
            installer.StartInfo.Verb = "runas";

            try
            {
                installer.Start();
                installer.WaitForExit();

                using (var testClient = new ViGEmClient()) { }
                Console.WriteLine("[DependencyManager] ViGEmBus installed successfully.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DependencyManager] CRITICAL: Failed to install ViGEmBus. You must install it manually. Error: {ex.Message}");
            }
            finally
            {
                if (File.Exists(installerPath)) File.Delete(installerPath);
            }
        }
    }
}
#endif
