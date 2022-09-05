using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Web;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NPoco;
using Umbraco.Core;
using Umbraco.Core.Migrations;
using Umbraco.Core.Migrations.PostMigrations;
using Umbraco.Core.Models;
using Umbraco.Core.Models.Blocks;
using Umbraco.Core.Persistence;
using Umbraco.Core.Persistence.DatabaseAnnotations;
using Umbraco.Core.Persistence.DatabaseModelDefinitions;
using Umbraco.Core.PropertyEditors;

namespace ProWorks.Umbraco8.Migrations.Migrations
{
    public class NestedContentToBlockListEditor : MigrationBase
    {
        public NestedContentToBlockListEditor(IMigrationContext context) : base(context)
        {
        }

        public override void Migrate()
        {
            var dataTypes = GetDataTypes(Constants.PropertyEditors.Aliases.NestedContent);

            var changes = MigrateElementTypes();

            // Convert all Stacked Content properties to Block List properties, both in the data types and in the property data
            changes = Migrate(dataTypes, GetKnownDocumentTypes()) || changes;

            // if some data types have been updated directly in the database (editing DataTypeDto and/or PropertyDataDto),
            // bypassing the services, then we need to rebuild the cache entirely, including the umbracoContentNu table
            if (changes)
                Context.AddPostMigration<RebuildPublishedSnapshot>();
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
            var old = JsonConvert.DeserializeObject<NcConfig>(dataType.Configuration);
            
            var config = new BlockListConfiguration
            {
                Blocks = old.ContentTypes?.Select(t =>
                {
                    var ct = knownDocumentTypes.Values.FirstOrDefault(x => x.Alias == t.Alias);
                    
                    if (ct is null)
                    {
                        return null;
                    }
                    
                    return new BlockListConfiguration.BlockConfiguration
                    {
                        ContentElementTypeKey = ct.Key,
                        Label = t.NameTemplate,
                        EditorSize = "medium"
                    };
                })
                    .WhereNotNull()
                    .ToArray(),
                UseInlineEditingAsDefault = old.MaxItems == 1
            };

            if (old.MaxItems > 0)
            {
                config.ValidationLimit = new BlockListConfiguration.NumberRange { Max = old.MaxItems, Min = old.MinItems};
            }

            dataType.Configuration = ConfigurationEditor.ToDatabase(config);

            return config;
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
        
        private void UpdateDataType(DataTypeDto dataType)
        {
            dataType.DbType = ValueStorageType.Ntext.ToString();
            dataType.EditorAlias = Constants.PropertyEditors.Aliases.BlockList;

            Database.Update(dataType);
        }

        private List<DataTypeDto> GetDataTypes(string alias)
        {
            var sql = Sql()
                .Select<DataTypeDto>()
                .From<DataTypeDto>()
                .Where<DataTypeDto>(d => d.EditorAlias == alias);

            return Database.Fetch<DataTypeDto>(sql);
        }

        private Dictionary<Guid, KnownContentType> GetKnownDocumentTypes()
        {
            var sql = Sql()
                .Select<ContentTypeDto>(r => r.Select(x => x.NodeDto))
                .From<ContentTypeDto>()
                .InnerJoin<NodeDto>()
                .On<ContentTypeDto, NodeDto>(c => c.NodeId, n => n.NodeId);

            var contentTypes = Database.Fetch<ContentTypeDto>(sql);
            var contentTypeMap = new Dictionary<int, ContentTypeDto>(contentTypes.Count);
            contentTypes.ForEach(contentType => contentTypeMap[contentType.NodeId] = contentType);

            sql = Sql()
                .Select<ContentType2ContentTypeDto>()
                .From<ContentType2ContentTypeDto>();

            var contentTypeJoins = Database.Fetch<ContentType2ContentTypeDto>(sql);

            // Find all relationships between types, either inherited or composited
            var contentTypeJoinsLookup = contentTypeJoins
                .Union(contentTypes
                    .Where(t => contentTypeMap.ContainsKey(t.NodeDto.ParentId))
                    .Select(t => new ContentType2ContentTypeDto {ChildId = t.NodeId, ParentId = t.NodeDto.ParentId}))
                .ToLookup(j => j.ChildId, j => j.ParentId);

            sql = Sql()
                .Select<PropertyTypeDto>(r => r.Select(x => x.DataTypeDto))
                .From<PropertyTypeDto>()
                .InnerJoin<DataTypeDto>()
                .On<PropertyTypeDto, DataTypeDto>(c => c.DataTypeId, n => n.NodeId)
                .WhereIn<DataTypeDto>(d => d.EditorAlias,
                    new[]
                    {
                       Constants.PropertyEditors.Aliases.ColorPicker
                    });
            var stringToRawProperties = Database.Fetch<PropertyTypeDto>(sql);
            // Get all nested content and color picker property aliases by content type ID
            var stringToRawPropertiesLookup = stringToRawProperties.ToLookup(p => p.ContentTypeId, p => p.Alias);

            sql = Sql()
                .Select<PropertyTypeDto>(r => r.Select(x => x.DataTypeDto))
                .From<PropertyTypeDto>()
                .InnerJoin<DataTypeDto>()
                .On<PropertyTypeDto, DataTypeDto>(c => c.DataTypeId, n => n.NodeId)
                .WhereIn<DataTypeDto>(d => d.EditorAlias,
                    new[]
                    {
                        Constants.PropertyEditors.Aliases.DropDownListFlexible,
                        Constants.PropertyEditors.Aliases.CheckBoxList
                    });
            var stringToJsonProperties = Database.Fetch<PropertyTypeDto>(sql);
            // Get all dropdownlist and checkboxlist property aliases by content type ID
            var stringToJsonPropertiesLookup = stringToJsonProperties.ToLookup(p => p.ContentTypeId, p => p.Alias);
            
            sql = Sql()
                .Select<PropertyTypeDto>(r => r.Select(x => x.DataTypeDto))
                .From<PropertyTypeDto>()
                .InnerJoin<DataTypeDto>()
                .On<PropertyTypeDto, DataTypeDto>(c => c.DataTypeId, n => n.NodeId)
                .WhereIn<DataTypeDto>(d => d.EditorAlias, new[] { Constants.PropertyEditors.Aliases.NestedContent });
            var nestedContentProperties = Database.Fetch<PropertyTypeDto>(sql);
            // Get all nested content and color picker property aliases by content type ID
            var nestedContentPropertiesLookup = nestedContentProperties.ToLookup(p => p.ContentTypeId, p => p.Alias);
            
            var knownContentTypesMap = new Dictionary<Guid, KnownContentType>(contentTypes.Count);

            contentTypes.ForEach(contentType =>
            {
                var stringToRawPropertyAliases = stringToRawPropertiesLookup[contentType.NodeId]
                    .Union(contentTypeJoinsLookup[contentType.NodeId]
                        .SelectMany(r => stringToJsonPropertiesLookup[r]))
                    .ToArray();

                var stringToJsonPropertyAliases = stringToJsonPropertiesLookup[contentType.NodeId]
                    .Union(contentTypeJoinsLookup[contentType.NodeId]
                        .SelectMany(r => stringToJsonPropertiesLookup[r]))
                    .ToArray();
                
                var nestedContentPropertyAliases = nestedContentPropertiesLookup[contentType.NodeId]
                    .Union(contentTypeJoinsLookup[contentType.NodeId]
                        .SelectMany(r => nestedContentPropertiesLookup[r]))
                    .ToArray();

                knownContentTypesMap[contentType.NodeDto.UniqueId] =
                    new KnownContentType(contentType.Alias, contentType.NodeDto.UniqueId, stringToRawPropertyAliases,
                        stringToJsonPropertyAliases, nestedContentPropertyAliases);
            });
            return knownContentTypesMap;
        }

        private bool MigrateElementTypes()
        {
            var sql = Sql()
                .Select<ContentTypeDto>()
                .From<ContentTypeDto>()
                .Where<ContentTypeDto>(d => d.IsElement);

            // Don't run this migration in a database where someone has already manually setup element types
            if (Database.Fetch<ContentTypeDto>(sql).Count > 0)
                return false;

            sql = Sql()
                .Select<DataTypeDto>()
                .From<DataTypeDto>()
                .Where<DataTypeDto>(d => d.EditorAlias == Constants.PropertyEditors.Aliases.NestedContent);

            // Find all content types that are used in a nested content data type
            var dataTypes = Database.Fetch<DataTypeDto>(sql);
            var contentTypeAliases = new List<string>();
            dataTypes.ForEach(d => contentTypeAliases.AddRange(GetUsedContentTypes(d)));

            sql = Sql()
                .Select<ContentTypeDto>()
                .From<ContentTypeDto>()
                .WhereIn<ContentTypeDto>(d => d.Alias, contentTypeAliases);
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

        private IEnumerable<string> GetUsedContentTypes(DataTypeDto dto)
        {
            if (dto.Configuration.IsNullOrWhiteSpace() || dto.Configuration[0] != '{')
                return Enumerable.Empty<string>();

            var config = JsonConvert.DeserializeObject<NcConfig>(dto.Configuration);
            return config.ContentTypes?.Select(c => c.Alias) ?? Enumerable.Empty<string>();
        }

        private class NcConfig
        {
            [JsonProperty("contentTypes")]
            public List<NcContentType> ContentTypes { get; set; }

            [JsonProperty("minItems")]
            public int MinItems { get; set; }

            [JsonProperty("maxItems")]
            public int MaxItems { get; set; }

            [JsonProperty("confirmDeletes")]
            public bool ConfirmDeletes { get; set; }

            [JsonProperty("showIcons")]
            public bool ShowIcons { get; set; }

            [JsonProperty("hideLabel")]
            public bool HideLabel { get; set; }
        }

        private class NcContentType
        {
            [JsonProperty("ncAlias")]
            public string Alias { get; set; }

            [JsonProperty("ncTabAlias")]
            public string TabAlias { get; set; }

            [JsonProperty("nameTemplate")]
            public string NameTemplate { get; set; }
        }

        private class KnownContentType
        {
            public KnownContentType(string alias, Guid key, string[] stringToRawProperties,
                string[] stringToJsonProperties, string[] nestedContentPropertyAliases)
            {
                Alias = alias;
                Key = key;
                StringToRawProperties = stringToRawProperties;
                StringToJsonProperties = stringToJsonProperties;
                NestedContentPropertyAliases = nestedContentPropertyAliases;
            }

            public string Alias { get; }
            public Guid Key { get; }
            public string[] StringToRawProperties { get; }
            public string[] StringToJsonProperties { get; }
            public string[] NestedContentPropertyAliases { get; }
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

            [Column("propertyEditorAlias")] public string EditorAlias { get; set; } // TODO: should this have a length

            [Column("dbType")] [Length(50)] public string DbType { get; set; }

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

            [Column("level")] public short Level { get; set; }

            [Column("path")]
            [Length(150)]
            [Index(IndexTypes.NonClustered, Name = "IX_" + TableName + "_Path")]
            public string Path { get; set; }

            [Column("sortOrder")] public int SortOrder { get; set; }

            [Column("trashed")]
            [Constraint(Default = "0")]
            [Index(IndexTypes.NonClustered, Name = "IX_" + TableName + "_Trashed")]
            public bool Trashed { get; set; }

            [Column("nodeUser")] // TODO: db rename to 'createUserId'
            [NullSetting(NullSetting = NullSettings.Null)]
            public int? UserId
            {
                get => _userId == 0 ? null : _userId;
                set => _userId = value;
            } //return null if zero

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
            [PrimaryKeyColumn(AutoIncrement = false, Clustered = true, Name = "PK_cmsContentType2ContentType",
                OnColumns = "parentContentTypeId, childContentTypeId")]
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
            [Column("id")] [PrimaryKeyColumn] public int Id { get; set; }

            [Column("versionId")]
            [Index(IndexTypes.UniqueNonClustered, Name = "IX_" + TableName + "_VersionId",
                ForColumns = "versionId,propertyTypeId,languageId,segment")]
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

        private class SimpleModel
        {
            [JsonProperty("layout")]
            public SimpleLayout Layout { get; } = new SimpleLayout();
            [JsonProperty("contentData")]
            public List<JObject> ContentData { get; } = new List<JObject>();

            public void AddDataItem(JObject obj, Dictionary<Guid, KnownContentType> knownDocumentTypes)
            {
                var alias = obj["ncContentTypeAlias"].ToString();
                var contentType = knownDocumentTypes.Values.FirstOrDefault(x => x.Alias == alias);
                if (contentType is null)
                {
                    return;
                }
                
                if (!Guid.TryParse(obj["key"].ToString(), out var key)) key = Guid.NewGuid();

                obj.Remove("key");
                obj.Remove("ncContentTypeAlias");

                var udi = new GuidUdi(Constants.UdiEntityType.Element, key).ToString();
                obj["udi"] = udi;
                obj["contentTypeKey"] = contentType.Key;

                if (contentType.StringToRawProperties != null && contentType.StringToRawProperties.Length > 0)
                {
                    // Nested content inside a stacked content item used to be stored as a deserialized string of the JSON array
                    // Now we store the content as the raw JSON array, so we need to convert from the string form to the array
                    foreach (var property in contentType.StringToRawProperties)
                    {
                        var jToken = obj[property];
                        var value = jToken?.ToString();
                        if (jToken != null && jToken.Type == JTokenType.String && !value.IsNullOrWhiteSpace())
                        {
                            obj[property] = JsonConvert.DeserializeObject<JToken>(value);
                        }
                    }
                }
                
                if (contentType.StringToJsonProperties != null && contentType.StringToJsonProperties.Length > 0)
                {
                    // Dropdownlist and checkboxlist inside a stacked content item used to be stored as a string
                    // Now we store the string as the raw JSON array, so we need to convert from the string form to the array
                    foreach (var property in contentType.StringToJsonProperties)
                    {
                        var jToken = obj[property];
                        var propertyValue = jToken?.ToString();

                        if (jToken == null ||
                            (jToken.Type != JTokenType.Integer &&
                             jToken.Type != JTokenType.String) ||
                            propertyValue.IsNullOrWhiteSpace() ||
                            propertyValue[0] == '[')
                        {
                            continue;
                        }
                        
                        var items = propertyValue
                            .Split(new[] {','}, StringSplitOptions.RemoveEmptyEntries)
                            .ToList();

                        var newValue = new JArray(items);

                        obj[property] = newValue;
                    }
                }
                
                if (contentType.NestedContentPropertyAliases != null && contentType.NestedContentPropertyAliases.Length > 0)
                {
                    foreach (var property in contentType.NestedContentPropertyAliases)
                    {
                        var jToken = obj[property];
                        
                        if (jToken == null || jToken.Type != JTokenType.String) 
                            continue;

                        var jTokenValue = jToken.ToString();

                        var nestedContentObjects = JsonConvert.DeserializeObject<IEnumerable<JObject>>(jTokenValue);
                        
                        foreach (var nestedContentObject in nestedContentObjects)
                        {
                            var subModel = new SimpleModel();
                            subModel.AddDataItem(nestedContentObject, knownDocumentTypes);
                            var subModelValue = JsonConvert.SerializeObject(subModel);

                            nestedContentObject[property] = subModelValue;
                        }

                        var value = JsonConvert.SerializeObject(nestedContentObjects);

                        obj[property] = value;
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
        
        public static string Unescape(string value)
        {
            return value.Replace(@"\\", string.Empty);
        }
    }

}
