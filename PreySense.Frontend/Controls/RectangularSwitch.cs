using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace PreySense.Frontend.Controls
{
    public class RectangularSwitch : ToggleButton
    {
        static RectangularSwitch()
        {
            DefaultStyleKeyProperty.OverrideMetadata(
                typeof(RectangularSwitch),
                new FrameworkPropertyMetadata(typeof(RectangularSwitch)));
        }

        public static readonly DependencyProperty OnTextProperty =
            DependencyProperty.Register(
                nameof(OnText),
                typeof(string),
                typeof(RectangularSwitch),
                new PropertyMetadata("│"));

        public string OnText
        {
            get => (string)GetValue(OnTextProperty);
            set => SetValue(OnTextProperty, value);
        }

        public static readonly DependencyProperty OffTextProperty =
            DependencyProperty.Register(
                nameof(OffText),
                typeof(string),
                typeof(RectangularSwitch),
                new PropertyMetadata("○"));

        public string OffText
        {
            get => (string)GetValue(OffTextProperty);
            set => SetValue(OffTextProperty, value);
        }

        protected override void OnToggle()
        {
            if (!IsEnabled) return;

            // Determine if mouse was clicked on left half (Off) or right half (On)
            var pos = Mouse.GetPosition(this);
            if (ActualWidth > 0 && pos.X >= 0 && pos.X <= ActualWidth && pos.Y >= 0 && pos.Y <= ActualHeight)
            {
                bool targetState = pos.X > (ActualWidth / 2.0);
                // If user clicks currently active side, toggle it; otherwise set target state
                IsChecked = (IsChecked == targetState) ? !targetState : targetState;
            }
            else
            {
                // Keyboard (Space/Enter) or programmatic toggle
                IsChecked = !IsChecked;
            }
        }
    }
}
