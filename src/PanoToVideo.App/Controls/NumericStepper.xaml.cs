using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace PanoToVideo.App.Controls;

/// <summary>
/// 整数步进输入：保留上下按钮；启用文本输入时，会在确认输入后自动归整为范围内的合法整数。
/// </summary>
public partial class NumericStepper : UserControl
{
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(int), typeof(NumericStepper),
        new FrameworkPropertyMetadata(0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnStateChanged, CoerceValue));

    public static readonly DependencyProperty MinimumProperty = DependencyProperty.Register(
        nameof(Minimum), typeof(int), typeof(NumericStepper), new PropertyMetadata(0, OnStateChanged));

    public static readonly DependencyProperty MaximumProperty = DependencyProperty.Register(
        nameof(Maximum), typeof(int), typeof(NumericStepper), new PropertyMetadata(int.MaxValue, OnStateChanged));

    public static readonly DependencyProperty StepProperty = DependencyProperty.Register(
        nameof(Step), typeof(int), typeof(NumericStepper), new PropertyMetadata(1, OnStateChanged));

    public static readonly DependencyProperty AllowedValuesProperty = DependencyProperty.Register(
        nameof(AllowedValues), typeof(string), typeof(NumericStepper), new PropertyMetadata(string.Empty, OnStateChanged));

    public static readonly DependencyProperty IsReadOnlyProperty = DependencyProperty.Register(
        nameof(IsReadOnly), typeof(bool), typeof(NumericStepper), new PropertyMetadata(false, OnStateChanged));

    public static readonly DependencyProperty AllowTextInputProperty = DependencyProperty.Register(
        nameof(AllowTextInput), typeof(bool), typeof(NumericStepper), new PropertyMetadata(false, OnStateChanged));

    public NumericStepper()
    {
        InitializeComponent();
        Loaded += (_, _) => UpdateState();
        DataObject.AddPastingHandler(NumericTextBox, OnPaste);
    }

    public int Value { get => (int)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public int Minimum { get => (int)GetValue(MinimumProperty); set => SetValue(MinimumProperty, value); }
    public int Maximum { get => (int)GetValue(MaximumProperty); set => SetValue(MaximumProperty, value); }
    public int Step { get => (int)GetValue(StepProperty); set => SetValue(StepProperty, value); }
    /// <summary>逗号分隔的离散值，例如 24,25,30,50,60。</summary>
    public string AllowedValues { get => (string)GetValue(AllowedValuesProperty); set => SetValue(AllowedValuesProperty, value); }
    public bool IsReadOnly { get => (bool)GetValue(IsReadOnlyProperty); set => SetValue(IsReadOnlyProperty, value); }
    public bool AllowTextInput { get => (bool)GetValue(AllowTextInputProperty); set => SetValue(AllowTextInputProperty, value); }

    private static void OnStateChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (NumericStepper)d;
        if (e.Property != ValueProperty)
            control.CoerceValue(ValueProperty);
        control.UpdateState();
    }

    private static object CoerceValue(DependencyObject d, object baseValue)
    {
        var control = (NumericStepper)d;
        return Math.Clamp((int)baseValue, Math.Min(control.Minimum, control.Maximum), Math.Max(control.Minimum, control.Maximum));
    }

    private void UpdateState()
    {
        if (!IsLoaded) return;

        NumericTextBox.IsReadOnly = IsReadOnly || !AllowTextInput;
        ButtonsPanel.Visibility = IsReadOnly ? Visibility.Collapsed : Visibility.Visible;
        IncreaseButton.IsEnabled = !IsReadOnly && GetNextValue(1) != Value;
        DecreaseButton.IsEnabled = !IsReadOnly && GetNextValue(-1) != Value;
    }

    private void Increase_Click(object sender, RoutedEventArgs e) => Value = GetNextValue(1);
    private void Decrease_Click(object sender, RoutedEventArgs e) => Value = GetNextValue(-1);

    private int GetNextValue(int direction)
    {
        var allowed = ParseAllowedValues();
        if (allowed.Count > 0)
        {
            var next = direction > 0
                ? allowed.FirstOrDefault(x => x > Value)
                : allowed.LastOrDefault(x => x < Value);
            return next == 0 ? Value : next;
        }

        var step = Math.Max(1, Step);
        var candidate = (long)Value + direction * (long)step;
        return (int)Math.Clamp(candidate, Minimum, Maximum);
    }

    private List<int> ParseAllowedValues() => AllowedValues.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Select(text => int.TryParse(text, out var value) ? value : 0)
        .Where(value => value >= Minimum && value <= Maximum)
        .Distinct()
        .OrderBy(value => value)
        .ToList();

    private void NumericTextBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        if (!AllowTextInput || IsReadOnly)
        {
            e.Handled = true;
            return;
        }

        var box = (TextBox)sender;
        var candidate = box.Text.Remove(box.SelectionStart, box.SelectionLength).Insert(box.SelectionStart, e.Text);
        e.Handled = candidate != "-" && !int.TryParse(candidate, out _);
    }

    private void NumericTextBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e) => NormalizeTextInput();

    private void NumericTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        NormalizeTextInput();
        e.Handled = true;
    }

    private void NormalizeTextInput()
    {
        if (!AllowTextInput || IsReadOnly) return;

        if (int.TryParse(NumericTextBox.Text, out var parsed))
            Value = parsed; // Value 的 CoerceValue 负责限制到最小/最大合法整数。

        // 解析失败时恢复旧值；超出范围时显示已归整后的实际值。
        NumericTextBox.Text = Value.ToString();
    }

    private void OnPaste(object sender, DataObjectPastingEventArgs e)
    {
        if (!AllowTextInput || IsReadOnly || !e.DataObject.GetDataPresent(DataFormats.UnicodeText))
        {
            e.CancelCommand();
            return;
        }

        var text = e.DataObject.GetData(DataFormats.UnicodeText) as string;
        if (!int.TryParse(text, out _)) e.CancelCommand();
    }
}
