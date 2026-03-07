using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Management;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace NetFixer;

public enum IssueStatus { Unknown, Scanning, Pass, Warning, Fail, Fixed }

public class NetworkIssue
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string Detail { get; set; } = "";
    public IssueStatus Status { get; set; } = IssueStatus.Unknown;
    public bool CanFix { get; set; } = false;
    public Func<Task<string>> FixAction { get; set; }
}

public static class NetworkDiagnostics
{
    // ─── Check 1: WiFi Adapter Present & Connected ────────────────────────────
    public static async Task<NetworkIssue> CheckWiFiAdapter()
    {
        var issue = new NetworkIssue
        {
            Id = "wifi_adapter",
            Title = "WiFi Adapter",
            Description = "Intel Wi-Fi adapter status and connectivity"
        };
        await Task.Run(() =>
        {
            var wifiAdapter = NetworkInterface.GetAllNetworkInterfaces()
                .FirstOrDefault(n => n.NetworkInterfaceType == NetworkInterfaceType.Wireless80211);

            if (wifiAdapter == null)
            {
                issue.Status = IssueStatus.Fail;
                issue.Detail = "No WiFi adapter found on this system.";
                issue.CanFix = false;
            }
            else if (wifiAdapter.OperationalStatus != OperationalStatus.Up)
            {
                issue.Status = IssueStatus.Fail;
                issue.Detail = $"{wifiAdapter.Description} is {wifiAdapter.OperationalStatus}. Not connected to any network.";
                issue.CanFix = true;
                issue.FixAction = async () =>
                {
                    var r = RunCmd("netsh", "interface set interface \"Wi-Fi\" enable");
                    await Task.Delay(2000);
                    return r;
                };
            }
            else
            {
                var ipProps = wifiAdapter.GetIPProperties();
                var ip = ipProps.UnicastAddresses.FirstOrDefault(a => a.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork);
                var gw = ipProps.GatewayAddresses.FirstOrDefault(g => g.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork);
                issue.Status = IssueStatus.Pass;
                issue.Detail = $"{wifiAdapter.Description}\nIP: {ip?.Address} | Gateway: {gw?.Address} | Speed: {wifiAdapter.Speed / 1_000_000} Mbps";
            }
        });
        return issue;
    }

    // ─── Check 2: Interface Metric / Routing Priority ─────────────────────────
    public static async Task<NetworkIssue> CheckInterfaceMetric()
    {
        var issue = new NetworkIssue
        {
            Id = "interface_metric",
            Title = "Routing Priority (Interface Metric)",
            Description = "WiFi must have higher priority than USB/Ethernet adapters"
        };
        await Task.Run(() =>
        {
            var result = RunCmd("powershell", "-Command \"Get-NetIPInterface | Where-Object {$_.AddressFamily -eq 'IPv4'} | Select-Object InterfaceAlias,InterfaceMetric,AutomaticMetric,ConnectionState | ConvertTo-Csv -NoTypeInformation\"");
            var lines = result.Split('\n').Skip(1).Where(l => l.Contains(",")).ToList();
            int? wifiMetric = null, usbMetric = null;
            bool wifiAutomatic = false;

            var sb = new StringBuilder();
            foreach (var line in lines)
            {
                var parts = line.Split(',').Select(p => p.Trim('"', '\r', ' ')).ToArray();
                if (parts.Length < 4) continue;
                var alias = parts[0]; var metric = int.TryParse(parts[1], out int m) ? m : 999;
                var auto = parts[2];
                var state = parts[3];
                if (state != "Connected") continue;
                sb.AppendLine($"{alias}: Metric={metric}, Auto={auto}");
                if (alias.Equals("WiFi", StringComparison.OrdinalIgnoreCase)) { wifiMetric = metric; wifiAutomatic = auto.Equals("True", StringComparison.OrdinalIgnoreCase); }
                else if (alias.IndexOf("Ethernet", StringComparison.OrdinalIgnoreCase) >= 0 || alias.IndexOf("USB", StringComparison.OrdinalIgnoreCase) >= 0) { usbMetric = metric; }
            }

            if (wifiMetric == null)
            {
                issue.Status = IssueStatus.Warning;
                issue.Detail = "WiFi not connected — metric check skipped.\n" + sb;
            }
            else if (wifiAutomatic || (usbMetric.HasValue && wifiMetric >= usbMetric))
            {
                issue.Status = IssueStatus.Fail;
                issue.Detail = $"WiFi metric ({wifiMetric}) is NOT prioritized over other adapters.\nAutomatic metric: {wifiAutomatic}\n" + sb;
                issue.CanFix = true;
                issue.FixAction = async () =>
                {
                    var r1 = RunCmd("powershell", "-Command \"Set-NetIPInterface -InterfaceAlias 'WiFi' -InterfaceMetric 10 -AutomaticMetric Disabled\"");
                    var r2 = RunCmd("powershell", "-Command \"Get-NetAdapter | Where-Object {$_.Name -ne 'WiFi' -and $_.Status -eq 'Up'} | Get-NetIPInterface | Where-Object {$_.AddressFamily -eq 'IPv4'} | Set-NetIPInterface -InterfaceMetric 100 -AutomaticMetric Disabled\"");
                    await Task.Delay(500);
                    return $"WiFi metric set to 10. Other adapters set to 100.\n{r1}\n{r2}".Trim();
                };
            }
            else
            {
                issue.Status = IssueStatus.Pass;
                issue.Detail = $"WiFi metric ({wifiMetric}) is correctly prioritized.\nAutomatic metric: {wifiAutomatic}\n" + sb;
            }
        });
        return issue;
    }

    // ─── Check 3: WiFi Power Saving Mode ──────────────────────────────────────
    public static async Task<NetworkIssue> CheckWiFiPowerSaving()
    {
        var issue = new NetworkIssue
        {
            Id = "wifi_power_saving",
            Title = "WiFi Power Saving Mode",
            Description = "Power saving causes intermittent WiFi drops (especially on battery)"
        };
        await Task.Run(() =>
        {
            var result = RunCmd("powercfg", "/query SCHEME_CURRENT");
            var lines = result.Split('\n');
            int dcIdx = -1;
            bool inWireless = false;
            bool inPowerSaving = false;
            string dcValue = "Unknown";

            for (int i = 0; i < lines.Length; i++)
            {
                var l = lines[i].Trim();
                if (l.Contains("19cbb8fa-5279-450e-9fac-8a3d5fedd0c1")) inWireless = true;
                if (inWireless && l.Contains("12bbebe6-58d6-4636-95bb-3217ef867c1a")) inPowerSaving = true;
                if (inPowerSaving && l.StartsWith("Current DC Power Setting Index:"))
                {
                    var hex = l.Split(':').Last().Trim();
                    if (int.TryParse(hex.Replace("0x", ""), System.Globalization.NumberStyles.HexNumber, null, out int val)) dcIdx = val;
                }
                if (inPowerSaving && dcIdx >= 0 && l.StartsWith("Possible Setting Index:"))
                {
                    var idxStr = l.Split(':').Last().Trim();
                    if (int.TryParse(idxStr, out int idx) && idx == dcIdx)
                    {
                        if (i + 1 < lines.Length) dcValue = lines[i + 1].Replace("Possible Setting Friendly Name:", "").Trim();
                        break;
                    }
                }
            }

            if (dcIdx == 0)
            {
                issue.Status = IssueStatus.Pass;
                issue.Detail = $"WiFi Power Saving: {dcValue} (Maximum Performance) ✓";
            }
            else if (dcIdx > 0)
            {
                issue.Status = IssueStatus.Fail;
                issue.Detail = $"WiFi Power Saving on Battery: {dcValue} (Index {dcIdx})\nThis causes periodic WiFi disconnections on battery.";
                issue.CanFix = true;
                issue.FixAction = async () =>
                {
                    var r = RunCmd("powercfg", "/setdcvalueindex SCHEME_CURRENT 19cbb8fa-5279-450e-9fac-8a3d5fedd0c1 12bbebe6-58d6-4636-95bb-3217ef867c1a 0");
                    RunCmd("powercfg", "/setactive SCHEME_CURRENT");
                    await Task.Delay(300);
                    return $"WiFi power saving disabled on battery (set to Maximum Performance).\n{r}";
                };
            }
            else
            {
                issue.Status = IssueStatus.Warning;
                issue.Detail = $"Could not determine WiFi power saving setting. Please check manually.";
            }
        });
        return issue;
    }

    // ─── Check 4: Wi-Fi Direct Virtual Adapter ────────────────────────────────
    public static async Task<NetworkIssue> CheckWiFiDirectAdapter()
    {
        var issue = new NetworkIssue
        {
            Id = "wifi_direct",
            Title = "Wi-Fi Direct Virtual Adapter",
            Description = "Virtual adapter causes NDIS Event 74 conflicts with physical WiFi"
        };
        await Task.Run(() =>
        {
            var result = RunCmd("powershell", "-Command \"Get-NetAdapter | Where-Object {$_.InterfaceDescription -like '*Wi-Fi Direct*'} | Select-Object Name,Status | ConvertTo-Csv -NoTypeInformation\"");
            var adapters = result.Split('\n').Skip(1).Where(l => l.Contains(",")).ToList();
            if (!adapters.Any())
            {
                issue.Status = IssueStatus.Pass;
                issue.Detail = "No Wi-Fi Direct Virtual Adapters found.";
            }
            else
            {
                var enabled = adapters.Where(l => l.IndexOf("Up", StringComparison.OrdinalIgnoreCase) >= 0).ToList();
                if (enabled.Any())
                {
                    issue.Status = IssueStatus.Fail;
                    issue.Detail = $"Found {enabled.Count} active Wi-Fi Direct adapter(s):\n{string.Join("\n", enabled)}\nThese trigger daily NDIS Event 74 errors.";
                    issue.CanFix = true;
                    issue.FixAction = async () =>
                    {
                        var r = RunCmd("powershell", "-Command \"Get-NetAdapter | Where-Object {$_.InterfaceDescription -like '*Wi-Fi Direct*'} | Disable-NetAdapter -Confirm:$false\"");
                        await Task.Delay(1000);
                        return $"Wi-Fi Direct Virtual Adapters disabled.\n{r}";
                    };
                }
                else
                {
                    issue.Status = IssueStatus.Pass;
                    issue.Detail = $"Wi-Fi Direct adapters present but already disabled.";
                }
            }
        });
        return issue;
    }

    // ─── Check 5: DNS Resolution ──────────────────────────────────────────────
    public static async Task<NetworkIssue> CheckDNSResolution()
    {
        var issue = new NetworkIssue
        {
            Id = "dns_resolution",
            Title = "DNS Resolution",
            Description = "DNS servers must resolve external domains"
        };
        try
        {
            var sw = Stopwatch.StartNew();
            var entry = await Dns.GetHostEntryAsync("google.com");
            sw.Stop();
            issue.Status = IssueStatus.Pass;
            issue.Detail = $"google.com → {entry.AddressList.FirstOrDefault()} ({sw.ElapsedMilliseconds}ms)";
        }
        catch (Exception ex)
        {
            issue.Status = IssueStatus.Fail;
            issue.Detail = $"DNS resolution failed: {ex.Message}\nConfigured DNS servers may be unreachable.";
            issue.CanFix = true;
            issue.FixAction = async () =>
            {
                var r1 = RunCmd("powershell", "-Command \"Get-NetAdapter | Where-Object {$_.Status -eq 'Up' -and $_.Name -eq 'WiFi'} | Set-DnsClientServerAddress -ServerAddresses '8.8.8.8','8.8.4.4'\"");
                var r2 = RunCmd("ipconfig", "/flushdns");
                RunCmd("net", "stop dnscache"); RunCmd("net", "start dnscache");
                await Task.Delay(1000);
                return $"DNS set to Google (8.8.8.8, 8.8.4.4) and cache flushed.\n{r1}\n{r2}";
            };
        }
        return issue;
    }

    // ─── Check 6: Internet Reachability (ICMP + HTTP) ─────────────────────────
    public static async Task<NetworkIssue> CheckInternetReachability()
    {
        var issue = new NetworkIssue
        {
            Id = "internet_reach",
            Title = "Internet Reachability",
            Description = "ICMP ping and HTTP connectivity to internet"
        };
        try
        {
            using var ping = new Ping();
            var reply = await ping.SendPingAsync("8.8.8.8", 3000);
            if (reply.Status == IPStatus.Success)
            {
                using var http = new HttpClient();
                http.Timeout = TimeSpan.FromSeconds(5);
                var resp = await http.GetAsync("https://www.google.com");
                issue.Status = IssueStatus.Pass;
                issue.Detail = $"Ping 8.8.8.8: {reply.RoundtripTime}ms | HTTP google.com: {(int)resp.StatusCode} {resp.StatusCode}";
            }
            else
            {
                issue.Status = IssueStatus.Fail;
                issue.Detail = $"Ping to 8.8.8.8 failed: {reply.Status}";
                issue.CanFix = true;
                issue.FixAction = ResetNetworkStack;
            }
        }
        catch (Exception ex)
        {
            issue.Status = IssueStatus.Fail;
            issue.Detail = $"Internet unreachable: {ex.Message}";
            issue.CanFix = true;
            issue.FixAction = ResetNetworkStack;
        }
        return issue;
    }

    // ─── Check 7: Proxy Settings ──────────────────────────────────────────────
    public static async Task<NetworkIssue> CheckProxySettings()
    {
        var issue = new NetworkIssue
        {
            Id = "proxy",
            Title = "Proxy Settings",
            Description = "Incorrect proxy settings block internet access"
        };
        await Task.Run(() =>
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Internet Settings");
            if (key == null) { issue.Status = IssueStatus.Pass; issue.Detail = "Registry key not found — proxy not set."; return; }

            var proxyEnable = key.GetValue("ProxyEnable");
            var proxyServer = key.GetValue("ProxyServer")?.ToString();
            bool enabled = proxyEnable != null && Convert.ToInt32(proxyEnable) == 1;

            if (enabled)
            {
                issue.Status = IssueStatus.Warning;
                issue.Detail = $"Proxy is ENABLED: {proxyServer}\nThis may block internet if the proxy server is unavailable.";
                issue.CanFix = true;
                issue.FixAction = async () =>
                {
                    using var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Internet Settings", true);
                    k?.SetValue("ProxyEnable", 0, RegistryValueKind.DWord);
                    RunCmd("netsh", "winhttp reset proxy");
                    await Task.Delay(300);
                    return "Proxy disabled. WinHTTP proxy reset to direct.";
                };
            }
            else
            {
                issue.Status = IssueStatus.Pass;
                issue.Detail = proxyServer != null
                    ? $"Proxy server stored ({proxyServer}) but disabled — OK."
                    : "No proxy configured — direct connection.";
            }
        });
        return issue;
    }

    // ─── Check 8: Winsock / TCP-IP Stack ──────────────────────────────────────
    public static async Task<NetworkIssue> CheckWinsock()
    {
        var issue = new NetworkIssue
        {
            Id = "winsock",
            Title = "Winsock / TCP-IP Stack",
            Description = "Corrupted Winsock catalog causes erratic connectivity"
        };
        await Task.Run(() =>
        {
            var result = RunCmd("netsh", "winsock show catalog");
            var entries = result.Split('\n').Count(l => l.TrimStart().StartsWith("Entry"));
            if (entries < 10)
            {
                issue.Status = IssueStatus.Fail;
                issue.Detail = $"Winsock catalog has only {entries} entries — likely corrupted.";
                issue.CanFix = true;
                issue.FixAction = ResetNetworkStack;
            }
            else
            {
                issue.Status = IssueStatus.Pass;
                issue.Detail = $"Winsock catalog: {entries} entries — looks normal.";
            }
        });
        return issue;
    }

    // ─── Check 9: DHCP Lease ──────────────────────────────────────────────────
    public static async Task<NetworkIssue> CheckDHCPLease()
    {
        var issue = new NetworkIssue
        {
            Id = "dhcp",
            Title = "DHCP IP Lease",
            Description = "Valid DHCP IP address on WiFi interface"
        };
        await Task.Run(() =>
        {
            var wifiNic = NetworkInterface.GetAllNetworkInterfaces()
                .FirstOrDefault(n => n.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 && n.OperationalStatus == OperationalStatus.Up);
            if (wifiNic == null)
            {
                issue.Status = IssueStatus.Warning;
                issue.Detail = "WiFi not connected — DHCP check skipped.";
                return;
            }
            var ipv4 = wifiNic.GetIPProperties().UnicastAddresses
                .FirstOrDefault(a => a.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork);
            if (ipv4 == null)
            {
                issue.Status = IssueStatus.Fail;
                issue.Detail = "No IPv4 address assigned on WiFi — DHCP may have failed.";
                issue.CanFix = true;
                issue.FixAction = async () =>
                {
                    var r1 = RunCmd("ipconfig", "/release \"Wi-Fi\"");
                    await Task.Delay(1000);
                    var r2 = RunCmd("ipconfig", "/renew \"Wi-Fi\"");
                    await Task.Delay(2000);
                    return $"DHCP lease renewed.\n{r1}\n{r2}";
                };
            }
            else if (ipv4.Address.ToString().StartsWith("169.254"))
            {
                issue.Status = IssueStatus.Fail;
                issue.Detail = $"APIPA address detected: {ipv4.Address}\nDHCP server unreachable — router may not be responding.";
                issue.CanFix = true;
                issue.FixAction = async () =>
                {
                    RunCmd("ipconfig", "/release \"Wi-Fi\"");
                    await Task.Delay(2000);
                    var r = RunCmd("ipconfig", "/renew \"Wi-Fi\"");
                    await Task.Delay(3000);
                    return $"DHCP renew attempted.\n{r}";
                };
            }
            else
            {
                issue.Status = IssueStatus.Pass;
                var gw = wifiNic.GetIPProperties().GatewayAddresses.FirstOrDefault(g => g.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork);
                issue.Detail = $"IP: {ipv4.Address} | Gateway: {gw?.Address} | Lease valid.";
            }
        });
        return issue;
    }

    // ─── Check 10: Avast DNS Interference ────────────────────────────────────
    public static async Task<NetworkIssue> CheckAvastInterference()
    {
        var issue = new NetworkIssue
        {
            Id = "avast",
            Title = "Avast DNS Interference",
            Description = "Avast Web Shield intercepts DNS, causing timeouts (Event 1014)"
        };
        await Task.Run(() =>
        {
            // Check if Avast service is running first
            var avastRunning = RunCmd("sc", "query avast! Antivirus");
            bool isRunning = avastRunning.Contains("RUNNING");

            // Only scan the event log if Avast is actually running,
            // and only look at events from the last 24 hours to avoid
            // stale old entries permanently triggering this warning.
            int count = 0;
            if (isRunning)
            {
                var result = RunCmd("powershell",
                    "-Command \"$since = (Get-Date).AddHours(-24); " +
                    "Get-WinEvent -FilterHashtable @{LogName='System'; ProviderName='Microsoft-Windows-DNS-Client'; StartTime=$since} -ErrorAction SilentlyContinue | " +
                    "Where-Object { $_.Message -match 'avast' } | Measure-Object | Select-Object -ExpandProperty Count\"");
                int.TryParse(result.Trim(), out count);
            }

            if (isRunning && count > 0)
            {
                issue.Status = IssueStatus.Warning;
                issue.Detail = $"Found {count} Avast-related DNS timeout events in the last 24 hours.\nAvast Web Shield intercepts DNS queries, causing them to time out.";
                issue.CanFix = true;
                issue.FixAction = async () =>
                {
                    var r = RunCmd("ipconfig", "/flushdns");
                    RunCmd("net", "stop dnscache");
                    await Task.Delay(500);
                    RunCmd("net", "start dnscache");
                    await Task.Delay(500);
                    return $"DNS cache cleared and DNS client restarted.\nTo fully resolve: Open Avast → Menu → Settings → Protection → Core Shields → Web Shield → disable 'HTTPS scanning'.\n{r}";
                };
            }
            else if (isRunning)
            {
                issue.Status = IssueStatus.Pass;
                issue.Detail = "Avast is running but no DNS timeout events found in the last 24 hours.";
            }
            else
            {
                issue.Status = IssueStatus.Pass;
                issue.Detail = "Avast Antivirus is not running — no DNS interference detected.";
            }
        });
        return issue;
    }

    // ─── Shared: Full Network Stack Reset ─────────────────────────────────────
    private static async Task<string> ResetNetworkStack()
    {
        var sb = new StringBuilder();
        sb.AppendLine(RunCmd("netsh", "winsock reset"));
        sb.AppendLine(RunCmd("netsh", "int ip reset"));
        sb.AppendLine(RunCmd("netsh", "int ipv6 reset"));
        sb.AppendLine(RunCmd("ipconfig", "/flushdns"));
        sb.AppendLine(RunCmd("ipconfig", "/release"));
        await Task.Delay(1500);
        sb.AppendLine(RunCmd("ipconfig", "/renew"));
        return $"Network stack fully reset.\n{sb}";
    }

    // ─── Run All Checks ───────────────────────────────────────────────────────
    // Runs all 10 checks sequentially, calling onIssue after each one completes.
    // Using a callback instead of IAsyncEnumerable keeps .NET Framework 4.8 compatibility.
    public static async Task RunAllChecks(Func<NetworkIssue, Task> onIssue)
    {
        await onIssue(await CheckWiFiAdapter());
        await onIssue(await CheckDHCPLease());
        await onIssue(await CheckInterfaceMetric());
        await onIssue(await CheckWiFiPowerSaving());
        await onIssue(await CheckWiFiDirectAdapter());
        await onIssue(await CheckDNSResolution());
        await onIssue(await CheckProxySettings());
        await onIssue(await CheckInternetReachability());
        await onIssue(await CheckWinsock());
        await onIssue(await CheckAvastInterference());
    }

    // ─── Utility ──────────────────────────────────────────────────────────────
    private static string RunCmd(string exe, string args)
    {
        try
        {
            var psi = new ProcessStartInfo(exe, args)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var p = Process.Start(psi)!;
            string o = p.StandardOutput.ReadToEnd();
            string e = p.StandardError.ReadToEnd();
            p.WaitForExit();
            return (o + e).Trim();
        }
        catch (Exception ex) { return ex.Message; }
    }
}
