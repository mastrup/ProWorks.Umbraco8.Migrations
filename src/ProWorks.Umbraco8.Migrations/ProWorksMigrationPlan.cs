using Umbraco.Core.Migrations;

namespace ProWorks.Umbraco8.Migrations
{
    public class ProWorksMigrationPlan : MigrationPlan
    {
        public ProWorksMigrationPlan() : base("ProWorks-U8")
        {
            DefinePlan();
        }

        private void DefinePlan()
        {
            From(string.Empty);

            To<NestedContentUpgrade>();
            To<ColorPickerPreValueMigrator>();
            To<MultiNodeTreePickerPreValueMigrator>();
            To<SliderPreValueMigrator>();
            To<GridPreValueMigrator>();
            To<MultipleTextstringPreValueMigrator>();
            To<StackedContentToBlockListEditor>();
            To<MigrationInvalidData>();
            To<RemoveRelatedNodes>();
            To<ConvertProWorksInline>();
            To<NestedContentLinks>();
            To<BlockListLink>();
            To<UpdatePublishedVersionToLatest>();
        }

        private void To<TMigration>() where TMigration : IMigration => To<TMigration>($"{typeof(TMigration).FullName}");
    }
}
