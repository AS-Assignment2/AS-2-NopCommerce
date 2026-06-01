using FluentMigrator;
using Nop.Core.Domain.Events;
using Nop.Data.Extensions;

namespace Nop.Data.Migrations.UpgradeTo500;

// NoMatter so the outbox table is created on a FRESH install too — the default
// (Update) is only recorded-as-applied on install and never executed, which left
// the IntegrationEvent table missing on clean databases.
[NopSchemaMigration("2026-05-04 00:00:01", "IntegrationEvent spike migration", MigrationProcessType.NoMatter)]
public class IntegrationEventMigration : ForwardOnlyMigration
{
    /// <summary>
    /// Collect the UP migration expressions
    /// </summary>
    public override void Up()
    {
        this.CreateTableIfNotExists<IntegrationEvent>();
    }
}
