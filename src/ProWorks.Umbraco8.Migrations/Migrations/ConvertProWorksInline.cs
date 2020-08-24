using Newtonsoft.Json;
using NPoco;
using System;
using System.Collections.Generic;
using Umbraco.Core;
using Umbraco.Core.Migrations;
using Umbraco.Core.Migrations.PostMigrations;
using Umbraco.Core.Persistence;
using Umbraco.Core.Persistence.DatabaseAnnotations;

namespace ProWorks.Umbraco8.Migrations.Migrations
{
    public class ConvertProWorksInline : MigrationBase
    {
        public ConvertProWorksInline(IMigrationContext context) : base(context)
        {
        }

        public override void Migrate()
        {
            var sql = Sql().Select<DataTypeDto>().From<DataTypeDto>().Where<DataTypeDto>(d => d.EditorAlias == "ProWorksInlineHtmlHelpLabel");
            var dataTypes = Database.Fetch<DataTypeDto>(sql);

            foreach (var dataType in dataTypes)
            {
                ConvertDataType(dataType);
                Database.Update(dataType);
            }

            if (dataTypes.Count > 0)
                Context.AddPostMigration<RebuildPublishedSnapshot>();
        }

        private void ConvertDataType(DataTypeDto dataType)
        {
            var config = string.IsNullOrWhiteSpace(dataType?.Configuration) || dataType.Configuration[0] != '{' ? null : JsonConvert.DeserializeObject<ProWorksInlineConfig>(dataType.Configuration);

            var html = config?.Html;
            var updated = new NotesConfig { Notes = html };

            dataType.EditorAlias = "Umbraco.Community.Contentment.Notes";
            dataType.DbType = "Integer";
            dataType.Configuration = JsonConvert.SerializeObject(updated);
        }

        private class ProWorksInlineConfig
        {
            [JsonProperty("html")]
            public string Html { get; set; }
        }

        private class NotesConfig
        {
            [JsonProperty("notes")]
            public string Notes { get; set; }

            [JsonProperty("hideLabel")]
            public string HideLabel { get; set; } = "1";
        }

        [TableName(Constants.DatabaseSchema.Tables.DataType)]
        [PrimaryKey("nodeId", AutoIncrement = false)]
        [ExplicitColumns]
        private class DataTypeDto
        {
            [Column("nodeId")]
            [PrimaryKeyColumn(AutoIncrement = false)]
            public int NodeId { get; set; }

            [Column("propertyEditorAlias")]
            public string EditorAlias { get; set; }

            [Column("dbType")]
            [Length(50)]
            public string DbType { get; set; }

            [Column("config")]
            [SpecialDbType(SpecialDbTypes.NTEXT)]
            [NullSetting(NullSetting = NullSettings.Null)]
            public string Configuration { get; set; }
        }
    }
}
