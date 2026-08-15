using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

// Physically under _MalinovStation per the fork's project-folder convention, but kept in the vanilla
// Content.Server.Database namespace (matching Model.CustomVoteLog.cs's own file-per-feature pattern) so it
// plugs into ServerDbContext without extra usings.
#pragma warning disable IDE0130 // Namespace does not match folder structure
namespace Content.Server.Database;
#pragma warning restore IDE0130

//
// Milestone 9 (AI Players): optional cross-round persistence for an AI player's personality and most
// important memories, keyed by an admin-chosen PersistentId string rather than a NetUserId - AI players
// aren't tied to any player account. See Content.Server/_MalinovStation/AIPlayers/Systems/AiPlayerPersistenceSystem.cs
// for how this gets loaded/saved.
//

internal static class ModelAiPlayerPersistence
{
    public static void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AiPlayerPersonality>()
            .HasKey(p => p.PersistentId);

        modelBuilder.Entity<AiPlayerMemoryRecord>()
            .HasOne(m => m.Personality)
            .WithMany(p => p.Memories)
            .HasForeignKey(m => m.PersistentId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<AiPlayerMemoryRecord>()
            .HasIndex(m => m.PersistentId);
    }
}

/// <summary>
/// One saved AI player's personality traits, keyed by the admin-chosen PersistentId given at spawn time.
/// </summary>
public sealed class AiPlayerPersonality
{
    [Key, Required, MaxLength(64)]
    public string PersistentId { get; set; } = default!;

    public float Sociability { get; set; }
    public float Courage { get; set; }
    public float Curiosity { get; set; }
    public float Laziness { get; set; }
    public float Greed { get; set; }
    public float Aggression { get; set; }
    public float Loyalty { get; set; }
    public float RiskTolerance { get; set; }
    public float AuthorityRespect { get; set; }
    public float Professionalism { get; set; }
    public float Empathy { get; set; }
    public float Honesty { get; set; }
    public float Impulsiveness { get; set; }

    public DateTime LastSavedAt { get; set; }

    public List<AiPlayerMemoryRecord> Memories { get; set; } = new();
}

/// <summary>
/// One saved memory belonging to an <see cref="AiPlayerPersonality"/>. Deliberately has no foreign key to
/// "who" the memory is about: EntityUids aren't stable across rounds/restarts, so only the free-text
/// content (which already describes who/what in human-readable form) is persisted.
/// </summary>
public sealed class AiPlayerMemoryRecord
{
    public int Id { get; set; }

    [Required, MaxLength(64)]
    public string PersistentId { get; set; } = default!;

    public DateTime Timestamp { get; set; }
    public float Importance { get; set; }
    public float EmotionalWeight { get; set; }

    [Required, MaxLength(32)]
    public string Source { get; set; } = default!;

    [Required, MaxLength(500)]
    public string Content { get; set; } = default!;

    public AiPlayerPersonality? Personality { get; set; }
}
