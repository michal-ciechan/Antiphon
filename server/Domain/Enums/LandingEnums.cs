namespace Antiphon.Server.Domain.Enums;

public enum LandPhase
{
    Inspected = 0, RecoveryPinned = 1, RebaseStarted = 2, Prepared = 3, Verified = 4,
    TargetAdvanceStarted = 5, LocalTargetAdvanced = 6, PushStarted = 7,
    PublicationConfirmed = 8, CleanupStarted = 9, Complete = 10, Refused = 11,
}

public enum LandPublicationOutcome { Unconfirmed = 0, Landed = 1, AlreadyPresent = 2, Refused = 3 }
public enum LandCleanupStatus { NotStarted = 0, Pending = 1, Complete = 2, Refused = 3 }
public enum LandOperationMode { Fresh = 0, ResumePublication = 1, CleanupRetry = 2 }

public enum LandRequestState { Queued, Held, Running, NeedsResolution, Completed, Superseded, Canceled }
public enum LandNotificationKind { Held, Aged, Conflict, Outcome }
public enum LandNotificationState { Queued, RetryPending, AwaitingReceipt, Confirmed, NotRequired, DestinationUnavailable, Canceled, LegacyUnverified }

public enum LandSourceResolutionState { None = 0, Observed = 1, AdvanceStarted = 2, Resolved = 3 }

public enum LandSourceRelationship
{
    Unknown = 0,
    Equal = 1,
    Behind = 2,
    LocalAhead = 3,
    Diverged = 4,
    Missing = 5,
    Unavailable = 6,
}

public enum LandApprovalKind { ExplicitCaller = 0, ReviewEvidence = 1, InheritedResume = 2, LateBinding = 3 }
