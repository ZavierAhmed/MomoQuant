using MomoQuant.Domain.Enums;

namespace MomoQuant.Application.ValidationLab;

/// <summary>
/// Status-transition rules for Validation Laboratory lifecycle gates.
/// </summary>
public static class ValidationLifecycleGate
{
    public static bool CanPrepareData(ValidationExperimentStatus status) =>
        status is ValidationExperimentStatus.Draft
            or ValidationExperimentStatus.DataPreparing
            or ValidationExperimentStatus.Failed;

    public static bool CanRunTraining(ValidationExperimentStatus status) =>
        status == ValidationExperimentStatus.DataReady;

    public static bool CanResumeTraining(ValidationExperimentStatus status) =>
        status is ValidationExperimentStatus.Failed
            or ValidationExperimentStatus.TrainingInterrupted
            or ValidationExperimentStatus.TrainingPaused;

    public static bool IsTrainingInProgress(ValidationExperimentStatus status) =>
        status is ValidationExperimentStatus.TrainingRunning
            or ValidationExperimentStatus.TrainingResumed
            or ValidationExperimentStatus.ResumePreparing;

    public static bool CanFreeze(ValidationExperimentStatus status) =>
        status == ValidationExperimentStatus.TrainingCompleted;

    public static bool CanRunValidation(ValidationExperimentStatus status) =>
        status == ValidationExperimentStatus.ConfigurationFrozen;

    public static bool IsValidationPerformanceRevealed(ValidationRevealStatus reveal) =>
        reveal == ValidationRevealStatus.Revealed;
}