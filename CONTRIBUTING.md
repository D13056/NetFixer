# Contributing to NetFixer

Thank you for considering contributing to NetFixer! Every contribution helps make Windows network troubleshooting easier for millions of users.

## 🐛 Reporting Bugs

Use the [Bug Report](../../issues/new?template=bug_report.md) issue template. Include:
- Windows version (Win 10/11, build number)
- What you expected vs what happened
- Screenshot of the issue card if relevant

## 💡 Suggesting New Diagnostic Checks

Open a [Feature Request](../../issues/new?template=feature_request.md). Good candidates:
- IPv6 connectivity validation
- MTU / packet fragmentation detection
- VPN adapter conflict detection
- Windows Firewall rules inspection
- Network driver version checking
- Network adapter duplex mismatch

## 🛠 Pull Requests

1. Fork the repository
2. Create a branch: `git checkout -b feature/my-new-check`
3. Make your changes in `NetworkDiagnostics.cs` — follow the existing `Check*()` pattern
4. Test on both Windows 10 and Windows 11
5. Submit a PR with a clear description

## 📐 Code Style

- Each check is a standalone `private static async Task<NetworkIssue> Check*()` method
- Use `RunPowerShell(string cmd)` helper for PowerShell-based checks
- Always check for `null` / empty output before parsing
- Log messages go in `issue.Detail` — be specific and technical

## 🌍 Translations

If you'd like to translate the UI strings, open an issue and we'll discuss the best approach.
