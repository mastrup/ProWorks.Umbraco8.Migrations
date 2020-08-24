using NPoco;
using System;
using System.Collections.Generic;
using System.Linq;
using Umbraco.Core;
using Umbraco.Core.Migrations;
using Umbraco.Core.Migrations.PostMigrations;
using Umbraco.Core.Persistence;
using Umbraco.Core.Persistence.DatabaseAnnotations;

namespace ProWorks.Umbraco8.Migrations
{
    public abstract class DataCorrectionMigration : MigrationBase
    {
        private const string ContentTypePropertiesSql = "SELECT pt.contentTypeId, nc.uniqueID ContentTypeUniqueId, ct.alias ContentTypeAlias, pt.id PropertyTypeId, dt.propertyEditorAlias, dt.config DataTypeConfig, pt.Alias PropertyAlias FROM cmsPropertyType pt JOIN umbracoDataType dt ON pt.dataTypeId = dt.nodeId JOIN cmsContentType ct ON pt.contentTypeId = ct.nodeId JOIN umbracoNode nc ON ct.nodeId = nc.id WHERE propertyEditorAlias IN ('{0}')";
        private const string ParentToChildSql = "SELECT c2c.parentContentTypeId, c2c.childContentTypeId, nc.uniqueID ChildContentTypeUniqueId, cc.Alias ChildContentTypeAlias FROM cmsContentType2ContentType c2c JOIN cmsContentType cc ON c2c.childContentTypeId = cc.nodeId JOIN umbracoNode nc ON cc.nodeId = nc.id";

        public DataCorrectionMigration(IMigrationContext context) : base(context)
        {
        }

        public override void Migrate()
        {
            // if some data have been updated directly in the database (editing DataTypeDto and/or PropertyDataDto),
            // bypassing the services, then we need to rebuild the cache entirely, including the umbracoContentNu table
            if (MigrateData())
                Context.AddPostMigration<RebuildPublishedSnapshot>();
        }

        protected virtual bool MigrateData()
        {
            // Get all checkboxlist and dropdown data types so that we can identify them in the data
            var propsToUpdate = GetPropertiesToUpdate();
            if (propsToUpdate.Count == 0) return false;
            var propMap = new Dictionary<int, (int PropertyTypeId, string EditorAlias, string Config)>(propsToUpdate.Count);
            propsToUpdate.ForEach(p => propMap[p.PropertyTypeId] = p);

            var ptIds = string.Join(",", propsToUpdate.Select(p => p.PropertyTypeId));
            var sql = Sql($"SELECT pd.* FROM umbracoPropertyData pd JOIN umbracoContentVersion cv ON pd.versionId = cv.id LEFT JOIN umbracoDocumentVersion dv ON cv.id = dv.id WHERE (cv.[current] = 1 OR dv.published = 1) AND pd.propertytypeid IN ({ptIds})");
            var dtos = Database.Fetch<PropertyDataDto>(sql);
            var changes = false;

            foreach (var dto in dtos)
            {
                if (!propMap.TryGetValue(dto.PropertyTypeId, out var prop)) continue;

                var values = dto.ToDataValues();
                var updated = UpdateContent(prop.PropertyTypeId, prop.EditorAlias, prop.Config, values);
                if (!updated) continue;

                dto.FromDataValues(values);
                changes = true;
                Database.Update(dto);
            }

            return changes;
        }

        protected virtual List<(int PropertyTypeId, string EditorAlias, string Config)> GetPropertiesToUpdate()
        {
            var aliases = string.Join("', '", EditorAliases);
            var sql = Sql($"SELECT pt.contentTypeId ContentTypeId, pt.id PropertyTypeId, pt.Alias Alias, dt.propertyEditorAlias EditorAlias, dt.config Config FROM cmsPropertyType pt JOIN umbracoDataType dt ON pt.dataTypeId = dt.nodeId WHERE dt.propertyEditorAlias IN ('{aliases}')");
            var props = Database.Fetch<PropertyTypeDto>(sql);

            return props.Select(p => (p.PropertyTypeId, p.EditorAlias, p.Config)).ToList();
        }

        protected virtual List<(Guid ContentTypeUniqueId, string ContentTypeAlias, int PropertyTypeId, string PropertyEditorAlias, string DataTypeConfig, string PropertyAlias)> GetContentTypePropertiesForPropertyEditors(params string[] propertyEditorAliases)
        {
            var sql = Sql(string.Format(ContentTypePropertiesSql, string.Join("', '", propertyEditorAliases)));
            var ctProps = Database.Fetch<ContentTypeProperty>(sql);

            sql = Sql(ParentToChildSql);
            var p2c = Database.Fetch<ParentToChild>(sql);
            var p2cLk = p2c.ToLookup(p => p.ParentContentTypeId);

            return ctProps.SelectMany(p => GetPropertyOnAllRelatedContentTypes(p.ContentTypeId, p.ContentTypeUniqueId, p.ContentTypeAlias, p, p2cLk)).ToList();
        }

        protected virtual IEnumerable<(Guid, string, int, string, string, string)> GetPropertyOnAllRelatedContentTypes(int ctId, Guid ctUid, string ctAlias, ContentTypeProperty ctp, ILookup<int, ParentToChild> relationships)
        {
            yield return (ctUid, ctAlias, ctp.PropertyTypeId, ctp.PropertyEditorAlias, ctp.DataTypeConfig, ctp.PropertyAlias);

            foreach (var child in relationships[ctId])
            {
                foreach (var entry in GetPropertyOnAllRelatedContentTypes(child.ChildContentTypeId, child.ChildContentTypeUniqueId, child.ChildContentTypeAlias, ctp, relationships))
                    yield return entry;
            }
        }

        protected abstract IEnumerable<string> EditorAliases { get; }

        protected abstract bool UpdateContent(int propertyTypeId, string editorAlias, string config, DataValues dataValues);

        [TableName(TableName)]
        [PrimaryKey("id")]
        [ExplicitColumns]
        protected class PropertyDataDto
        {
            public const string TableName = Constants.DatabaseSchema.Tables.PropertyData;
            public const int VarcharLength = 512;
            public const int SegmentLength = 256;

            private decimal? _decimalValue;

            // pk, not used at the moment (never updating)
            [Column("id")]
            [PrimaryKeyColumn]
            public int Id { get; set; }

            [Column("versionId")]
            public int VersionId { get; set; }

            [Column("propertyTypeId")]
            public int PropertyTypeId { get; set; }

            [Column("languageId")]
            [NullSetting(NullSetting = NullSettings.Null)]
            public int? LanguageId { get; set; }

            [Column("segment")]
            [NullSetting(NullSetting = NullSettings.Null)]
            [Length(SegmentLength)]
            public string Segment { get; set; }

            [Column("intValue")]
            [NullSetting(NullSetting = NullSettings.Null)]
            public int? IntegerValue { get; set; }

            [Column("decimalValue")]
            [NullSetting(NullSetting = NullSettings.Null)]
            public decimal? DecimalValue
            {
                get => _decimalValue;
                set => _decimalValue = value?.Normalize();
            }

            [Column("dateValue")]
            [NullSetting(NullSetting = NullSettings.Null)]
            public DateTime? DateValue { get; set; }

            [Column("varcharValue")]
            [NullSetting(NullSetting = NullSettings.Null)]
            [Length(VarcharLength)]
            public string VarcharValue { get; set; }

            [Column("textValue")]
            [NullSetting(NullSetting = NullSettings.Null)]
            [SpecialDbType(SpecialDbTypes.NTEXT)]
            public string TextValue { get; set; }

            public DataValues ToDataValues() => new DataValues { DateValue = DateValue, DecimalValue = DecimalValue, IntegerValue = IntegerValue, LanguageId = LanguageId, Segment = Segment, TextValue = TextValue, VarcharValue = VarcharValue };

            public void FromDataValues(DataValues values)
            {
                DateValue = values.DateValue;
                DecimalValue = values.DecimalValue;
                IntegerValue = values.IntegerValue;
                LanguageId = values.LanguageId;
                Segment = values.Segment;
                TextValue = values.TextValue;
                VarcharValue = values.VarcharValue;
            }
        }

        protected class PropertyTypeDto
        {
            public int ContentTypeId { get; set; }
            public int PropertyTypeId { get; set; }
            public string Alias { get; set; }
            public string EditorAlias { get; set; }
            public string Config { get; set; }
        }

        protected class ContentTypeProperty
        {
            public int ContentTypeId { get; set; }
            public Guid ContentTypeUniqueId { get; set; }
            public string ContentTypeAlias { get; set; }
            public int PropertyTypeId { get; set; }
            public string PropertyEditorAlias { get; set; }
            public string DataTypeConfig { get; set; }
            public string PropertyAlias { get; set; }
        }

        protected class ParentToChild
        {
            public int ParentContentTypeId { get; set; }
            public int ChildContentTypeId { get; set; }
            public Guid ChildContentTypeUniqueId { get; set; }
            public string ChildContentTypeAlias { get; set; }
        }

        protected sealed class DataValues
        {
            public int? LanguageId { get; set; }
            public string Segment { get; set; }
            public int? IntegerValue { get; set; }
            public decimal? DecimalValue { get; set; }
            public DateTime? DateValue { get; set; }
            public string VarcharValue { get; set; }
            public string TextValue { get; set; }
        }
    }
}
