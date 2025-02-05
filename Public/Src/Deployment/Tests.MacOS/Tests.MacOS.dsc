// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import {Transformer} from "Sdk.Transformers";
import * as Deployment from "Sdk.Deployment";
import * as BuildXLSdk from "Sdk.BuildXL";

namespace Tests.MacOS {
    export declare const qualifier : { configuration: "debug" | "release", targetFramework: BuildXLSdk.TargetFrameworks.CoreClrTargetFrameworks, targetRuntime: "osx-x64" };

    const sharedBinFolderName = a`sharedbin`;
    const tests = createAllDefs();

    function createAllDefs() : TestDeploymentDefinition[] {
        return [
            // Utilities
            createDef(importFrom("BuildXL.Utilities.Instrumentation.UnitTests").Core.withQualifier({ targetFramework: "net6.0" }).dll, true),
            createDef(importFrom("BuildXL.Utilities.UnitTests").Collections.withQualifier({ targetFramework: "net6.0" }).dll, true),
            createDef(importFrom("BuildXL.Utilities.UnitTests").Configuration.withQualifier({ targetFramework: "net6.0" }).dll, true),
            createDef(importFrom("BuildXL.Utilities.UnitTests").Ipc.withQualifier({ targetFramework: "net6.0" }).dll, true),
            createDef(importFrom("BuildXL.Utilities.UnitTests").KeyValueStoreTests.withQualifier({ targetFramework: "net6.0" }).dll, true),
            createDef(importFrom("BuildXL.Utilities.UnitTests").Storage.withQualifier({ targetFramework: "net6.0" }).dll, true),
            createDef(importFrom("BuildXL.Utilities.UnitTests").Storage.Untracked.withQualifier({ targetFramework: "net6.0" }).dll, true),
            createDef(importFrom("BuildXL.Utilities.UnitTests").ToolSupport.withQualifier({ targetFramework: "net6.0" }).dll, true),
            createDef(importFrom("BuildXL.Utilities.UnitTests").Core.withQualifier({ targetFramework: "net6.0" }).dll, true),

            // Cache
            createDef(importFrom("BuildXL.Cache.ContentStore").Test.withQualifier({ targetFramework: "net6.0" }).dll, true),
            createDef(importFrom("BuildXL.Cache.ContentStore").GrpcTest.withQualifier({ targetFramework: "net6.0" }).dll, true),
            createDef(importFrom("BuildXL.Cache.ContentStore").InterfacesTest.withQualifier({ targetFramework: "net6.0" }).dll, true),
            createDef(importFrom("BuildXL.Cache.ContentStore").DistributedTest.withQualifier({ targetFramework: "net6.0" }).dll, true),
            createDef(importFrom("BuildXL.Cache.MemoizationStore").Test.withQualifier({ targetFramework: "net6.0" }).dll, true),
            createDef(importFrom("BuildXL.Cache.MemoizationStore").InterfacesTest.withQualifier({ targetFramework: "net6.0" }).dll, true),
            createDef(importFrom("BuildXL.Cache.DistributedCache.Host").Test.withQualifier({ targetFramework: "net6.0" }).dll, true),
            createDef(importFrom("BuildXL.Cache.Core.UnitTests").Analyzer.withQualifier({ targetFramework: "net6.0" }).dll, true),
            createDef(importFrom("BuildXL.Cache.Core.UnitTests").BasicFilesystem.withQualifier({ targetFramework: "net6.0" }).dll, true),
            createDef(importFrom("BuildXL.Cache.Core.UnitTests").InputListFilter.withQualifier({ targetFramework: "net6.0" }).dll, true),
            createDef(importFrom("BuildXL.Cache.Core.UnitTests").Interfaces.withQualifier({ targetFramework: "net6.0" }).dll, true),
            createDef(importFrom("BuildXL.Cache.Core.UnitTests").MemoizationStoreAdapter.withQualifier({ targetFramework: "net6.0" }).dll, true),
            createDef(importFrom("BuildXL.Cache.Core.UnitTests").VerticalAggregator.withQualifier({ targetFramework: "net6.0" }).dll, true),
            ...addIfLazy(BuildXLSdk.Flags.isMicrosoftInternal, () => [
                createDef(importFrom("BuildXL.Cache.Logging").Test.withQualifier({ targetFramework: "net6.0" }).dll, true) 
            ]),
            createDef(importFrom("BuildXL.Cache.MemoizationStore").Test.withQualifier({ targetFramework: "net6.0" }).dll, true)
        ];
    }

    interface TestDeploymentDefinition extends Deployment.NestedDefinition {
        assembly: File;
        enabled: boolean;
        testClasses: string[];
        categoriesToRunInParallel: string[];
        categoriesToNeverRun: string[];
        runSuppliedCategoriesOnly: boolean;
    }

    function createDef(testResult: BuildXLSdk.TestResult, enabled: boolean) : TestDeploymentDefinition {
        let assembly = testResult.testDeployment.primaryFile;
        return <TestDeploymentDefinition>{
            subfolder: sharedBinFolderName,
            contents: [
                testResult.testDeployment.deployedDefinition
            ],

            assembly: assembly,
            testAssemblies: [],
            enabled: enabled,
            testClasses: undefined,
            categoriesToRunInParallel: undefined,
            categoriesToNeverRun: undefined,
            runSuppliedCategoriesOnly: false
        };
    }

    function genXUnitExtraArgs(definition: TestDeploymentDefinition): string {
        return [
            ...(definition.testClasses || []).map(testClass => `-class ${testClass}`),
            ...(definition.categoriesToNeverRun || []).map(cat => `-notrait "Category=${cat}"`)
        ].join(" ");
    }

    function getRunXunitCommands(def: TestDeploymentDefinition): string[] {
        const base: string = `run_xunit "\${MY_DIR}/tests/${def.subfolder}"${' '}${def.assembly.name}${' '}${genXUnitExtraArgs(def)}`;
        const traits: string[] = (def.categoriesToRunInParallel || [])
            .map(cat => `${base} -trait "Category=${cat}"`);
        const rest: string = [
            base,
            ...(def.categoriesToRunInParallel || []).map(cat => `-notrait "Category=${cat}"`)
        ].join(" ");
        return def.runSuppliedCategoriesOnly
            ? traits
            : [...traits, rest];
    }

    function createUnixTestRunnerScript(definitions: TestDeploymentDefinition[]): string {
        const runTestCommands = tests
            .filter(def => def.enabled)
            .mapMany(getRunXunitCommands);

        return [
            "#!/bin/bash",
            "",
            "MY_DIR=$(cd `dirname ${BASH_SOURCE[0]}` && pwd)",
            "source $MY_DIR/xunitrunner.sh",
            "",
            "find . \\( -name SandboxedProcessExecutor -o -name Test.BuildXL.Executables.InfiniteWaiter -o -name Test.BuildXL.Executables.TestProcess \\) -print0 | xargs -0 chmod +x",
            "",
            "numTestFailures=0",
            "trap \"((numTestFailures++))\" ERR",
            "",
            ...runTestCommands,
            "",
            "exit $numTestFailures"
        ].join("\n");
    }

    function writeFile(fileName: PathAtom, content: string): DerivedFile {
        return Transformer.writeAllText({
            outputPath: p`${Context.getNewOutputDirectory("standalone-tests")}/${fileName}`,
            text: content
        });
    }

    /*
        Folder layout:

            ├── [tests]
            │   └── [sharedbin]
            |       └── ... (BuildXL core drop + test deployments)
            ├── bashrunner.sh
            ├── env.sh
            └── xunitrunner.sh
    */
    @@public
    export const deployment : Deployment.Definition = {
        contents: [
            f`xunitrunner.sh`,
            writeFile(a`bashrunner.sh`, createUnixTestRunnerScript(tests)),
            f`${Context.getMount("Sandbox").path}/MacOs/scripts/env.sh`,
            {
                subfolder: r`tests`,
                contents: [
                    // BuildXL core drop (Contains test dependencies)
                    {
                        subfolder: sharedBinFolderName,
                        contents: [ BuildXL.deployment ]
                    },
                    // Test dlls
                    ...tests,
                ]

            }
        ]
    };

    @@public
    export const deployed = BuildXLSdk.DeploymentHelpers.deploy({
        definition: deployment,
        targetLocation: r`${qualifier.configuration}/tests/${qualifier.targetRuntime}`,
    });
}
