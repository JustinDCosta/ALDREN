namespace Aldren.Security.Gateway;

public enum CallOrigin
{
    User,
    Model,
}

public enum ToolOutcomeStatus
{
    Succeeded,
    UnknownTool,
    InvalidArguments,
    PermissionDenied,
    Forbidden,
    Declined,
    TimedOut,
    PostconditionFailed,
    Failed,
    AuditUnavailable,
}
