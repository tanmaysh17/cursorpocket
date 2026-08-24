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
    }

    [Fact]
    public void FlowHasThreePurposefulStepsAndClampsNavigation()
    {
        Assert.Equal(
            [OnboardingStep.Welcome, OnboardingStep.Activate, OnboardingStep.Rehearse],
            OnboardingFlow.Steps.Select(step => step.Id));
        Assert.Equal(OnboardingStep.Welcome, OnboardingFlow.At(-1).Id);
        Assert.Equal(OnboardingStep.Rehearse, OnboardingFlow.At(99).Id);
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
}
