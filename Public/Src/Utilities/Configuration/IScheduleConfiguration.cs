// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using BuildXL.Interop.Unix;

namespace BuildXL.Utilities.Configuration
{
    /// <summary>
    /// Defines a named root
    /// </summary>
    public partial interface IScheduleConfiguration
    {
        /// <summary>
        /// Specifies the maximum amount of RAM which can be utilized before scheduling is paused to allow freeing resources.
        /// NOTE: In order for scheduling to be paused, both this limit and <see cref="MinimumTotalAvailableRamMb"/> must be met.
        /// </summary>
        int MaximumRamUtilizationPercentage { get; }

        /// <summary>
        /// Specifies the minimum amount of available RAM before scheduling is paused to allow freeing resources.
        /// NOTE: In order for scheduling to be paused, both this limit and <see cref="MaximumRamUtilizationPercentage"/> must be met.
        /// </summary>
        int? MinimumTotalAvailableRamMb { get; }

        /// <summary>
        /// Indicates that processes should not be cancelled and retried when machine RAM is low as specified by
        /// <see cref="MaximumRamUtilizationPercentage"/> and <see cref="MinimumTotalAvailableRamMb"/>
        /// </summary>
        bool DisableProcessRetryOnResourceExhaustion { get; }

        /// <summary>
        /// Specifies the maximum allowed memory pressure level on macOS before scheduling is paused to allow freeing resources.
        /// </summary>
        Memory.PressureLevel MaximumAllowedMemoryPressureLevel { get; }

        /// <summary>
        /// Stops the build engine the first time an error is generated by either BuildXL or one of the tool it runs.
        /// </summary>
        bool StopOnFirstError { get; }

        /// <summary>
        /// Specifies the maximum number of concurrent worker selections for pip execution.
        /// Default is 1 for single machine build and 5 for distributed build
        /// </summary>
        int MaxChooseWorkerCpu { get; }

        /// <summary>
        /// Specifies the maximum number of concurrent worker selections for cachelookup.
        /// Default is 1.
        /// </summary>
        int MaxChooseWorkerCacheLookup { get; }

        /// <summary>
        /// Specifies the maximum number of concurrent worker selections for light pips.
        /// Default is 100.
        /// </summary>
        int MaxChooseWorkerLight { get; }

        /// <summary>
        /// Specifies the maximum number of processes that BuildXL will launch at one time locally. The default value is the total number of processors in the current machine.
        /// </summary>
        /// <remarks>
        /// This configuration is used also to control parallelism of components other than the number or processes to execute. For proper control of maximum number of processes,
        /// one should use <see cref="EffectiveMaxProcesses"/>.
        /// </remarks>
        int MaxProcesses { get; }

        /// <summary>
        /// Specifies the maximum number of cache lookups that can be concurrently done.
        /// </summary>
        int MaxCacheLookup { get; }

        /// <summary>
        /// Specifies the maximum number of processes that do materialize (e.g., materialize inputs, storing two-phase cache entries, analyzing pip violations).
        /// </summary>
        int MaxMaterialize { get; }

        /// <summary>
        /// Desired size of the light process queue.  Processes that have Process.Option.IsLight set (indicating
        /// that they are neither CPU nor IO bound; rather, for example, they more like lazy lingering observers)
        /// are placed in a special queue which has a much bigger capacity than the CpuQueue (which is meant for
        /// CPU intensive processes).
        /// </summary>
        int MaxLightProcesses { get; }

        /// <summary>
        /// Specifies the maximum number of I/O operations that BuildXL will launch at one time. The default value is 1/4 of the number of processors in the current machine, but at least 1.
        /// </summary>
        int MaxIO { get; }

        /// <summary>
        /// Adaptive IO limit
        /// </summary>
        bool AdaptiveIO { get; }

        /// <summary>
        /// Runs the build engine and all tools at a lower priority in order to provide better responsiveness to interactive processes on the current machine.
        /// </summary>
        bool LowPriority { get; }

        /// <summary>
        /// Enables lazy materialization (deployment) of pips' outputs from local cache. Defaults to on.
        /// </summary>
        /// <remarks>
        /// Previous internal name: EnableLazyOutputMaterialization
        /// </remarks>
        bool EnableLazyOutputMaterialization { get; }

        /// <summary>
        /// Forces skipping dependencies of explicitly scheduled pips unless inputs are non-existent on filesystem
        /// </summary>
        /// <remarks>
        /// Internal name: Dirty Build
        /// </remarks>
        ForceSkipDependenciesMode ForceSkipDependencies { get; }

        /// <summary>
        /// Flag for maintaining (reading and writing) historical performance information; future build schedules will improve by leveraging the collected information.
        /// </summary>
        bool UseHistoricalPerformanceInfo { get; }

        /// <summary>
        /// If True, dirtying a succeed fast pip doesn't automatically dirty downstream pips.
        /// </summary>
        bool StopDirtyOnSucceedFastPips { get; }

        /// <summary>
        /// Ensures historic performance information is loaded from cache
        /// </summary>
        bool ForceUseEngineInfoFromCache { get; }

        /// <summary>
        /// Indicates whether historic perf information should be used to speculatively limit the RAM utilization
        /// of launched processes
        /// </summary>
        bool? UseHistoricalRamUsageInfo { get; }

        /// <summary>
        /// Specifies the set of outputs which must be materialized
        /// </summary>
        RequiredOutputMaterialization RequiredOutputMaterialization { get; }

        /// <summary>
        /// Gets set of excluded paths for output file materialization
        /// </summary>
        IReadOnlyList<AbsolutePath> OutputMaterializationExclusionRoots { get; }

        /// <summary>
        /// Treats directory as absent file when getting a content hash of inputs
        /// </summary>
        /// <remarks>
        /// This flag is temporary to avoid breaking changes caused by a fix for
        /// Bug #698382
        /// </remarks>
        bool TreatDirectoryAsAbsentFileOnHashingInputContent { get; }

        /// <summary>
        /// Allow copying symlink.
        /// </summary>
        bool AllowCopySymlink { get; }

        /// <summary>
        /// Reuse output files on disk during cache lookup (to check up-to-dateness) and materialization.
        /// </summary>
        /// <remarks>
        /// The up-to-dateness checks are done by querying the USN journal.
        /// </remarks>
        bool ReuseOutputsOnDisk { get; }

        /// <summary>
        /// Unsafe configuration that allows for disabling pip graph post validation.
        /// </summary>
        /// <remarks>
        /// TODO: Remove this!
        /// </remarks>
        bool UnsafeDisableGraphPostValidation { get; }

        /// <summary>
        /// String used to generate environment specific fingerprint for scheduler performance data (this is automatically computed)
        /// </summary>
        string EnvironmentFingerprint { get; }

        /// <summary>
        /// Verifies pins for cache lookup output content by attempting to materialize the content.
        /// </summary>
        bool VerifyCacheLookupPin { get; }

        /// <summary>
        /// Indicates whether outputs of cached pips should be pinned. (Defaults to true)
        /// </summary>
        bool PinCachedOutputs { get; }

        /// <summary>
        /// Canonicalize filter outputs.
        /// </summary>
        bool CanonicalizeFilterOutputs { get; }

        /// <summary>
        /// Schedule meta pips
        /// </summary>
        bool ScheduleMetaPips { get; }

        /// <summary>
        /// Number of retries for processes that users can specify.
        /// </summary>
        /// <remarks>
        /// One use of this process retry is when a process specifies some exit codes that allow it to be retried.
        /// </remarks>
        int ProcessRetries { get; }

        /// <summary>
        /// Enables lazy materialization of write file outputs. Defaults to off (on for CloudBuild)
        /// </summary>
        /// <remarks>
        /// TODO: This should be removed when lazy write file materialization works appropriately with copy pips AND incremental scheduling.
        /// </remarks>
        bool EnableLazyWriteFileMaterialization { get; }

        /// <summary>
        /// Gets whether IPC pip output should be written to disk. Defaults to on (off for CloudBuild)
        /// </summary>
        bool WriteIpcOutput { get; }

        /// <summary>
        /// Stores pip outputs to cache.
        /// </summary>
        bool StoreOutputsToCache { get; }

        /// <summary>
        /// Infers the non-existence of a path based on the parent path when checking the real file system in file system view.
        /// </summary>
        bool InferNonExistenceBasedOnParentPathInRealFileSystem { get; }

        /// <summary>
        /// Enables incremental scheduling to schedule fewer pips.
        /// </summary>
        /// <remarks>
        /// This implies <see cref="IEngineConfiguration.ScanChangeJournal" /> and is functionally a superset.
        /// </remarks>
        bool IncrementalScheduling { get; }

        /// <summary>
        /// Computes static fingerprints of pips during graph construction.
        /// </summary>
        /// <remarks>
        /// This option is enabled when <see cref="IncrementalScheduling"/> is set to true.
        /// On Word build, the graph construction is 10%-13% slower when static fingerprints are computed.
        /// In the future BuildXL may want to use this static fingerprints to compute weak content fingerprints, and thus this option
        /// can be deprecated.
        /// </remarks>
        bool ComputePipStaticFingerprints { get; }

        /// <summary>
        /// Logs static fingerprints of pips during graph construction.
        /// </summary>
        /// <remarks>
        /// This option is useful for debugging graph agnostic incremental scheduling state.
        /// </remarks>
        bool LogPipStaticFingerprintTexts { get; }

        /// <summary>
        /// Creates handle with sequential scan when hashing output files specified in <see cref="OutputFileExtensionsForSequentialScanHandleOnHashing"/>.
        /// </summary>
        /// <remarks>
        /// Currently, this option will only have effect if <see cref="StoreOutputsToCache"/> is disabled.
        /// </remarks>
        bool CreateHandleWithSequentialScanOnHashingOutputFiles { get; }

        /// <summary>
        /// File extensions of outputs files which BuildXL will create handles with sequential scan when hashing the files.
        /// </summary>
        IReadOnlyList<PathAtom> OutputFileExtensionsForSequentialScanHandleOnHashing { get; }

        /// <summary>
        /// Prefix of tag considered for sending aggregate statistics to telemetry.
        /// </summary>
        string TelemetryTagPrefix { get; }

        /// <summary>
        /// Specifies the cpu queue limit in terms of a multiplier of the normal limit when at least one remote worker gets connected.
        /// </summary>
        double? OrchestratorCpuMultiplier { get; }

        /// <summary>
        /// Specifies the cachelookup queue limit in terms of a multiplier of the normal limit when at least one remote worker gets connected.
        /// </summary>
        double? OrchestratorCacheLookupMultiplier { get; }

        /// <summary>
        /// Skip hash source file pips during graph creation.
        /// </summary>
        bool SkipHashSourceFile { get; }

        /// <summary>
        /// Unsafe configuration that stops the shared opaque scrubber from deleting empty directories
        /// </summary>
        /// <remarks>
        /// The reason this flag is unsafe is because not deleting empty directories may introduce
        /// nondeterminism to a build; shared opaques should be wiped-clean of non-build files before
        /// engine execution.
        ///
        /// For example, if ./a is a shared opaque, ./a/b is an undeclared empty directory, and some.exe
        /// fails if ./a/b does not exist, then this flag will allow some.exe to execute successfully
        /// even though it normally would not.
        ///
        /// TODO: Remove this when https://gitlab.kitware.com/cmake/cmake/issues/19162 has reached mainstream versions
        /// </remarks>
        bool UnsafeDisableSharedOpaqueEmptyDirectoryScrubbing { get; }

        /// <summary>
        /// Delay scrubbing of shared opaque outputs until right before the pip is executed.
        /// 
        /// It's currently unsafe because not all corner cases have been worked out.
        /// </summary>
        bool UnsafeLazySODeletion { get; }

        /// <summary>
        /// Indicates whether historic cpu information should be used to decide the weight of process pips.
        /// </summary>
        bool? UseHistoricalCpuUsageInfo { get; }

        /// <summary>
        /// Instead of creating a random moniker for API server, use a fixed predetermined moniker.
        /// </summary>
        bool UseFixedApiServerMoniker { get; }

        /// <summary>
        /// Path to file containing input changes.
        /// </summary>
        AbsolutePath InputChanges { get; }

        /// <summary>
        /// Required minimum available disk space on all drives to keep executing pips 
        /// Checked every 2 seconds.
        /// </summary>
        int? MinimumDiskSpaceForPipsGb { get; }

        /// <summary>
        /// Number of retries allowed per Pip failing due to low memory. 
        /// null represents inifinite attempts.
        /// </summary>
        int? MaxRetriesDueToLowMemory { get; }

        /// <summary>
        /// Number of retries allowed per pip failing due to retryable failures. 
        /// </summary>
        int MaxRetriesDueToRetryableFailures { get; }

        /// <summary>
        /// Instructs the scheduler to only perform cache lookup and skip execution of pips that are cache misses.
        /// </summary>
        bool CacheOnly { get; }

        /// <summary>
        /// Enable estimating the setup cost when choosing worker.
        /// </summary>
        bool EnableSetupCostWhenChoosingWorker { get; }

        /// <summary>
        /// Specifies the maximum number of sealdirectory pips that BuildXL will process at one time. The default value is the number of processors in the current machine.
        /// </summary>
        int MaxSealDirs { get; }

        /// <summary>
        /// Enable memory projection based on historic commit memory usage.
        /// </summary>
        bool EnableHistoricCommitMemoryProjection { get; }

        /// <summary>
        /// Specifies the maximum amount of commit memory which makes scheduler stop executing more pips.
        /// </summary>
        int MaximumCommitUtilizationPercentage { get; }

        /// <summary>
        /// Specifies the critical amount of commit memory which makes scheduler cancel ongoing/suspended pips.
        /// </summary>
        int CriticalCommitUtilizationPercentage { get; }

        /// <summary>
        /// Specifies the min multiplier for the number of elements in ChooseWorkerCPU queue
        /// </summary>
        /// <remarks>
        /// The actual number is determined at runtime by applying the multiplier to the total number of CPU slots across all workers,
        /// e.g., MinElements = multiplier * TotalCpuSlots;
        /// 
        /// The idea is to always have at least 'min' number of elements sitting in ChooseWorkerCPU queue, but stop populating
        /// that queue if there are 'max' number of elements.
        /// </remarks>
        double? DelayedCacheLookupMinMultiplier { get; }

        /// <summary>
        /// Specifies the max multiplier for the number of elements in ChooseWorkerCPU queue
        /// </summary>
        double? DelayedCacheLookupMaxMultiplier { get; }

        /// <summary>
        /// Enable less aggresive memory projection by using average memory usage instead of peak usage
        /// </summary>
        bool EnableLessAggresiveMemoryProjection { get; }

        /// <summary>
        /// Mode for managing memory during builds
        /// </summary>
        ManageMemoryMode? ManageMemoryMode { get; }

        /// <summary>
        /// Ignores any filters that might have been specified for composite shared opaques.
        /// Temporary option. For A/B testing purposes only.
        /// </summary>
        bool? DisableCompositeOpaqueFilters { get; }
        
        /// <summary>
        /// Enable plugin mode
        /// </summary>
        bool? EnablePlugin { get; }

        /// <summary>
        /// Paths to load plugins
        /// </summary>
        IReadOnlyList<AbsolutePath> PluginLocations { get; }

        /// <summary>
        /// Treats absent directory as existent when it is probed and the path is under an opaque directory
        /// </summary>
        bool TreatAbsentDirectoryAsExistentUnderOpaque { get; }

        /// <summary>
        /// Maximum allowed workers per module
        /// </summary>
        int? MaxWorkersPerModule { get; }

        /// <summary>
        /// Load factor allowed to try another worker for the module.
        /// </summary>
        double? ModuleAffinityLoadFactor { get; }

        /// <summary>
        /// Updates file content table by scanning change journal.
        /// </summary>
        bool UpdateFileContentTableByScanningChangeJournal { get; }

        /// <summary>
        /// Makes scheduler aware of process remoting via AnyBuild.
        /// </summary>
        bool EnableProcessRemoting { get; }

        /// <summary>
        /// Number of remote leases for process remoting.
        /// </summary>
        /// <remarks>
        /// This setting determines the maximum number of processes that can be executed remotely at one time
        /// when <see cref="EnableProcessRemoting"/> is true.
        /// 
        /// AnyBuild agents have a leasing mechanism for executing processes in them. AnyBuild client needs to get a lease
        /// from an agent in order to execute a process in that agent. The more agents we have, the more leases available, and
        /// the more processes can be executed remotely.
        /// 
        /// In the current implementation, BuildXL does not know the number of available leases (or agents). So, in conjuction with <see cref="MaxProcesses"/>,
        /// this number is only used to increase the number of ready-to-execute processes that can be released at one time by the CPU dispatcher queue. Having
        /// more released processes gives the scheduler chance to execute some of them remotely.
        /// 
        /// TODO: Get information about agent leases from AnyBuild, and so this number can be set dynamically.
        /// </remarks>
        int? NumOfRemoteAgentLeases { get; }

        /// <summary>
        /// Tags indicating that the process can run on remote agent.
        /// </summary>
        /// <remarks>
        /// These tags are only applicable when <see cref="EnableProcessRemoting"/> is true.
        /// </remarks>
        IReadOnlyList<string> ProcessCanRunRemoteTags { get; }

        /// <summary>
        /// Tags indicating that the process must run locally.
        /// </summary>
        /// <remarks>
        /// These tags are only applicable when <see cref="EnableProcessRemoting"/> is true.
        /// </remarks>
        IReadOnlyList<string> ProcessMustRunLocalTags { get; }

        /// <summary>
        /// Specifies the maximum number of processes that BuildXL will execute (or launch) at one time.
        /// </summary>
        /// <remarks>
        /// This setting includes the maximum number of processes that BuildXL will execute (or launch) locally at one time, as
        /// specified by <see cref="MaxProcesses"/>. This setting also includes the maximum number of processes that BuildXL
        /// will execute in remote agents when <see cref="EnableProcessRemoting"/> is true. Thus, this configuration should not
        /// be less than <see cref="MaxProcesses"/>.
        /// 
        /// The reason for introducing this configuration is because <see cref="MaxProcesses"/> is also used to control parallelism of
        /// components other than the number of processes to execute.
        /// 
        /// By default the value is the sum of <see cref="MaxProcesses"/> and <see cref="NumOfRemoteAgentLeases"/>. If <see cref="EnableProcessRemoting"/>
        /// is false, then <see cref="NumOfRemoteAgentLeases"/> is assumed to be 0.
        /// 
        /// Note that, even though we have <see cref="MaxProcesses"/> + <see cref="NumOfRemoteAgentLeases"/> processes ready to execute, that does not mean
        /// that <see cref="MaxProcesses"/> will execute locally and <see cref="NumOfRemoteAgentLeases"/> will execute remotely. The scheduler may decide
        /// to execute <see cref="MaxProcesses"/> locally, but only start remoting processes when certain threshold is exceeded. However, the number
        /// of (ready-to-execute) processes that will be released by the (CPU) dispatcher queue will not exceed <see cref="EffectiveMaxProcesses"/>.
        /// </remarks>
        int EffectiveMaxProcesses { get; }

        /// <summary>
        /// The multiplier for <see cref="MaxProcesses"/> that determines when the scheduler starts to execute process pips remotely when <see cref="EnableProcessRemoting"/> is true.
        /// </summary>
        double RemotingThresholdMultiplier { get; }

        /// <summary>
        /// The amount of wait time in seconds for getting a remote agent to execute process pip remotely when <see cref="EnableProcessRemoting"/> is true.
        /// </summary>
        /// <remarks>
        /// When a remote agent cannot be obtained by the specified time, then the process pip will fallback to local execution.
        /// When the specified time is negative, then the process pip will wait forever for a remote agent.
        /// </remarks>
        double RemoteAgentWaitTimeSec { get; }

        /// <summary>
        /// Whether Cpu resource determines the scheduling behavior
        /// </summary>
        bool CpuResourceAware { get; }

        /// <summary>
        /// Whether to enable the remote cache cut-off feature, which avoids lookups to the remote cache
        /// after a number of consecutive cache misses in a dependency chain
        /// </summary>
        public bool RemoteCacheCutoff { get; set; }

        /// <summary>
        /// Number of consecutive cache misses in a dependency chain before avoiding the remote cache
        /// </summary>
        public int RemoteCacheCutoffLength { get; set; }
    }
}
