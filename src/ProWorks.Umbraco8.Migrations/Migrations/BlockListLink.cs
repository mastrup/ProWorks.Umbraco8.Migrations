using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using Umbraco.Core;
using Umbraco.Core.Migrations;

namespace ProWorks.Umbraco8.Migrations.Migrations
{
    public class BlockListLink : DataCorrectionMigration
    {
        private List<(Guid ContentTypeUniqueId, string ContentTypeAlias, int PropertyTypeId, string PropertyEditorAlias, string DataTypeConfig, string PropertyAlias)> _ctps;

        public BlockListLink(IMigrationContext context) : base(context)
        {
        }

        protected override IEnumerable<string> EditorAliases => new[] { Constants.PropertyEditors.Aliases.BlockList, Constants.PropertyEditors.Aliases.NestedContent };

        public override void Migrate()
        {
            _ctps = GetContentTypePropertiesForPropertyEditors(Constants.PropertyEditors.Aliases.BlockList, Constants.PropertyEditors.Aliases.NestedContent, Constants.PropertyEditors.Aliases.MultiUrlPicker);
            base.Migrate();
        }

        protected override bool UpdateContent(int propertyTypeId, string editorAlias, string config, DataValues dataValues)
        {
            if (config.IsNullOrWhiteSpace() || dataValues.TextValue.IsNullOrWhiteSpace()) return false;
            var data = JsonConvert.DeserializeObject(dataValues.TextValue) as JToken;

            if (!UpdateContainer(data, editorAlias)) return false;

            dataValues.TextValue = data.ToString(Formatting.None);

            return true;
        }

        private bool UpdateContainer(JToken container, string editorAlias)
        {
            var isNested = editorAlias == Constants.PropertyEditors.Aliases.NestedContent;
            var finder = isNested ? (Func<JObject, List<(string PropertyEditorAlias, string PropertyAlias)>>)NestedContentPropertyFinder : BlockListPropertyFinder;
            var items = (isNested ? container : (container as JObject)?["data"]) as JArray;

            return UpdateItems(items, finder);
        }

        private List<(string PropertyEditorAlias, string PropertyAlias)> NestedContentPropertyFinder(JObject item)
        {
            if (item == null || !item.ContainsKey("ncContentTypeAlias") || !(item["ncContentTypeAlias"]?.ToString() is string alias)) return new List<(string, string)>();
            return _ctps.Where(c => c.ContentTypeAlias == alias).Select(c => (c.PropertyEditorAlias, c.PropertyAlias)).ToList();
        }

        private List<(string PropertyEditorAlias, string PropertyAlias)> BlockListPropertyFinder(JObject item)
        {
            if (item == null || !item.ContainsKey("contentTypeKey") || !Guid.TryParse(item["contentTypeKey"]?.ToString(), out var key)) return new List<(string, string)>();
            return _ctps.Where(c => c.ContentTypeUniqueId == key).Select(c => (c.PropertyEditorAlias, c.PropertyAlias)).ToList();
        }

        private bool UpdateItems(JArray items, Func<JObject, List<(string PropertyEditorAlias, string PropertyAlias)>> relevantPropertiesFinder)
        {
            if (items == null || items.Count == 0) return false;

            var anyChange = false;

            foreach (var item in items)
            {
                var obj = item as JObject;
                var relevantProperties = relevantPropertiesFinder(obj);
                if (relevantProperties.Count == 0) continue;

                foreach (var relevantProperty in relevantProperties)
                {
                    var value = obj.ContainsKey(relevantProperty.PropertyAlias) ? obj[relevantProperty.PropertyAlias] : null;
                    if (value == null || value.Type == JTokenType.Null) continue;

                    if (relevantProperty.PropertyEditorAlias != Constants.PropertyEditors.Aliases.MultiUrlPicker) anyChange = UpdateContainer(value, relevantProperty.PropertyEditorAlias) || anyChange;
                    else if (value.Type != JTokenType.String) continue;
                    else
                    {
                        anyChange = true;
                        var val = JsonConvert.DeserializeObject(value?.ToString()) as JToken;
                        obj[relevantProperty.PropertyAlias] = val;
                    }
                }
            }

            return anyChange;
        }
    }
}
