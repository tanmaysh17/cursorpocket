using CursorPocket.Core.Models;
using CursorPocket.Core.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace CursorPocket_App;

public sealed partial class OnboardingPage : Page
{
    private int _stepIndex;
    private CaptureActionId _starterTask = CaptureActionId.Screenshot;

    public IReadOnlyList<CaptureActionDescriptor> Commands { get; } = OnboardingFlow.Commands;
    public IReadOnlyList<OnboardingCapabilityStageDescriptor> CapabilityStages { get; } = OnboardingFlow.CapabilityStages;

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
        WelcomeFolderPathText.Text = App.Services.Settings.CaptureDirectory;
        RehearsalFolderPathText.Text = App.Services.Settings.CaptureDirectory;
        GestureStatusText.Text = App.Services.Settings.MouseGestureEnabled ? "On" : "Off";
        ChordStatusText.Text = App.Services.Settings.MouseChordEnabled ? "On" : "Off";
        CompanionStatusText.Text = App.Services.Settings.CursorCompanionMode == "off"
            ? "Off now. You can turn it on before finishing."
            : "Green when ready, red throughout recording.";
        _stepIndex = 0;
        RenderCommandPreview(CaptureActionId.Screenshot);
        RenderStarterTask();
        RenderStep(moveFocus: false);
    }

    private void Page_Unloaded(object sender, RoutedEventArgs eventArgs) =>
        App.Theme.ThemeChanged -= Theme_ThemeChanged;

    private void Theme_ThemeChanged(object? sender, EventArgs eventArgs)
    {
        RenderStep(moveFocus: false);
        RenderStarterTask();
    }

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

        await FinishAsync(openCommandMode: true);
    }

    private async void Skip_Click(object sender, RoutedEventArgs eventArgs) =>
        await FinishAsync(openCommandMode: false);

    private async void FinishForNow_Click(object sender, RoutedEventArgs eventArgs) =>
        await FinishAsync(openCommandMode: false);

    private void CommandPreview_Click(object sender, RoutedEventArgs eventArgs)
    {
        if (sender is FrameworkElement { Tag: CaptureActionId action })
        {
            RenderCommandPreview(action);
        }
    }

    private void StarterTask_Click(object sender, RoutedEventArgs eventArgs)
    {
        if (sender is FrameworkElement { Tag: string value } &&
            Enum.TryParse<CaptureActionId>(value, ignoreCase: true, out var action) &&
            OnboardingFlow.StarterTasks.Any(task => task.Id == action))
        {
            _starterTask = action;
            RenderStarterTask();
        }
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs eventArgs)
    {
        var path = App.Services.Settings.CaptureDirectory;
        Directory.CreateDirectory(path);
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
    }

    private async Task FinishAsync(bool openCommandMode)
    {
        NextButton.IsEnabled = false;
        FinalFinishButton.IsEnabled = false;
        await (App.Window as MainWindow)!.CompleteOnboardingAsync(
            StartWithWindowsCheckBox.IsChecked == true,
            ShowCompanionCheckBox.IsChecked == true,
            openCommandMode);
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
        FinalFinishButton.Visibility = _stepIndex == OnboardingFlow.Steps.Count - 1
            ? Visibility.Visible
            : Visibility.Collapsed;
        NextButton.Content = _stepIndex == OnboardingFlow.Steps.Count - 1 ? "Open command mode" : "Continue";

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

        // The title is a polite live region, so screen readers announce the new
        // step while keyboard focus stays on the control the user just invoked.
        _ = moveFocus;
    }

    private void RenderCommandPreview(CaptureActionId action)
    {
        var command = CaptureActionCatalog.Get(action);
        CommandPreviewTitle.Text = command.Title;
        CommandPreviewDescription.Text = command.Description;
        CommandPreviewFollowUp.Text = action switch
        {
            CaptureActionId.Screenshot => "Next: R region · W window · D display · A all displays · P previous region",
            CaptureActionId.Video => "Preflight confirms screen, microphone, camera, effects, frame rate, pointer, and countdown before anything starts.",
            CaptureActionId.RepeatVideo => "Starts with the last video source and recording choices, without repeating preflight.",
            CaptureActionId.Audio => "Starts on the remembered microphone. The red HUD stays visible; Escape stops and saves.",
            CaptureActionId.Text => "Reads the highlighted text from the window you were just using and saves a local text capture.",
            CaptureActionId.Link => "Reads the address from the active browser and saves it with the rest of the Library.",
            CaptureActionId.Library => "Opens previews, filters, playback, copy, reveal, markup, and recoverable deletion.",
            _ => string.Empty,
        };
    }

    private void RenderStarterTask()
    {
        var task = OnboardingFlow.StarterTask(_starterTask);
        StarterKeySequenceText.Text = task.KeySequence;
        StarterInstructionText.Text = task.Instruction;

        var buttons = new (Button Button, CaptureActionId Action)[]
        {
            (ScreenshotStarterButton, CaptureActionId.Screenshot),
            (VideoStarterButton, CaptureActionId.Video),
            (AudioStarterButton, CaptureActionId.Audio),
        };
        foreach (var (button, action) in buttons)
        {
            var selected = action == _starterTask;
            button.Background = App.Theme.Brush(selected ? "PocketGreenSoft" : "PocketRaised");
            button.BorderBrush = App.Theme.Brush(selected ? "PocketGreen" : "PocketLine");
            AutomationProperties.SetName(button, selected
                ? $"{task.Title}, selected first capture"
                : $"Choose {OnboardingFlow.StarterTask(action).Title} as the first capture");
        }
    }
}
