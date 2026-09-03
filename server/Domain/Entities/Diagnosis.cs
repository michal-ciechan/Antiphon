using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Domain.Entities;

/// <summary>
/// Append-only ledger of diagnose-seat work (CARD-0352 D7). Table <c>Diagnoses</c>.
/// Named away from the static <c>Diagnosis</c> contract helpers in Application.Services so
/// the two can coexist; the table name is the public one.
/// </summary>
public class DiagnosisRecord
{
    public Guid Id { get; set; }
    public DiagnosisKind Kind { get; set; }
    public Guid? TaskId { get; set; }
    public Guid? CardId { get; set; }

    /// <summary>The seat's Diagnose-role row, when one was created.</summary>
    public Guid? DiagnoseTaskId { get; set; }

    public DiagnosisOutcome Outcome { get; set; }
    public string? Answer { get; set; }
    public string? Applied { get; set; }
    public string? Reason { get; set; }
    public string? BundleStamp { get; set; }
    public decimal CostUsd { get; set; }
    public int WaitMs { get; set; }
    public bool Forced { get; set; }
    public DateTime CreatedAt { get; set; }
}
