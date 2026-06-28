using System.Reflection;
using System.Windows.Forms.DataVisualization.Charting;

namespace PreySense.UI
{
    public class RChart : Chart
    {
        // Rare GDI+ ObjectBusy races in DrawException (issue #5468) propagate out of
        // base.OnPaint and pop the .NET unhandled-exception modal. Base also leaves
        // `disableInvalidates` stuck true on throw, which would silently kill all
        // future Invalidate() calls. Swallow + reset so the chart self-heals.
        private static readonly FieldInfo? _disableInvalidates =
            typeof(Chart).GetField("disableInvalidates", BindingFlags.Instance | BindingFlags.NonPublic);

        protected override void OnPaint(PaintEventArgs e)
        {
            try
            {
                base.OnPaint(e);
            }
            catch (Exception)
            {
                _disableInvalidates?.SetValue(this, false);
            }
        }
    }
}
