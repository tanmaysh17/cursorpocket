using CursorPocket.Core.Models;

namespace CursorPocket.Core.Services;

public enum OnboardingStep
{
    CaptureLoop,
    CommandMode,
    FirstCapture,
}

public sealed record OnboardingStepDescriptor(
    OnboardingStep Id,
    string Label,
    string Title,
    string Description);

public enum OnboardingCapabilityStage
{
    Capture,
    Shape,
    Retrieve,
}

public sealed record OnboardingCapabilityStageDescriptor(
    OnboardingCapabilityStage Id,
    string Number,
    string Label,
    string Title,
    string Description,
    string Examples,
    string Glyph);

public sealed record OnboardingStarterTaskDescriptor(
    CaptureActionId Id,
    string Title,
    string KeySequence,
    string Instruction,
    string Glyph);

/// <summary>
/// The first-run sequence is deliberately small: understand where CursorPocket
/// lives, learn the one global activation gesture, then open the real command
/// surface. Product UI and tests share this ordering instead of duplicating it.
/// </summary>
public static class OnboardingFlow
{
    // Version 2 replaces the passive welcome carousel with a capability map and
    // a real first-capture handoff. Existing users should see that material change
    // once, while background launches remain silent.
    public const int CurrentVersion = 2;

    public static IReadOnlyList<OnboardingStepDescriptor> Steps { get; } =
    [
        new(
            OnboardingStep.CaptureLoop,
            "See the whole loop",
            "Capture is only the first half.",
            "CursorPocket carries a moment from the screen to a useful local file. Here is what happens before, during, and after every capture."),
        new(
            OnboardingStep.CommandMode,
            "Learn command mode",
            "Seven actions. One small surface.",
            "Open command mode from any app, then choose the mnemonic for what you want to keep. Select an action below to see what happens next."),
        new(
            OnboardingStep.FirstCapture,
            "Make a first capture",
            "Leave with something useful.",
            "Pick a starting task. CursorPocket will save these choices, close the guide, and open the real command surface."),
    ];

    public static IReadOnlyList<OnboardingCapabilityStageDescriptor> CapabilityStages { get; } =
    [
        new(
            OnboardingCapabilityStage.Capture,
            "01",
            "Capture",
            "Keep the source in context",
            "Take a screenshot, record video or audio, or save the text and link from the app you were just using.",
            "Region, window, display, camera, microphone, highlighted text, current link",
            "\uE722"),
        new(
            OnboardingCapabilityStage.Shape,
            "02",
            "Shape",
            "Turn it into something usable",
            "Crop and mark screenshots, redact details, extract text locally, pin a reference, or clean up camera and narration before saving.",
            "Markup, crop, redact, OCR, pin, backdrops, camera effects, audio cleanup",
            "\uE70F"),
        new(
            OnboardingCapabilityStage.Retrieve,
            "03",
            "Retrieve",
            "Know exactly where it went",
            "A receipt confirms every save. The Library keeps previews, playback, filters, copy, reveal, and recoverable deletion together.",
            "Local folder, receipts, filters, playback, copy, reveal, Recycle Bin",
            "\uE8B9"),
    ];

    public static IReadOnlyList<OnboardingStarterTaskDescriptor> StarterTasks { get; } =
    [
        new(
            CaptureActionId.Screenshot,
            "Screenshot",
            "S  then  R",
            "Draw any area. The editor opens ready to crop, mark, redact, extract text, copy, or pin.",
            "\uE91B"),
        new(
            CaptureActionId.Video,
            "Video",
            "V",
            "Choose a display, window, or region, then confirm microphone, camera, effects, and countdown before recording.",
            "\uE714"),
        new(
            CaptureActionId.Audio,
            "Audio note",
            "A",
            "Record from the remembered microphone. Press Escape when you are done to stop and save.",
            "\uE720"),
    ];

    public static int ClampIndex(int index) => Math.Clamp(index, 0, Steps.Count - 1);

    public static OnboardingStepDescriptor At(int index) => Steps[ClampIndex(index)];

    public static bool ShouldPresent(bool onboardingSeen, bool startedInBackground) =>
        !onboardingSeen && !startedInBackground;

    public static bool ShouldPresent(int completedVersion, bool startedInBackground) =>
        completedVersion < CurrentVersion && !startedInBackground;

    public static IReadOnlyList<CaptureActionDescriptor> Commands => CaptureActionCatalog.Primary;

    public static OnboardingStarterTaskDescriptor StarterTask(CaptureActionId id) =>
        StarterTasks.First(task => task.Id == id);
}
