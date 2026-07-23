using MomoQuant.Application.ValidationLab;
using MomoQuant.Domain.Enums;

namespace MomoQuant.UnitTests.ValidationLab;

public sealed class ValidationLifecycleGateTests
{
    [Theory]
    [InlineData(ValidationExperimentStatus.Draft, false)]
    [InlineData(ValidationExperimentStatus.DataReady, false)]
    [InlineData(ValidationExperimentStatus.TrainingCompleted, false)]
    [InlineData(ValidationExperimentStatus.ConfigurationFrozen, true)]
    [InlineData(ValidationExperimentStatus.Completed, false)]
    public void Validation_cannot_run_before_freeze(ValidationExperimentStatus status, bool expected)
    {
        Assert.Equal(expected, ValidationLifecycleGate.CanRunValidation(status));
    }

    [Fact]
    public void Freeze_requires_training_completed()
    {
        Assert.True(ValidationLifecycleGate.CanFreeze(ValidationExperimentStatus.TrainingCompleted));
        Assert.False(ValidationLifecycleGate.CanFreeze(ValidationExperimentStatus.DataReady));
        Assert.False(ValidationLifecycleGate.CanFreeze(ValidationExperimentStatus.ConfigurationFrozen));
    }

    [Fact]
    public void Training_requires_data_ready()
    {
        Assert.True(ValidationLifecycleGate.CanRunTraining(ValidationExperimentStatus.DataReady));
        Assert.False(ValidationLifecycleGate.CanRunTraining(ValidationExperimentStatus.Draft));
    }
}
