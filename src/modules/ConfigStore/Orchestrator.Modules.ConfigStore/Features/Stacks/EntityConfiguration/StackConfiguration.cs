using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Orchestrator.Constants;
using Orchestrator.Modules.ConfigStore.Features.Stacks.Domain;

namespace Orchestrator.Modules.ConfigStore.Features.Stacks.EntityConfiguration;

sealed class StackConfiguration : IEntityTypeConfiguration<Stack>
{
    public void Configure(EntityTypeBuilder<Stack> builder)
    {
        builder.ToTable("stacks");

        // The stack id is the natural key: already stable, already unique, already what URLs and audit
        // events key on.
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).HasMaxLength(DbFieldLengths.Code).IsRequired();

        builder.Property(s => s.DisplayName).HasMaxLength(DbFieldLengths.Name).IsRequired();

        builder.Property(s => s.SubscriptionId).HasMaxLength(DbFieldLengths.Code).IsRequired();
        builder.Property(s => s.ResourceGroup).HasMaxLength(DbFieldLengths.Name).IsRequired();
        builder.Property(s => s.QueueServiceUri).HasMaxLength(DbFieldLengths.Url).IsRequired();
        builder.Property(s => s.QueueName).HasMaxLength(DbFieldLengths.Name).IsRequired();
        builder.Property(s => s.JobName).HasMaxLength(DbFieldLengths.Name).IsRequired();
        builder
            .Property(s => s.LogAnalyticsWorkspaceId)
            .HasMaxLength(DbFieldLengths.Code)
            .IsRequired();

        // Enums as strings, ds-connect's convention: the column stays readable in a manual query, and
        // inserting a vendor into the middle of the enum cannot silently re-point existing rows.
        builder
            .Property(s => s.Vendor)
            .HasConversion<string>()
            .HasMaxLength(DbFieldLengths.Code)
            .IsRequired();

        builder.Property(s => s.VendorProject).HasMaxLength(DbFieldLengths.Name);
        builder.Property(s => s.VendorRepo).HasMaxLength(DbFieldLengths.Name).IsRequired();
        builder.Property(s => s.VendorBaseUrl).HasMaxLength(DbFieldLengths.Url).IsRequired();
        builder.Property(s => s.VendorDeployment).HasMaxLength(DbFieldLengths.Code);

        // A Key Vault secret NAME. The store must never hold a credential value (design D6); the length
        // being name-sized rather than token-sized is a small structural nudge in that direction.
        builder
            .Property(s => s.ReadCredentialSecretName)
            .HasMaxLength(DbFieldLengths.Name)
            .IsRequired();

        builder.Property(s => s.LastWrittenBy).HasMaxLength(DbFieldLengths.Name).IsRequired();
        builder.Property(s => s.LastWrittenAt).IsRequired();

        // Cascade, so an orphan state entry is unrepresentable rather than merely unlikely (design D7).
        builder
            .HasMany(s => s.States)
            .WithOne()
            .HasForeignKey(m => m.StackId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

sealed class MappingStateConfiguration : IEntityTypeConfiguration<MappingState>
{
    public void Configure(EntityTypeBuilder<MappingState> builder)
    {
        builder.ToTable("mapping_states");

        // Composite natural key. A stack cannot declare the same native state twice, which the mapping
        // format already implies (its states are a keyed map) and which the database should enforce
        // rather than leave to whoever writes.
        builder.HasKey(m => new { m.StackId, m.NativeState });

        builder.Property(m => m.StackId).HasMaxLength(DbFieldLengths.Code).IsRequired();
        builder.Property(m => m.NativeState).HasMaxLength(DbFieldLengths.Text).IsRequired();
        builder.Property(m => m.Action).HasMaxLength(DbFieldLengths.Text).IsRequired();

        builder
            .Property(m => m.Runtime)
            .HasConversion<string>()
            .HasMaxLength(DbFieldLengths.Code)
            .IsRequired();

        builder.Property(m => m.OnSuccessState).HasMaxLength(DbFieldLengths.Text);
        builder.Property(m => m.OnFailureState).HasMaxLength(DbFieldLengths.Text).IsRequired();

        builder
            .Property(m => m.CodeHost)
            .HasConversion<string>()
            .HasMaxLength(DbFieldLengths.Code)
            .IsRequired();

        builder.Property(m => m.CodeRepo).HasMaxLength(DbFieldLengths.Name).IsRequired();

        builder.Property(m => m.LastWrittenBy).HasMaxLength(DbFieldLengths.Name).IsRequired();
        builder.Property(m => m.LastWrittenAt).IsRequired();

        // The query the whole change was partly motivated by: "which stacks dispatch this action".
        builder.HasIndex(m => m.Action);
    }
}
