# ProWorks Umbraco 8 Migrations
This is a package of migrations that allow an Umbraco v7 system to be upgraded to the latest Umbraco v8 (currently v8.7 as of this writing) without having to stop in the middle.  It includes a number of conversions that have been pull requested into Umbraco, but which have not been included as yet.  It also includes a few migrations that are particular to scenarios ProWorks deal with, but these are written in such a way that if the situation doesn't exist, the migration will be skipped, and so the package as a whole can still be used by the community.

## Included
1. **BlockListLink** - Fixes how MultiUrlPickers are stored in block list (stacked content) data converted from v7
2. **ColorPickerPreValueMigrator** - See https://github.com/umbraco/Umbraco-CMS/issues/7939
3. **ConvertProWorksInline** - ProWorks Internal
4. **FixAlphaVersion** - ProWorks Internal
5. **GridPreValueMigrator** - See https://github.com/umbraco/Umbraco-CMS/issues/7939
6. **HandleMediaServiceMigration** - Fixes https://github.com/umbraco/Umbraco-CMS/issues/7914 by injecting a custom IMediaService if the current version in the config file is v7 (i.e. we are upgrading from v7).  The custom IMediaService works around the bug in that issue so that upgrades from v7 to v8.6+ can happen directly
7. **MigrationInvalidData** - See https://github.com/umbraco/Umbraco-CMS/issues/7939
8. **MultiNodeTreePickerPreValueMigrator** - See https://github.com/umbraco/Umbraco-CMS/issues/7939
9. **MultipleTextstringPreValueMigrator** - See https://github.com/umbraco/Umbraco-CMS/issues/7939
10. **NestedContentLinks** - See https://github.com/umbraco/Umbraco-CMS/issues/7939
11. **NestedContentUpgrade** - See https://github.com/umbraco/Umbraco-CMS/issues/7939
12. **RemoveRelatedNodes** - Removed Nexu relations since related nodes are tracked natively in v8.6+
13. **SliderPreValueMigrator** - See https://github.com/umbraco/Umbraco-CMS/issues/7939
14. **StackedContentToBlockListEditor** - Converts Stacked Content v7 data to Block List Editor v8 data, including converting document types to Element types
15. **UpdatePublishedVersionToLatest** - If before the upgrade there was a published version and a later unpublished version, after the upgrade the document is listed in an unpublished state, even though the published version's data was upgraded.  This migration restores the published state of the document, pointing to the previously published version, not the later unpublished version