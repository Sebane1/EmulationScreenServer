using System;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace EmulationScreenServer
{
    public static class NetworkAddressHelper
    {
        private static readonly string[] PublicIpEndpoints =
        {
            "https://api.ipify.org",
            "https://icanhazip.com",
            "https://ifconfig.me/ip",
        };

        public static string GetLanIPv4OrLocalhost()
        {
            try
            {
                foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != OperationalStatus.Up) continue;
                    if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;

                    var ipProps = ni.GetIPProperties();
                    foreach (var ua in ipProps.UnicastAddresses)
                    {
                        if (ua.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                        if (IPAddress.IsLoopback(ua.Address)) continue;

                        var bytes = ua.Address.GetAddressBytes();
                        if (bytes[0] == 169 && bytes[1] == 254) continue;

                        return ua.Address.ToString();
                    }
                }
            }
            catch { }

            return "127.0.0.1";
        }

        public static async Task<string?> TryGetPublicIPv4Async(CancellationToken cancellationToken = default)
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };

            foreach (var endpoint in PublicIpEndpoints)
            {
                try
                {
                    var response = await http.GetStringAsync(endpoint, cancellationToken).ConfigureAwait(false);
                    var candidate = response.Trim();

                    if (IPAddress.TryParse(candidate, out var address)
                        && address.AddressFamily == AddressFamily.InterNetwork
                        && !IPAddress.IsLoopback(address))
                    {
                        return candidate;
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch
                {
                    // Try the next provider.
                }
            }

            return null;
        }
    }
}
