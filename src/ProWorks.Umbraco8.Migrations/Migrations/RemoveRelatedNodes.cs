using System.Linq;
using Umbraco.Core.Migrations;
using Umbraco.Core.Persistence;

namespace ProWorks.Umbraco8.Migrations
{
    public class RemoveRelatedNodes : MigrationBase
    {
        public RemoveRelatedNodes(IMigrationContext context) : base(context)
        {
        }

        public override void Migrate()
        {
            RemoveNexuRelations();
            RemoveDataType();
            RemoveComposition();
        }

        private void RemoveNexuRelations()
        {
            Database.Execute("DELETE FROM umbracoRelation WHERE relType in (SELECT id FROM umbracoRelationType WHERE alias IN ('nexuDocumentToDocument','nexuDocumentToMedia'))");
            Database.Execute("DELETE FROM umbracoRelationType WHERE alias IN ('nexuDocumentToDocument','nexuDocumentToMedia')");
        }

        private void RemoveDataType()
        {
            var ids = GetNodeIds("umbracoDataType WHERE propertyEditorAlias = 'Our.Umbraco.RelatedNodes.Display'");
            if (ids == null) return;

            // Remove data type from all doc types that use it, then remove data type
            Database.Execute($"DELETE FROM umbracoPropertyData WHERE propertytypeid IN (SELECT id FROM cmsPropertyType WHERE dataTypeId IN ({ids}))");
            Database.Execute($"DELETE FROM cmsPropertyType WHERE dataTypeId IN ({ids})");
            Database.Execute($"DELETE FROM umbracoDataType WHERE nodeId IN ({ids})");
            Database.Execute($"DELETE FROM umbracoNode WHERE id IN ({ids})");
        }

        private void RemoveComposition()
        {
            var ids = GetNodeIds("cmsContentType WHERE alias = 'relatedNodes'");
            if (ids == null) return;

            // Remove composition from all doc types that use it, then remove the composition
            Database.Execute($"DELETE FROM cmsContentType2ContentType WHERE parentContentTypeId IN ({ids})");
            Database.Execute($"DELETE FROM cmsPropertyTypeGroup WHERE contenttypeNodeId IN ({ids})");
            Database.Execute($"DELETE FROM cmsContentType WHERE nodeId IN ({ids})");
            Database.Execute($"DELETE FROM umbracoNode WHERE id IN ({ids})");
        }

        private string GetNodeIds(string dataSource)
        {
            var nodeDtos = Database.Fetch<NodeDto>("SELECT nodeId FROM " + dataSource);
            if (nodeDtos == null || nodeDtos.Count == 0) return null;

            var nodeIds = nodeDtos.Select(n => n.NodeId);
            return string.Join(",", nodeIds);
        }

        private class NodeDto
        {
            public int NodeId { get; set; }
        }
    }
}
