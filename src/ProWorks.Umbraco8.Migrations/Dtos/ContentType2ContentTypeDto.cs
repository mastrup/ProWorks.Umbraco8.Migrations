using NPoco;
using Umbraco.Core;
using Umbraco.Core.Persistence.DatabaseAnnotations;

namespace ProWorks.Umbraco8.Migrations.Dtos
{
    [TableName(Constants.DatabaseSchema.Tables.ElementTypeTree)]
    [ExplicitColumns]
    public class ContentType2ContentTypeDto
    {
        [Column("parentContentTypeId")]
        [PrimaryKeyColumn(AutoIncrement = false, Clustered = true, Name = "PK_cmsContentType2ContentType", OnColumns = "parentContentTypeId, childContentTypeId")]
        [ForeignKey(typeof(NodeDto), Name = "FK_cmsContentType2ContentType_umbracoNode_parent")]
        public int ParentId { get; set; }

        [Column("childContentTypeId")]
        [ForeignKey(typeof(NodeDto), Name = "FK_cmsContentType2ContentType_umbracoNode_child")]
        public int ChildId { get; set; }
    }
}
