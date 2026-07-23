using MomoQuant.Domain.Common;

namespace MomoQuant.Domain.ValidationLab;

public class ValidationExperimentExecutionLease : Entity
{
    public long ValidationExperimentId { get; set; }
    public string LeaseOwner { get; set; } = string.Empty;
    public DateTime AcquiredAtUtc { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime HeartbeatAtUtc { get; set; }
}
