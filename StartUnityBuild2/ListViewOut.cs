using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using NLog;

namespace StartUnityBuild;

public class ListViewOut
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    private readonly ListView _dataLine;

    public ListViewOut(Control parent, ListView infoLine, ListView dataLine)
    {
        infoLine.Hide();
        _dataLine = SetLayout(dataLine);
        ClearLines();
        var infoWidth = infoLine.Size.Width;
        var screenWidth = Screen.FromControl(parent).WorkingArea.Width;
        dataLine.Columns.Add("info", infoWidth, HorizontalAlignment.Left);
        dataLine.Columns.Add("data", screenWidth - infoWidth, HorizontalAlignment.Left);
        return;

        ListView SetLayout(ListView listView)
        {
            // Reduce Graphics Flicker with Double Buffering for Forms and Controls
            // https://learn.microsoft.com/en-us/dotnet/desktop/winforms/advanced/how-to-reduce-graphics-flicker-with-double-buffering-for-forms-and-controls?view=netframeworkdesktop-4.8
            listView.DoubleBuffered(true);
            listView.Font = new Font("Cascadia Mono", 10);
            listView.FullRowSelect = true;
            listView.MultiSelect = false;
            listView.View = View.Details;
            return listView;
        }
    }

    public void ClearLines()
    {
        _dataLine.BeginUpdate();
        _dataLine.Items.Clear();
        _dataLine.EndUpdate();
    }

    /// <summary>
    /// Adds line to list views.
    /// </summary>
    /// <param name="info">short line info</param>
    /// <param name="data">data line output</param>
    /// <param name="infoColor">info color</param>
    /// <param name="dataColor">data color</param>
    public void AddLine(string info, string data, Color infoColor, Color dataColor)
    {
        Logger.Trace($"{info}: {data}");
        var item = new ListViewItem([info, data]);
        item.SubItems[0].ForeColor = infoColor;
        item.SubItems[1].ForeColor = dataColor;
        item.UseItemStyleForSubItems = false;
        _dataLine.BeginUpdate();
        _dataLine.Items.Add(item);
        _dataLine.EndUpdate();
        _dataLine.EnsureVisible(_dataLine.Items.Count - 1);
    }
}

public static class Extensions
{
    [SuppressMessage("ReSharper", "NullableWarningSuppressionIsUsed")]
    public static void DoubleBuffered(this Control control, bool enabled)
    {
        var propertyInfo = control.GetType()
            .GetProperty("DoubleBuffered", BindingFlags.Instance | BindingFlags.NonPublic);
        propertyInfo!.SetValue(control, enabled, null);
    }
}
