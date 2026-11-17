using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace PreySense.Frontend.Controls;

public sealed class ThemedIcon : Control
{
    static ThemedIcon()
    {
        ForegroundProperty.OverrideMetadata(typeof(ThemedIcon),
            new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.Inherits | FrameworkPropertyMetadataOptions.AffectsRender));
    }

    public static readonly DependencyProperty SourceProperty = DependencyProperty.Register(
        nameof(Source), typeof(ImageSource), typeof(ThemedIcon),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnSourceChanged));

    private ImageBrush? _cachedMask;

    private static void OnSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var icon = (ThemedIcon)d;
        if (e.NewValue is ImageSource source)
        {
            if (source.CanFreeze && !source.IsFrozen) source.Freeze();
            var brush = new ImageBrush(source) { Stretch = Stretch.Uniform };
            if (brush.CanFreeze) brush.Freeze();
            icon._cachedMask = brush;
        }
        else
        {
            icon._cachedMask = null;
        }
    }

    public ImageSource? Source
    {
        get => (ImageSource?)GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        if (_cachedMask == null || ActualWidth <= 0 || ActualHeight <= 0) return;

        drawingContext.PushOpacityMask(_cachedMask);
        drawingContext.DrawRectangle(Foreground, null, new Rect(0, 0, ActualWidth, ActualHeight));
        drawingContext.Pop();
    }
}
