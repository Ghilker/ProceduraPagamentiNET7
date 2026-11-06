using System.Collections.Generic;

namespace DomicilioAutomation
{
    public sealed class DomicilioAutomationJob
    {
        public string ConnectionString { get; init; } = string.Empty;
        public List<DomicilioAutomationRequest> Requests { get; init; } = new();
    }

    public sealed record DomicilioAutomationRequest(int NumIstanza, string Fingerprint)
    {
        public List<DomicilioAutomationOperationCorrection> Corrections { get; init; } = new();
    }

    public sealed record DomicilioAutomationOperationCorrection(int OperationId, string BeforePayload,
        string AfterPayload, List<DomicilioAutomationFieldCorrection> Fields);

    public sealed record DomicilioAutomationFieldCorrection(string Field, string Before, string After,
        int Page, decimal AttachmentId, string Label);

    public sealed record DomicilioAutomationResult(int NumIstanza, string Outcome, string Reason);
}
