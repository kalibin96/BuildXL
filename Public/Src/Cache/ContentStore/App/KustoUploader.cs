// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using BuildXL.Cache.ContentStore.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.ContentStore.Logging;
using BuildXL.Utilities.ParallelAlgorithms;
using Kusto.Data.Common;
using Kusto.Ingest;

#nullable enable

namespace BuildXL.Cache.ContentStore.App
{
    public record struct FileDescription
    {
        public required string FilePath { get; init; }
        public required Guid SourceId { get; init; }
    }

    /// <summary>
    ///     Responsible for pumping provided log files to Kusto.
    ///
    ///     To post a log file for upload, call <see cref="PostFileForUpload(string, Guid)"/>.
    ///     This method is asynchronous and thread-safe, meaning:
    ///       (1) it only enqueues a request for upload and doesn't wait for it to complete before it returns, and
    ///       (2) posting multiple files from concurrent threads is ok.
    ///
    ///     To complete this process and wait for all pending uploads to finish, call
    ///     <see cref="CompleteAndWaitForPendingUploadsToFinish"/>.  After this method has been
    ///     called, <see cref="PostFileForUpload(string, Guid)"/> does not accept any new requests.
    ///
    ///     The <see cref="Dispose"/> method of this class implicitly waits for pending ingestions
    ///     to finish (by calling <see cref="CompleteAndWaitForPendingUploadsToFinish"/>.
    ///
    ///     Note that this class only uploads log files to a blob storage.  From there, the logs are
    ///     asynchronously ingested to Kusto by a different service and so it may take minutes
    ///     after a log file is uploaded by this class before its content becomes available in Kusto.
    /// </summary>
    public sealed class KustoUploader : IDisposable
    {
        private readonly ILog? _log;
        private readonly bool _deleteFilesOnSuccess;
        private readonly bool _checkForIngestionErrors;
        private readonly IKustoQueuedIngestClient _client;
        private readonly KustoQueuedIngestionProperties _ingestionProperties;
        private readonly ActionBlockSlim<FileDescription> _block;
        private readonly IAbsFileSystem _fileSystem;

        private bool _hasUploadErrors = false;

        /// <summary>
        ///     Constructor.  Initializes this object and does nothing else.
        /// </summary>
        /// <param name="connectionString">Kusto connection string.</param>
        /// <param name="database">Database into which to ingest.</param>
        /// <param name="table">Table into which to ingest.</param>
        /// <param name="csvMapping">Csv mapping that applies to all CSV files to be uploaded via <see cref="UploadSingleCsvFile"/></param>
        /// <param name="deleteFilesOnSuccess">Whether to delete files upon successful upload.</param>
        /// <param name="checkForIngestionErrors">
        ///     Whether to check for ingestion errors before disposing this object.
        ///     Note that at this time not all uploaded files have necessarily been ingested; this class
        ///     does not wait for ingestions to complete, it only checks for failures of those that have completed.
        /// </param>
        /// <param name="log">An optional log to which to write some debug information.</param>
        /// <param name="fileSystem">An option file system abstraction layer.</param>
        public KustoUploader
            (
            string connectionString,
            string database,
            string table,
            IEnumerable<ColumnMapping> csvMapping,
            bool deleteFilesOnSuccess,
            bool checkForIngestionErrors,
            ILog? log = null,
            IAbsFileSystem? fileSystem = null
            )
        {
            _log = log;
            _fileSystem = fileSystem ?? new PassThroughFileSystem();
            _deleteFilesOnSuccess = deleteFilesOnSuccess;
            _checkForIngestionErrors = checkForIngestionErrors;
            _client = KustoIngestFactory.CreateQueuedIngestClient(connectionString);
            _hasUploadErrors = false;
            _ingestionProperties = new KustoQueuedIngestionProperties(database, table)
            {
                ReportLevel  = IngestionReportLevel.FailuresOnly,
                ReportMethod = IngestionReportMethod.Queue,
            };
            _ingestionProperties.IngestionMapping.IngestionMappings = csvMapping;

            _block = ActionBlockSlim.Create<FileDescription>(
                degreeOfParallelism: 1,
                UploadSingleCsvFile);
        }

        /// <summary>
        ///     Posts <paramref name="filePath"/> for upload and returns immediately.
        /// </summary>
        public void PostFileForUpload(string filePath, Guid sourceId)
        {
            Always("Posting file '{0}' for upload", filePath);
            _block.Post(new FileDescription
            {
                FilePath = filePath,
                SourceId = sourceId
            });
        }

        /// <summary>
        ///     Synchronously waits until all posted files have been uploaded.
        /// </summary>
        /// <returns>
        ///     Whether any failures were encountered.  All encountered failures are logged by this class.
        /// </returns>
        public bool CompleteAndWaitForPendingUploadsToFinish()
        {
            if (_block.Completion.IsCompleted)
            {
                return true;
            }

            _block.Complete();

            var start = DateTime.UtcNow;
            _block.Completion.GetAwaiter().GetResult();
            var duration = DateTime.UtcNow.Subtract(start);
            Always("Waited {0} ms for queued upload tasks to complete", duration.TotalMilliseconds);

            return CheckForFailures();
        }

        /// <summary>
        ///     Synchronously waits until all posted files have been uploaded, then disposes the internal Kusto client.
        /// </summary>
        public void Dispose()
        {
            CompleteAndWaitForPendingUploadsToFinish();
            _client.Dispose();
        }

        private void UploadSingleCsvFile(FileDescription fileDesc)
        {
            try
            {
                var start = DateTime.UtcNow;
                using var file = _fileSystem.Open(new AbsolutePath(fileDesc.FilePath), FileAccess.Read, FileMode.Open, FileShare.None, FileOptions.DeleteOnClose);
                var result = _client.IngestFromStream(file, _ingestionProperties);
                var duration = DateTime.UtcNow.Subtract(start);

                Always("Uploading file '{0}' took {1} ms", fileDesc.FilePath, duration.TotalMilliseconds);
            }
            catch (Exception e)
            {
                Error("Failed to upload file '{0}': {1}", fileDesc.FilePath, e);
                _hasUploadErrors = true;
            }
        }

        private bool CheckForFailures()
        {
            if (!_checkForIngestionErrors)
            {
                return !_hasUploadErrors;
            }

            var start = DateTime.UtcNow;
            var ingestionFailures = _client.GetAndDiscardTopIngestionFailuresAsync().GetAwaiter().GetResult().ToList();
            var duration = DateTime.UtcNow.Subtract(start);
            Always("Checking for ingestion failures took {0} ms", duration.TotalMilliseconds);

            if (ingestionFailures.Any() && _log != null)
            {
                var failures = ingestionFailures.Select(f => $"{Environment.NewLine}  {RenderIngestionFailure(f)}");
                Error("The following ingestions failed:{0}", string.Join(string.Empty, failures));
            }

            return !_hasUploadErrors && ingestionFailures.Count == 0;
        }

        private string RenderIngestionFailure(IngestionFailure f)
        {
            return $"File: {f.Info.IngestionSourcePath}, Status: {f.Info.FailureStatus}, Error code: {f.Info.ErrorCode}, Details: {f.Info.Details}";
        }

        private void Always(string format, params object[] args) => Log(Severity.Always, format, args);
        private void Error(string format, params object[] args) => Log(Severity.Error, format, args);

        private void Log(Severity severity, string format, params object[] args)
        {
            if (_log == null)
            {
                return;
            }

            var message = string.Format(CultureInfo.InvariantCulture, format, args);
            _log.Write(DateTime.Now, Environment.CurrentManagedThreadId, severity, message);
        }
    }
}
