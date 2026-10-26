using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace PreySense.Frontend.Controls
{
    public partial class SettingValueRow : UserControl
    {
        private bool _syncingText;

        public SettingValueRow()
        {
            InitializeComponent();
            InnerValueBox.LostFocus += (_, _) => CommitText(notify: true);
            InnerValueBox.KeyDown += OnValueKeyDown;
            InnerValueBox.TextChanged += OnTextChanged;
            Loaded += (_, _) => SyncTextFromValue();
        }

        public event EventHandler? ValueCommitted;

        public static readonly DependencyProperty TitleProperty =
            DependencyProperty.Register(
                nameof(Title),
                typeof(string),
                typeof(SettingValueRow),
                new PropertyMetadata(string.Empty));

        public string Title
        {
            get => (string)GetValue(TitleProperty);
            set => SetValue(TitleProperty, value);
        }

        public static readonly DependencyProperty DescriptionProperty =
            DependencyProperty.Register(
                nameof(Description),
                typeof(string),
                typeof(SettingValueRow),
                new PropertyMetadata(string.Empty));

        public string Description
        {
            get => (string)GetValue(DescriptionProperty);
            set => SetValue(DescriptionProperty, value);
        }

        public static readonly DependencyProperty ValueProperty =
            DependencyProperty.Register(
                nameof(Value),
                typeof(double),
                typeof(SettingValueRow),
                new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnValueChanged));

        public double Value
        {
            get => (double)GetValue(ValueProperty);
            set => SetValue(ValueProperty, value);
        }

        public static readonly DependencyProperty MinimumProperty =
            DependencyProperty.Register(
                nameof(Minimum),
                typeof(double),
                typeof(SettingValueRow),
                new PropertyMetadata(double.NaN));

        public double Minimum
        {
            get => (double)GetValue(MinimumProperty);
            set => SetValue(MinimumProperty, value);
        }

        public static readonly DependencyProperty MaximumProperty =
            DependencyProperty.Register(
                nameof(Maximum),
                typeof(double),
                typeof(SettingValueRow),
                new PropertyMetadata(double.NaN));

        public double Maximum
        {
            get => (double)GetValue(MaximumProperty);
            set => SetValue(MaximumProperty, value);
        }

        public static readonly DependencyProperty DecimalPlacesProperty =
            DependencyProperty.Register(
                nameof(DecimalPlaces),
                typeof(int),
                typeof(SettingValueRow),
                new PropertyMetadata(0, OnDisplayFormatChanged));

        public int DecimalPlaces
        {
            get => (int)GetValue(DecimalPlacesProperty);
            set => SetValue(DecimalPlacesProperty, value);
        }

        public static readonly DependencyProperty UnitProperty =
            DependencyProperty.Register(
                nameof(Unit),
                typeof(string),
                typeof(SettingValueRow),
                new PropertyMetadata("W"));

        public string Unit
        {
            get => (string)GetValue(UnitProperty);
            set => SetValue(UnitProperty, value);
        }

        public static readonly DependencyProperty ValueAutomationIdProperty =
            DependencyProperty.Register(
                nameof(ValueAutomationId),
                typeof(string),
                typeof(SettingValueRow),
                new PropertyMetadata(string.Empty));

        public string ValueAutomationId
        {
            get => (string)GetValue(ValueAutomationIdProperty);
            set => SetValue(ValueAutomationIdProperty, value);
        }

        private static void OnValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var row = (SettingValueRow)d;
            double clamped = row.Clamp((double)e.NewValue);
            if (Math.Abs(clamped - (double)e.NewValue) > 0.0001)
            {
                row.SetCurrentValue(ValueProperty, clamped);
                return;
            }

            row.SyncTextFromValue();
        }

        private static void OnDisplayFormatChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            ((SettingValueRow)d).SyncTextFromValue();
        }

        private void OnValueKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter)
                return;
            CommitText(notify: true);
            Keyboard.ClearFocus();
            e.Handled = true;
        }

        private void OnTextChanged(object sender, TextChangedEventArgs e)
        {
            if (_syncingText)
                return;
            if (!TryParse(InnerValueBox.Text, out double parsed))
                return;
            if (!IsInRange(parsed))
                return;
            if (Math.Abs(Value - parsed) <= 0.0001)
                return;
            SetCurrentValue(ValueProperty, parsed);
            ValueCommitted?.Invoke(this, EventArgs.Empty);
        }

        public void CommitText(bool notify = true)
        {
            if (!TryParse(InnerValueBox.Text, out double parsed))
                parsed = double.IsFinite(Minimum) ? Minimum : 0;
            SetCurrentValue(ValueProperty, Clamp(parsed));
            SyncTextFromValue();
            if (notify)
                ValueCommitted?.Invoke(this, EventArgs.Empty);
        }

        private void SyncTextFromValue()
        {
            if (InnerValueBox == null)
                return;
            string formatted = Format(Value);
            if (InnerValueBox.Text == formatted)
                return;
            _syncingText = true;
            InnerValueBox.Text = formatted;
            _syncingText = false;
        }

        private bool IsInRange(double value)
        {
            if (double.IsFinite(Minimum) && value < Minimum)
                return false;
            if (double.IsFinite(Maximum) && value > Maximum)
                return false;
            return true;
        }

        private double Clamp(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
                value = double.IsFinite(Minimum) ? Minimum : 0;
            if (double.IsFinite(Minimum))
                value = Math.Max(Minimum, value);
            if (double.IsFinite(Maximum))
                value = Math.Min(Maximum, value);
            return value;
        }

        private static bool TryParse(string? text, out double value)
        {
            return double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value)
                || double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }

        private string Format(double value)
        {
            int places = Math.Clamp(DecimalPlaces, 0, 6);
            if (places == 0)
            {
                if (Math.Abs(value - Math.Round(value)) < 0.0001)
                    return Math.Round(value).ToString(CultureInfo.CurrentCulture);
                return value.ToString("0.#", CultureInfo.CurrentCulture);
            }

            return value.ToString("0." + new string('0', places), CultureInfo.CurrentCulture);
        }
    }
}
