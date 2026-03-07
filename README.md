# ⚡ NetFixer — Windows Network Diagnostic & Auto-Repair Tool

<p align="center">
  <img src="screenshots/main_window.png" alt="NetFixer Main Window" width="700"/>
</p>

<p align="center">
  <img src="https://img.shields.io/badge/Platform-Windows%2010%2F11-0078D4?style=flat-square&logo=windows"/>
  <img src="https://img.shields.io/badge/.NET%20Framework-4.8-512BD4?style=flat-square&logo=dotnet"/>
  <img src="https://img.shields.io/badge/Language-C%23-239120?style=flat-square&logo=csharp"/>
  <img src="https://img.shields.io/badge/UI-WPF-68217A?style=flat-square"/>
  <img src="https://img.shields.io/badge/Size-64%20KB-brightgreen?style=flat-square"/>
  <img src="https://img.shields.io/badge/Admin-Required-red?style=flat-square"/>
  <img src="https://img.shields.io/badge/License-MIT-blue?style=flat-square"/>
</p>

---

**NetFixer** is a portable, single-file (~64 KB) Windows desktop application built with C# WPF that scans your network configuration for 10 common issues, explains what it found, and auto-fixes everything in one click — no installation required.

---

## 📸 Screenshots

| Main Window | After Scan |
|-------------|-----------|
| ![Main](screenshots/main_window.png) | ![Scan](screenshots/after_scan.png) |

---

## ✨ Features

- **10 automated diagnostic checks** covering the full network stack
- **One-click "Fix All Issues"** — applies all available auto-fixes sequentially
- **Per-issue "Auto-Fix" buttons** — fix individual problems
- **Live progress bar** with check counter
- **Summary strip** — Pass / Fail / Warning / Fixed counts at a glance
- **Activity log** — timestamped trace of every action
- **Portable single EXE** — 64 KB, no installer, no runtime to bundle
- **Instant startup** — uses pre-installed .NET Framework 4.8
- **Premium dark UI** — custom-styled WPF with pill buttons and glassmorphism cards
- **UAC elevation** — automatically requests Administrator privileges

---

## 🔍 Diagnostic Checks

| # | Check | What It Detects | Auto-Fix Available |
|---|-------|----------------|--------------------|
| 1 | **WiFi Adapter** | Adapter presence, IP, gateway, link speed | ✅ Re-enables adapter |
| 2 | **DHCP IP Lease** | APIPA 169.x addresses indicating DHCP failure | ✅ Release/renew lease |
| 3 | **Routing Priority (Interface Metric)** | USB tethering outranking WiFi, causing it to be bypassed | ✅ Sets WiFi metric=10, others=100, disables AutomaticMetric |
| 4 | **WiFi Power Saving Mode** | Battery DC power plan causing periodic disconnections | ✅ Sets Maximum Performance via `powercfg` |
| 5 | **Wi-Fi Direct Virtual Adapter** | NDIS Event 74 conflicts from virtual adapters | ✅ Disables all Wi-Fi Direct adapters |
| 6 | **DNS Resolution** | Failed `Dns.GetHostEntry("google.com")` with latency reporting | ✅ Sets Google DNS (8.8.8.8, 8.8.4.4) |
| 7 | **Proxy Settings** | Registry proxy enabled but server unreachable | ✅ Disables proxy, resets WinHTTP |
| 8 | **Internet Reachability** | ICMP ping to 8.8.8.8 + HTTPS GET to google.com | ✅ Full network stack reset |
| 9 | **Winsock / TCP-IP Stack** | Corrupted Winsock catalog (< 10 entries) | ✅ `netsh winsock reset` + IP reset |
| 10 | **Avast DNS Interference** | Avast Web Shield DNS timeouts in Event Log (last 24h only) | ✅ Flush DNS + restart dnscache |

---

## 🛠 Technical Details

### Architecture
```
NetFixer/
├── app.manifest              # UAC requireAdministrator elevation
├── NetFixer.csproj           # .NET Framework 4.8, WPF, x64
├── App.xaml / App.xaml.cs    # Application entry point
├── MainWindow.xaml           # Dark premium UI (569 lines)
├── MainWindow.xaml.cs        # Code-behind + 5 value converters
└── NetworkDiagnostics.cs     # All 10 checks + fix engine (547 lines)
```

### Diagnostics Engine (`NetworkDiagnostics.cs`)
- `CheckWiFiAdapter()` — uses `System.Net.NetworkInformation.NetworkInterface`
- `CheckDHCPLease()` — detects `169.254.x.x` APIPA addresses
- `CheckInterfaceMetric()` — parses `Get-NetIPInterface` CSV output
- `CheckWiFiPowerSaving()` — reads `powercfg /query SCHEME_CURRENT` for GUID `19cbb8fa-5279-450e-9fac-8a3d5fedd0c1`
- `CheckWiFiDirectAdapter()` — queries `Get-NetAdapter` for `*Wi-Fi Direct*` adapters
- `CheckDNSResolution()` — `Dns.GetHostEntryAsync("google.com")` with stopwatch timing
- `CheckInternetReachability()` — `Ping.SendPingAsync("8.8.8.8")` + `HttpClient.GetAsync("https://google.com")`
- `CheckProxySettings()` — reads `HKCU\Software\Microsoft\Windows\CurrentVersion\Internet Settings`
- `CheckWinsock()` — `netsh winsock show catalog` entry count validation
- `CheckAvastInterference()` — `sc query "avast! Antivirus"` + `Get-WinEvent` last 24h filter
- `RunAllChecks(Func<NetworkIssue, Task> onIssue)` — callback-based async runner, .NET 4.8 compatible
- `ResetNetworkStack()` — winsock reset, ip reset, ipv6 reset, flushdns, release/renew

### UI (`MainWindow.xaml`)
- Custom `WindowStyle="None"` with `AllowsTransparency="True"` frameless window
- Pill-shaped gradient buttons (`CornerRadius="21"`) with ambient blur glow effects
- Per-status colored icon badges using `StatusColorConverter` (alpha-tinted backgrounds)
- 5 MVVM-style `IValueConverter` implementations:
  - `StatusColorConverter` — Color/Brush from status, supports `bg` (alpha tint) and `fg` (solid) params
  - `StatusIconConverter` — Unicode icons per status (✓ / ✕ / ⚠ / ⚡ / ⟳)
  - `StatusTextConverter` — PASS / FAIL / WARN / FIXED / SCANNING labels
  - `BoolToVisConverter` — `Visibility` from non-empty string
  - `FixButtonVisConverter` — shows fix button only when `CanFix=true`
- `ObservableCollection<NetworkIssue>` bound to `ItemsControl`
- Manual `RefreshIssue()` — remove/re-insert hack to force `ItemsControl` re-render

### Build Output
- Target: `net48` (no bundled runtime)
- Output: single `NetFixer.exe` at **~64 KB**
- Requires: Windows 10/11 with .NET Framework 4.8 (pre-installed on all modern Windows)

---

## 🚀 Getting Started

### Download
👉 **[Download NetFixer.exe (64 KB) — v1.0.0](https://github.com/D13056/NetFixer/releases/download/v1.0.0/NetFixer.exe)**

No installation needed. Just download and run as Administrator.

### Build from Source
```powershell
git clone https://github.com/YOUR_USERNAME/NetFixer.git
cd NetFixer
dotnet build -c Release
# Output: bin\Release\net48\NetFixer.exe  (~64 KB)
```

**Requirements:**
- [.NET 8 SDK](https://dotnet.microsoft.com/download) (to build — not needed to run)
- Windows 10/11 x64
- Administrator privileges at runtime

### Run
```powershell
# Right-click → Run as administrator, OR:
Start-Process "NetFixer.exe" -Verb RunAs
```

---

## 🔧 How It Works

1. Click **Scan Network** — all 10 checks run sequentially, results stream in as cards
2. Each card shows: status badge (✓/✕/⚠), title, description, and technical detail
3. Issues with available fixes show an **⚡ Auto-Fix This Issue** button
4. Click **Fix All Issues** to apply every available fix in one pass
5. Re-run scan to verify everything is now passing

---

## 📋 Requirements

| Requirement | Details |
|-------------|---------|
| OS | Windows 10 / Windows 11 |
| Architecture | x64 |
| Runtime | .NET Framework 4.8 (pre-installed) |
| Privileges | Administrator (auto-requested via UAC) |
| Dependencies | None |

---

## 🐛 Known Issues & Notes

- **Avast check** only triggers if Avast is actively running AND has produced DNS timeout events in the **last 24 hours** (not historical events)
- **Wi-Fi Direct** fix disables the adapter at OS level — re-enable via Device Manager if needed
- **Winsock reset** and **full network stack reset** require a system reboot to fully take effect
- Interface metric fix is permanent (sets `AutomaticMetric Disabled`) and survives reboots

---

## 📁 Project Structure

```
NetFixer/
├── app.manifest
├── NetFixer.csproj
├── App.xaml
├── App.xaml.cs
├── MainWindow.xaml
├── MainWindow.xaml.cs
├── NetworkDiagnostics.cs
└── screenshots/
    ├── main_window.png
    └── after_scan.png
```

---

## 🏷️ Keywords & Tags

`windows` `network-diagnostic` `wifi-fix` `network-repair` `wpf` `csharp` `dotnet` `dotnet-framework`  
`portable` `single-exe` `dns-fix` `winsock` `dhcp` `proxy-settings` `wifi-power-saving`  
`avast` `interface-metric` `wifi-direct` `network-troubleshooting` `windows-utility` `admin-tool`  
`dark-theme` `wpf-ui` `network-monitor` `system-utility` `windows-10` `windows-11`

`#NetFixer` `#WindowsNetwork` `#WiFiFix` `#NetworkDiagnostic` `#CSharp` `#WPF` `#DotNet`  
`#WindowsUtility` `#DNS` `#Winsock` `#DHCP` `#WiFiTroubleshooting` `#AdminTool` `#PortableApp`

---

## 📄 License

MIT License — free to use, modify, and distribute.

---

<p align="center">Built with ❤️ using C# WPF · .NET Framework 4.8 · Windows</p>
