using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Autofac;
using Autofac.Core;
using Common.Logging;
using Easy.MessageHub;
using JetBrains.Annotations;
using Remembrance.Contracts.DAL;
using Remembrance.Contracts.DAL.Local;
using Remembrance.Contracts.Sync;
using Remembrance.Resources;
using Scar.Common.DAL;
using Scar.Common.DAL.Model;
using Scar.Common.Messages;

namespace Remembrance.Core.Sync
{
    [UsedImplicitly]
    internal sealed class RepositorySynhronizer<TEntity, TId, TRepository> : IRepositorySynhronizer
        where TRepository : ITrackedRepository<TEntity, TId>
        where TEntity : IEntity<TId>, ITrackedEntity
    {
        [NotNull]
        private readonly ILocalSettingsRepository _localSettingsRepository;

        [NotNull]
        private readonly ILog _logger;

        [NotNull]
        private readonly IMessageHub _messageHub;

        [NotNull]
        private readonly INamedInstancesFactory _namedInstancesFactory;

        [NotNull]
        private readonly TRepository _ownRepository;

        [NotNull]
        private readonly ICollection<ISyncExtender<TEntity, TId, TRepository>> _syncExtenders;

        [CanBeNull]
        private readonly ISyncPostProcessor<TEntity> _syncPostProcessor;

        [CanBeNull]
        private readonly ISyncPreProcessor<TEntity> _syncPreProcessor;

        public RepositorySynhronizer(
            [NotNull] INamedInstancesFactory namedInstancesFactory,
            [NotNull] ILog logger,
            [NotNull] TRepository ownRepository,
            [NotNull] IMessageHub messageHub,
            [NotNull] ILocalSettingsRepository localSettingsRepository,
            [NotNull] ICollection<ISyncExtender<TEntity, TId, TRepository>> syncExtenders,
            [CanBeNull] ISyncPreProcessor<TEntity> syncPreProcessor = null,
            [CanBeNull] ISyncPostProcessor<TEntity> syncPostProcessor = null)
        {
            _ownRepository = ownRepository;
            _localSettingsRepository = localSettingsRepository ?? throw new ArgumentNullException(nameof(localSettingsRepository));
            _syncExtenders = syncExtenders ?? throw new ArgumentNullException(nameof(syncExtenders));
            _messageHub = messageHub ?? throw new ArgumentNullException(nameof(messageHub));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _namedInstancesFactory = namedInstancesFactory ?? throw new ArgumentNullException(nameof(namedInstancesFactory));
            _syncPreProcessor = syncPreProcessor;
            _syncPostProcessor = syncPostProcessor;
        }

        public string FileName => _ownRepository.DbFileName;

        public void SyncRepository(string filePath)
        {
            ApplyRemoteRepositoryAction(filePath, SyncInternal);
        }

        private void ApplyRemoteRepositoryAction([NotNull] string filePath, [NotNull] Action<TRepository> action)
        {
            var fileName = Path.GetFileNameWithoutExtension(filePath);
            var extension = Path.GetExtension(filePath);
            if (extension != _ownRepository.DbFileExtension)
            {
                throw new NotSupportedException($"Improper repository file extension: {filePath}");
            }

            // Copy is needed because LiteDB changes the remote file when creation a repository over it and it could lead to the conflicts.
            var newDirectoryPath = Path.GetTempPath();
            var newFileName = Path.Combine(newDirectoryPath, fileName + extension);
            if (File.Exists(newFileName))
            {
                File.Delete(newFileName);
            }

            File.Copy(filePath, newFileName);
            var parameters = new Parameter[]
            {
                new PositionalParameter(0, newDirectoryPath),
                new TypedParameter(typeof(bool), false)
            };
            using (var remoteRepository = _namedInstancesFactory.GetInstance<TRepository>(parameters))
            {
                foreach (var syncExtender in _syncExtenders)
                {
                    syncExtender.OnSynchronizing(remoteRepository);
                }

                action(remoteRepository);
            }

            File.Delete(newFileName);
        }

        private void SyncInternal([NotNull] TRepository remoteRepository)
        {
            var localSettings = _localSettingsRepository.Get();
            if (!localSettings.SyncTimes.TryGetValue(FileName, out var lastSyncTime))
            {
                lastSyncTime = DateTimeOffset.MinValue;
            }

            var changed = remoteRepository.GetModifiedAfter(lastSyncTime);
            Parallel.ForEach(
                changed,
                async remoteEntity =>
                {
                    try
                    {
                        _logger.TraceFormat("Processing {0}...", remoteEntity);
                        var existingEntity = _ownRepository.TryGetById(remoteEntity.Id);
                        var insert = false;
                        if (!Equals(existingEntity, default(TEntity)))
                        {
                            if (remoteEntity.ModifiedDate <= existingEntity.ModifiedDate)
                            {
                                _logger.DebugFormat("Existing entity {0} is newer than the remote one {1}", existingEntity, remoteEntity);
                                return;
                            }
                        }
                        else
                        {
                            insert = true;
                        }

                        if (_syncPreProcessor != null)
                        {
                            if (!await _syncPreProcessor.BeforeEntityChangedAsync(existingEntity, remoteEntity).ConfigureAwait(false))
                            {
                                _logger.DebugFormat("Preprocessor condition not satisfied for {0}", remoteEntity);
                                return;
                            }
                        }

                        if (insert)
                        {
                            _ownRepository.Insert(remoteEntity);
                            _logger.InfoFormat("{0} inserted", remoteEntity);
                        }
                        else
                        {
                            _ownRepository.Update(remoteEntity);
                            _logger.InfoFormat("{0} updated", remoteEntity);
                        }

                        if (_syncPostProcessor != null)
                        {
                            await _syncPostProcessor.AfterEntityChangedAsync(existingEntity, remoteEntity).ConfigureAwait(false);
                        }

                        localSettings.SyncTimes[FileName] = DateTimeOffset.UtcNow;
                        _localSettingsRepository.UpdateOrInsert(localSettings);
                    }
                    catch (Exception ex)
                    {
                        _messageHub.Publish(
                            string.Format(Errors.CannotSynchronize, remoteEntity, Path.Combine(remoteRepository.DbDirectoryPath, $"{remoteRepository.DbFileName}{remoteRepository.DbFileExtension}")).ToError(ex));
                    }
                });
        }
    }
}