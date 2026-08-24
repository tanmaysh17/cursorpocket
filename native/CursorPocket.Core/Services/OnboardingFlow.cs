using CursorPocket.Core.Models;

namespace CursorPocket.Core.Services;

public enum OnboardingStep
{
    Welcome,
    Activate,
    Rehearse,
}

public sealed record OnboardingStepDescriptor(
    OnboardingStep Id,
    string Label,
    string Title,
    string Description);

/// <summary>
/// The first-run sequence is deliberately small: understand where CursorPocket
/// lives, learn the one global activation gesture, then open the real command
/// surface. Product UI and tests share this ordering instead of duplicating it.
/// </summary>
public static class OnboardingFlow
{
    public static IReadOnlyList<OnboardingStepDescriptor> Steps { get; } =
    [
        new(
            OnboardingStep.Welcome,
            "Meet CursorPocket",
            "Keep the moment. Stay in the flow.",
            "CursorPocket waits beside your pointer, captures locally, and gets out of the way."),
        new(
            OnboardingStep.Activate,
            "Open command mode",
            "One shortcut, every capture.",
            "Use the global shortcut from any app, then choose the mnemonic that matches what you want to keep."),
        new(
            OnboardingStep.Rehearse,
            "Try it once",
            "Your first capture is one key away.",
            "Open the real command surface now. Nothing is captured until you choose an action."),
    ];

    public static int ClampIndex(int index) => Math.Clamp(index, 0, Steps.Count - 1);

    public static OnboardingStepDescriptor At(int index) => Steps[ClampIndex(index)];

    public static bool ShouldPresent(bool onboardingSeen, bool startedInBackground) =>
        !onboardingSeen && !startedInBackground;

    public static IReadOnlyList<CaptureActionDescriptor> Commands => CaptureActionCatalog.Primary;
}
