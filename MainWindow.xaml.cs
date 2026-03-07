using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;

namespace NetFixer;

public partial class MainWindow : Window
{
    private readonly ObservableCollection<NetworkIssue> _issues = new();
    private bool _scanning = false;
    private int _fixedCount = 0;

    public MainWindow()
    {
        InitializeComponent();
        IssueList.ItemsSource = _issues;
        Log("NetFixer started — running as Administrator.");
    }

    // ── Dragging ──────────────────────────────────────────────────────────────
    private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed) DragMove();
    }
    private void BtnMinimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();
    private void BtnClearLog_Click(object sender, RoutedEventArgs e) { TxtLog.Text = ""; Log("Log cleared."); }

    // ── Scan ──────────────────────────────────────────────────────────────────
    private async void BtnScan_Click(object sender, RoutedEventArgs e)
    {
        if (_scanning) return;
        _scanning = true;
        _fixedCount = 0;
        BtnScan.IsEnabled = false;
        BtnFixAll.IsEnabled = false;
        _issues.Clear();
        UpdateSummary();
        ProgressPanel.Visibility = Visibility.Visible;
        TxtStatusLine.Text = "Scanning your network...";
        Log("─────────────────────────────────────");
        Log($"Starting full scan at {DateTime.Now:HH:mm:ss}");

        int done = 0;
        const int total = 10;

        await NetworkDiagnostics.RunAllChecks(async issue =>
        {
            done++;
            _issues.Add(issue);
            UpdateProgress(done, total);
            UpdateSummary();
            Log($"[{issue.Status,-8}] {issue.Title}");
            await Task.Delay(80); // micro-delay for smooth feel
        });

        ProgressPanel.Visibility = Visibility.Collapsed;
        int fails = _issues.Count(i => i.Status == IssueStatus.Fail);
        int warns = _issues.Count(i => i.Status == IssueStatus.Warning);
        TxtStatusLine.Text = fails == 0 && warns == 0
            ? "✓ All checks passed — your network is healthy."
            : $"{fails} issue(s) found, {warns} warning(s). Use Fix buttons to repair.";
        Log($"Scan complete — {fails} fail(s), {warns} warning(s).");
        BtnFixAll.IsEnabled = fails > 0 || warns > 0;
        BtnScan.IsEnabled = true;
        _scanning = false;
    }

    // ── Fix All ───────────────────────────────────────────────────────────────
    private async void BtnFixAll_Click(object sender, RoutedEventArgs e)
    {
        BtnFixAll.IsEnabled = false;
        BtnScan.IsEnabled = false;
        Log("─────────────────────────────────────");
        Log($"Starting Fix All at {DateTime.Now:HH:mm:ss}");

        var fixable = _issues.Where(i => i.CanFix && i.FixAction != null &&
            (i.Status == IssueStatus.Fail || i.Status == IssueStatus.Warning)).ToList();

        foreach (var issue in fixable)
        {
            Log($"Fixing: {issue.Title}...");
            issue.Status = IssueStatus.Scanning;
            RefreshIssue(issue);
            try
            {
                var result = await issue.FixAction!();
                issue.Status = IssueStatus.Fixed;
                issue.CanFix = false;
                _fixedCount++;
                Log($"  ✓ Fixed: {issue.Title}\n  {result?.Split('\n').FirstOrDefault()}");
            }
            catch (Exception ex)
            {
                issue.Status = IssueStatus.Fail;
                Log($"  ✕ Failed to fix {issue.Title}: {ex.Message}");
            }
            RefreshIssue(issue);
            UpdateSummary();
            await Task.Delay(200);
        }

        Log($"Fix All complete. {_fixedCount} issue(s) fixed.");
        BtnScan.IsEnabled = true;
        TxtStatusLine.Text = $"{_fixedCount} issues fixed. Run Scan again to verify.";
    }

    // ── Fix One ───────────────────────────────────────────────────────────────
    private async void BtnFixOne_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn) return;
        if (btn.Tag is not NetworkIssue issue || issue.FixAction == null) return;

        btn.IsEnabled = false;
        Log($"Fixing: {issue.Title}...");
        var prevStatus = issue.Status;
        issue.Status = IssueStatus.Scanning;
        RefreshIssue(issue);

        try
        {
            var result = await issue.FixAction();
            issue.Status = IssueStatus.Fixed;
            issue.CanFix = false;
            _fixedCount++;
            var logLines = result?.Split('\n').Take(3);
            Log($"  ✓ Fixed: {issue.Title}");
            if (logLines != null) foreach (var l in logLines) if (!string.IsNullOrWhiteSpace(l)) Log($"    {l.Trim()}");
        }
        catch (Exception ex)
        {
            issue.Status = prevStatus;
            btn.IsEnabled = true;
            Log($"  ✕ Failed to fix {issue.Title}: {ex.Message}");
        }
        RefreshIssue(issue);
        UpdateSummary();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────
    private void RefreshIssue(NetworkIssue issue)
    {
        // Force ItemsControl to refresh the item
        var idx = _issues.IndexOf(issue);
        if (idx < 0) return;
        _issues.RemoveAt(idx);
        _issues.Insert(idx, issue);
    }

    private void UpdateSummary()
    {
        TxtPass.Text  = _issues.Count(i => i.Status == IssueStatus.Pass).ToString();
        TxtFail.Text  = _issues.Count(i => i.Status == IssueStatus.Fail).ToString();
        TxtWarn.Text  = _issues.Count(i => i.Status == IssueStatus.Warning).ToString();
        TxtFixed.Text = _issues.Count(i => i.Status == IssueStatus.Fixed).ToString();
    }

    private void UpdateProgress(int done, int total)
    {
        TxtProgress.Text = $"{done} / {total}";
        double pct = (double)done / total;
        ProgressFill.Width = 140 * pct;
    }

    private void Log(string msg)
    {
        var ts = DateTime.Now.ToString("HH:mm:ss");
        TxtLog.Text += $"\n[{ts}] {msg}";
        LogScroll.ScrollToEnd();
    }
}

// ─────────────────────────────── CONVERTERS ───────────────────────────────────

public class StatusColorConverter : IValueConverter
{
    private static readonly Dictionary<IssueStatus, Color> _colors = new()
    {
        [IssueStatus.Pass]     = Color.FromRgb(34, 197, 94),
        [IssueStatus.Fail]     = Color.FromRgb(239, 68, 68),
        [IssueStatus.Warning]  = Color.FromRgb(245, 158, 11),
        [IssueStatus.Fixed]    = Color.FromRgb(0, 212, 255),
        [IssueStatus.Scanning] = Color.FromRgb(129, 140, 248),
        [IssueStatus.Unknown]  = Color.FromRgb(100, 116, 139),
    };

    public object Convert(object value, Type t, object param, CultureInfo c)
    {
        var status = value is IssueStatus s ? s : IssueStatus.Unknown;
        var color = _colors.TryGetValue(status, out var col) ? col : _colors[IssueStatus.Unknown];
        // "bg" param: used inside <SolidColorBrush Color="..."> → return Color
        // "fg" param: used as Foreground / TextBlock.Foreground → return Brush
        if (param?.ToString() == "bg")
            return Color.FromArgb(55, color.R, color.G, color.B); // semi-transparent bg tint
        return new SolidColorBrush(color);
    }
    public object ConvertBack(object v, Type t, object p, CultureInfo c) => throw new NotImplementedException();
}

public class StatusIconConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c) =>
        (IssueStatus)value switch
        {
            IssueStatus.Pass     => "✓",
            IssueStatus.Fail     => "✕",
            IssueStatus.Warning  => "⚠",
            IssueStatus.Fixed    => "⚡",
            IssueStatus.Scanning => "⟳",
            _                    => "·",
        };
    public object ConvertBack(object v, Type t, object p, CultureInfo c) => throw new NotImplementedException();
}

public class StatusTextConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c) =>
        (IssueStatus)value switch
        {
            IssueStatus.Pass     => "PASS",
            IssueStatus.Fail     => "FAIL",
            IssueStatus.Warning  => "WARN",
            IssueStatus.Fixed    => "FIXED",
            IssueStatus.Scanning => "SCANNING",
            _                    => "—",
        };
    public object ConvertBack(object v, Type t, object p, CultureInfo c) => throw new NotImplementedException();
}

public class BoolToVisConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c) =>
        value is string s && !string.IsNullOrWhiteSpace(s) ? Visibility.Visible : Visibility.Collapsed;
    public object ConvertBack(object v, Type t, object p, CultureInfo c) => throw new NotImplementedException();
}

public class FixButtonVisConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c) =>
        value is bool b && b ? Visibility.Visible : Visibility.Collapsed;
    public object ConvertBack(object v, Type t, object p, CultureInfo c) => throw new NotImplementedException();
}
