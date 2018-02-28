using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using JetBrains.Annotations;
using Remembrance.Contracts.DAL.Model;
using Remembrance.Contracts.Processing.Data;
using Scar.Common.WPF.View.Contracts;

namespace Remembrance.Contracts.Processing
{
    public interface ITranslationEntryProcessor
    {
        [ItemNotNull]
        [NotNull]
        Task<TranslationInfo> AddOrUpdateTranslationEntryAsync(
            [NotNull] TranslationEntryAdditionInfo translationEntryAdditionInfo,
            CancellationToken cancellationToken,
            [CanBeNull] IWindow ownerWindow = null,
            bool needPostProcess = true,
            [CanBeNull] ICollection<ManualTranslation> manualTranslations = null);

        void DeleteTranslationEntry([NotNull] TranslationEntryKey translationEntryKey, bool needDeletionRecord = true);

        [ItemNotNull]
        [NotNull]
        Task<string> GetDefaultTargetLanguageAsync([NotNull] string sourceLanguage, CancellationToken cancellationToken);

        [ItemNotNull]
        [NotNull]
        Task<TranslationDetails> ReloadTranslationDetailsIfNeededAsync(
            [NotNull] TranslationEntryKey translationEntryKey,
            [CanBeNull] ICollection<ManualTranslation> manualTranslations,
            CancellationToken cancellationToken,
            [CanBeNull] Action<TranslationDetails> processNonReloaded = null);

        [ItemNotNull]
        [NotNull]
        Task<TranslationInfo> UpdateManualTranslationsAsync([NotNull] TranslationEntryKey translationEntryKey, [CanBeNull] ICollection<ManualTranslation> manualTranslations, CancellationToken cancellationToken);
    }
}