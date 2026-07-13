using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;

namespace CW6000.Inspector;

public partial class MainWindow : Window
{
    private readonly ObservableCollection<UiNode> _nodes = new();

    public MainWindow()
    {
        InitializeComponent();
        ElementsGrid.ItemsSource = _nodes;
    }

    private void Scan_Click(object sender, RoutedEventArgs e)
    {
        _nodes.Clear();
        StatusText.Text = "Scanning...";

        foreach (AutomationElement window in AutomationElement.RootElement.FindAll(
                     TreeScope.Children,
                     System.Windows.Automation.Condition.TrueCondition))
        {
            var title = Safe(() => window.Current.Name);
            if (!title.Contains("EPSON", StringComparison.OrdinalIgnoreCase) &&
                !title.Contains("Media Definition", StringComparison.OrdinalIgnoreCase) &&
                !title.Equals("New", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            Walk(window, 0);
        }

        StatusText.Text = $"Found {_nodes.Count} UI elements.";
    }

    private void Walk(AutomationElement element, int depth)
    {
        _nodes.Add(UiNode.From(element, depth));

        AutomationElementCollection children;
        try
        {
            children = element.FindAll(
                TreeScope.Children,
                System.Windows.Automation.Condition.TrueCondition);
        }
        catch
        {
            return;
        }

        foreach (AutomationElement child in children)
        {
            Walk(child, depth + 1);
        }
    }

    private void Export_Click(object sender, RoutedEventArgs e)
    {
        if (_nodes.Count == 0)
        {
            MessageBox.Show("Run Scan first.", "CW6000 Inspector", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialog = new SaveFileDialog
        {
            FileName = $"cw6000-ui-{DateTime.Now:yyyyMMdd-HHmmss}.json",
            Filter = "JSON files (*.json)|*.json"
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var json = JsonSerializer.Serialize(_nodes, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(dialog.FileName, json);
        StatusText.Text = $"Exported: {dialog.FileName}";
    }

    private static string Safe(Func<string> getter)
    {
        try { return getter() ?? string.Empty; }
        catch { return string.Empty; }
    }
}

public sealed record UiNode(
    int Depth,
    string Name,
    string AutomationId,
    string ControlType,
    string ClassName,
    string Bounds,
    string Patterns)
{
    public static UiNode From(AutomationElement element, int depth)
    {
        static string Safe(Func<string> getter)
        {
            try { return getter() ?? string.Empty; }
            catch { return string.Empty; }
        }

        string bounds;
        try
        {
            var r = element.Current.BoundingRectangle;
            bounds = $"{r.Left:0},{r.Top:0},{r.Width:0},{r.Height:0}";
        }
        catch
        {
            bounds = string.Empty;
        }

        string patterns;
        try
        {
            patterns = string.Join(", ", element.GetSupportedPatterns().Select(p => p.ProgrammaticName));
        }
        catch
        {
            patterns = string.Empty;
        }

        return new UiNode(
            depth,
            Safe(() => element.Current.Name),
            Safe(() => element.Current.AutomationId),
            Safe(() => element.Current.ControlType.ProgrammaticName.Replace("ControlType.", string.Empty)),
            Safe(() => element.Current.ClassName),
            bounds,
            patterns);
    }
}
