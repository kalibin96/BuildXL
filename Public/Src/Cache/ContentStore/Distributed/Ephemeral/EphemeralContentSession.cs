﻿// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed.NuCache;
using BuildXL.Cache.ContentStore.Distributed.Stores;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Sessions;
using BuildXL.Cache.ContentStore.Sessions.Internal;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Utilities.Core;
using ContentStore.Grpc;
using AbsolutePath = BuildXL.Cache.ContentStore.Interfaces.FileSystem.AbsolutePath;

namespace BuildXL.Cache.ContentStore.Distributed.Ephemeral;

public class EphemeralContentSession : ContentSessionBase
{
    protected override Tracer Tracer { get; } = new(nameof(EphemeralContentSession));

    private readonly IContentSession _local;
    private readonly IContentSession _persistent;

    private readonly EphemeralHost _ephemeralHost;
    private readonly IDistributedContentCopierHost2 _contentCopierAdapter;

    /// <summary>
    /// This is a dummy implementation of the interface required by <see cref="DistributedContentCopier"/>.
    ///
    /// We don't use it because it's unnecessary.
    /// </summary>
    private class DistributedContentCopierAdapter : IDistributedContentCopierHost2
    {
        public required AbsolutePath WorkingFolder { get; init; }

        public void ReportReputation(Context context, MachineLocation location, MachineReputation reputation)
        {
        }

        public string ReportCopyResult(OperationContext context, ContentLocation info, CopyFileResult result)
        {
            return string.Empty;
        }
    }

    // TODO: when we confirm existence (or lack of) of content in the persistent session, it'd be ideal to add that
    // fact to the ephemeral cache as a "permanent fact". This would allow us to avoid the existence check in the
    // future.

    public EphemeralContentSession(string name, IContentSession local, IContentSession persistent, EphemeralHost ephemeralHost)
        : base(name, counterTracker: null)
    {
        _local = local;
        _persistent = persistent;
        _ephemeralHost = ephemeralHost;
        _contentCopierAdapter = new DistributedContentCopierAdapter { WorkingFolder = _ephemeralHost.Configuration.Workspace };
    }

    protected override Task<PinResult> PinCoreAsync(OperationContext context, ContentHash contentHash, UrgencyHint urgencyHint, Counter retryCounter)
    {
        // Pins are sent directly to the persistent store because the local store is expected to be too small to hold
        // the entire content of the build.
        return _persistent.PinAsync(context, contentHash, context.Token, urgencyHint);
    }

    protected override Task<IEnumerable<Task<Indexed<PinResult>>>> PinCoreAsync(OperationContext context, IReadOnlyList<ContentHash> contentHashes, UrgencyHint urgencyHint, Counter retryCounter, Counter fileCounter)
    {
        // Pins are sent directly to the persistent store because the local store is expected to be too small to hold
        // the entire content of the build.
        return _persistent.PinAsync(context, contentHashes, context.Token, urgencyHint);
    }

    public override Task<IEnumerable<Task<Indexed<PinResult>>>> PinAsync(Context context, IReadOnlyList<ContentHash> contentHashes, PinOperationConfiguration configuration)
    {
        // Pins are sent directly to the persistent store because the local store is expected to be too small to hold
        // the entire content of the build.
        return _persistent.PinAsync(context, contentHashes, configuration);
    }

    protected override async Task<OpenStreamResult> OpenStreamCoreAsync(OperationContext context, ContentHash contentHash, UrgencyHint urgencyHint, Counter retryCounter)
    {
        // The following logic relies on the fact that we can create a file stream pointing to a file, emit a delete,
        // and the file will be deleted when the last remaining file handle is closed.
        using var temporary = new DisposableFile(context, _ephemeralHost.FileSystem, AbsolutePath.CreateRandomFileName(_ephemeralHost.Configuration.Workspace));

        var placeResult = await PlaceFileCoreAsync(
            context,
            contentHash,
            temporary.Path,
            FileAccessMode.ReadOnly,
            FileReplacementMode.ReplaceExisting,
            FileRealizationMode.Any,
            urgencyHint,
            retryCounter);
        if (!placeResult.Succeeded)
        {
            if (placeResult.Code == PlaceFileResult.ResultCode.NotPlacedContentNotFound)
            {
                return new OpenStreamResult(OpenStreamResult.ResultCode.ContentNotFound, errorMessage: $"Content with hash {contentHash} was not found");
            }

            return new OpenStreamResult(placeResult, message: $"Failed to find content with hash {contentHash}");
        }

        // We don't dispose the stream on purpose, because the callee takes ownership of it.
        var stream = _ephemeralHost.FileSystem.TryOpen(
            temporary.Path,
            FileAccess.Read,
            FileMode.Open,
            FileShare.Delete);
        return new OpenStreamResult(stream);
    }

    protected override async Task<PlaceFileResult> PlaceFileCoreAsync(
        OperationContext context,
        ContentHash contentHash,
        AbsolutePath path,
        FileAccessMode accessMode,
        FileReplacementMode replacementMode,
        FileRealizationMode realizationMode,
        UrgencyHint urgencyHint,
        Counter retryCounter)
    {
        // Step 1: try to fetch it from the local content store.
        var local = await _local.PlaceFileAsync(context, contentHash, path, accessMode, replacementMode, realizationMode, context.Token, urgencyHint);
        if (local.Succeeded)
        {
            _ephemeralHost.PutElisionCache.TryAdd(contentHash, local.FileSize, _ephemeralHost.Configuration.PutCacheTimeToLive);

            return local.WithMaterializationSource(PlaceFileResult.Source.LocalCache);
        }

        using var guard = await _ephemeralHost.RemoteFetchLocks.AcquireAsync(contentHash, context.Token);

        // Some other thread may have been downloading and inserting into the local cache. In such a case, we'll have
        // blocked above, and we can just return the result of the local cache.
        if (!guard.WaitFree)
        {
            local = await _local.PlaceFileAsync(context, contentHash, path, accessMode, replacementMode, realizationMode, context.Token, urgencyHint);
            if (local.Succeeded)
            {
                _ephemeralHost.PutElisionCache.TryAdd(contentHash, local.FileSize, _ephemeralHost.Configuration.PutCacheTimeToLive);

                return local.WithMaterializationSource(PlaceFileResult.Source.LocalCache);
            }
        }

        // Step 2: try to fetch it from the datacenter cache.
        var datacenter = await TryPlaceFromDatacenterCacheAsync(
            context,
            contentHash,
            path,
            accessMode,
            replacementMode,
            realizationMode,
            urgencyHint);
        if (datacenter.Succeeded)
        {
            _ephemeralHost.PutElisionCache.TryAdd(contentHash, datacenter.FileSize, _ephemeralHost.Configuration.PutCacheTimeToLive);

            return datacenter;
        }

        // Step 3: try to fetch it from the persistent cache.
        var persistent = await _persistent.PlaceFileAsync(
            context,
            contentHash,
            path,
            accessMode,
            replacementMode,
            realizationMode,
            context.Token,
            urgencyHint);
        if (persistent.Succeeded)
        {
            _ephemeralHost.PutElisionCache.TryAdd(contentHash, persistent.FileSize, _ephemeralHost.Configuration.PutCacheTimeToLive);

            // We're inserting into the local fully asynchronously here because we don't need it to succeed at all for
            // the build to succeed.
            // TODO: figure out how to deal with local PutFile failign because OpenStream deletes the file too early
            await _local.PutFileAsync(context, contentHash, path, FileRealizationMode.Any, context.Token, urgencyHint).IgnoreFailure();
        }

        return persistent.WithMaterializationSource(PlaceFileResult.Source.BackingStore);
    }

    private Task<PlaceFileResult> TryPlaceFromDatacenterCacheAsync(
        OperationContext context,
        ContentHash contentHash,
        AbsolutePath path,
        FileAccessMode accessMode,
        FileReplacementMode replacementMode,
        FileRealizationMode realizationMode,
        UrgencyHint urgencyHint)
    {
        return context.PerformOperationAsync(
            Tracer,
            async () =>
            {
                var locations = await _ephemeralHost.ContentResolver.GetLocationsAsync(
                    context,
                    GetLocationsRequest.SingleHash(contentHash, recursive: true));
                if (locations.Succeeded && locations.Value.Results.Count > 0)
                {
                    // We're requesting a single hash, so we need to look only at that one request.
                    var contentEntry = locations.Value.Results[0];

                    var active = new List<MachineLocation>(capacity: contentEntry.Operations.Count);
                    var inactive = new List<MachineLocation>();
                    var invalid = new List<MachineId>();
                    foreach (var machineId in contentEntry.Existing())
                    {
                        if (_ephemeralHost.ClusterStateManager.ClusterState.QueryableClusterState.RecordsByMachineId.TryGetValue(machineId, out var record))
                        {
                            if (record.IsInactive())
                            {
                                inactive.Add(record.Location);
                            }
                            else
                            {
                                active.Add(record.Location);
                            }
                        }
                        else
                        {
                            invalid.Add(machineId);
                        }
                    }
                    // TODO: sort so open machines wind up at the end

                    if (invalid.Count > 0)
                    {
                        Tracer.Warning(context, $"Found {invalid.Count} invalid machine IDs for content {contentHash}: {string.Join(", ", invalid)}");
                    }

                    // TODO: this could write the file down directly into the final destination, and then do an async putfile. The putfile should be fast to complete
                    if (active.Count > 0)
                    {
                        var contentHashWithSizeAndLocations = new ContentHashWithSizeAndLocations(
                            contentHash,
                            contentEntry.Size,
                            active,
                            filteredOutLocations: inactive,
                            origin: GetBulkOrigin.Local);
                        var datacenter = await _ephemeralHost.ContentCopier.TryCopyAndPutAsync(
                            context,
                            new DistributedContentCopier.CopyRequest(
                                _contentCopierAdapter,
                                contentHashWithSizeAndLocations,
                                CopyReason.Place,
                                copyInfo =>
                                {
                                    var (copyResult, tempLocation, attemptCount) = copyInfo;
                                    var local = _local as ITrustedContentSession;
                                    Contract.AssertNotNull(local, "The local content session was expected to be a trusted session, but failed to cast.");
                                    return local.PutTrustedFileAsync(context, new ContentHashWithSize(contentHash, contentEntry.Size), tempLocation, FileRealizationMode.Any, context.Token, urgencyHint);
                                },
                                CopyCompression.None,
                                null,
                                _ephemeralHost.Configuration.Workspace));

                        if (datacenter.Succeeded)
                        {
                            var local = await _local.PlaceFileAsync(
                                context,
                                contentHash,
                                path,
                                accessMode,
                                replacementMode,
                                realizationMode,
                                context.Token,
                                urgencyHint);
                            if (local.Succeeded)
                            {
                                return local.WithMaterializationSource(PlaceFileResult.Source.DatacenterCache);
                            }

                            return new PlaceFileResult(PlaceFileResult.ResultCode.NotPlacedContentNotFound, errorMessage: $"Content hash `{contentHash}` inserted into local cache, but couldn't place from local");
                        }

                        return new PlaceFileResult(PlaceFileResult.ResultCode.NotPlacedContentNotFound, errorMessage: $"Content hash `{contentHash}` couldn't be downloaded from peers");
                    }

                    return new PlaceFileResult(PlaceFileResult.ResultCode.NotPlacedContentNotFound, errorMessage: $"Content hash `{contentHash}` found in the content tracker, but without any active locations");
                }

                return new PlaceFileResult(PlaceFileResult.ResultCode.NotPlacedContentNotFound, errorMessage: $"Content hash `{contentHash}` not found in the content tracker");

            },
            extraStartMessage: $"({contentHash.ToShortString()},{path},{accessMode},{replacementMode},{realizationMode})",
            traceOperationStarted: TraceOperationStarted,
            extraEndMessage: result =>
                             {
                                 var message = $"({contentHash.ToShortString()},{path},{accessMode},{replacementMode},{realizationMode})";
                                 if (result.Metadata == null)
                                 {
                                     return message;
                                 }

                                 return message + $" Gate.OccupiedCount={result.Metadata.GateOccupiedCount} Gate.Wait={result.Metadata.GateWaitTime.TotalMilliseconds}ms";
                             },
            traceErrorsOnly: TraceErrorsOnlyForPlaceFile(path));
    }

    protected override async Task<PutResult> PutFileCoreAsync(
        OperationContext context,
        HashType hashType,
        AbsolutePath path,
        FileRealizationMode realizationMode,
        UrgencyHint urgencyHint,
        Counter retryCounter)
    {
        // We can't move into the persistent store. No one should be doing this anyways, so it's fine to assert that.
        Contract.Requires(realizationMode != FileRealizationMode.Move, $"{nameof(EphemeralContentSession)} doesn't support {nameof(PutFileCoreAsync)} with {nameof(FileRealizationMode)} = {FileRealizationMode.Move}");

        var local = await _local.PutFileAsync(context, hashType, path, realizationMode, context.Token, urgencyHint);
        if (local.Succeeded && local.ContentAlreadyExistsInCache)
        {
            // If content already exists locally, then it means that it must have been placed there by a previous call
            // to PutFileAsync. Because the cache gets reset in every build, the content would have been uploaded by
            // the previous call, and so we don't need to do it ourselves here.
            return local;
        }

        if (_ephemeralHost.PutElisionCache.TryGetValue(local.ContentHash, out var contentSize))
        {
            return new PutResult(local.ContentHash, contentSize, contentAlreadyExistsInCache: true);
        }

        // Prevents duplicate PutFileAsync calls from uploading the same content at the same time. More importantly,
        // it deduplicates requests about the existence of content.
        using var guard = await _ephemeralHost.RemoteFetchLocks.AcquireAsync(local.ContentHash, context.Token);

        if (!guard.WaitFree && _ephemeralHost.PutElisionCache.TryGetValue(local.ContentHash, out contentSize))
        {
            return new PutResult(local.ContentHash, contentSize, contentAlreadyExistsInCache: true);
        }

        var locations = await ExistsElsewhereAsync(context, local.ContentHash);
        if (locations.Succeeded && locations.Value)
        {
            _ephemeralHost.PutElisionCache.TryAdd(local.ContentHash, local.ContentSize, _ephemeralHost.Configuration.PutCacheTimeToLive);

            // If the content already exists in any machine in the cluster, then it has already been uploaded by a
            // previous call. Therefore, we also don't need to upload it.
            return new PutResult(local.ContentHash, local.ContentSize, contentAlreadyExistsInCache: true);
        }

        var persistent = await _persistent.PutFileAsync(context, hashType, path, realizationMode, context.Token, urgencyHint);
        if (persistent.Succeeded)
        {
            _ephemeralHost.PutElisionCache.TryAdd(persistent.ContentHash, persistent.ContentSize, _ephemeralHost.Configuration.PutCacheTimeToLive);
        }

        return persistent;
    }

    protected override async Task<PutResult> PutFileCoreAsync(
        OperationContext context,
        ContentHash contentHash,
        AbsolutePath path,
        FileRealizationMode realizationMode,
        UrgencyHint urgencyHint,
        Counter retryCounter)
    {
        // We can't move into the persistent store. No one should be doing this anyways, so it's fine to assert that.
        Contract.Requires(realizationMode != FileRealizationMode.Move, $"{nameof(EphemeralContentSession)} doesn't support {nameof(PutFileCoreAsync)} with {nameof(FileRealizationMode)} = {FileRealizationMode.Move}");

        if (_ephemeralHost.PutElisionCache.TryGetValue(contentHash, out var contentSize))
        {
            return new PutResult(contentHash, contentSize, contentAlreadyExistsInCache: true);
        }

        var local = await _local.PutFileAsync(context, contentHash, path, realizationMode, context.Token, urgencyHint);
        if (local.Succeeded && local.ContentAlreadyExistsInCache)
        {
            // If content already exists locally, then it means that it must have been placed there by a previous call
            // to PutFileAsync. Because the cache gets reset in every build, the content would have been uploaded by
            // the previous call, and so we don't need to do it ourselves here.
            return local;
        }

        // Prevents duplicate PutFileAsync calls from uploading the same content at the same time. More importantly,
        // it deduplicates requests about the existence of content.
        using var guard = await _ephemeralHost.RemoteFetchLocks.AcquireAsync(local.ContentHash, context.Token);

        if (!guard.WaitFree && _ephemeralHost.PutElisionCache.TryGetValue(local.ContentHash, out contentSize))
        {
            return new PutResult(local.ContentHash, contentSize, contentAlreadyExistsInCache: true);
        }

        var exists = await ExistsElsewhereAsync(context, local.ContentHash);
        if (exists.Succeeded && exists.Value)
        {
            _ephemeralHost.PutElisionCache.TryAdd(local.ContentHash, local.ContentSize, _ephemeralHost.Configuration.PutCacheTimeToLive);

            // If the content already exists in any machine in the cluster, then it has already been uploaded by a
            // previous call. Therefore, we also don't need to upload it.
            return new PutResult(local.ContentHash, local.ContentSize, contentAlreadyExistsInCache: true);
        }

        var persistent = await _persistent.PutFileAsync(context, local.ContentHash, path, realizationMode, context.Token, urgencyHint);
        if (persistent.Succeeded)
        {
            _ephemeralHost.PutElisionCache.TryAdd(persistent.ContentHash, persistent.ContentSize, _ephemeralHost.Configuration.PutCacheTimeToLive);
        }

        return persistent;
    }

    protected override async Task<PutResult> PutStreamCoreAsync(OperationContext context, HashType hashType, Stream stream, UrgencyHint urgencyHint, Counter retryCounter)
    {
        Contract.Requires(stream.CanSeek, $"{nameof(EphemeralContentSession)} needs to be able to seek the incoming stream.");

        var position = stream.Position;
        var local = await _local.PutStreamAsync(context, hashType, stream, context.Token, urgencyHint);
        if (local.Succeeded && local.ContentAlreadyExistsInCache)
        {
            // If content already exists locally, then it means that it must have been placed there by a previous call
            // to PutFileAsync. Because the cache gets reset in every build, the content would have been uploaded by
            // the previous call, and so we don't need to do it ourselves here.
            return local;
        }

        if (_ephemeralHost.PutElisionCache.TryGetValue(local.ContentHash, out var contentSize))
        {
            return new PutResult(local.ContentHash, contentSize, contentAlreadyExistsInCache: true);
        }

        // Prevents duplicate PutFileAsync calls from uploading the same content at the same time. More importantly,
        // it deduplicates requests about the existence of content.
        using var guard = await _ephemeralHost.RemoteFetchLocks.AcquireAsync(local.ContentHash, context.Token);

        if (!guard.WaitFree && _ephemeralHost.PutElisionCache.TryGetValue(local.ContentHash, out contentSize))
        {
            return new PutResult(local.ContentHash, contentSize, contentAlreadyExistsInCache: true);
        }

        var exists = await ExistsElsewhereAsync(context, local.ContentHash);
        if (exists.Succeeded && exists.Value)
        {
            _ephemeralHost.PutElisionCache.TryAdd(local.ContentHash, local.ContentSize, _ephemeralHost.Configuration.PutCacheTimeToLive);

            // If the content already exists in any machine in the cluster, then it has already been uploaded by a
            // previous call. Therefore, we also don't need to upload it.
            return new PutResult(local.ContentHash, local.ContentSize, contentAlreadyExistsInCache: true);
        }

        stream.Position = position;
        var persistent = await _persistent.PutStreamAsync(context, hashType, stream, context.Token, urgencyHint);
        if (persistent.Succeeded)
        {
            _ephemeralHost.PutElisionCache.TryAdd(persistent.ContentHash, persistent.ContentSize, _ephemeralHost.Configuration.PutCacheTimeToLive);
        }

        return persistent;
    }

    protected override async Task<PutResult> PutStreamCoreAsync(OperationContext context, ContentHash contentHash, Stream stream, UrgencyHint urgencyHint, Counter retryCounter)
    {
        Contract.Requires(stream.CanSeek, $"{nameof(EphemeralContentSession)} needs to be able to seek the incoming stream.");

        if (_ephemeralHost.PutElisionCache.TryGetValue(contentHash, out var contentSize))
        {
            return new PutResult(contentHash, contentSize, contentAlreadyExistsInCache: true);
        }

        var position = stream.Position;
        var local = await _local.PutStreamAsync(context, contentHash, stream, context.Token, urgencyHint);
        if (local.Succeeded && local.ContentAlreadyExistsInCache)
        {
            return local;
        }

        // Prevents duplicate PutFileAsync calls from uploading the same content at the same time. More importantly,
        // it deduplicates requests about the existence of content.
        using var guard = await _ephemeralHost.RemoteFetchLocks.AcquireAsync(local.ContentHash, context.Token);

        if (!guard.WaitFree && _ephemeralHost.PutElisionCache.TryGetValue(local.ContentHash, out contentSize))
        {
            return new PutResult(local.ContentHash, contentSize, contentAlreadyExistsInCache: true);
        }

        var exists = await ExistsElsewhereAsync(context, local.ContentHash);
        if (exists.Succeeded && exists.Value)
        {
            _ephemeralHost.PutElisionCache.TryAdd(local.ContentHash, local.ContentSize, _ephemeralHost.Configuration.PutCacheTimeToLive);

            // If the content already exists in any machine in the cluster, then it has already been uploaded by a
            // previous call. Therefore, we also don't need to upload it.
            return new PutResult(local.ContentHash, local.ContentSize, contentAlreadyExistsInCache: true);
        }

        stream.Position = position;
        var persistent = await _persistent.PutStreamAsync(context, contentHash, stream, context.Token, urgencyHint);
        if (persistent.Succeeded)
        {
            _ephemeralHost.PutElisionCache.TryAdd(persistent.ContentHash, persistent.ContentSize, _ephemeralHost.Configuration.PutCacheTimeToLive);
        }

        return persistent;
    }

    public Task<Result<bool>> ExistsElsewhereAsync(OperationContext context, ShortHash contentHash)
    {
        // This checks for file existence elsewhere in the cluster. The reason for this is that this can and does race
        // with all local PutFile for whether the event about the existence of the content gets processed before we
        // query or not.
        // TODO: add timeout here.
        return context.PerformOperationAsync(
            Tracer,
            async () =>
            {
                var local = await _ephemeralHost.LocalContentTracker.GetSingleLocationAsync(context, contentHash).ThrowIfFailureAsync();
                if (local.Existing().Any(machineId => machineId != _ephemeralHost.ClusterStateManager.ClusterState.PrimaryMachineId && !_ephemeralHost.ClusterStateManager.ClusterState.IsMachineMarkedInactive(machineId)))
                {
                    return Result.Success(true);
                }

                var remote = await _ephemeralHost.ContentResolver.GetSingleLocationAsync(context, contentHash).ThrowIfFailureAsync();
                if (remote.Existing().Any(machineId => machineId != _ephemeralHost.ClusterStateManager.ClusterState.PrimaryMachineId && !_ephemeralHost.ClusterStateManager.ClusterState.IsMachineMarkedInactive(machineId)))
                {
                    return Result.Success(true);
                }

                return Result.Success(false);
            },
            traceOperationStarted: false);
    }

}
