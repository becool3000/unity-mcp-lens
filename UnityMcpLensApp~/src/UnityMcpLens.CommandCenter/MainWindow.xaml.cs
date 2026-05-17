using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace UnityMcpLens.CommandCenter;

public partial class MainWindow : Window
{
    readonly CommandLineOptions m_Options;
    readonly ProjectSettingsStore m_SettingsStore;
    readonly InstallerService m_InstallerService;
    readonly StatusScanner m_StatusScanner;
    readonly TelemetryScanner m_TelemetryScanner;
    readonly DispatcherTimer m_Timer;
    TelemetrySnapshot? m_LastTelemetrySnapshot;

    public MainWindow()
    {
        InitializeComponent();

        m_Options = CommandLineOptions.Parse(Environment.GetCommandLineArgs().Skip(1).ToArray());
        m_SettingsStore = new ProjectSettingsStore(m_Options.ProjectRoot);
        m_InstallerService = new InstallerService(m_Options.PackageRoot);
        m_StatusScanner = new StatusScanner(m_Options.StatusDirectory, m_Options.ProjectRoot);
        m_TelemetryScanner = new TelemetryScanner(m_Options.ProjectRoot);

        ContextText.Text = m_Options.ProjectRoot;
        m_Timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        m_Timer.Tick += (_, _) => RefreshStatusOnly();

        Loaded += (_, _) =>
        {
            LoadEverything();
            m_Timer.Start();
        };
    }

    void RefreshButton_Click(object sender, RoutedEventArgs e) => LoadEverything();

    void RefreshTelemetryButton_Click(object sender, RoutedEventArgs e) => _ = RefreshTelemetryAsync();

    void RefreshServerButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            StatusText.Text = "Refreshing installed server...";
            string message = m_InstallerService.RefreshServer();
            StatusText.Text = message;
            LoadInstallSnapshot();
        }
        catch (Exception ex)
        {
            StatusText.Text = "Server refresh failed.";
            MessageBox.Show(this, ex.Message, "Server Refresh Failed", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    void SaveSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            LensSettingsSnapshot settings = ReadSettingsFromUi();
            m_SettingsStore.Save(settings);
            StatusText.Text = "Settings saved.";
            LoadSettings();
        }
        catch (Exception ex)
        {
            StatusText.Text = "Settings save failed.";
            MessageBox.Show(this, ex.Message, "Settings Save Failed", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    void ReloadSettingsButton_Click(object sender, RoutedEventArgs e) => LoadSettings();

    void OpenServerFolder_Click(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(m_InstallerService.InstalledDirectory);
        OpenPath(m_InstallerService.InstalledDirectory);
    }

    void OpenStatusFolder_Click(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(m_Options.StatusDirectory);
        OpenPath(m_Options.StatusDirectory);
    }

    void OpenTelemetryFileButton_Click(object sender, RoutedEventArgs e)
    {
        string path = m_TelemetryScanner.StatsPath;
        if (File.Exists(path))
        {
            OpenPath(path);
            return;
        }

        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
            OpenPath(directory);
        }
    }

    void CopyTelemetrySummaryButton_Click(object sender, RoutedEventArgs e)
    {
        if (m_LastTelemetrySnapshot == null)
        {
            StatusText.Text = "No telemetry summary loaded.";
            return;
        }

        Clipboard.SetText(m_LastTelemetrySnapshot.BuildClipboardSummary());
        StatusText.Text = "Telemetry summary copied.";
    }

    void LoadEverything()
    {
        LoadInstallSnapshot();
        LoadSettings();
        RefreshStatusOnly();
        LoadPaths();
        _ = RefreshTelemetryAsync();
    }

    void RefreshStatusOnly()
    {
        IReadOnlyList<BridgeStatusItem> statuses = m_StatusScanner.Scan();
        StatusList.ItemsSource = statuses;

        BridgeStatusItem? best = statuses.FirstOrDefault();
        if (best == null)
        {
            BridgeSummaryText.Text = "No bridge or editor health files found.";
        }
        else
        {
            BridgeSummaryText.Text = $"{best.Status} / {best.BasicHealth} / {best.CommandHealth}";
        }

        UnityPidText.Text = m_Options.UnityPid > 0
            ? $"Unity PID {m_Options.UnityPid}: {(IsProcessAlive(m_Options.UnityPid) ? "alive" : "not running")}"
            : "Unity PID was not provided.";
        UnityPidHint.Text = "This check uses the operating system process table, not a Unity API call.";
    }

    void LoadInstallSnapshot()
    {
        ServerInstallSnapshot snapshot = m_InstallerService.GetSnapshot();
        ServerVersionText.Text = snapshot.InstalledServerExists
            ? $"Installed {snapshot.InstalledVersion}; bundled {snapshot.BundledVersion}"
            : $"Server missing; bundled {snapshot.BundledVersion}";
        CommandCenterUpdateText.Text = snapshot.CommandCenterUpdateAvailable
            ? "Command Center update available. Relaunch from Unity to update this app."
            : "Command Center binary is current for this package source.";
    }

    void LoadSettings()
    {
        LensSettingsSnapshot settings = m_SettingsStore.Load();
        BridgeEnabledCheckBox.IsChecked = settings.BridgeEnabled;
        BatchModeEnabledCheckBox.IsChecked = settings.BatchModeEnabled;
        AutoApproveBatchCheckBox.IsChecked = settings.AutoApproveInBatchMode;
        LegacyRelayCheckBox.IsChecked = settings.LegacyRelayEnabled;
        MaxDirectConnectionsTextBox.Text = settings.MaxDirectConnections.ToString();
        EnabledOverridesTextBox.Text = string.Join(Environment.NewLine, settings.EnabledToolOverrides);
        DisabledOverridesTextBox.Text = string.Join(Environment.NewLine, settings.DisabledToolOverrides);
        SelectValidationLevel(settings.ValidationLevel);
        StatusText.Text = "Settings loaded.";
    }

    void LoadPaths()
    {
        ProjectRootText.Text = $"Project root: {m_Options.ProjectRoot}";
        PackageRootText.Text = $"Package root: {m_Options.PackageRoot}";
        StatusDirectoryText.Text = $"Status directory: {m_Options.StatusDirectory}";
        SettingsPathText.Text = $"Settings file: {m_SettingsStore.SettingsPath}";
        InstalledServerPathText.Text = $"Installed server: {m_InstallerService.InstalledServerPath}";
    }

    async Task RefreshTelemetryAsync()
    {
        try
        {
            StatusText.Text = "Loading telemetry...";
            TelemetrySnapshot snapshot = await Task.Run(() => m_TelemetryScanner.Scan()).ConfigureAwait(true);
            RenderTelemetry(snapshot);
            StatusText.Text = snapshot.StatusMessage;
        }
        catch (Exception ex)
        {
            StatusText.Text = "Telemetry load failed.";
            RenderTelemetry(new TelemetrySnapshot
            {
                StatsPath = m_TelemetryScanner.StatsPath,
                Exists = File.Exists(m_TelemetryScanner.StatsPath),
                StatusMessage = $"Telemetry load failed: {ex.Message}"
            });
        }
    }

    void RenderTelemetry(TelemetrySnapshot snapshot)
    {
        m_LastTelemetrySnapshot = snapshot;

        TelemetryStatusText.Text = snapshot.StatusMessage;
        TelemetryPathText.Text = snapshot.StatsPath;
        TelemetryFileInfoText.Text = snapshot.Exists
            ? $"Size {snapshot.FileSizeDisplay}; last write {snapshot.LastWriteDisplay}; scope {snapshot.Scope}; next line {snapshot.NextLine:N0}"
            : "No telemetry file exists for this project yet.";
        TelemetryPayloadSummaryText.Text = snapshot.PayloadSummaryDisplay;
        TelemetryRowsText.Text = snapshot.RowSummaryDisplay;
        TelemetryDateRangeText.Text = $"Date range: {snapshot.DateRangeDisplay}";
        TelemetryBridgeSummaryText.Text = snapshot.BridgeSummaryDisplay;
        TelemetrySnapshotSummaryText.Text = snapshot.SnapshotSummaryDisplay;
        TelemetryTopSavingsList.ItemsSource = snapshot.TopSavings;
        TelemetryTopStagesList.ItemsSource = snapshot.TopStages;
        TelemetryTopNamesList.ItemsSource = snapshot.TopNames;
        TelemetrySlowOperationsList.ItemsSource = snapshot.SlowOperations;
        TelemetryFailureClassesList.ItemsSource = snapshot.FailureClasses;
        TelemetryUnmatchedRequestsList.ItemsSource = snapshot.UnmatchedRequests;
    }

    LensSettingsSnapshot ReadSettingsFromUi()
    {
        if (!int.TryParse(MaxDirectConnectionsTextBox.Text, out int maxDirectConnections))
            maxDirectConnections = -1;

        return new LensSettingsSnapshot
        {
            BridgeEnabled = BridgeEnabledCheckBox.IsChecked == true,
            BatchModeEnabled = BatchModeEnabledCheckBox.IsChecked == true,
            AutoApproveInBatchMode = AutoApproveBatchCheckBox.IsChecked == true,
            LegacyRelayEnabled = LegacyRelayCheckBox.IsChecked == true,
            ValidationLevel = GetSelectedValidationLevel(),
            MaxDirectConnections = maxDirectConnections,
            EnabledToolOverrides = ParseToolOverrides(EnabledOverridesTextBox.Text),
            DisabledToolOverrides = ParseToolOverrides(DisabledOverridesTextBox.Text)
        };
    }

    void SelectValidationLevel(string validationLevel)
    {
        foreach (ComboBoxItem item in ValidationLevelComboBox.Items)
        {
            if (string.Equals(item.Content?.ToString(), validationLevel, StringComparison.OrdinalIgnoreCase))
            {
                ValidationLevelComboBox.SelectedItem = item;
                return;
            }
        }

        ValidationLevelComboBox.SelectedIndex = 1;
    }

    string GetSelectedValidationLevel()
    {
        return (ValidationLevelComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "standard";
    }

    static List<string> ParseToolOverrides(string text)
    {
        return text
            .Split([',', '\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    static bool IsProcessAlive(int pid)
    {
        if (pid <= 0)
            return false;

        try
        {
            using Process process = Process.GetProcessById(pid);
            return !process.HasExited;
        }
        catch
        {
            return false;
        }
    }

    static void OpenPath(string path)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = path,
            UseShellExecute = true
        });
    }
}
