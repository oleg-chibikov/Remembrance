using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Common.Logging;
using Easy.MessageHub;
using JetBrains.Annotations;
using Microsoft.Win32;
using Remembrance.Contracts.CardManagement;
using Remembrance.Contracts.CardManagement.Data;
using Remembrance.Resources;
using Scar.Common.Events;
using Scar.Common.Messages;

namespace Remembrance.Core.Exchange
{
    [UsedImplicitly]
    internal sealed class CardsExchanger : ICardsExchanger, IDisposable
    {
        [NotNull]
        private readonly IFileExporter _exporter;

        [NotNull]
        private readonly IFileImporter[] _importers;

        [NotNull]
        private readonly ILog _logger;

        [NotNull]
        private readonly IMessageHub _messenger;

        [NotNull]
        private readonly OpenFileDialog _openFileDialog;

        [NotNull]
        private readonly SaveFileDialog _saveFileDialog;

        public CardsExchanger(
            [NotNull] ILog logger,
            [NotNull] IFileExporter exporter,
            [NotNull] IFileImporter[] importers,
            [NotNull] IMessageHub messenger,
            [NotNull] OpenFileDialog openFileDialog,
            [NotNull] SaveFileDialog saveFileDialog)
        {
            _openFileDialog = openFileDialog ?? throw new ArgumentNullException(nameof(openFileDialog));
            _saveFileDialog = saveFileDialog ?? throw new ArgumentNullException(nameof(saveFileDialog));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _exporter = exporter ?? throw new ArgumentNullException(nameof(exporter));
            _importers = importers ?? throw new ArgumentNullException(nameof(importers));
            _messenger = messenger ?? throw new ArgumentNullException(nameof(messenger));
            foreach (var importer in importers)
            {
                importer.Progress += ImporterExporter_Progress;
            }

            exporter.Progress += ImporterExporter_Progress;
        }

        public event EventHandler<ProgressEventArgs> Progress;

        public async Task ExportAsync(CancellationToken cancellationToken)
        {
            var fileName = ShowSaveFileDialog();
            if (fileName == null)
            {
                return;
            }

            _logger.Info($"Performing export to {fileName}...");
            ExchangeResult exchangeResult = null;
            OnProgress(0, 1);
            try
            {
                await Task.Run(
                        async () =>
                        {
                            exchangeResult = await _exporter.ExportAsync(fileName, cancellationToken)
                                .ConfigureAwait(false);
                        },
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            finally
            {
                OnProgress(1, 1);
            }

            if (exchangeResult.Success)
            {
                _logger.Info($"Export to {fileName} has been performed");
                _messenger.Publish(Texts.ExportSucceeded.ToMessage());
                Process.Start(fileName);
            }
            else
            {
                _logger.Warn($"Export to {fileName} failed");
                _messenger.Publish(Texts.ExportFailed.ToError());
            }
        }

        public async Task ImportAsync(CancellationToken cancellationToken)
        {
            var fileName = ShowOpenFileDialog();
            if (fileName == null)
            {
                return;
            }

            OnProgress(0, 1);
            try
            {
                await Task.Run(
                        async () =>
                        {
                            foreach (var importer in _importers)
                            {
                                _logger.Info($"Performing import from {fileName} with {importer.GetType() .Name}...");
                                var exchangeResult = await importer.ImportAsync(fileName, cancellationToken)
                                    .ConfigureAwait(false);

                                if (exchangeResult.Success)
                                {
                                    _logger.Info($"ImportAsync from {fileName} has been performed");
                                    var mainMessage = string.Format(Texts.ImportSucceeded, exchangeResult.Count);
                                    _messenger.Publish(
                                        exchangeResult.Errors != null
                                            ? $"[{importer.GetType() .Name}] {mainMessage}. {Texts.ImportErrors}:{Environment.NewLine}{string.Join(Environment.NewLine, exchangeResult.Errors)}".ToWarning()
                                            : $"[{importer.GetType() .Name}] {mainMessage}".ToMessage());
                                    return;
                                }

                                _logger.Warn($"ImportAsync from {fileName} failed");
                            }

                            _messenger.Publish(Texts.ImportFailed.ToError());
                        },
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            finally
            {
                OnProgress(1, 1);
            }
        }

        public void Dispose()
        {
            foreach (var importer in _importers)
            {
                importer.Progress -= ImporterExporter_Progress;
            }

            _exporter.Progress -= ImporterExporter_Progress;
        }

        [CanBeNull]
        private string ShowOpenFileDialog()
        {
            return _openFileDialog.ShowDialog() == true
                ? _openFileDialog.FileName
                : null;
        }

        [CanBeNull]
        private string ShowSaveFileDialog()
        {
            _saveFileDialog.FileName = $"{nameof(Remembrance)} {DateTime.Now:yyyy-MM-dd hh-mm-ss}.json";
            return _saveFileDialog.ShowDialog() == true
                ? _openFileDialog.FileName
                : null;
        }

        private void ImporterExporter_Progress(object sender, ProgressEventArgs e)
        {
            Progress?.Invoke(this, e);
        }

        private void OnProgress(int current, int total)
        {
            Progress?.Invoke(this, new ProgressEventArgs(current, total));
        }
    }
}