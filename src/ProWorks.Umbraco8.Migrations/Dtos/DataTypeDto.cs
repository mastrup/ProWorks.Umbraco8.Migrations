using NPoco;
using Umbraco.Core;
using Umbraco.Core.Persistence.DatabaseAnnotations;

namespace ProWorks.Umbraco8.Dtos
{
    [TableName(Constants.DatabaseSchema.Tables.DataType)]
    [PrimaryKey("nodeId", AutoIncrement = false)]
    [ExplicitColumns]
    internal class DataTypeDto
    {
        [Column("nodeId")]
        [PrimaryKeyColumn(AutoIncrement = false)]
        [ForeignKey(typeof(NodeDto))]
        public int NodeId { get; set; }

        [Column("propertyEditorAlias")]
        public string EditorAlias { get; set; } // TODO: should this have a length

        [Column("dbType")]
        [Length(50)]
        public string DbType { get; set; }

        [Column("config")]
        [SpecialDbType(SpecialDbTypes.NTEXT)]
        [NullSetting(NullSetting = NullSettings.Null)]
        public string Configuration { get; set; }

        [ResultColumn]
        [Reference(ReferenceType.OneToOne, ColumnName = "NodeId")]
        public NodeDto NodeDto { get; set; }
    }
}
