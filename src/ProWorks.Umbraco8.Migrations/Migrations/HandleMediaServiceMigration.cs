using Semver;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Umbraco.Core;
using Umbraco.Core.Composing;
using Umbraco.Core.Events;
using Umbraco.Core.IO;
using Umbraco.Core.Logging;
using Umbraco.Core.Models;
using Umbraco.Core.Models.Entities;
using Umbraco.Core.Persistence;
using Umbraco.Core.Persistence.Querying;
using Umbraco.Core.Persistence.Repositories;
using Umbraco.Core.Scoping;
using Umbraco.Core.Services;
using Umbraco.Core.Services.Implement;

namespace ProWorks.Umbraco8.Migrations.Migrations
{
    /// <summary>
    /// This fixes a bug when upgrading from v7 to post-v8.1 directly, where it uses the media service in a v8.1 upgrade, but in v8.4 the required columns for the media service changed
    /// To overcome this, we created an IMediaService that returns what that upgrade step needs via a direct DB query, and for everything else returns the underlying result
    /// </summary>
    public class HandleMediaServiceMigration : IPreUpgradeComposer
    {
        public void Compose(Composition composition)
        {
            composition.RegisterUnique<IMediaService, UpgradeMediaService>();
        }

        public bool ShouldCompose(SemVersion currentVersion) => currentVersion.Major == 7;

        private class UpgradeMediaService : IMediaService
        {
            private const string MediaPathKeysSql7 = "SELECT m.mediaPath, n.uniqueID FROM cmsMedia m JOIN umbracoNode n ON m.nodeId = n.id WHERE m.mediaPath IS NOT NULL AND LTRIM(RTRIM(m.mediaPath)) <> ''";
            private const string MediaPathKeysSql8 = "SELECT m.[path] MediaPath, n.uniqueID FROM umbracoMediaVersion m JOIN umbracoContentVersion cv ON m.id = cv.id JOIN umbracoNode n ON cv.nodeId = n.id WHERE m.[path] IS NOT NULL AND LTRIM(RTRIM(m.[path])) <> ''";

            private readonly IMediaService _realMediaService;
            private readonly IUmbracoDatabaseFactory _umbracoDatabaseFactory;
            private readonly IScopeAccessor _scopeAccessor;
            private readonly SemaphoreSlim _semaphore = new SemaphoreSlim(1);
            private readonly ConcurrentDictionary<string, IMedia> _knownMediaByPath = new ConcurrentDictionary<string, IMedia>();

            public UpgradeMediaService(IScopeProvider provider, IMediaFileSystem mediaFileSystem, ILogger logger, IEventMessagesFactory eventMessagesFactory, IMediaRepository mediaRepository, IAuditRepository auditRepository, IMediaTypeRepository mediaTypeRepository, IEntityRepository entityRepository, IUmbracoDatabaseFactory umbracoDatabaseFactory, IScopeAccessor scopeAccessor)
            {
                _umbracoDatabaseFactory = umbracoDatabaseFactory;
                _scopeAccessor = scopeAccessor;
                _realMediaService = new MediaService(provider, mediaFileSystem, logger, eventMessagesFactory, mediaRepository, auditRepository, mediaTypeRepository, entityRepository);
            }

            public IMedia GetMediaByPath(string mediaPath)
            {
                if (_knownMediaByPath.TryGetValue(mediaPath, out var item)) return item;

                _semaphore.Wait();
                try
                {
                    if (_knownMediaByPath.Count == 0)
                    {
                        List<DbMediaEntry> entries;
                        var ambientScope = _scopeAccessor.AmbientScope;
                        if (ambientScope?.Database != null)
                        {
                            entries = GetEntries(ambientScope.Database);
                        }
                        else
                        {
                            using (var db = _umbracoDatabaseFactory.CreateDatabase())
                            {
                                entries = GetEntries(db);
                            }
                        }

                        foreach (var entry in entries)
                        {
                            var mPath = entry.MediaPath;
                            _knownMediaByPath[mPath] = new LazyMedia(entry.UniqueId, () => _realMediaService.GetMediaByPath(mPath));
                        }
                    }

                    return _knownMediaByPath.TryGetValue(mediaPath, out item) ? item : null;
                }
                catch
                {
                    return null;
                }
                finally
                {
                    _semaphore.Release();
                }
            }

            private List<DbMediaEntry> GetEntries(IUmbracoDatabase db)
            {
                var cnt = db.ExecuteScalar<int>("SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_SCHEMA='dbo' AND TABLE_NAME='cmsMedia'");

                return db.Fetch<DbMediaEntry>(cnt == 0 ? MediaPathKeysSql8 : MediaPathKeysSql7);
            }

            public ContentDataIntegrityReport CheckDataIntegrity(ContentDataIntegrityReportOptions options) => _realMediaService.CheckDataIntegrity(options);
            public int Count(string mediaTypeAlias = null) => _realMediaService.Count(mediaTypeAlias);
            public int CountChildren(int parentId, string mediaTypeAlias = null) => _realMediaService.CountChildren(parentId, mediaTypeAlias);
            public int CountDescendants(int parentId, string mediaTypeAlias = null) => _realMediaService.CountDescendants(parentId, mediaTypeAlias);
            public int CountNotTrashed(string contentTypeAlias = null) => _realMediaService.CountNotTrashed(contentTypeAlias);
            public IMedia CreateMedia(string name, Guid parentId, string mediaTypeAlias, int userId = -1) => _realMediaService.CreateMedia(name, parentId, mediaTypeAlias, userId);
            public IMedia CreateMedia(string name, int parentId, string mediaTypeAlias, int userId = -1) => _realMediaService.CreateMedia(name, parentId, mediaTypeAlias, userId);
            public IMedia CreateMedia(string name, IMedia parent, string mediaTypeAlias, int userId = -1) => _realMediaService.CreateMedia(name, parent, mediaTypeAlias, userId);
            public IMedia CreateMediaWithIdentity(string name, IMedia parent, string mediaTypeAlias, int userId = -1) => _realMediaService.CreateMediaWithIdentity(name, parent, mediaTypeAlias, userId);
            public IMedia CreateMediaWithIdentity(string name, int parentId, string mediaTypeAlias, int userId = -1) => _realMediaService.CreateMediaWithIdentity(name, parentId, mediaTypeAlias, userId);
            public Attempt<OperationResult> Delete(IMedia media, int userId = -1) => _realMediaService.Delete(media, userId);
            public void DeleteMediaFile(string filepath) => _realMediaService.DeleteMediaFile(filepath);
            public void DeleteMediaOfType(int mediaTypeId, int userId = -1) => _realMediaService.DeleteMediaOfType(mediaTypeId, userId);
            public void DeleteMediaOfTypes(IEnumerable<int> mediaTypeIds, int userId = -1) => _realMediaService.DeleteMediaOfTypes(mediaTypeIds, userId);
            public void DeleteVersion(int id, int versionId, bool deletePriorVersions, int userId = -1) => _realMediaService.DeleteVersion(id, versionId, deletePriorVersions, userId);
            public void DeleteVersions(int id, DateTime versionDate, int userId = -1) => _realMediaService.DeleteVersions(id, versionDate, userId);
            public OperationResult EmptyRecycleBin() => _realMediaService.EmptyRecycleBin();
            public OperationResult EmptyRecycleBin(int userId = -1) => _realMediaService.EmptyRecycleBin(userId);
            public IEnumerable<IMedia> GetAncestors(int id) => _realMediaService.GetAncestors(id);
            public IEnumerable<IMedia> GetAncestors(IMedia media) => _realMediaService.GetAncestors(media);
            public IMedia GetById(int id) => _realMediaService.GetById(id);
            public IMedia GetById(Guid key) => _realMediaService.GetById(key);
            public IEnumerable<IMedia> GetByIds(IEnumerable<int> ids) => _realMediaService.GetByIds(ids);
            public IEnumerable<IMedia> GetByIds(IEnumerable<Guid> ids) => _realMediaService.GetByIds(ids);
            public IEnumerable<IMedia> GetByLevel(int level) => _realMediaService.GetByLevel(level);
            public Stream GetMediaFileContentStream(string filepath) => _realMediaService.GetMediaFileContentStream(filepath);
            public long GetMediaFileSize(string filepath) => _realMediaService.GetMediaFileSize(filepath);
            public IEnumerable<IMedia> GetPagedChildren(int id, long pageIndex, int pageSize, out long totalRecords, IQuery<IMedia> filter = null, Ordering ordering = null) => _realMediaService.GetPagedChildren(id, pageIndex, pageSize, out totalRecords, filter, ordering);
            public IEnumerable<IMedia> GetPagedDescendants(int id, long pageIndex, int pageSize, out long totalRecords, IQuery<IMedia> filter = null, Ordering ordering = null) => _realMediaService.GetPagedDescendants(id, pageIndex, pageSize, out totalRecords, filter, ordering);
            public IEnumerable<IMedia> GetPagedMediaInRecycleBin(long pageIndex, int pageSize, out long totalRecords, IQuery<IMedia> filter = null, Ordering ordering = null) => _realMediaService.GetPagedMediaInRecycleBin(pageIndex, pageSize, out totalRecords, filter, ordering);
            public IEnumerable<IMedia> GetPagedOfType(int contentTypeId, long pageIndex, int pageSize, out long totalRecords, IQuery<IMedia> filter = null, Ordering ordering = null) => _realMediaService.GetPagedOfType(contentTypeId, pageIndex, pageSize, out totalRecords, filter, ordering);
            public IEnumerable<IMedia> GetPagedOfTypes(int[] contentTypeIds, long pageIndex, int pageSize, out long totalRecords, IQuery<IMedia> filter = null, Ordering ordering = null) => _realMediaService.GetPagedOfTypes(contentTypeIds, pageIndex, pageSize, out totalRecords, filter, ordering);
            public IMedia GetParent(int id) => _realMediaService.GetParent(id);
            public IMedia GetParent(IMedia media) => _realMediaService.GetParent(media);
            public IEnumerable<IMedia> GetRootMedia() => _realMediaService.GetRootMedia();
            public IMedia GetVersion(int versionId) => _realMediaService.GetVersion(versionId);
            public IEnumerable<IMedia> GetVersions(int id) => _realMediaService.GetVersions(id);
            public bool HasChildren(int id) => _realMediaService.HasChildren(id);
            public Attempt<OperationResult> Move(IMedia media, int parentId, int userId = -1) => _realMediaService.Move(media, parentId, userId);
            public Attempt<OperationResult> MoveToRecycleBin(IMedia media, int userId = -1) => _realMediaService.MoveToRecycleBin(media, userId);
            public Attempt<OperationResult> Save(IMedia media, int userId = -1, bool raiseEvents = true) => _realMediaService.Save(media, userId, raiseEvents);
            public Attempt<OperationResult> Save(IEnumerable<IMedia> medias, int userId = -1, bool raiseEvents = true) => _realMediaService.Save(medias, userId, raiseEvents);
            public void SetMediaFileContent(string filepath, Stream content) => _realMediaService.SetMediaFileContent(filepath, content);
            public bool Sort(IEnumerable<IMedia> items, int userId = -1, bool raiseEvents = true) => _realMediaService.Sort(items, userId, raiseEvents);

            private class DbMediaEntry
            {
                public string MediaPath { get; set; }
                public Guid UniqueId { get; set; }
            }

            private class LazyMedia : IMedia
            {
                private readonly Lazy<IMedia> _lookup;

                public LazyMedia(Guid key, Func<IMedia> lookup)
                {
                    Key = key;
                    _lookup = new Lazy<IMedia>(lookup);
                }

                public Guid Key { get; set; }

                public int ContentTypeId => _lookup.Value.ContentTypeId;
                public ISimpleContentType ContentType => _lookup.Value.ContentType;
                public int WriterId { get => _lookup.Value.WriterId; set => _lookup.Value.WriterId = value; }
                public int VersionId { get => _lookup.Value.VersionId; set => _lookup.Value.VersionId = value; }
                public ContentCultureInfosCollection CultureInfos { get => _lookup.Value.CultureInfos; set => _lookup.Value.CultureInfos = value; }
                public IEnumerable<string> AvailableCultures => _lookup.Value.AvailableCultures;
                public PropertyCollection Properties { get => _lookup.Value.Properties; set => _lookup.Value.Properties = value; }
                public string Name { get => _lookup.Value.Name; set => _lookup.Value.Name = value; }
                public int CreatorId { get => _lookup.Value.CreatorId; set => _lookup.Value.CreatorId = value; }
                public int ParentId { get => _lookup.Value.ParentId; set => _lookup.Value.ParentId = value; }
                public int Level { get => _lookup.Value.Level; set => _lookup.Value.Level = value; }
                public string Path { get => _lookup.Value.Path; set => _lookup.Value.Path = value; }
                public int SortOrder { get => _lookup.Value.SortOrder; set => _lookup.Value.SortOrder = value; }
                public bool Trashed => _lookup.Value.Trashed;
                public int Id { get => _lookup.Value.Id; set => _lookup.Value.Id = value; }
                public DateTime CreateDate { get => _lookup.Value.CreateDate; set => _lookup.Value.CreateDate = value; }
                public DateTime UpdateDate { get => _lookup.Value.UpdateDate; set => _lookup.Value.UpdateDate = value; }
                public DateTime? DeleteDate { get => _lookup.Value.DeleteDate; set => _lookup.Value.DeleteDate = value; }
                public bool HasIdentity => _lookup.Value.HasIdentity;

                public object DeepClone() => _lookup.Value.DeepClone();
                public string GetCultureName(string culture) => _lookup.Value.GetCultureName(culture);
                public IEnumerable<string> GetDirtyProperties() => _lookup.Value.GetDirtyProperties();
                public DateTime? GetUpdateDate(string culture) => _lookup.Value.GetUpdateDate(culture);
                public object GetValue(string propertyTypeAlias, string culture = null, string segment = null, bool published = false) => _lookup.Value.GetValue(propertyTypeAlias, culture, segment, published);
                public TValue GetValue<TValue>(string propertyTypeAlias, string culture = null, string segment = null, bool published = false) => _lookup.Value.GetValue<TValue>(propertyTypeAlias, culture, segment, published);
                public IEnumerable<string> GetWereDirtyProperties() => _lookup.Value.GetWereDirtyProperties();
                public bool HasProperty(string propertyTypeAlias) => _lookup.Value.HasProperty(propertyTypeAlias);
                public bool IsCultureAvailable(string culture) => _lookup.Value.IsCultureAvailable(culture);
                public bool IsDirty() => _lookup.Value.IsDirty();
                public bool IsPropertyDirty(string propName) => _lookup.Value.IsPropertyDirty(propName);
                public void ResetDirtyProperties(bool rememberDirty) => _lookup.Value.ResetDirtyProperties(rememberDirty);
                public void ResetDirtyProperties() => _lookup.Value.ResetDirtyProperties();
                public void ResetWereDirtyProperties() => _lookup.Value.ResetWereDirtyProperties();
                public void SetCultureName(string value, string culture) => _lookup.Value.SetCultureName(value, culture);
                public void SetParent(ITreeEntity parent) => _lookup.Value.SetParent(parent);
                public void SetValue(string propertyTypeAlias, object value, string culture = null, string segment = null) => _lookup.Value.SetValue(propertyTypeAlias, value, culture, segment);
                public bool WasDirty() => _lookup.Value.WasDirty();
                public bool WasPropertyDirty(string propertyName) => _lookup.Value.WasPropertyDirty(propertyName);
            }
        }
    }
}
