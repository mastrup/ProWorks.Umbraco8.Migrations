using Semver;
using Umbraco.Core.Composing;

namespace ProWorks.Umbraco8.Migrations
{
    public interface IPreUpgradeComposer
    {
        void Compose(Composition composition);
        bool ShouldCompose(SemVersion currentVersion);
    }
}
