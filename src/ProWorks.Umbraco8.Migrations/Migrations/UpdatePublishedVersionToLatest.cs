using Umbraco.Core.Migrations;
using Umbraco.Core.Migrations.PostMigrations;

namespace ProWorks.Umbraco8.Migrations
{
    public class UpdatePublishedVersionToLatest : MigrationBase
    {
        public UpdatePublishedVersionToLatest(IMigrationContext context) : base(context)
        {
        }

        public override void Migrate()
        {
            Context.AddPostMigration<PostUpdatePublishedVersionToLatest>();
        }
    }

    public class PostUpdatePublishedVersionToLatest : MigrationBase
    {
        private const string UpdateSql = "UPDATE d SET d.published = 1, d.edited = 1 " +
            "FROM umbracoDocument d JOIN umbracoContentVersion cv ON cv.nodeId = d.nodeId JOIN umbracoDocumentVersion dv ON cv.id = dv.id " +
            "WHERE dv.published = 1 AND d.published = 0 AND d.edited = 0 AND d.nodeId IN (SELECT vc.nodeId FROM umbracoContentVersion vc JOIN umbracoDocumentVersion vd ON vc.id = vd.id WHERE vc.[current] = 1 AND vd.published = 0)";
        private readonly IPublishedSnapshotRebuilder _rebuilder;

        public PostUpdatePublishedVersionToLatest(IMigrationContext context, IPublishedSnapshotRebuilder rebuilder) : base(context)
        {
            _rebuilder = rebuilder;
        }

        public override void Migrate()
        {
            // If, before the upgrade, there was a published version and a later saved version,
            // after the upgrade the document is listed in an unpublished state.  This query
            // fixes that by declaring that there is a published version and a more recent edited
            // version, since both are already in the database but just not declared as such.
            var updated = Database.Execute(UpdateSql);
            if (updated > 0) _rebuilder.Rebuild();
        }
    }
}
