using Umbraco.Core;
using Umbraco.Core.Composing;

namespace ProWorks.Umbraco8.Migrations
{
    [RuntimeLevel(MinLevel = RuntimeLevel.Run)]
    public class ProWorksMigrationComposer : IUserComposer
    {
        public void Compose(Composition composition)
        {
            var components = composition.Components();
            var types = components.GetTypes();
            var inserted = false;

            foreach (var type in types)
            {
                if (type.FullName != "uSync8.BackOffice.uSyncBackofficeComponent") continue;

                components.InsertBefore(type, typeof(ProWorksMigrationComponent));
                inserted = true;
                break;
            }

            if (!inserted) components.Append<ProWorksMigrationComponent>();
        }
    }
}
