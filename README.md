Source code for BorsukSoftware.Tools.AWSDeviceFarm.Launcher

## Purpose
AWS Device Farm allows a user to run their app on a range of devices and perform tests accordingly. This can be driven entirely by the provided SDK, but there was no single command line option for doing so (that we are / were aware of anyway). To that end, we've put together a small dotnet tool to make it very easy to launch a  device farm job from the command line without needing to write any code.

It's currently targetting Appium Python tests (because that was our use-case), but can be easily extended to cover additional types very easily. If this is a requirement, then please do get in touch, raise an issue above or raise a PR with the suggested improvement.

Internally, we use this to launch device farm jobsfrom our devops pipelines as part of automating the testing for our clients' mobile apps.

## Usage

To use the tool, it first needs to be installed

```
dotnet new tool-manifest
dotnet tool install BorsukSoftware.Tools.AWSDeviceFarm.Launcher
```

It can then be run using the following:

```
dotnet tool run BorsukSoftware.Tools.AWSDeviceFarm.Launcher -- `
  -project "awsDeviceFarmProjectName" `
  -devicePool "Pixel-5" `
  -testType AppiumPython `
  -tests "$testsPath" `
  -app "$buildDetails" `
  -prefix "build#4798-" `
  -testSpec "android-test.yml"
```

Notes:
* $testsPath - The tests zip file
* $buildDetails - the path to the .ipa / .apk file
* android-test.yml - this is our named custom testSpec (FWIW we increase the logging level and run with -rA to enable detailed logs)

As with all AWS jobs, the security information is sourced at runtime from environment variables. These should be set as per any other job. 

## Extensions
We would like to:

* extend the tool to allow the launching of non-AppiumPython tests. Currently we have no use-case nor client request for it, if this is required, then please do get in touch.
* expose all of the additional config options (currently, these haven't been required by our clients and so they've not been a priority)

## FAQs
#### How do we use the tool to run tests against both Apple devices and Android devices?
Run the tool twice, once per device category and then collate the results later on

#### How do we know when the job has completed?
Control will be returned to the user immediately after the job has been launched with an exit code of 0 if the job was successfully launched, otherwies a non-zero error code will be returned.

To operate in a synchronous mode, we also provide BorsukSoftware.Tools.AWSDeviceFarm.RunWaiter as a dotnet tool to wait on job completion.

#### We've discovered a bug, what do we do?
Contact us / raise an issue / raise a PR.

#### We would like an additional feature, what do we do?
See above
