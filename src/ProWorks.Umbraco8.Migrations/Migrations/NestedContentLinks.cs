using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Collections.Generic;
using System.Linq;
using Umbraco.Core;
using Umbraco.Core.Migrations;

namespace ProWorks.Umbraco8.Migrations.Migrations
{
    public class NestedContentLinks : DataCorrectionMigration
    {
        public NestedContentLinks(IMigrationContext context) : base(context)
        {
        }

        protected override IEnumerable<string> EditorAliases => new[] { Constants.PropertyEditors.Aliases.NestedContent };

        protected override bool UpdateContent(int propertyTypeId, string editorAlias, string config, DataValues dataValues)
        {
            if (config.IsNullOrWhiteSpace() || !config.Contains("ncMenuLink") || dataValues.TextValue.IsNullOrWhiteSpace() || dataValues.TextValue[0] != '[') return false;

            var items = JArray.Parse(dataValues.TextValue);
            var anyChange = false;

            foreach (var item in items)
            {
                if (!(item is JObject obj) || !obj.ContainsKey("link") || !(obj["link"]?.ToString() is string menuLinksStr) || menuLinksStr.IsNullOrWhiteSpace() || menuLinksStr[0] != '[') continue;
                var hasNewLines = menuLinksStr.Contains("\\r\\n");
                var menuLinks = JArray.Parse(menuLinksStr);
                var hadChange = false;

                foreach (var menuLinkToken in menuLinks)
                {
                    if (!(menuLinkToken is JObject menuLink) || (!hasNewLines && (!menuLink.ContainsKey("caption") || !menuLink.ContainsKey("link") || !menuLink.ContainsKey("newWindow")))) continue;

                    hadChange = true;
                    var caption = menuLink["caption"]?.ToString() ?? menuLink["name"]?.ToString();
                    var link = menuLink["link"]?.ToString() ?? menuLink["url"]?.ToString();
                    var newWindow = bool.TryParse(menuLink["newWindow"]?.ToString(), out var b) ? b : (menuLink["target"]?.ToString() == "_blank");

                    menuLink.Properties().ToList().ForEach(p => menuLink.Remove(p.Name));

                    menuLink["name"] = caption;
                    menuLink["url"] = link;
                    if (newWindow) menuLink["target"] = "_blank";
                }

                if (!hadChange) continue;
                obj["link"] = menuLinks.ToString(Formatting.None);
                anyChange = true;
            }

            if (anyChange) dataValues.TextValue = items.ToString(Formatting.None);
            return anyChange;
        }
    }
}
