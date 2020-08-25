using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NPoco;
using Umbraco.Core;
using Umbraco.Core.Migrations;
using Umbraco.Core.Migrations.PostMigrations;
using Umbraco.Core.Models;
using Umbraco.Core.Persistence;
using Umbraco.Core.Persistence.DatabaseAnnotations;
using Umbraco.Core.Persistence.DatabaseModelDefinitions;
using Umbraco.Core.PropertyEditors;

namespace ProWorks.Umbraco8.Migrations.Migrations
{
    public class StackedContentToBlockListEditor : MigrationBase
    {
        public StackedContentToBlockListEditor(IMigrationContext context) : base(context)
        {
        }

        public override void Migrate()
        {
            var dataTypes = GetDataTypes("Our.Umbraco.StackedContent");

            // Convert all content types used in stacked content data types to be element document types
            var refreshCache = MigrateElementTypes(dataTypes);

            // Convert all Stacked Content properties to Block List properties, both in the data types and in the property data
            refreshCache = Migrate(dataTypes, GetKnownDocumentTypes()) || refreshCache;

            // if some data types have been updated directly in the database (editing DataTypeDto and/or PropertyDataDto),
            // bypassing the services, then we need to rebuild the cache entirely, including the umbracoContentNu table
            if (refreshCache)
                Context.AddPostMigration<RebuildPublishedSnapshot>();
        }

        private List<DataTypeDto> GetDataTypes(string alias)
        {
            var sql = Sql()
                .Select<DataTypeDto>()
                .From<DataTypeDto>()
                .Where<DataTypeDto>(d => d.EditorAlias == alias);

            return Database.Fetch<DataTypeDto>(sql);
        }

        private bool MigrateElementTypes(List<DataTypeDto> dataTypes)
        {
            var contentUniqueIds = new List<Guid>();
            dataTypes.ForEach(d => contentUniqueIds.AddRange(GetUsedContentTypes(d)));

            var sql = Sql()
                .Select<NodeDto>()
                .From<NodeDto>()
                .WhereIn<NodeDto>(d => d.UniqueId, contentUniqueIds);
            var nodeIds = Database.Fetch<NodeDto>(sql).Select(d => d.NodeId).ToList();

            sql = Sql()
                .Select<ContentTypeDto>()
                .From<ContentTypeDto>()
                .WhereIn<ContentTypeDto>(d => d.NodeId, nodeIds);
            var dtos = Database.Fetch<ContentTypeDto>(sql);

            // Find all compositions used on content types used in a nested content data type
            sql = Sql()
                .Select<ContentType2ContentTypeDto>()
                .From<ContentType2ContentTypeDto>()
                .WhereIn<ContentType2ContentTypeDto>(c => c.ChildId, dtos.Select(d => d.NodeId));
            var c2cs = Database.Fetch<ContentType2ContentTypeDto>(sql);

            sql = Sql()
                .Select<ContentTypeDto>()
                .From<ContentTypeDto>()
                .WhereIn<ContentTypeDto>(d => d.NodeId, c2cs.Select(c => c.ParentId));
            dtos.AddRange(Database.Fetch<ContentTypeDto>(sql));

            foreach (var dto in dtos)
            {
                dto.IsElement = true;
                Database.Update(dto);
            }

            return dtos.Count > 0;
        }

        private IEnumerable<Guid> GetUsedContentTypes(DataTypeDto dto)
        {
            if (dto.Configuration.IsNullOrWhiteSpace() || dto.Configuration[0] != '{') return Enumerable.Empty<Guid>();

            var config = JsonConvert.DeserializeObject<StackedContentConfiguration>(dto.Configuration);
            return config.ContentTypes?.Select(c => c.IcContentTypeGuid) ?? Enumerable.Empty<Guid>();
        }

        private Dictionary<Guid, KnownContentType> GetKnownDocumentTypes()
        {
            var sql = Sql()
                .Select<ContentTypeDto>(r => r.Select(x => x.NodeDto))
                .From<ContentTypeDto>()
                .InnerJoin<NodeDto>()
                .On<ContentTypeDto, NodeDto>(c => c.NodeId, n => n.NodeId);

            var types = Database.Fetch<ContentTypeDto>(sql);
            var typeMap = new Dictionary<int, ContentTypeDto>(types.Count);
            types.ForEach(t => typeMap[t.NodeId] = t);

            sql = Sql()
                .Select<ContentType2ContentTypeDto>()
                .From<ContentType2ContentTypeDto>();
            var joins = Database.Fetch<ContentType2ContentTypeDto>(sql);
            // Find all relationships between types, either inherited or composited
            var joinLk = joins
                .Union(types
                    .Where(t => typeMap.ContainsKey(t.NodeDto.ParentId))
                    .Select(t => new ContentType2ContentTypeDto { ChildId = t.NodeId, ParentId = t.NodeDto.ParentId }))
                .ToLookup(j => j.ChildId, j => j.ParentId);

            sql = Sql()
                .Select<PropertyTypeDto>(r => r.Select(x => x.DataTypeDto))
                .From<PropertyTypeDto>()
                .InnerJoin<DataTypeDto>()
                .On<PropertyTypeDto, DataTypeDto>(c => c.DataTypeId, n => n.NodeId)
                .WhereIn<DataTypeDto>(d => d.EditorAlias, new[] { Constants.PropertyEditors.Aliases.NestedContent, Constants.PropertyEditors.Aliases.ColorPicker });
            var props = Database.Fetch<PropertyTypeDto>(sql);
            // Get all nested content and color picker property aliases by content type ID
            var propLk = props.ToLookup(p => p.ContentTypeId, p => p.Alias);

            var knownMap = new Dictionary<Guid, KnownContentType>(types.Count);
            types.ForEach(t => knownMap[t.NodeDto.UniqueId] = new KnownContentType(t.Alias, t.NodeDto.UniqueId, propLk[t.NodeId].Union(joinLk[t.NodeId].SelectMany(r => propLk[r])).ToArray()));
            return knownMap;
        }

        private bool Migrate(IEnumerable<DataTypeDto> dataTypesToMigrate, Dictionary<Guid, KnownContentType> knownDocumentTypes)
        {
            var refreshCache = false;

            foreach (var dataType in dataTypesToMigrate)
            {
                if (!dataType.Configuration.IsNullOrWhiteSpace())
                {
                    var config = UpdateConfiguration(dataType, knownDocumentTypes);

                    if (config.Blocks.Length > 0) UpdatePropertyData(dataType, config, knownDocumentTypes);
                }

                UpdateDataType(dataType);

                refreshCache = true;
            }

            return refreshCache;
        }

        private BlockListConfiguration UpdateConfiguration(DataTypeDto dataType, Dictionary<Guid, KnownContentType> knownDocumentTypes)
        {
            var old = JsonConvert.DeserializeObject<StackedContentConfiguration>(dataType.Configuration);
            var config = new BlockListConfiguration
            {
                Blocks = old.ContentTypes?.Select(t => new BlockListConfiguration.BlockConfiguration
                {
                    ContentElementTypeKey = knownDocumentTypes.TryGetValue(t.IcContentTypeGuid, out var ct) && ct.Key != Guid.Empty ? ct.Key : t.IcContentTypeGuid,
                    Label = t.NameTemplate,
                    EditorSize = "medium"
                }).Where(c => c.ContentElementTypeKey != null).ToArray(),
                UseInlineEditingAsDefault = old.SingleItemMode == "1" || old.SingleItemMode == bool.TrueString
            };

            if (int.TryParse(old.MaxItems, out var max) && max > 0)
            {
                config.ValidationLimit = new BlockListConfiguration.NumberRange { Max = max };
            }

            dataType.Configuration = ConfigurationEditor.ToDatabase(config);

            return config;
        }

        private void UpdatePropertyData(DataTypeDto dataType, BlockListConfiguration config, Dictionary<Guid, KnownContentType> knownDocumentTypes)
        {
            // get property data dtos
            var propertyDataDtos = Database.Fetch<PropertyDataDto>(Sql()
                .Select<PropertyDataDto>()
                .From<PropertyDataDto>()
                .InnerJoin<PropertyTypeDto>().On<PropertyTypeDto, PropertyDataDto>((pt, pd) => pt.Id == pd.PropertyTypeId)
                .InnerJoin<DataTypeDto>().On<DataTypeDto, PropertyTypeDto>((dt, pt) => dt.NodeId == pt.DataTypeId)
                .Where<PropertyTypeDto>(x => x.DataTypeId == dataType.NodeId));

            // update dtos
            var updatedDtos = propertyDataDtos.Where(x => UpdatePropertyDataDto(x, config, knownDocumentTypes));

            // persist changes
            foreach (var propertyDataDto in updatedDtos)
                Database.Update(propertyDataDto);
        }

        private bool UpdatePropertyDataDto(PropertyDataDto dto, BlockListConfiguration config, Dictionary<Guid, KnownContentType> knownDocumentTypes)
        {
            var model = new SimpleModel();

            if (dto != null && !dto.TextValue.IsNullOrWhiteSpace() && dto.TextValue[0] == '[')
            {
                var scObjs = JsonConvert.DeserializeObject<JObject[]>(dto.TextValue);
                foreach (var obj in scObjs) model.AddDataItem(obj, knownDocumentTypes);
            }

            dto.TextValue = JsonConvert.SerializeObject(model);

            return true;
        }

        private void UpdateDataType(DataTypeDto dataType)
        {
            dataType.DbType = ValueStorageType.Ntext.ToString();
            dataType.EditorAlias = Constants.PropertyEditors.Aliases.BlockList;

            Database.Update(dataType);
        }

        private class NcConfig
        {
            [JsonProperty("contentTypes")]
            public NcContentType[] ContentTypes { get; set; }
        }

        private class NcContentType
        {
            [JsonProperty("ncAlias")]
            public string Alias { get; set; }
        }

        private class BlockListConfiguration
        {
            [JsonProperty("blocks")]
            public BlockConfiguration[] Blocks { get; set; }

            public class BlockConfiguration
            {

                [JsonProperty("backgroundColor")]
                public string BackgroundColor { get; set; }

                [JsonProperty("iconColor")]
                public string IconColor { get; set; }

                [JsonProperty("thumbnail")]
                public string Thumbnail { get; set; }

                [JsonProperty("contentElementTypeKey")]
                public Guid ContentElementTypeKey { get; set; }

                [JsonProperty("settingsElementTypeKey")]
                public Guid? SettingsElementTypeKey { get; set; }

                [JsonProperty("view")]
                public string View { get; set; }

                [JsonProperty("stylesheet")]
                public string Stylesheet { get; set; }

                [JsonProperty("label")]
                public string Label { get; set; }

                [JsonProperty("editorSize")]
                public string EditorSize { get; set; }

                [JsonProperty("forceHideContentEditorInOverlay")]
                public bool ForceHideContentEditorInOverlay { get; set; }
            }

            [JsonProperty("validationLimit")]
            public NumberRange ValidationLimit { get; set; } = new NumberRange();

            public class NumberRange
            {
                [JsonProperty("min")]
                public int? Min { get; set; }

                [JsonProperty("max")]
                public int? Max { get; set; }
            }

            [JsonProperty("useLiveEditing")]
            public bool UseLiveEditing { get; set; }

            [JsonProperty("useInlineEditingAsDefault")]
            public bool UseInlineEditingAsDefault { get; set; }

            [JsonProperty("maxPropertyWidth")]
            public string MaxPropertyWidth { get; set; }
        }

        private class StackedContentConfiguration
        {

            public class StackedContentType
            {
                public Guid IcContentTypeGuid { get; set; }
                public string NameTemplate { get; set; }
            }

            public StackedContentType[] ContentTypes { get; set; }
            public string EnableCopy { get; set; }
            public string EnableFilter { get; set; }
            public string EnablePreview { get; set; }
            public string HideLabel { get; set; }
            public string MaxItems { get; set; }
            public string SingleItemMode { get; set; }
        }

        private class SimpleModel
        {
            [JsonProperty("layout")]
            public SimpleLayout Layout { get; } = new SimpleLayout();
            [JsonProperty("contentData")]
            public List<JObject> ContentData { get; } = new List<JObject>();

            public void AddDataItem(JObject obj, Dictionary<Guid, KnownContentType> knownDocumentTypes)
            {
                if (!Guid.TryParse(obj["key"].ToString(), out var key)) key = Guid.NewGuid();
                if (!Guid.TryParse(obj["icContentTypeGuid"].ToString(), out var ctGuid)) ctGuid = Guid.Empty;
                if (!knownDocumentTypes.TryGetValue(ctGuid, out var ct)) ct = new KnownContentType(null, ctGuid, null);

                obj.Remove("key");
                obj.Remove("icContentTypeGuid");

                var udi = new GuidUdi(Constants.UdiEntityType.Element, key).ToString();
                obj["udi"] = udi;
                obj["contentTypeKey"] = ct.Key;

                if (ct.StringToRawProperties != null && ct.StringToRawProperties.Length > 0)
                {
                    // Nested content inside a stacked content item used to be stored as a deserialized string of the JSON array
                    // Now we store the content as the raw JSON array, so we need to convert from the string form to the array
                    foreach (var prop in ct.StringToRawProperties)
                    {
                        var val = obj[prop];
                        var value = val?.ToString();
                        if (val != null && val.Type == JTokenType.String && !value.IsNullOrWhiteSpace())
                            obj[prop] = JsonConvert.DeserializeObject<JToken>(value);
                    }
                }

                ContentData.Add(obj);
                Layout.Refs.Add(new SimpleLayout.SimpleLayoutRef { ContentUdi = udi });
            }

            public class SimpleLayout
            {
                [JsonProperty(Constants.PropertyEditors.Aliases.BlockList)]
                public List<SimpleLayoutRef> Refs { get; } = new List<SimpleLayoutRef>();

                public class SimpleLayoutRef
                {
                    [JsonProperty("contentUdi")]
                    public string ContentUdi { get; set; }
                }
            }
        }

        private class KnownContentType
        {
            public KnownContentType(string alias, Guid key, string[] stringToRawProperties)
            {
                Alias = alias ?? throw new ArgumentNullException(nameof(alias));
                Key = key;
                StringToRawProperties = stringToRawProperties ?? throw new ArgumentNullException(nameof(stringToRawProperties));
            }

            public string Alias { get; }
            public Guid Key { get; }
            public string[] StringToRawProperties { get; }
        }

        [TableName(Constants.DatabaseSchema.Tables.DataType)]
        [PrimaryKey("nodeId", AutoIncrement = false)]
        [ExplicitColumns]
        private class DataTypeDto
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

        [TableName(TableName)]
        [PrimaryKey("pk")]
        [ExplicitColumns]
        private class ContentTypeDto
        {
            public const string TableName = Constants.DatabaseSchema.Tables.ContentType;

            [Column("pk")]
            [PrimaryKeyColumn(IdentitySeed = 535)]
            public int PrimaryKey { get; set; }

            [Column("nodeId")]
            [ForeignKey(typeof(NodeDto))]
            [Index(IndexTypes.UniqueNonClustered, Name = "IX_cmsContentType")]
            public int NodeId { get; set; }

            [Column("alias")]
            [NullSetting(NullSetting = NullSettings.Null)]
            public string Alias { get; set; }

            [Column("icon")]
            [Index(IndexTypes.NonClustered)]
            [NullSetting(NullSetting = NullSettings.Null)]
            public string Icon { get; set; }

            [Column("thumbnail")]
            [Constraint(Default = "folder.png")]
            public string Thumbnail { get; set; }

            [Column("description")]
            [NullSetting(NullSetting = NullSettings.Null)]
            [Length(1500)]
            public string Description { get; set; }

            [Column("isContainer")]
            [Constraint(Default = "0")]
            public bool IsContainer { get; set; }

            [Column("isElement")]
            [Constraint(Default = "0")]
            public bool IsElement { get; set; }

            [Column("allowAtRoot")]
            [Constraint(Default = "0")]
            public bool AllowAtRoot { get; set; }

            [Column("variations")]
            [Constraint(Default = "1" /*ContentVariation.InvariantNeutral*/)]
            public byte Variations { get; set; }

            [ResultColumn]
            [Reference(ReferenceType.OneToOne, ColumnName = "NodeId")]
            public NodeDto NodeDto { get; set; }
        }

        [TableName(TableName)]
        [PrimaryKey("id")]
        [ExplicitColumns]
        private class NodeDto
        {
            public const string TableName = Constants.DatabaseSchema.Tables.Node;
            public const int NodeIdSeed = 1060;
            private int? _userId;

            [Column("id")]
            [PrimaryKeyColumn(IdentitySeed = NodeIdSeed)]
            public int NodeId { get; set; }

            [Column("uniqueId")]
            [NullSetting(NullSetting = NullSettings.NotNull)]
            [Index(IndexTypes.UniqueNonClustered, Name = "IX_" + TableName + "_UniqueId")]
            [Constraint(Default = SystemMethods.NewGuid)]
            public Guid UniqueId { get; set; }

            [Column("parentId")]
            [ForeignKey(typeof(NodeDto))]
            [Index(IndexTypes.NonClustered, Name = "IX_" + TableName + "_ParentId")]
            public int ParentId { get; set; }

            [Column("level")]
            public short Level { get; set; }

            [Column("path")]
            [Length(150)]
            [Index(IndexTypes.NonClustered, Name = "IX_" + TableName + "_Path")]
            public string Path { get; set; }

            [Column("sortOrder")]
            public int SortOrder { get; set; }

            [Column("trashed")]
            [Constraint(Default = "0")]
            [Index(IndexTypes.NonClustered, Name = "IX_" + TableName + "_Trashed")]
            public bool Trashed { get; set; }

            [Column("nodeUser")] // TODO: db rename to 'createUserId'
            [NullSetting(NullSetting = NullSettings.Null)]
            public int? UserId { get => _userId == 0 ? null : _userId; set => _userId = value; } //return null if zero

            [Column("text")]
            [NullSetting(NullSetting = NullSettings.Null)]
            public string Text { get; set; }

            [Column("nodeObjectType")] // TODO: db rename to 'objectType'
            [NullSetting(NullSetting = NullSettings.Null)]
            [Index(IndexTypes.NonClustered, Name = "IX_" + TableName + "_ObjectType")]
            public Guid? NodeObjectType { get; set; }

            [Column("createDate")]
            [Constraint(Default = SystemMethods.CurrentDateTime)]
            public DateTime CreateDate { get; set; }
        }

        [TableName(Constants.DatabaseSchema.Tables.ElementTypeTree)]
        [ExplicitColumns]
        private class ContentType2ContentTypeDto
        {
            [Column("parentContentTypeId")]
            [PrimaryKeyColumn(AutoIncrement = false, Clustered = true, Name = "PK_cmsContentType2ContentType", OnColumns = "parentContentTypeId, childContentTypeId")]
            [ForeignKey(typeof(NodeDto), Name = "FK_cmsContentType2ContentType_umbracoNode_parent")]
            public int ParentId { get; set; }

            [Column("childContentTypeId")]
            [ForeignKey(typeof(NodeDto), Name = "FK_cmsContentType2ContentType_umbracoNode_child")]
            public int ChildId { get; set; }
        }

        [TableName(Constants.DatabaseSchema.Tables.PropertyType)]
        [PrimaryKey("id")]
        [ExplicitColumns]
        private class PropertyTypeDto
        {
            [Column("id")]
            [PrimaryKeyColumn(IdentitySeed = 50)]
            public int Id { get; set; }

            [Column("dataTypeId")]
            [ForeignKey(typeof(DataTypeDto), Column = "nodeId")]
            public int DataTypeId { get; set; }

            [Column("contentTypeId")]
            [ForeignKey(typeof(ContentTypeDto), Column = "nodeId")]
            public int ContentTypeId { get; set; }

            [Column("propertyTypeGroupId")]
            [NullSetting(NullSetting = NullSettings.Null)]
            public int? PropertyTypeGroupId { get; set; }

            [Index(IndexTypes.NonClustered, Name = "IX_cmsPropertyTypeAlias")]
            [Column("Alias")]
            public string Alias { get; set; }

            [Column("Name")]
            [NullSetting(NullSetting = NullSettings.Null)]
            public string Name { get; set; }

            [Column("sortOrder")]
            [Constraint(Default = "0")]
            public int SortOrder { get; set; }

            [Column("mandatory")]
            [Constraint(Default = "0")]
            public bool Mandatory { get; set; }

            [Column("mandatoryMessage")]
            [NullSetting(NullSetting = NullSettings.Null)]
            [Length(500)]
            public string MandatoryMessage { get; set; }

            [Column("validationRegExp")]
            [NullSetting(NullSetting = NullSettings.Null)]
            public string ValidationRegExp { get; set; }

            [Column("validationRegExpMessage")]
            [NullSetting(NullSetting = NullSettings.Null)]
            [Length(500)]
            public string ValidationRegExpMessage { get; set; }

            [Column("Description")]
            [NullSetting(NullSetting = NullSettings.Null)]
            [Length(2000)]
            public string Description { get; set; }

            [Column("variations")]
            [Constraint(Default = "1" /*ContentVariation.InvariantNeutral*/)]
            public byte Variations { get; set; }

            [ResultColumn]
            [Reference(ReferenceType.OneToOne, ColumnName = "DataTypeId")]
            public DataTypeDto DataTypeDto { get; set; }

            [Column("UniqueID")]
            [NullSetting(NullSetting = NullSettings.NotNull)]
            [Constraint(Default = SystemMethods.NewGuid)]
            [Index(IndexTypes.UniqueNonClustered, Name = "IX_cmsPropertyTypeUniqueID")]
            public Guid UniqueId { get; set; }
        }

        [TableName(TableName)]
        [PrimaryKey("id")]
        [ExplicitColumns]
        private class PropertyDataDto
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
            [Index(IndexTypes.UniqueNonClustered, Name = "IX_" + TableName + "_VersionId", ForColumns = "versionId,propertyTypeId,languageId,segment")]
            public int VersionId { get; set; }

            [Column("propertyTypeId")]
            [ForeignKey(typeof(PropertyTypeDto))]
            [Index(IndexTypes.NonClustered, Name = "IX_" + TableName + "_PropertyTypeId")]
            public int PropertyTypeId { get; set; }

            [Column("languageId")]
            [Index(IndexTypes.NonClustered, Name = "IX_" + TableName + "_LanguageId")]
            [NullSetting(NullSetting = NullSettings.Null)]
            public int? LanguageId { get; set; }

            [Column("segment")]
            [Index(IndexTypes.NonClustered, Name = "IX_" + TableName + "_Segment")]
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

            [ResultColumn]
            [Reference(ReferenceType.OneToOne, ColumnName = "PropertyTypeId")]
            public PropertyTypeDto PropertyTypeDto { get; set; }

            [Ignore]
            public object Value
            {
                get
                {
                    if (IntegerValue.HasValue)
                        return IntegerValue.Value;

                    if (DecimalValue.HasValue)
                        return DecimalValue.Value;

                    if (DateValue.HasValue)
                        return DateValue.Value;

                    if (!string.IsNullOrEmpty(VarcharValue))
                        return VarcharValue;

                    if (!string.IsNullOrEmpty(TextValue))
                        return TextValue;

                    return null;
                }
            }

            public PropertyDataDto Clone(int versionId)
            {
                return new PropertyDataDto
                {
                    VersionId = versionId,
                    PropertyTypeId = PropertyTypeId,
                    LanguageId = LanguageId,
                    Segment = Segment,
                    IntegerValue = IntegerValue,
                    DecimalValue = DecimalValue,
                    DateValue = DateValue,
                    VarcharValue = VarcharValue,
                    TextValue = TextValue,
                    PropertyTypeDto = PropertyTypeDto
                };
            }

            protected bool Equals(PropertyDataDto other)
            {
                return Id == other.Id;
            }

            public override bool Equals(object other)
            {
                return
                    !ReferenceEquals(null, other) // other is not null
                    && (ReferenceEquals(this, other) // and either ref-equals, or same id
                        || other is PropertyDataDto pdata && pdata.Id == Id);
            }

            public override int GetHashCode()
            {
                // ReSharper disable once NonReadonlyMemberInGetHashCode
                return Id;
            }
        }
    }
}