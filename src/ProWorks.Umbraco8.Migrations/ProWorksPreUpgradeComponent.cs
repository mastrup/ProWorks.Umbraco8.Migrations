using Semver;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using Umbraco.Core;
using Umbraco.Core.Composing;
using Umbraco.Core.Logging;
using Umbraco.Core.Persistence;

namespace ProWorks.Umbraco8.Migrations
{
    public class ProWorksPreUpgradeComponent : IComponent
    {
        public static SemVersion StartingVersion { get; } = SemVersion.TryParse(ConfigurationManager.AppSettings[Constants.AppSettings.ConfigurationStatus], out var v) ? v : new SemVersion(0);

        private readonly IPreUpgradeStep[] _preUpgradeSteps;
        private readonly IUmbracoDatabaseFactory _umbracoDatabaseFactory;
        private readonly ILogger _logger;

        public ProWorksPreUpgradeComponent(IEnumerable<IPreUpgradeStep> preUpgradeSteps, IUmbracoDatabaseFactory umbracoDatabaseFactory, ILogger logger)
        {
            _preUpgradeSteps = preUpgradeSteps.ToArray();
            _umbracoDatabaseFactory = umbracoDatabaseFactory;
            _logger = logger;
        }

        public void Initialize()
        {
            var hadException = false;

            try
            {
                using (var db = _umbracoDatabaseFactory.CreateDatabase())
                {
                    foreach (var step in _preUpgradeSteps)
                    {
                        try
                        {
                            if (step.ShouldExecute(StartingVersion)) step.Execute(db, _logger);
                        }
                        catch (Exception ex)
                        {
                            _logger.Error(step.GetType(), ex, "Could not execute the step");
                            hadException = true;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Error<ProWorksPreUpgradeComponent>(ex, "Could not execute the pre-upgrade steps");
                hadException = true;
            }

            if (hadException) throw new Exception("One or more pre-upgrade steps failed");
        }

        public void Terminate()
        {
        }
    }
}
