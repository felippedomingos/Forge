namespace Forge.Domain.Entities;

// docs/010-Plugins.md - a concrete provider behind a swappable capability.
public class Plugin
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public PluginKind Kind { get; set; }
    public required string Version { get; set; }
    public string Configuration { get; set; } = "{}"; // jsonb
}
