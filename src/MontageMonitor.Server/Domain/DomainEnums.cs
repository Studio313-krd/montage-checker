namespace MontageMonitor.Server.Domain;

public enum UserRole
{
    Owner = 0,
    Admin = 1,
    Manager = 2,
    Viewer = 3,
}

public enum AgentStatus
{
    Pending = 0,
    Online = 1,
    Offline = 2,
    Revoked = 3,
}

public enum ProcessingType
{
    Render = 0,
    Proxy = 1,
    Background = 2,
}

public enum WatchedFolderType
{
    Render = 0,
    Proxy = 1,
    Cache = 2,
}
