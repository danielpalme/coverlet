using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

using Coverlet.Core.Abstracts;
using Coverlet.Core.Helpers;
using Coverlet.Core.Samples.Tests;
using Coverlet.Tests.RemoteExecutor;
using Moq;
using Xunit;

namespace Coverlet.Core.Tests
{
    public class CoverageTests
    {
        private readonly InstrumentationHelper _instrumentationHelper = new InstrumentationHelper(new ProcessExitHandler(), new RetryHelper(), new FileSystem());
        private readonly Mock<ILogger> _mockLogger = new Mock<ILogger>();

        [Fact]
        public void TestCoverage()
        {
            string module = GetType().Assembly.Location;
            string pdb = Path.Combine(Path.GetDirectoryName(module), Path.GetFileNameWithoutExtension(module) + ".pdb");

            var directory = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()));

            File.Copy(module, Path.Combine(directory.FullName, Path.GetFileName(module)), true);
            File.Copy(pdb, Path.Combine(directory.FullName, Path.GetFileName(pdb)), true);

            // TODO: Find a way to mimick hits

            var coverage = new Coverage(Path.Combine(directory.FullName, Path.GetFileName(module)), Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(), false, false, string.Empty, false, _mockLogger.Object, _instrumentationHelper, new FileSystem());
            coverage.PrepareModules();

            var result = coverage.GetCoverageResult();

            Assert.Empty(result.Modules);

            directory.Delete(true);
        }

        [Fact]
        public void TestCoverageSkipModule__AssemblyMarkedAsExcludeFromCodeCoverage()
        {
            Mock<FileSystem> partialMockFileSystem = new Mock<FileSystem>();
            partialMockFileSystem.CallBase = true;
            partialMockFileSystem.Setup(fs => fs.NewFileStream(It.IsAny<string>(), It.IsAny<FileMode>(), It.IsAny<FileAccess>())).Returns((string path, FileMode mode, FileAccess access) =>
            {
                return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            });
            var loggerMock = new Mock<ILogger>();

            string excludedbyattributeDll = Directory.GetFiles(Path.Combine(Directory.GetCurrentDirectory(), "TestAssets"), "coverlet.tests.projectsample.excludedbyattribute.dll").First();
            // test skip module include test assembly feature
            var coverage = new Coverage(excludedbyattributeDll, new string[] { "[coverlet.tests.projectsample.excludedbyattribute*]*" }, Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(), true, false, string.Empty, false, loggerMock.Object, _instrumentationHelper, partialMockFileSystem.Object);
            CoveragePrepareResult result = coverage.PrepareModules();
            Assert.Empty(result.Results);
            loggerMock.Verify(l => l.LogVerbose(It.IsAny<string>()));
        }

        [Fact]
        public void TestCoverageWithTestAssembly()
        {
            string module = GetType().Assembly.Location;
            string pdb = Path.Combine(Path.GetDirectoryName(module), Path.GetFileNameWithoutExtension(module) + ".pdb");

            var directory = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()));

            File.Copy(module, Path.Combine(directory.FullName, Path.GetFileName(module)), true);
            File.Copy(pdb, Path.Combine(directory.FullName, Path.GetFileName(pdb)), true);

            var coverage = new Coverage(Path.Combine(directory.FullName, Path.GetFileName(module)), Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(), true, false, string.Empty, false, _mockLogger.Object, _instrumentationHelper, new FileSystem());
            coverage.PrepareModules();

            var result = coverage.GetCoverageResult();

            Assert.NotEmpty(result.Modules);

            directory.Delete(true);
        }

        [Fact]
        public void SelectionStatements_If()
        {
            // We need to pass file name to remote process where it save instrumentation result
            // Similar to msbuild input/output
            string path = Path.GetTempFileName();
            try
            {
                // Lambda will run in a custom process to avoid issue with statics and file locking
                RemoteExecutor.Invoke(async pathSerialize =>
                {
                    // Run load and call a delegate passing class as dynamic to simplify method call
                    CoveragePrepareResult coveragePrepareResult = await TestInstrumentationHelper.Run<SelectionStatements>(instance =>
                    {
                        // We call method to trigger coverage hits
                        instance.If(true);

                        // For now we have only async Run helper
                        return Task.CompletedTask;
                    }, persistPrepareResultToFile: pathSerialize);

                    // we return 0 if we return something different assert fail
                    return 0;
                }, path).Dispose();

                // We retrive and load CoveragePrepareResult and run coverage calculation
                // Similar to msbuild coverage result task
                CoverageResult result = TestInstrumentationHelper.GetCoverageResult(path);

                // Generate html report to check
                // TestInstrumentationHelper.GenerateHtmlReport(result);

                // Asserts on doc/lines/branches
                result.Document("Instrumentation.SelectionStatements.cs")
                      // (line, hits)
                      .AssertLinesCovered((11, 1), (15, 0))
                      // (line,ordinal,hits)
                      .AssertBranchesCovered((9, 0, 1), (9, 1, 0));
            }
            finally
            {
                // Cleanup tmp file
                File.Delete(path);
            }
        }

        [Fact]
        public void SelectionStatements_Switch()
        {
            string path = Path.GetTempFileName();
            try
            {
                RemoteExecutor.Invoke(async pathSerialize =>
                {
                    CoveragePrepareResult coveragePrepareResult = await TestInstrumentationHelper.Run<SelectionStatements>(instance =>
                    {
                        instance.Switch(1);
                        return Task.CompletedTask;
                    }, persistPrepareResultToFile: pathSerialize);
                    return 0;
                }, path).Dispose();

                CoverageResult result = TestInstrumentationHelper.GetCoverageResult(path);

                result.Document("Instrumentation.SelectionStatements.cs")
                      .AssertLinesCovered(BuildConfiguration.Release, (24, 1), (26, 0), (28, 0))
                      .AssertBranchesCovered(BuildConfiguration.Release, (24, 1, 1))
                      .AssertLinesCovered(BuildConfiguration.Debug, (20, 1), (21, 1), (24, 1), (30, 1))
                      .AssertBranchesCovered(BuildConfiguration.Debug, (21, 0, 0), (21, 1, 1), (21, 2, 0), (21, 3, 0));
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public void AsyncAwait()
        {
            string path = Path.GetTempFileName();
            try
            {
                RemoteExecutor.Invoke(async pathSerialize =>
                {
                    CoveragePrepareResult coveragePrepareResult = await TestInstrumentationHelper.Run<AsyncAwait>(instance =>
                    {
                        instance.SyncExecution();

                        int res = ((Task<int>)instance.AsyncExecution(true)).ConfigureAwait(false).GetAwaiter().GetResult();
                        res = ((Task<int>)instance.AsyncExecution(1)).ConfigureAwait(false).GetAwaiter().GetResult();
                        res = ((Task<int>)instance.AsyncExecution(2)).ConfigureAwait(false).GetAwaiter().GetResult();
                        res = ((Task<int>)instance.AsyncExecution(3)).ConfigureAwait(false).GetAwaiter().GetResult();
                        res = ((Task<int>)instance.ContinuationCalled()).ConfigureAwait(false).GetAwaiter().GetResult();
                        res = ((Task<int>)instance.ConfigureAwait()).ConfigureAwait(false).GetAwaiter().GetResult();

                        return Task.CompletedTask;
                    }, persistPrepareResultToFile: pathSerialize);
                    return 0;
                }, path).Dispose();

                CoverageResult result = TestInstrumentationHelper.GetCoverageResult(path);

                result.Document("Instrumentation.AsyncAwait.cs")
                      .AssertLinesCovered(BuildConfiguration.Debug,
                                            // AsyncExecution(bool)
                                            (10, 1), (11, 1), (12, 1), (14, 1), (16, 1), (17, 0), (18, 0), (19, 0), (21, 1), (22, 1),
                                            // Async
                                            (25, 9), (26, 9), (27, 9), (28, 9),
                                            // SyncExecution
                                            (31, 1), (32, 1), (33, 1),
                                            // Sync
                                            (36, 1), (37, 1), (38, 1),
                                            // AsyncExecution(int)
                                            (41, 3), (42, 3), (43, 3), (46, 1), (47, 1), (48, 1), (51, 1),
                                            (52, 1), (53, 1), (56, 1), (57, 1), (58, 1), (59, 1),
                                            (62, 0), (63, 0), (64, 0), (65, 0), (68, 0), (70, 3), (71, 3),
                                            // ContinuationNotCalled
                                            (74, 0), (75, 0), (76, 0), (77, 0), (78, 0),
                                            // ContinuationCalled -> line 83 should be 1 hit some issue with Continuation state machine
                                            (81, 1), (82, 1), (83, 2), (84, 1), (85, 1),
                                            // ConfigureAwait
                                            (89, 1), (90, 1)
                                         )
                      .AssertBranchesCovered(BuildConfiguration.Debug, (16, 0, 0), (16, 1, 1), (43, 0, 3), (43, 1, 1), (43, 2, 1), (43, 3, 1), (43, 4, 0))
                      // Real branch should be 2, we should try to remove compiler generated branch in method ContinuationNotCalled/ContinuationCalled
                      // for Continuation state machine
                      .ExpectedTotalNumberOfBranches(BuildConfiguration.Debug, 4);
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public void Lambda_Issue343()
        {
            string path = Path.GetTempFileName();
            try
            {
                RemoteExecutor.Invoke(async pathSerialize =>
                {
                    CoveragePrepareResult coveragePrepareResult = await TestInstrumentationHelper.Run<Lambda_Issue343>(instance =>
                    {
                        instance.InvokeAnonymous_Test();
                        ((Task<bool>)instance.InvokeAnonymousAsync_Test()).ConfigureAwait(false).GetAwaiter().GetResult();
                        return Task.CompletedTask;
                    }, persistPrepareResultToFile: pathSerialize);
                    return 0;
                }, path).Dispose();

                CoverageResult result = TestInstrumentationHelper.GetCoverageResult(path);

                result.Document("Instrumentation.Lambda.cs")
                      .AssertLinesCoveredAllBut(BuildConfiguration.Debug, 23, 51)
                      .AssertBranchesCovered(BuildConfiguration.Debug,
                        // Expected branches
                        (22, 0, 0),
                        (22, 1, 1),
                        (50, 2, 0),
                        (50, 3, 1),
                        // Unexpected branches
                        (20, 0, 1),
                        (20, 1, 1),
                        (49, 0, 1),
                        (49, 1, 0),
                        (54, 4, 0),
                        (54, 5, 1),
                        (39, 0, 1),
                        (39, 1, 0),
                        (48, 0, 1),
                        (48, 1, 1)
                      );
            }
            finally
            {
                File.Delete(path);
            }
        }


        [Fact]
        public void ExcludeFromCodeCoverage_CompilerGeneratedMethodsAndTypes()
        {
            string path = Path.GetTempFileName();
            try
            {
                RemoteExecutor.Invoke(async pathSerialize =>
                {
                    CoveragePrepareResult coveragePrepareResult = await TestInstrumentationHelper.Run<MethodsWithExcludeFromCodeCoverageAttr>(instance =>
                    {
                        ((Task<int>)instance.Test("test")).ConfigureAwait(false).GetAwaiter().GetResult();
                        return Task.CompletedTask;
                    }, persistPrepareResultToFile: pathSerialize);

                    return 0;

                }, path).Dispose();

                CoverageResult result = TestInstrumentationHelper.GetCoverageResult(path);

                var document = result.Document("Instrumentation.ExcludeFromCoverage.cs");

                // Invoking method "Test" of class "MethodsWithExcludeFromCodeCoverageAttr" we expect to cover 100% lines for MethodsWithExcludeFromCodeCoverageAttr 
                Assert.DoesNotContain(document.Lines, l =>
                    (l.Value.Class == "Coverlet.Core.Samples.Tests.MethodsWithExcludeFromCodeCoverageAttr" ||
                    // Compiler generated
                    l.Value.Class.StartsWith("Coverlet.Core.Samples.Tests.MethodsWithExcludeFromCodeCoverageAttr/")) &&
                    l.Value.Hits == 0);
                // and 0% for MethodsWithExcludeFromCodeCoverageAttr2
                Assert.DoesNotContain(document.Lines, l =>
                    (l.Value.Class == "Coverlet.Core.Samples.Tests.MethodsWithExcludeFromCodeCoverageAttr2" ||
                    // Compiler generated
                    l.Value.Class.StartsWith("Coverlet.Core.Samples.Tests.MethodsWithExcludeFromCodeCoverageAttr2/")) &&
                    l.Value.Hits == 1);
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public void ExcludeFromCodeCoverage_CompilerGeneratedMethodsAndTypes_NestedMembers()
        {
            string path = Path.GetTempFileName();
            try
            {
                RemoteExecutor.Invoke(async pathSerialize =>
                {
                    CoveragePrepareResult coveragePrepareResult = await TestInstrumentationHelper.Run<MethodsWithExcludeFromCodeCoverageAttr_NestedStateMachines>(instance =>
                    {
                        instance.Test();
                        return Task.CompletedTask;
                    }, persistPrepareResultToFile: pathSerialize);

                    return 0;

                }, path).Dispose();

                CoverageResult result = TestInstrumentationHelper.GetCoverageResult(path);

                result.Document("Instrumentation.ExcludeFromCoverage.NestedStateMachines.cs")
                        .AssertLinesCovered(BuildConfiguration.Debug, (14, 1), (15, 1), (16, 1))
                        .AssertNonInstrumentedLines(BuildConfiguration.Debug, 9, 11);
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public void ExcludeFromCodeCoverageCompilerGeneratedMethodsAndTypes_Issue670()
        {
            string path = Path.GetTempFileName();
            try
            {
                RemoteExecutor.Invoke(async pathSerialize =>
                {
                    CoveragePrepareResult coveragePrepareResult = await TestInstrumentationHelper.Run<MethodsWithExcludeFromCodeCoverageAttr_Issue670>(instance =>
                    {
                        instance.Test("test");
                        return Task.CompletedTask;
                    }, persistPrepareResultToFile: pathSerialize);

                    return 0;

                }, path).Dispose();

                CoverageResult result = TestInstrumentationHelper.GetCoverageResult(path);

                result.Document("Instrumentation.ExcludeFromCoverage.Issue670.cs")
                        .AssertLinesCovered(BuildConfiguration.Debug, (8, 1), (9, 1), (10, 1), (11, 1))
                        .AssertNonInstrumentedLines(BuildConfiguration.Debug, 15, 53);
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public void ExcludeFromCodeCoverageNextedTypes()
        {
            string path = Path.GetTempFileName();
            try
            {
                RemoteExecutor.Invoke(async pathSerialize =>
                {
                    CoveragePrepareResult coveragePrepareResult = await TestInstrumentationHelper.Run<ExcludeFromCoverageAttrFilterClass1>(instance =>
                    {
                        Assert.Equal(42, instance.Run());
                        return Task.CompletedTask;
                    }, persistPrepareResultToFile: pathSerialize);

                    return 0;
                }, path).Dispose();

                TestInstrumentationHelper.GetCoverageResult(path)
                .Document("Instrumentation.ExcludeFromCoverage.cs")
                .AssertLinesCovered(BuildConfiguration.Debug, (143, 1))
                .AssertNonInstrumentedLines(BuildConfiguration.Debug, 146, 160);
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public void ExcludeFilteredNestedAutogeneratedTypes()
        {
            string path = Path.GetTempFileName();
            try
            {
                RemoteExecutor.Invoke(async pathSerialize =>
                {
                    CoveragePrepareResult coveragePrepareResult = await TestInstrumentationHelper.Run<ExcludeFilterNestedAutogeneratedTypes>(instance =>
                    {
                        instance.Run();

                        PropertyInfo stateProp = null;
                        foreach (Type type in ((Type)instance.GetType()).Assembly.GetTypes())
                        {
                            if (typeof(Issue_689).FullName == type.FullName)
                            {
                                Assert.Equal(0, (stateProp = type.GetProperty("State")).GetValue(null));
                                break;
                            }
                        }

                        foreach (Type type in ((Type)instance.GetType()).Assembly.GetTypes())
                        {
                            if (typeof(EventSource_Issue_689).FullName == type.FullName)
                            {
                                type.GetMethod("RaiseEvent").Invoke(null, null);
                                break;
                            }
                        }

                        Assert.Equal(2, stateProp.GetValue(null));

                        return Task.CompletedTask;
                    },
                    includeFilter: moduleFileName => new string[] { $"[{Path.GetFileNameWithoutExtension(moduleFileName)}*]*ExcludeFilterNestedAutogeneratedTypes", $"[{Path.GetFileNameWithoutExtension(moduleFileName)}*]*Issue_689" },
                    excludeFilter: moduleFileName => new string[] { $"[{Path.GetFileNameWithoutExtension(moduleFileName)}*]*NestedToFilterOut", $"[{Path.GetFileNameWithoutExtension(moduleFileName)}*]*Uncoverlet" },
                    persistPrepareResultToFile: pathSerialize);

                    return 0;
                }, path).Dispose();

               TestInstrumentationHelper.GetCoverageResult(path)
               .Document("Instrumentation.ExcludeFilter.cs")
               .AssertLinesCovered(BuildConfiguration.Debug, (12, 1), (13, 1), (14, 1))
               .AssertLinesCovered(BuildConfiguration.Debug, (27, 1), (28, 1), (29, 1), (30, 1), (31, 1))
               .AssertLinesCovered(BuildConfiguration.Debug, (39, 2), (40, 2), (41, 2), (43, 5))
               .AssertLinesCovered(BuildConfiguration.Debug, (50, 1), (51, 1), (52, 1))
               .AssertNonInstrumentedLines(BuildConfiguration.Debug, 17, 21)
               .AssertNonInstrumentedLines(BuildConfiguration.Debug, 33, 36);
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public void ExcludeFilteredTypes()
        {
            string path = Path.GetTempFileName();
            try
            {
                RemoteExecutor.Invoke(async pathSerialize =>
                {
                    CoveragePrepareResult coveragePrepareResult = await TestInstrumentationHelper.Run<ExcludeFilterOuterTypes>(instance =>
                    {
                        Assert.Equal(42, instance.Run());
                        return Task.CompletedTask;
                    },
                    excludeFilter: moduleFileName => new string[] { $"[{Path.GetFileNameWithoutExtension(moduleFileName)}*]*ExcludeFilterOuterTypes" },
                    persistPrepareResultToFile: pathSerialize);

                    return 0;
                }, path).Dispose();

                TestInstrumentationHelper.GetCoverageResult(path)
                .Document("Instrumentation.ExcludeFilter.cs")
                .AssertNonInstrumentedLines(BuildConfiguration.Debug, 1, 62)
                .AssertLinesCovered(BuildConfiguration.Debug, (66, 1), (68, 1));
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public void ExcludeFilteredNestedTypes()
        {
            string path = Path.GetTempFileName();
            try
            {
                RemoteExecutor.Invoke(async pathSerialize =>
                {
                    CoveragePrepareResult coveragePrepareResult = await TestInstrumentationHelper.Run<ExcludeFilterClass1>(instance =>
                    {
                        Assert.Equal(42, instance.Run());
                        return Task.CompletedTask;
                    },
                    excludeFilter: moduleFileName => new string[] { $"[{Path.GetFileNameWithoutExtension(moduleFileName)}*]*ExcludeFilterClass2" },
                    persistPrepareResultToFile: pathSerialize);

                    return 0;
                }, path).Dispose();

                TestInstrumentationHelper.GetCoverageResult(path)
                .Document("Instrumentation.ExcludeFilter.cs")
                .AssertLinesCovered(BuildConfiguration.Debug, (73, 1))
                .AssertNonInstrumentedLines(BuildConfiguration.Debug, 75, 93);
            }
            finally
            {
                File.Delete(path);
            }
        }
    }
}
