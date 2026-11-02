using System.Text.Json;

namespace Antiphon.SessionRunner;

/// <summary>CARD-0824 S1 §3.3. Slot states. Quarantine applies to a directory, never to a slot.</summary>
public static class WorkspaceSlotStates
{
    public const string Free = "Free";
    public const string Leasing = "Leasing";
    public const string Leased = "Leased";
    public const string Releasing = "Releasing";
    public const string ColdResetting = "ColdResetting";
    public const string Rehoming = "Rehoming";
    public const string Retiring = "Retiring";
    public const string Failed = "Failed";
    public const string Retired = "Retired";

    public static bool IsActive(string state) => state is not (Failed or Retired);
}

public sealed class WorkspaceSlotRecord
{
    public string Id { get; set; } = "";
    public int Number { get; set; }
    public string State { get; set; } = WorkspaceSlotStates.Free;
    public int DirectoryGeneration { get; set; } = 1;
    public string Path { get; set; } = "";
    public int LeaseGeneration { get; set; }
    public Guid? TaskId { get; set; }
    public Guid? PreparationId { get; set; }
    public string? Branch { get; set; }
    public string? Sha { get; set; }
    public bool Cold { get; set; }
    public DateTimeOffset? LeasedAt { get; set; }
    public DateTimeOffset? IdleSince { get; set; }
    public string CreatedAs { get; set; } = "base";
    public Guid? CreatedByTask { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public WorkspaceSlotColdResetRecord? ColdResetPending { get; set; }
    public WorkspaceSlotColdResetRecord? LastColdReset { get; set; }
    public string? LastResetKind { get; set; }
    public List<DateTimeOffset> Rehomes { get; set; } = [];
    public int LingerAttempts { get; set; }
    public DateTimeOffset? NextLingerAt { get; set; }
    public bool ReleaseRequested { get; set; }
    public Guid? LastTaskId { get; set; }
    public string? Detail { get; set; }
}

public sealed class WorkspaceSlotColdResetRecord
{
    public string RequestId { get; set; } = "";
    public string Requester { get; set; } = "";
    public string Reason { get; set; } = "";
    public DateTimeOffset At { get; set; }
}

public sealed class QuarantinedDirectoryRecord
{
    public string DirId { get; set; } = "";
    public string Path { get; set; } = "";
    public string SlotId { get; set; } = "";
    public int DirectoryGeneration { get; set; }
    public string Class { get; set; } = "";
    public string Reason { get; set; } = "";
    public string Error { get; set; } = "";
    public string? Branch { get; set; }
    public string? Head { get; set; }
    public Guid? TaskId { get; set; }
    public long Bytes { get; set; }
    public bool Locked { get; set; }
    public string? LockError { get; set; }
    public DateTimeOffset At { get; set; }
    public string? Disposition { get; set; }
    public DateTimeOffset? ClosedAt { get; set; }
    public string? Outcome { get; set; }

    public bool IsOpen => ClosedAt is null;
}

public sealed class WorkspaceSlotAttention
{
    public string Key { get; set; } = "";
    public string SlotId { get; set; } = "";
    public string? Path { get; set; }
    public string? Detail { get; set; }
    public DateTimeOffset At { get; set; }
}

public sealed class WorkspaceSlotLedgerState
{
    public List<WorkspaceSlotRecord> Slots { get; set; } = [];
    public List<QuarantinedDirectoryRecord> QuarantinedDirectories { get; set; } = [];
    public List<WorkspaceSlotAttention> Attention { get; set; } = [];
    public DateTimeOffset? LastScheduledBoundary { get; set; }
    public int ScheduledMarks { get; set; }
    public List<string> ColdResetRequests { get; set; } = [];

    public WorkspaceSlotRecord? Slot(string id) => Slots.FirstOrDefault(slot => slot.Id == id);
}

/// <summary>
/// CARD-0824 S1. The durable slot ledger beside the binding store (<c>&lt;allowed&gt;/workspace-slots/</c>).
/// Every state transition is written before the git work it announces, as the binding store does,
/// so a crash leaves an intent that recovery evaluates. Writes are atomic replace.
/// </summary>
public sealed class RunnerWorkspaceSlotLedger(string stateRoot)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public string StateRoot { get; } = stateRoot;

    public string LedgerPath => StateRoot + "/ledger.json";

    public WorkspaceSlotLedgerState Read()
    {
        if (!File.Exists(LedgerPath))
            return new WorkspaceSlotLedgerState();
        var text = File.ReadAllText(LedgerPath);
        return JsonSerializer.Deserialize<WorkspaceSlotLedgerState>(text, Json)
               ?? throw new InvalidDataException("Workspace slot ledger is unreadable.");
    }

    public void Write(WorkspaceSlotLedgerState state)
    {
        Directory.CreateDirectory(StateRoot);
        var temporary = LedgerPath + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(state, Json));
        File.Move(temporary, LedgerPath, overwrite: true);
    }
}
