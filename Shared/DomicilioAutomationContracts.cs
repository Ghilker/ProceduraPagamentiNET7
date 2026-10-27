using System.Collections.Generic;

namespace DomicilioAutomation
{
    public sealed class DomicilioAutomationJob
    {
        public string ConnectionString { get; init; } = string.Empty;
        public List<DomicilioAutomationRequest> Requests { get; init; } = new();
    }

    public sealed record DomicilioAutomationRequest(int NumIstanza, string Fingerprint);

    public sealed record DomicilioAutomationResult(int NumIstanza, string Outcome, string Reason);
}
