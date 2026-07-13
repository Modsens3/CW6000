using Microsoft.Win32;
using Microsoft.VisualBasic.FileIO;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

namespace CW6000.BulkManager;

public partial class MainWindow : Window
{
    private readonly ObservableCollection<MediaPreset> _presets = new();
    private CancellationTokenSource? _cts;

    public MainWindow()
    {
        InitializeComponent();
        PresetsGrid.ItemsSource = _presets;
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*" };
        if (dialog.ShowDialog() != true) return;
        CsvPathText.Text = dialog.FileName;
        LoadCsv(dialog.FileName);
    }

    private void LoadCsv(string path)
    {
        _presets.Clear();
        using var parser = new TextFieldParser(path);
        parser.TextFieldType = FieldType.Delimited;
        parser.SetDelimiters(",");
        parser.HasFieldsEnclosedInQuotes = true;

        var headers = parser.ReadFields() ?? throw new InvalidDataException("CSV header missing.");
        var map = headers.Select((h, i) => new { Name = h.Trim(), Index = i })
            .ToDictionary(x => x.Name, x => x.Index, StringComparer.OrdinalIgnoreCase);

        int row = 0;
        while (!parser.EndOfData)
        {
            var fields = parser.ReadFields();
            if (fields is null || fields.Length == 0) continue;
            row++;

            string name = Read(fields, map, "Preset Name", "Name", "Media Name");
            double widthMm = ReadDouble(fields, map, "Width (mm)", "Width mm", "Width");
            double lengthMm = ReadDouble(fields, map, "Height (mm)", "Length (mm)", "Height mm", "Length mm", "Height");

            if (string.IsNullOrWhiteSpace(name)) name = $"Preset_{row}";

            bool rotated = widthMm > 112.0 && lengthMm <= 112.0;
            if (rotated) (widthMm, lengthMm) = (lengthMm, widthMm);

            var preset = new MediaPreset
            {
                Index = row,
                Name = name,
                WidthMm = Math.Round(widthMm, 2),
                LengthMm = Math.Round(lengthMm, 2),
                WidthIn = Math.Round(widthMm / 25.4, 2),
                LengthIn = Math.Round(lengthMm / 25.4, 2),
                Rotated = rotated,
                Status = Validate(widthMm, lengthMm)
            };
            _presets.Add(preset);
        }

        StatusText.Text = $"Loaded {_presets.Count} presets.";
        Log($"Loaded {_presets.Count} rows from {Path.GetFileName(path)}");
    }

    private static string Validate(double widthMm, double lengthMm)
    {
        double w = widthMm / 25.4;
        double l = lengthMm / 25.4;
        return w is < 0.84 or > 4.41 || l is < 0.31 or > 24.00 ? "OUT OF RANGE" : "Ready";
    }

    private static string Read(string[] fields, Dictionary<string, int> map, params string[] names)
    {
        foreach (var name in names)
            if (map.TryGetValue(name, out int i) && i < fields.Length) return fields[i].Trim();
        return string.Empty;
    }

    private static double ReadDouble(string[] fields, Dictionary<string, int> map, params string[] names)
    {
        string value = Read(fields, map, names).Replace(',', '.');
        if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var result))
            throw new InvalidDataException($"Invalid or missing numeric column: {string.Join(" / ", names)}");
        return result;
    }

    private async void Test_Click(object sender, RoutedEventArgs e)
    {
        var test = new MediaPreset
        {
            Index = 1,
            Name = $"KAPA_TEST_{DateTime.Now:HHmmss}",
            WidthMm = 40,
            LengthMm = 40,
            WidthIn = 1.57,
            LengthIn = 1.57,
            Status = "Ready"
        };
        await RunAsync(new[] { test });
    }

    private async void CreateAll_Click(object sender, RoutedEventArgs e)
    {
        if (_presets.Count == 0)
        {
            MessageBox.Show("Load a CSV first.");
            return;
        }
        await RunAsync(_presets.Where(p => p.Status != "OUT OF RANGE").ToArray());
    }

    private void Stop_Click(object sender, RoutedEventArgs e) => _cts?.Cancel();

    private async Task RunAsync(IReadOnlyList<MediaPreset> items)
    {
        if (items.Count == 0) return;
        if (!TryNumber(GapText.Text, out double gap) || !TryNumber(SideGapText.Text, out double sideGap))
        {
            MessageBox.Show("Invalid Gap value.");
            return;
        }

        var mediaForm = ((ComboBoxItem)MediaFormCombo.SelectedItem).Content.ToString()!;
        var coating = ((ComboBoxItem)CoatingCombo.SelectedItem).Content.ToString()!;
        var quality = ((ComboBoxItem)QualityCombo.SelectedItem).Content.ToString()!;

        _cts = new CancellationTokenSource();
        Progress.Value = 0;
        int success = 0;

        try
        {
            for (int i = 0; i < items.Count; i++)
            {
                _cts.Token.ThrowIfCancellationRequested();
                var preset = items[i];
                StatusText.Text = $"Creating {i + 1}/{items.Count}: {preset.Name}";
                preset.Status = "Creating";
                PresetsGrid.Items.Refresh();

                try
                {
                    CreatePreset(preset, gap, sideGap, mediaForm, coating, quality);
                    preset.Status = "Created";
                    success++;
                    Log($"OK  {preset.Name}  {preset.WidthIn:0.00} x {preset.LengthIn:0.00} in");
                }
                catch (Exception ex)
                {
                    preset.Status = "FAILED";
                    Log($"FAIL {preset.Name}: {ex.Message}");
                    var answer = MessageBox.Show($"Failed: {preset.Name}\n\n{ex.Message}\n\nContinue?", "CW6000", MessageBoxButton.YesNo, MessageBoxImage.Error);
                    if (answer == MessageBoxResult.No) break;
                }

                PresetsGrid.Items.Refresh();
                Progress.Value = (i + 1) * 100.0 / items.Count;
                await Task.Delay(400, _cts.Token);
            }
        }
        catch (OperationCanceledException)
        {
            Log("Stopped by user.");
        }
        finally
        {
            StatusText.Text = $"Finished. Created {success}/{items.Count}.";
            SaveLog();
            _cts.Dispose();
            _cts = null;
        }
    }

    private static bool TryNumber(string text, out double value) =>
        double.TryParse(text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out value);

    private static void CreatePreset(MediaPreset p, double gap, double sideGap, string mediaForm, string coating, string quality)
    {
        var mediaWindow = FindElement(AutomationElement.RootElement, ControlType.Window, null, "Media Definition", 5)
            ?? throw new InvalidOperationException("Media Definition window not found.");

        var newButton = FindById(mediaWindow, "4502")
            ?? throw new InvalidOperationException("New button (4502) not found.");
        Invoke(newButton);
        Thread.Sleep(500);

        var newWindow = FindElement(mediaWindow, ControlType.Window, null, "New", 5)
            ?? FindElement(AutomationElement.RootElement, ControlType.Window, null, "New", 5)
            ?? throw new InvalidOperationException("New dialog not found.");

        SetValue(RequireById(newWindow, "4513"), p.Name);
        SetRange(RequireById(newWindow, "4516"), p.WidthIn);
        SetRange(RequireById(newWindow, "4519"), p.LengthIn);
        SetRange(RequireById(newWindow, "4571"), gap);
        SetRange(RequireById(newWindow, "4522"), sideGap);

        SelectCombo(newWindow, "4527", mediaForm);
        SelectCombo(newWindow, "4529", coating);
        SelectCombo(newWindow, "4531", quality);

        Invoke(RequireById(newWindow, "4542"));
        Thread.Sleep(700);

        try
        {
            _ = newWindow.Current.Name;
            throw new InvalidOperationException("New dialog remained open; Epson rejected a value or the name already exists.");
        }
        catch (ElementNotAvailableException)
        {
            // Success: dialog closed.
        }
    }

    private static AutomationElement RequireById(AutomationElement root, string id) =>
        FindById(root, id) ?? throw new InvalidOperationException($"Control {id} not found.");

    private static AutomationElement? FindById(AutomationElement root, string id)
    {
        var condition = new PropertyCondition(AutomationElement.AutomationIdProperty, id);
        return root.FindFirst(TreeScope.Descendants, condition);
    }

    private static AutomationElement? FindElement(AutomationElement root, ControlType type, string? automationId, string? name, int timeoutSeconds)
    {
        var end = DateTime.UtcNow.AddSeconds(timeoutSeconds);
        do
        {
            var conditions = new List<System.Windows.Automation.Condition>
            {
                new PropertyCondition(AutomationElement.ControlTypeProperty, type)
            };
            if (automationId is not null) conditions.Add(new PropertyCondition(AutomationElement.AutomationIdProperty, automationId));
            if (name is not null) conditions.Add(new PropertyCondition(AutomationElement.NameProperty, name));
            var condition = conditions.Count == 1 ? conditions[0] : new AndCondition(conditions.ToArray());
            var found = root.FindFirst(TreeScope.Descendants | TreeScope.Children, condition);
            if (found is not null) return found;
            Thread.Sleep(150);
        } while (DateTime.UtcNow < end);
        return null;
    }

    private static void SetValue(AutomationElement element, string value)
    {
        if (!element.TryGetCurrentPattern(ValuePattern.Pattern, out object? pattern))
            throw new InvalidOperationException($"ValuePattern unavailable for {element.Current.AutomationId}.");
        ((ValuePattern)pattern).SetValue(value);
    }

    private static void SetRange(AutomationElement element, double value)
    {
        if (!element.TryGetCurrentPattern(RangeValuePattern.Pattern, out object? pattern))
            throw new InvalidOperationException($"RangeValuePattern unavailable for {element.Current.AutomationId}.");
        var range = (RangeValuePattern)pattern;
        value = Math.Clamp(value, range.Current.Minimum, range.Current.Maximum);
        range.SetValue(value);
    }

    private static void Invoke(AutomationElement element)
    {
        if (!element.TryGetCurrentPattern(InvokePattern.Pattern, out object? pattern))
            throw new InvalidOperationException($"InvokePattern unavailable for {element.Current.Name}.");
        ((InvokePattern)pattern).Invoke();
    }

    private static void SelectCombo(AutomationElement root, string comboId, string itemName)
    {
        var combo = RequireById(root, comboId);
        if (combo.TryGetCurrentPattern(ExpandCollapsePattern.Pattern, out object? expandPattern))
            ((ExpandCollapsePattern)expandPattern).Expand();
        Thread.Sleep(180);

        var itemCondition = new AndCondition(
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ListItem),
            new PropertyCondition(AutomationElement.NameProperty, itemName));
        var item = root.FindFirst(TreeScope.Descendants, itemCondition)
            ?? AutomationElement.RootElement.FindFirst(TreeScope.Descendants, itemCondition)
            ?? throw new InvalidOperationException($"Combo option not found: {itemName}");

        if (!item.TryGetCurrentPattern(SelectionItemPattern.Pattern, out object? selectionPattern))
            throw new InvalidOperationException($"SelectionItemPattern unavailable: {itemName}");
        ((SelectionItemPattern)selectionPattern).Select();
        Thread.Sleep(120);
    }

    private void Log(string message)
    {
        LogText.AppendText($"{DateTime.Now:HH:mm:ss}  {message}{Environment.NewLine}");
        LogText.ScrollToEnd();
    }

    private void SaveLog()
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, $"CW6000-log-{DateTime.Now:yyyyMMdd-HHmmss}.txt");
            File.WriteAllText(path, LogText.Text);
            Log($"Log saved: {path}");
        }
        catch { }
    }
}

public sealed class MediaPreset
{
    public int Index { get; set; }
    public string Name { get; set; } = string.Empty;
    public double WidthMm { get; set; }
    public double LengthMm { get; set; }
    public double WidthIn { get; set; }
    public double LengthIn { get; set; }
    public bool Rotated { get; set; }
    public string Status { get; set; } = string.Empty;
}
