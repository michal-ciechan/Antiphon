namespace Antiphon.Server.Domain.Enums;

/// <summary>Desktop custody is recorded explicitly; directory presence never selects this value.</summary>
public enum DesktopWorkspaceKind { Checkout = 0, RefOnly = 1 }

public enum TaskRefWorkspacePreparationState { Planned = 0, RefCreated = 1, Published = 2 }
