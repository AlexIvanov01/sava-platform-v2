namespace OrderService.Auditing;

public interface ICurrentUser
{
    // User name of the caller, or "system" when there is no HTTP request (events, sync, seeding).
    string Name { get; }
}
