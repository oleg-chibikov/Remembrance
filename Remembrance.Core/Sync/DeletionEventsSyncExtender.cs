using System;
using System.Collections.Generic;
using System.Linq;
using JetBrains.Annotations;
using Remembrance.Contracts.Sync;
using Scar.Common.DAL;
using Scar.Common.DAL.Model;

namespace Remembrance.Core.Sync
{
    [UsedImplicitly]
    internal sealed class DeletionEventsSyncExtender<TEntity, TDeletionEntity, TId, TRepository, TDeletionEventRepository> : ISyncExtender<TEntity, TId, TRepository>
        where TEntity : IEntity<TId>, ITrackedEntity
        where TDeletionEntity : IEntity<TId>, ITrackedEntity
        where TRepository : ITrackedRepository<TEntity, TId>
        where TDeletionEventRepository : class, ITrackedRepository<TDeletionEntity, TId>
    {
        private readonly object _locker = new object();

        private readonly IList<TId> _ownDeletionEventsToClear;

        [NotNull]
        private readonly TDeletionEventRepository _ownRepository;

        private bool _collectInfo = true;

        public DeletionEventsSyncExtender([NotNull] TDeletionEventRepository ownRepository)
        {
            _ownRepository = ownRepository ?? throw new ArgumentNullException(nameof(ownRepository));
            _ownDeletionEventsToClear = new List<TId>(_ownRepository.GetAll().Select(x => x.Id));
        }

        public void OnSynchronizationFinished()
        {
            lock (_locker)
            {
                _collectInfo = false;
                if (_ownDeletionEventsToClear.Any())
                {
                    _ownRepository.Delete(_ownDeletionEventsToClear);
                }
            }
        }

        public void OnSynchronizing(TRepository remoteRepository)
        {
            if (!_collectInfo)
            {
                return;
            }

            lock (_locker)
            {
                if (!_collectInfo)
                {
                    return;
                }

                var existInRemoteRepository = _ownDeletionEventsToClear.Where(remoteRepository.Check).ToArray();
                foreach (var translationEntryKey in existInRemoteRepository)
                {
                    _ownDeletionEventsToClear.Remove(translationEntryKey);
                }
            }
        }
    }
}