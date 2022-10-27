using Amazon.DeviceFarm;
using Amazon.DeviceFarm.Model;
using Amazon.Runtime;
using System;
using System.Collections.Generic;
using System.Diagnostics.SymbolStore;
using System.IO;
using System.Linq;
using System.Reflection.Metadata.Ecma335;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace BorsukSoftware.Tools.AWSDeviceFarm.Launcher
{
    public class Program
    {
        private const string CONST_HELPTEXT = @"AWS Device Farm Job Launcher
============================

Summary:
This app is designed to make it easy to launch an AWS Device Farm test job from the command line.

The tool allows the specification of all of the inputs to the run, either by files to upload or
by using existing resources (specified by their existing arn).

Currently, this is aimed at Appium-Python tests, but updating this is trivial.

Note that the tool itself returns control to the end user as soon as the AWS job has been requested
and as such, it behooves the caller to orchestrate their own waiting process.

We provide BorsukSoftware.Tools.AWSDeviceFarm.RunWaiter to perform this function.

Security Model:
The security variables are read in from environment variables and as such, they should be set accordingly.

Required parameters:
 -project XXX               The name or arn of the AWS device farm project
 -devicePool XXX            The name of arn of the device pool to use
 -app XXX                   The name or arn of the app file to upload (.ipa or .apk)
 -tests XXX                 The name or arn of the tests file (typically tests.zip)
 -testSpec XXX              The name or arn of the custom test spec to run
 -customTestSpecFile XXX    The name of the local file to upload as the custom test spec file
 -testType XXX              The type of the test to be run, currently only AppiumPython is supported

Optional:
 -testRunName XXX           The name of the test run (optional)
 -prefix XXX                The prefix to add to any file name uploaded to AWS (typically 'buildNumber-')

 -testFilter XXX            Optional test filter
 -testParameter Key Value   Optional test parameter

The following can be used to override the execution configuration:
 -executionConfigAccountsCleanup bool
 -executionConfigAppPackagesCleanup bool
 -executionConfigJobTimeoutMinutes XXX
 -executionConfigSkipAppResign bool
 -executionConfigVideoCapture bool

Others:
 --help                     Show this help text";


        public static async Task<int> Main(string[] args)
        {
            if (args.Length == 0)
            {
                Console.WriteLine(CONST_HELPTEXT);
                return 0;
            }

            var testRunName = $"Test Run - {DateTime.UtcNow:yyyy-MM-dd HHmmss}";
            string paramProject = null, paramDevicePool = null, paramApp = null, paramTests = null, paramTestSpec = null, paramCustomTestSpecFile = null, testFilter = null;
            string prefix = null;
            var testParameters = new Dictionary<string, string>();
            bool? executionConfigAccountsCleanup = null, executionConfigAppPackagesCleanup = null, executionConfigSkipAppResign = null, executionConfigVideoCapture = null;
            int? executionConfigJobTimeoutMinutes = 5;
            TestType? testType = null;
            for (int i = 0; i < args.Length; ++i)
            {
                switch (args[i].ToLower())
                {
                    case "-project":
                        paramProject = args[++i];
                        break;

                    case "-devicepool":
                        paramDevicePool = args[++i];
                        break;

                    case "-testrunname":
                        testRunName = args[++i];
                        break;

                    case "-testfilter":
                        testFilter = args[++i];
                        break;

                    case "-testparameter":
                        {
                            var key = args[++i];
                            var value = args[++i];
                            testParameters.Add(key, value);
                        }
                        break;

                    case "-testtype":
                        {
                            var testTypeStr = args[++i];
                            if (!Enum.TryParse<TestType>(testTypeStr, true, out var tt))
                            {
                                Console.WriteLine($"Unable to parse '{testTypeStr}' as a valid test type");
                                return 1;
                            }

                            testType = tt;
                        }
                        break;

                    case "-app":
                        paramApp = args[++i];
                        break;

                    case "-tests":
                        paramTests = args[++i];
                        break;

                    case "-prefix":
                        prefix = args[++i];
                        break;

                    case "-testspec":
                        paramTestSpec = args[++i];
                        break;

                    case "-customtestspecfile":
                        paramCustomTestSpecFile = args[++i];
                        break;

                    /* Execution Config */

                    case "-executionconfigaccountscleanup":
                        executionConfigAccountsCleanup = bool.Parse(args[++i]);
                        break;

                    case "-executionconfigapppackagescleanup":
                        executionConfigAppPackagesCleanup = bool.Parse(args[++i]);
                        break;

                    case "-executionconfigskipappresign":
                        executionConfigSkipAppResign = bool.Parse(args[++i]);
                        break;

                    case "-executionconfigvideocapture":
                        executionConfigVideoCapture = bool.Parse(args[++i]);
                        break;

                    case "-executionconfigjobtimeoutminutes":
                        {
                            var str = args[++i];
                            if (!int.TryParse(str, out var v))
                            {
                                Console.WriteLine($"Unable to parse '{str}' as a valid job time out");
                                return 1;
                            }

                            executionConfigJobTimeoutMinutes = v;
                            break;
                        }

                    /* General */
                    case "--help":
                        Console.WriteLine(CONST_HELPTEXT);
                        return 0;

                    default:
                        {
                            Console.WriteLine($"Unknown command line arg - {args[i]}");
                            return 1;
                        }
                }
            }

            if( !testType.HasValue)
            {
                Console.WriteLine("No test type specified");
                return 1;
            }

            if (string.IsNullOrEmpty(paramProject))
            {
                Console.WriteLine("No project specified");
                return 1;
            }

            if (string.IsNullOrEmpty(paramDevicePool))
            {
                Console.WriteLine("No device pool specified");
                return 1;
            }

            if (string.IsNullOrEmpty(paramTests))
            {
                Console.WriteLine("No tests file specified");
                return 1;
            }

            if (string.IsNullOrEmpty(paramApp))
            {
                Console.WriteLine("No app file specified");
                return 1;
            }

            if (string.IsNullOrEmpty(paramCustomTestSpecFile) && string.IsNullOrEmpty(paramTestSpec))
            {
                Console.WriteLine("No custom env file or test env specified");
                return 1;
            }

            if (!string.IsNullOrEmpty(paramCustomTestSpecFile) && !string.IsNullOrEmpty(paramTestSpec))
            {
                Console.WriteLine("Both custom env file and test env specified, only 1 can be specified at a time");
                return 1;
            }

            Console.WriteLine("Creating farm client");
            var amazonDeviceFarmClient = new Amazon.DeviceFarm.AmazonDeviceFarmClient(Amazon.RegionEndpoint.USWest2);

            /************************************ Fetch all details from AWS ***********************************/
            var projectArn = paramProject;
            if (!paramProject.StartsWith("arn:"))
            {
                Console.WriteLine("Sourcing project details from AWS");
                var projectList = await amazonDeviceFarmClient.ListProjectsAsync(new ListProjectsRequest());
                var project = projectList.Projects.SingleOrDefault(p => StringComparer.InvariantCultureIgnoreCase.Compare(paramProject, p.Name) == 0);
                if (project == null)
                {
                    Console.WriteLine($"No project name '{paramProject}' found");
                    return 1;
                }

                projectArn = project.Arn;
            }

            var devicePoolArn = paramDevicePool;
            if (!paramDevicePool.StartsWith("arn:"))
            {
                Console.WriteLine("Sourcing device pool details from AWS");
                var devicePools = await amazonDeviceFarmClient.ListDevicePoolsAsync(new ListDevicePoolsRequest { Arn = projectArn });
                var devicePool = devicePools.DevicePools.SingleOrDefault(p => StringComparer.InvariantCultureIgnoreCase.Compare(paramDevicePool, p.Name) == 0);
                if (devicePool == null)
                {
                    Console.WriteLine($"No device pool '{paramDevicePool}' found");
                    return 1;
                }

                devicePoolArn = devicePool.Arn;
            }

            var testSpecArn = paramTestSpec;
            if (!string.IsNullOrEmpty(paramTestSpec) && !paramTestSpec.StartsWith("arn:"))
            {
                var uploads = await amazonDeviceFarmClient.ListUploadsAsync(new ListUploadsRequest { Arn = projectArn, Type = UploadType.APPIUM_PYTHON_TEST_SPEC });
                var testEnvUpload = uploads.Uploads.SingleOrDefault(u => StringComparer.InvariantCultureIgnoreCase.Compare(paramTestSpec, u.Name) == 0);
                if (testEnvUpload == null)
                {
                    Console.WriteLine($"No test env '{paramTestSpec}' found");
                    return 1;
                }

                testSpecArn = testEnvUpload.Arn;
            }
            else if (!string.IsNullOrEmpty(paramCustomTestSpecFile))
            {
                var fileName = System.IO.Path.GetFileName(paramCustomTestSpecFile);
                Console.WriteLine($"Uploading {fileName}");
                testSpecArn = await UploadFile(amazonDeviceFarmClient,
                    projectArn,
                    prefix,
                    System.IO.Path.GetFileName(paramCustomTestSpecFile),
                    UploadType.APPIUM_PYTHON_TEST_SPEC,
                    paramCustomTestSpecFile);
            }

            var packageArn = paramApp;
            if (!packageArn.StartsWith("arn:"))
            {
                UploadType uploadType;
                switch (System.IO.Path.GetExtension(packageArn).ToLower())
                {
                    case ".ipa":
                        uploadType = UploadType.IOS_APP;
                        break;

                    case ".apk":
                        uploadType = UploadType.ANDROID_APP;
                        break;

                    default:
                        Console.WriteLine($"Don't know how to handle '{packageArn}' as an app upload");
                        return 1;
                }

                Console.WriteLine("Uploading app");
                packageArn = await UploadFile(amazonDeviceFarmClient,
                    projectArn,
                    prefix,
                    System.IO.Path.GetFileName(paramApp),
                    uploadType,
                    paramApp);
            }

            var testsFileArn = paramTests;
            if (!testsFileArn.StartsWith("arn:"))
            {
                Console.WriteLine("Uploading tests.zip");
                testsFileArn = await UploadFile(amazonDeviceFarmClient,
                    projectArn,
                    prefix,
                    "tests.zip",
                    UploadType.APPIUM_PYTHON_TEST_PACKAGE,
                    paramTests);
            }

            // Wait for 5s to give AWS time to have processed the uploads
            // 
            // There's probably a better way of doing this, but this guarantees it at least...
            int delayCount = 5;
            Console.WriteLine($"Waiting for {delayCount}s to allow AWS to have processed the uploads");
            for (int i = 0; i < delayCount; ++i)
            {
                await Task.Delay(1000);
                Console.Write(".");
            }

            Console.WriteLine();

            Console.WriteLine("Submitting requests to AWS");
            var executionConfiguration = new ExecutionConfiguration();
            if (executionConfigAccountsCleanup.HasValue)
                executionConfiguration.AccountsCleanup = executionConfigAccountsCleanup.Value;
            if (executionConfigAppPackagesCleanup.HasValue)
                executionConfiguration.AppPackagesCleanup = executionConfigAppPackagesCleanup.Value;
            if (executionConfigJobTimeoutMinutes.HasValue)
                executionConfiguration.JobTimeoutMinutes = executionConfigJobTimeoutMinutes.Value;
            if (executionConfigSkipAppResign.HasValue)
                executionConfiguration.SkipAppResign = executionConfigSkipAppResign.Value;
            if (executionConfigVideoCapture.HasValue)
                executionConfiguration.VideoCapture = executionConfigVideoCapture.Value;

            var runRequest = new ScheduleRunRequest
            {
                AppArn = packageArn,
                DevicePoolArn = devicePoolArn,
                Name = testRunName,
                ExecutionConfiguration = executionConfiguration,
                ProjectArn = projectArn,
                Test = new ScheduleRunTest
                {
                    Type = Amazon.DeviceFarm.TestType.APPIUM_PYTHON,
                    TestPackageArn = testsFileArn,
                    Filter = testFilter,
                    Parameters = testParameters,
                    TestSpecArn = testSpecArn,
                }
            };

            Console.WriteLine($" App: {runRequest.AppArn}");
            Console.WriteLine($" Device Pool: {runRequest.DevicePoolArn}");
            Console.WriteLine($" Name: {runRequest.Name}");
            Console.WriteLine($" Project: {runRequest.ProjectArn}");
            Console.WriteLine($" Test.Type: {runRequest.Test.Type}");
            Console.WriteLine($" Test.TestPackageArn: {runRequest.Test.TestPackageArn}");
            Console.WriteLine($" Test.TestSpecArn: {runRequest.Test.TestSpecArn}");

            try
            {
                var run = await amazonDeviceFarmClient.ScheduleRunAsync(runRequest);

                Console.WriteLine($"Run started - {run.Run.Arn}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($" => failed to launch job - {ex.Message}");
                Console.WriteLine(ex);
                return 1;
            }

            return 0;
        }

        #region Utils methods

        /// <summary>
        /// Debug method to output everything which has been uploaded 
        /// </summary>
        /// <param name="amazonDeviceFarmClient"></param>
        /// <returns></returns>
        private static async Task OutputAllUploads(AmazonDeviceFarmClient amazonDeviceFarmClient, string projectArn)
        {
            var allUploads = await amazonDeviceFarmClient.ListUploadsAsync(new ListUploadsRequest { Arn = projectArn });
            foreach (var upload in allUploads.Uploads)
            {
                Console.WriteLine("Upload");
                Console.WriteLine($" -name: {upload.Name}");
                Console.WriteLine($" -arn: {upload.Arn}");
                Console.WriteLine($" -type: {upload.Type}");
                Console.WriteLine($" -created: {upload.Created}");
                Console.WriteLine();
            }
        }

        private static async Task<string> UploadFile(
            Amazon.DeviceFarm.AmazonDeviceFarmClient amazonDeviceFarmClient,
            string projectArn,
            string prefix,
            string itemName,
            UploadType uploadType,
            string path)
        {
            var combinedName = $"{prefix}{itemName}";

            Console.WriteLine($" => creating '{combinedName}' ({uploadType})");

            var uploadRequest = new CreateUploadRequest
            {
                ProjectArn = projectArn,
                Name = combinedName,
                Type = uploadType
            };
            var upload = await amazonDeviceFarmClient.CreateUploadAsync(uploadRequest);

            Console.WriteLine($" => {upload.Upload.Arn}");

            Console.WriteLine($" => pushing content");
            using (var fileStream = new System.IO.FileStream(path, FileMode.Open, FileAccess.Read))
            {
                var client = new System.Net.Http.HttpClient();
                var putResponse = await client.PutAsync(upload.Upload.Url, new System.Net.Http.StreamContent(fileStream));

                putResponse.EnsureSuccessStatusCode();
            };
            Console.WriteLine($" => upload complete");

            Console.Write($" => checking processing");

            int count = 0;
            while (true)
            {
                var getUploadResponse = await amazonDeviceFarmClient.GetUploadAsync(new GetUploadRequest { Arn = upload.Upload.Arn });

                if (getUploadResponse.Upload.Status == UploadStatus.FAILED)
                {
                    Console.WriteLine();
                    throw new System.InvalidOperationException($"Failed to upload '{itemName}': {getUploadResponse.Upload.Metadata}");
                }
                else if (getUploadResponse.Upload.Status == UploadStatus.SUCCEEDED)
                {
                    Console.WriteLine();
                    Console.WriteLine(" => complete");
                    break;
                }
                else
                {
                    ++count;
                    if (count > 100)
                        throw new System.InvalidOperationException("Failed to process in time");
                    Console.Write('.');
                    await Task.Delay(100);
                }
            }

            return upload.Upload.Arn;
        }

        #endregion

        #region TestType enum

        /// <summary>
        /// Enum to allow callers to specify what type of test should be run
        /// </summary>
        private enum TestType
        {
            AppiumPython
        }

        #endregion
    }
}