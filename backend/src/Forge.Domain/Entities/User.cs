namespace Forge.Domain.Entities;

// docs/003-Domain.md §1, docs/000-Vision.md §6 personas.
public class User
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public required string Email { get; set; }
    // Free-text persona per docs/000-Vision.md §6 ("Product Owner / Founder", "Tech
    // Lead", ...), except the literal value "Admin" also grants access to POST /users
    // (docs/012-API.md) - a minimal authorization check, not a full RBAC system.
    public required string Role { get; set; }
    // BCrypt hash (docs/adr/ADR-0006) - never the plaintext password, never returned
    // by any endpoint.
    public required string PasswordHash { get; set; }
}
