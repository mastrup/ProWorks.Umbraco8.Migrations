using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using Umbraco.Core;
using Umbraco.Core.Migrations;
using Umbraco.Core.PropertyEditors;
using static Umbraco.Core.Constants.PropertyEditors;

namespace ProWorks.Umbraco8.Migrations.Migrations
{
    public class MigrationInvalidData : DataCorrectionMigration
    {
        private readonly Dictionary<int, PropertyTypeInfo> _props = new Dictionary<int, PropertyTypeInfo>();

        protected override IEnumerable<string> EditorAliases => new[] { Aliases.CheckBoxList, Aliases.DropDownListFlexible, Aliases.RadioButtonList };

        public MigrationInvalidData(IMigrationContext context) : base(context)
        {
        }

        protected override bool UpdateContent(int propertyTypeId, string editorAlias, string config, DataValues dataValues)
        {
            if (!_props.TryGetValue(propertyTypeId, out var prop)) _props[propertyTypeId] = prop = new PropertyTypeInfo(propertyTypeId, editorAlias, config);

            if (prop.Values.Count == 0)
            {
                dataValues.TextValue = null;
                dataValues.IntegerValue = null;
                dataValues.VarcharValue = prop.Array ? "[]" : null;
                return true;
            }

            var value = dataValues.VarcharValue.IsNullOrWhiteSpace()
                ? (dataValues.TextValue.IsNullOrWhiteSpace()
                    ? (dataValues.IntegerValue.HasValue && dataValues.IntegerValue.Value != 0 ? dataValues.IntegerValue.Value.ToString() : "")
                    : dataValues.TextValue
                )
                : dataValues.VarcharValue;
            if (!value.IsNullOrWhiteSpace() && value[0] == '[') return false;

            var values = new List<string>();
            foreach (var val in value.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (int.TryParse(val, out var id) && prop.ValuesById.TryGetValue(id, out var newVal))
                {
                    values.Add(newVal);
                }
                else if (prop.Values.Contains(val, StringComparer.InvariantCultureIgnoreCase))
                {
                    values.Add(prop.Values.First(v => string.Equals(v, val, StringComparison.InvariantCultureIgnoreCase)));
                }
                else if (!values.Contains(prop.Values[0]))
                {
                    values.Add(prop.Values[0]);
                }
            }

            var vals = prop.Array ? new JArray(values).ToString(Formatting.None) : (values.Count > 0 ? JsonConvert.SerializeObject(values[0]) : null);
            if (vals == dataValues.VarcharValue && dataValues.TextValue == null && dataValues.IntegerValue == null) return false;

            dataValues.VarcharValue = vals;
            dataValues.TextValue = null;
            dataValues.IntegerValue = null;

            return true;
        }

        private class PropertyTypeInfo
        {
            public PropertyTypeInfo(int propertyTypeId, string editorAlias, string config)
            {
                PropertyTypeId = propertyTypeId;

                if (!config.IsNullOrWhiteSpace() && config[0] == '{')
                {
                    Array = editorAlias != Aliases.RadioButtonList;
                    var values = JsonConvert.DeserializeObject<ValueListConfiguration>(config);
                    foreach (var item in values.Items)
                    {
                        ValuesById[item.Id] = item.Value;
                        Values.Add(item.Value);
                    }
                }
            }

            public bool Array { get; set; }
            public Dictionary<int, string> ValuesById { get; set; } = new Dictionary<int, string>();
            public List<string> Values { get; set; } = new List<string>();
            public int PropertyTypeId { get; set; }
        }
    }
}
