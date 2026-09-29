using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace ReelWalk.Views;
public partial class BlueStepper : UserControl
{
    public static readonly StyledProperty<double> ValueProperty =
        AvaloniaProperty.Register<BlueStepper, double>(nameof(Value), 0,
            defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);

    public static readonly StyledProperty<double> MinimumProperty =
        AvaloniaProperty.Register<BlueStepper, double>(nameof(Minimum), 0);

    public static readonly StyledProperty<double> MaximumProperty =
        AvaloniaProperty.Register<BlueStepper, double>(nameof(Maximum), 100);

    public static readonly StyledProperty<double> IncrementProperty =
        AvaloniaProperty.Register<BlueStepper, double>(nameof(Increment), 1);

    public static readonly StyledProperty<string> FormatStringProperty =
        AvaloniaProperty.Register<BlueStepper, string>(nameof(FormatString), "0");

    private bool _syncing;

    // Builds the blue numeric stepper.
    // Returns nothing.
    public BlueStepper()
    {
        InitializeComponent();
        ValueProperty.Changed.AddClassHandler<BlueStepper>((s, e) => s.WriteText());
        FormatStringProperty.Changed.AddClassHandler<BlueStepper>((s, e) => s.WriteText());
        AttachedToVisualTree += (s, e) => WriteText();
    }

    public double Value
    {
        get { return GetValue(ValueProperty); }
        set { SetValue(ValueProperty, value); }
    }

    public double Minimum
    {
        get { return GetValue(MinimumProperty); }
        set { SetValue(MinimumProperty, value); }
    }

    public double Maximum
    {
        get { return GetValue(MaximumProperty); }
        set { SetValue(MaximumProperty, value); }
    }

    public double Increment
    {
        get { return GetValue(IncrementProperty); }
        set { SetValue(IncrementProperty, value); }
    }

    public string FormatString
    {
        get { return GetValue(FormatStringProperty); }
        set { SetValue(FormatStringProperty, value); }
    }

    // Raises Value by Increment.
    // Returns nothing.
    private void Up_Click(object sender, RoutedEventArgs e)
    {
        Value = Clamp(Value + Increment);
        WriteText();
    }

    // Lowers Value by Increment.
    // Returns nothing.
    private void Down_Click(object sender, RoutedEventArgs e)
    {
        Value = Clamp(Value - Increment);
        WriteText();
    }

    // Commits typed text when Enter is pressed.
    // Returns nothing.
    private void Field_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            ReadText();
            e.Handled = true;
        }
    }

    // Commits typed text when the field loses focus.
    // Returns nothing.
    private void Field_LostFocus(object sender, RoutedEventArgs e)
    {
        ReadText();
    }

    // Writes Value into the text field.
    // Returns nothing.
    private void WriteText()
    {
        if (Field == null || _syncing)
            return;
        _syncing = true;
        try
        {
            var format = string.IsNullOrEmpty(FormatString) ? "0" : FormatString;
            Field.Text = Value.ToString(format, CultureInfo.InvariantCulture);
        }
        finally
        {
            _syncing = false;
        }
    }

    // Parses the text field into Value.
    // Returns nothing when the text is not a number.
    private void ReadText()
    {
        if (Field == null || _syncing)
            return;
        double parsed;
        if (!double.TryParse(Field.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed) &&
            !double.TryParse(Field.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out parsed))
        {
            WriteText();
            return;
        }
        Value = Clamp(parsed);
        WriteText();
    }

    // Keeps value inside Minimum and Maximum.
    // v is the proposed value. Returns the clamped value.
    private double Clamp(double v)
    {
        if (v < Minimum) return Minimum;
        if (v > Maximum) return Maximum;
        return v;
    }
}
