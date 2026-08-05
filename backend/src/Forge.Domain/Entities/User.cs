namespace Forge.Domain.Entities;

// docs/003-Domain.md §1, docs/000-Vision.md §6 personas.
public class User
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public required string Email { get; set; }
    public required string Role { get; set; }
}
