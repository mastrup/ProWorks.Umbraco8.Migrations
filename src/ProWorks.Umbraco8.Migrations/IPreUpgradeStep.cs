using Semver;
using Umbraco.Core.Logging;
using Umbraco.Core.Persistence;

namespace ProWorks.Umbraco8.Migrations
{
    public interface IPreUpgradeStep
    {
        bool ShouldExecute(SemVersion currentVersion);
        void Execute(IUmbracoDatabase umbracoDatabase, ILogger logger);
    }
}
