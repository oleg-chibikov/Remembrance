using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Common.Logging;
using JetBrains.Annotations;
using Newtonsoft.Json;
using Remembrance.Contracts.DAL.Local;
using Remembrance.Contracts.DAL.Model;
using Remembrance.Contracts.DAL.Shared;
using Remembrance.Contracts.Exchange;
using Remembrance.Contracts.Exchange.Data;
using Remembrance.Core.CardManagement.Data;
using Scar.Common.Events;

namespace Remembrance.Core.Exchange
{
    [UsedImplicitly]
    internal sealed class RemembranceFileExporter : IFileExporter
    {
        private static readonly JsonSerializerSettings ExportEntrySerializerSettings = new JsonSerializerSettings
        {
            ContractResolver = new TranslationEntryContractResolver(),
            NullValueHandling = NullValueHandling.Ignore
        };

        [NotNull]
        private readonly ILearningInfoRepository _learningInfoRepository;

        [NotNull]
        private readonly ILog _logger;

        [NotNull]
        private readonly ITranslationEntryRepository _translationEntryRepository;

        public RemembranceFileExporter(
            [NotNull] ITranslationEntryRepository translationEntryRepository,
            [NotNull] ITranslationDetailsRepository translationDetailsRepository,
            [NotNull] ILog logger,
            [NotNull] ILearningInfoRepository learningInfoRepository)
        {
            _translationEntryRepository = translationEntryRepository ?? throw new ArgumentNullException(nameof(translationEntryRepository));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _learningInfoRepository = learningInfoRepository ?? throw new ArgumentNullException(nameof(learningInfoRepository));
        }

        public event EventHandler<ProgressEventArgs> Progress;

        public async Task<ExchangeResult> ExportAsync(string fileName, CancellationToken cancellationToken)
        {
            if (fileName == null)
            {
                throw new ArgumentNullException(nameof(fileName));
            }

            var translationEntries = _translationEntryRepository.GetAll();
            var learningInfos = _learningInfoRepository.GetAll().ToDictionary(x => x.Id, x => x);
            var exportEntries = new List<RemembranceExchangeEntry>(translationEntries.Length);
            var totalCount = translationEntries.Length;
            var count = 0;
            foreach (var translationEntry in translationEntries)
            {
                learningInfos.TryGetValue(translationEntry.Id, out var learningInfo);
                exportEntries.Add(new RemembranceExchangeEntry(translationEntry, learningInfo ?? new LearningInfo()));
                OnProgress(Interlocked.Increment(ref count), totalCount);
            }

            try
            {
                var json = JsonConvert.SerializeObject(exportEntries, Formatting.Indented, ExportEntrySerializerSettings);
                File.WriteAllText(fileName, json);
                return new ExchangeResult(true, null, exportEntries.Count);
            }
            catch (IOException ex)
            {
                _logger.Warn("Cannot save file to disk", ex);
            }
            catch (JsonSerializationException ex)
            {
                _logger.Warn("Cannot serialize object", ex);
            }

            return new ExchangeResult(false, null, 0);
        }

        private void OnProgress(int current, int total)
        {
            Progress?.Invoke(this, new ProgressEventArgs(current, total));
        }
    }
}