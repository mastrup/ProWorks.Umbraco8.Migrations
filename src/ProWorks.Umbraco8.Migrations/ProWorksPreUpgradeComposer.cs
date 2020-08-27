using System;
using System.Linq;
using Umbraco.Core;
using Umbraco.Core.Composing;

namespace ProWorks.Umbraco8.Migrations
{
    [RuntimeLevel(MinLevel = RuntimeLevel.Upgrade)]
    [ComposeAfter(typeof(ICoreComposer))]
    public class ProWorksPreUpgradeComposer : IComposer
    {
        public void Compose(Composition composition)
        {
            composition.TypeLoader.GetTypes<IPreUpgradeStep>().ToList().ForEach(t => composition.Register(typeof(IPreUpgradeStep), t, Lifetime.Singleton));
            composition.Components().Insert<ProWorksPreUpgradeComponent>(0);

            composition.TypeLoader.GetTypes<IPreUpgradeComposer>().ToList().ForEach(t => {
                var instance = Activator.CreateInstance(t) as IPreUpgradeComposer;
                if (instance.ShouldCompose(ProWorksPreUpgradeComponent.StartingVersion)) instance.Compose(composition);
            });
        }
    }
}
