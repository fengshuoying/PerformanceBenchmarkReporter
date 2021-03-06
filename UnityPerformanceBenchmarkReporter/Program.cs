﻿using System;
using System.Collections.Generic;
using System.Linq;
using UnityPerformanceBenchmarkReporter.Entities;
using UnityPerformanceBenchmarkReporter.Report;

namespace UnityPerformanceBenchmarkReporter
{
    internal class Program
    {
        private static int indentLevel;

        private static readonly Dictionary<Type, string[]> ExcludedConfigFieldNames = new Dictionary<Type, string[]>
        {
            {typeof(EditorVersion), new []
            {
                "DateSeconds",
                "RevisionValue"
            }},
            {typeof(BuildSettings), new []
            {
                "DevelopmentPlayer"
            }},
            {typeof(PlayerSystemInfo), new []
            {
                "XrModel"
            }},
            {typeof(PlayerSettings), new []
            {
                "MtRendering", // Hidden because we have a calculated field, RenderThreadingMode, that provides a more succinct value (SingleThreaded, MultiThreaded, GfxJobs)
                "GraphicsJobs", // Hidden because we have a calculated field, RenderThreadingMode, that provides a more succinct value (SingleThreaded, MultiThreaded, GfxJobs)
                "VrSupported", // Hidden because this value doesn't seem to be coming through as 'True' when it should be true.
                "AndroidMinimumSdkVersion",
                "AndroidTargetSdkVersion"
            }}
        };

        private static int Main(string[] args)
        {
            var aggregateTestRunResults = new List<PerformanceTestRunResult>();
            var baselinePerformanceTestRunResults = new List<PerformanceTestRunResult>();
            var baselineTestResults = new List<TestResult>();
            var performanceTestRunResults = new List<PerformanceTestRunResult>();
            var testResults = new List<TestResult>();
            var performanceBenchmark = new PerformanceBenchmark(ExcludedConfigFieldNames);
            var optionsParser = new OptionsParser();

            optionsParser.ParseOptions(performanceBenchmark, args);
            var testResultXmlParser = new TestResultXmlParser();

            if (performanceBenchmark.BaselineResultFilesExist)
            {
                performanceBenchmark.AddBaselinePerformanceTestRunResults(testResultXmlParser, baselinePerformanceTestRunResults, baselineTestResults);

                if (baselinePerformanceTestRunResults.Any())
                {
                    aggregateTestRunResults.AddRange(baselinePerformanceTestRunResults);
                }
                else
                {
                    Environment.Exit(1);
                }
            }

            if (performanceBenchmark.ResultFilesExist)
            {
                performanceBenchmark.AddPerformanceTestRunResults(testResultXmlParser, performanceTestRunResults, testResults, baselineTestResults);

                if (performanceTestRunResults.Any())
                {
                    aggregateTestRunResults.AddRange(performanceTestRunResults);
                }
                else
                {
                    Environment.Exit(1);
                }
            }

            var performanceTestResults = new PerformanceTestRunResult[0]; 

            // If we have a baseline
            if (aggregateTestRunResults.Any(a => a.IsBaseline))
            {
                // Insert the baseline in the front of the array results; this way we can display the baseline first in the report
                Array.Resize(ref performanceTestResults, 1);
                performanceTestResults[0] = aggregateTestRunResults.First(a => a.IsBaseline);
            }

            var nonBaselineTestRunResults = aggregateTestRunResults.Where(a => !a.IsBaseline).ToList();

            nonBaselineTestRunResults.Sort((run1, run2) => string.Compare(run1.ResultName, run2.ResultName, StringComparison.Ordinal));


            foreach (var performanceTestRunResult in nonBaselineTestRunResults)
            {
                Array.Resize(ref performanceTestResults, performanceTestResults.Length + 1);
                performanceTestResults[performanceTestResults.Length - 1] = performanceTestRunResult;
            }

            performanceBenchmark.TestRunMetadataProcessor.PerformFinalMetadataUpdate(performanceBenchmark);

            var reportWriter = new ReportWriter(performanceBenchmark.TestRunMetadataProcessor);

            reportWriter.WriteReport(
                performanceTestResults,
                performanceBenchmark.SigFig,
                performanceBenchmark.ReportDirPath,
                performanceBenchmark.BaselineResultFilesExist);

            return WriteFailedTestsAndMetricsToConsole(performanceTestResults, performanceBenchmark);
        }

        private static int WriteFailedTestsAndMetricsToConsole(PerformanceTestRunResult[] performanceTestResults, PerformanceBenchmark performanceBenchmark)
        {
            var failedTestsExist = performanceTestResults.SelectMany(ptr => ptr.TestResults)
                .Any(tr => tr.State == (int) TestState.Failure);
            if (failedTestsExist)
            {
                WriteLine("FAILURE: One ore more performance test metric aggregations is out of threshold from the baseline value.");
                WriteLine("-------------------------------------");
                WriteLine(" Performance tests with failed metrics");
                WriteLine("-------------------------------------");
                foreach (var performanceTestRunResult in performanceTestResults)
                {
                    var failedTests = performanceTestRunResult.TestResults.Where(tr => tr.State == (int)TestState.Failure);
                    if (failedTests.Any())
                    {
                        foreach (var failedTest in failedTests)
                        {
                            ++indentLevel;
                            WriteLine("{0}", failedTest.TestName);

                            var regressedSgs = failedTest.SampleGroupResults.Where(sgr => sgr.Regressed);
                            foreach (var sampleGroupResult in regressedSgs)
                            {
                                WriteLine("----");
                                WriteLine("Metric        : {0}", sampleGroupResult.SampleGroupName);
                                WriteLine("Aggregation   : {0}", sampleGroupResult.AggregationType);
                                WriteLine("Failed Value  : {0,8:F2}", sampleGroupResult.AggregatedValue);
                                WriteLine("Baseline Value: {0,8:F2}", sampleGroupResult.BaselineValue);
                                WriteLine("Threshold %   : {0,8:F2}", sampleGroupResult.Threshold);
                                WriteLine("Actual Diff % : {0,8:F2}", Math.Abs(sampleGroupResult.AggregatedValue - sampleGroupResult.BaselineValue) / sampleGroupResult.BaselineValue);
                            }
                            --indentLevel;
                            WriteLine("\r\n");
                        }
                    }
                }
            }
            
            return performanceBenchmark.FailOnBaseline && failedTestsExist ? 1 : 0;
        }

        private static void WriteLine(string format, params object[] args)
        {
            Console.Write(new string('\t', indentLevel));
            Console.WriteLine(format, args);
        }
    }
}
