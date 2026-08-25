using CursorPocket.Core.Models;
using CursorPocket.Core.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CursorPocket_App;

public sealed partial class OnboardingPage : Page
{
    private int _stepIndex;

    public IReadOnlyList<CaptureActionDescriptor> Commands { get; } = OnboardingFlow.Commands;

    public OnboardingPage()
    {
        InitializeComponent();
    }

    private void Page_Loaded(object sender, RoutedEventArgs eventArgs)
    {
        App.Theme.ThemeChanged -= Theme_ThemeChanged;
        App.Theme.ThemeChanged += Theme_ThemeChanged;
        StartWithWindowsCheckBox.IsChecked = App.Services.Settings.StartWithWindows;
        ShowCompanionCheckBox.IsChecked = App.Services.Settings.CursorCompanionMode != "off";
        _stepIndex = 0;
        RenderStep(moveFocus: false);
    }

    private void Page_Unloaded(object sender, RoutedEventArgs eventArgs) =>
        App.Theme.ThemeChanged -= Theme_ThemeChanged;

    private void Theme_ThemeChanged(object? sender, EventArgs eventArgs) =>
        RenderStep(moveFocus: false);

    private void Step_Click(object sender, RoutedEventArgs eventArgs)
    {
        if (sender is Button { Tag: string value } && int.TryParse(value, out var index))
        {
            _stepIndex = OnboardingFlow.ClampIndex(index);
            RenderStep(moveFocus: true);
        }
    }

    private void Back_Click(object sender, RoutedEventArgs eventArgs)
    {
        _stepIndex = OnboardingFlow.ClampIndex(_stepIndex - 1);
        RenderStep(moveFocus: true);
    }

    private async void Next_Click(object sender, RoutedEventArgs eventArgs)
    {
        if (_stepIndex < OnboardingFlow.Steps.Count - 1)
        {
            _stepIndex++;
            RenderStep(moveFocus: true);
            return;
        }

        await FinishAsync();
    }

    private async void Skip_Click(object sender, RoutedEventArgs eventArgs) => await FinishAsync();

    private void TryCommand_Click(object sender, RoutedEventArgs eventArgs) =>
        (App.Window as MainWindow)?.ShowCommandPalette();

    private void OpenFolder_Click(object sender, RoutedEventArgs eventArgs)
    {
        var path = App.Services.Settings.CaptureDirectory;
        Directory.CreateDirectory(path);
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
    }

    private async Task FinishAsync()
    {
        NextButton.IsEnabled = false;
        await (App.Window as MainWindow)!.CompleteOnboardingAsync(
            StartWithWindowsCheckBox.IsChecked == true,
            ShowCompanionCheckBox.IsChecked == true);
    }

    private void RenderStep(bool moveFocus)
    {
        var step = OnboardingFlow.At(_stepIndex);
        StepEyebrow.Text = $"{_stepIndex + 1} of {OnboardingFlow.Steps.Count} · {step.Label}";
        StepTitle.Text = step.Title;
        StepDescription.Text = step.Description;

        WelcomeField.Visibility = _stepIndex == 0 ? Visibility.Visible : Visibility.Collapsed;
        ActivationField.Visibility = _stepIndex == 1 ? Visibility.Visible : Visibility.Collapsed;
        RehearsalField.Visibility = _stepIndex == 2 ? Visibility.Visible : Visibility.Collapsed;
        BackButton.Visibility = _stepIndex == 0 ? Visibility.Collapsed : Visibility.Visible;
        NextButton.Content = _stepIndex == OnboardingFlow.Steps.Count - 1 ? "Finish setup" : "Continue";

        ShortcutText.Text = App.Services.Hotkey.RegisteredShortcut ?? App.Services.Settings.ActivationShortcut;
        ShortcutStatus.Text = App.Services.Hotkey.RegisteredShortcut is null
            ? "That shortcut was already in use. Choose another in Settings after the tour."
            : "Registered and ready. It works while CursorPocket is in the notification area.";

        var buttons = new[] { WelcomeStepButton, ActivateStepButton, RehearseStepButton };
        var marks = new[] { WelcomeStepMark, ActivateStepMark, RehearseStepMark };
        for (var index = 0; index < buttons.Length; index++)
        {
            var selected = index == _stepIndex;
            buttons[index].Background = App.Theme.Brush(selected ? "PocketGreenSoft" : "PocketBase");
            buttons[index].BorderBrush = App.Theme.Brush(selected ? "PocketGreen" : "PocketBase");
            marks[index].Background = App.Theme.Brush(index <= _stepIndex ? "PocketGreenSoft" : "PocketBase");
            marks[index].BorderBrush = App.Theme.Brush(index <= _stepIndex ? "PocketGreen" : "PocketLine");
        }

        if (moveFocus)
        {
            StepTitle.Focus(FocusState.Programmatic);
        }
    }
}
