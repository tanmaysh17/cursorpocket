using CursorPocket.Core.Models;
using CursorPocket.Core.Services;

namespace CursorPocket.Tests;

public sealed class OnboardingFlowTests
{
    [Fact]
    public void FirstVisibleLaunchPresentsOnboarding()
    {
        Assert.True(OnboardingFlow.ShouldPresent(onboardingSeen: false, startedInBackground: false));
        Assert.False(OnboardingFlow.ShouldPresent(onboardingSeen: true, startedInBackground: false));
        Assert.False(OnboardingFlow.ShouldPresent(onboardingSeen: false, startedInBackground: true));
        Assert.True(OnboardingFlow.ShouldPresent(OnboardingFlow.CurrentVersion - 1, startedInBackground: false));
        Assert.False(OnboardingFlow.ShouldPresent(OnboardingFlow.CurrentVersion, startedInBackground: false));
        Assert.False(OnboardingFlow.ShouldPresent(OnboardingFlow.CurrentVersion - 1, startedInBackground: true));
    }

    [Fact]
    public void FlowHasThreePurposefulStepsAndClampsNavigation()
    {
        Assert.Equal(
            [OnboardingStep.CaptureLoop, OnboardingStep.CommandMode, OnboardingStep.FirstCapture],
            OnboardingFlow.Steps.Select(step => step.Id));
        Assert.Equal(OnboardingStep.CaptureLoop, OnboardingFlow.At(-1).Id);
        Assert.Equal(OnboardingStep.FirstCapture, OnboardingFlow.At(99).Id);
    }

    [Fact]
    public void CapabilityMapCoversTheCompleteLocalWorkflow()
    {
        Assert.Equal(
            [OnboardingCapabilityStage.Capture, OnboardingCapabilityStage.Shape, OnboardingCapabilityStage.Retrieve],
            OnboardingFlow.CapabilityStages.Select(stage => stage.Id));
        Assert.Contains("camera", OnboardingFlow.CapabilityStages[0].Examples, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("OCR", OnboardingFlow.CapabilityStages[1].Examples, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Recycle Bin", OnboardingFlow.CapabilityStages[2].Examples, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TourTeachesTheCompleteCommandCatalogue()
    {
        Assert.Equal(7, OnboardingFlow.Commands.Count);
        Assert.Equal(
            ["S", "V", "Shift+V", "A", "T", "L", "O"],
            OnboardingFlow.Commands.Select(command => command.Key));
        Assert.All(OnboardingFlow.Commands, command => Assert.True(command.IsPrimary));
        Assert.Equal(7, OnboardingFlow.Commands.Select(command => command.Id).Distinct().Count());
    }

    [Fact]
    public void RehearsalOffersThreeUsefulFirstWins()
    {
        Assert.Equal(
            [CaptureActionId.Screenshot, CaptureActionId.Video, CaptureActionId.Audio],
            OnboardingFlow.StarterTasks.Select(task => task.Id));
        Assert.All(OnboardingFlow.StarterTasks, task => Assert.False(string.IsNullOrWhiteSpace(task.KeySequence)));
        Assert.Equal("S  then  R", OnboardingFlow.StarterTask(CaptureActionId.Screenshot).KeySequence);
    }
}
