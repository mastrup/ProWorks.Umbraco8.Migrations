using Semver;
using Umbraco.Core.Logging;
using Umbraco.Core.Persistence;

namespace ProWorks.Umbraco8.Migrations.Migrations
{
    public class FixAlphaVersion : IPreUpgradeStep
    {
        public void Execute(IUmbracoDatabase umbracoDatabase, ILogger logger)
        {
            var exists = umbracoDatabase.ExecuteScalar<int>("SELECT CAST(COUNT(*) AS int) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_SCHEMA = 'dbo' AND TABLE_NAME = 'umbracoKeyValue'");
            if (exists != 1)
            {
                logger.Debug<FixAlphaVersion>("The umbracoKeyValue table doesn't exist yet, no fix is needed");
                return;
            }

            exists = umbracoDatabase.ExecuteScalar<int>("SELECT CAST(COUNT(*) AS int) FROM umbracoKeyValue WHERE [key] = 'Umbraco.Core.Upgrader.State+Umbraco.Core' AND [value] = '{A93768A9-EAAD-44F5-8341-3CF61B676D65}'");
            if (exists <= 0)
            {
                logger.Debug<FixAlphaVersion>("The Umbraco.Core upgrade value is not the broken value for v8.6.4-alpha4, no fix is needed");
                return;
            }

            logger.Info<FixAlphaVersion>("Fixing the Umbraco.Core upgrade value");
            umbracoDatabase.Execute("UPDATE umbracoKeyValue SET [value] = '{a78e3369-8ea3-40ec-ad3f-5f76929d2b20}' WHERE [key] = 'Umbraco.Core.Upgrader.State+Umbraco.Core' AND [value] = '{A93768A9-EAAD-44F5-8341-3CF61B676D65}'");
        }

        public bool ShouldExecute(SemVersion currentVersion) => currentVersion.Major == 8 && currentVersion.Minor == 6 && currentVersion.Patch == 4 && currentVersion.Build != null && currentVersion.Build.Contains("alpha");
    }
}
