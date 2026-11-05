namespace Antiphon.Server.Domain.Enums;

/// <summary>Desktop custody is recorded explicitly; directory presence never selects this value.</summary>
public enum DesktopWorkspaceKind { Checkout = 0, RefOnly = 1 }

public enum TaskRefWorkspacePreparationState { Planned = 0, RefCreated = 1, Published = 2 }

/// <summary>Whether there was a desktop cleanup component; NotRequired never claims a deletion.</summary>
public enum DesktopCleanupDisposition { Required = 0, NotRequired = 1 }
