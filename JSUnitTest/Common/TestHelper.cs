// TestHelper.cs
//
// Copyright 2010 Microsoft Corporation
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using System;
using System.IO;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Xml;

using Microsoft.Ajax.Utilities;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace JSUnitTest
{
    /// <summary>
    /// This class implements a Singleton Pattern.
    /// The purpose of class is to encapsulate the Unit Test related methods.
    /// 
    /// Note - The implementation is not designed thread safe.
    /// </summary>
    sealed class TestHelper
    {
        /// <summary>
        /// the name of the unit test folder under the main project folder
        /// </summary>
        private const string c_unitTestsDataFolder = "JS";

        /// <summary>
        /// folder path for input files to tests
        /// </summary>
        private string m_inputFolder;

        /// <summary>
        /// folder path for output files generated by tests
        /// </summary>
        private string m_outputFolder;

        /// <summary>
        /// folder path for expected results to compare against output
        /// </summary>
        private string m_expectedFolder;

        /// <summary>
        /// singleton construct
        /// </summary>
        private static readonly TestHelper m_instance = new TestHelper();
        public static TestHelper Instance
        {
            get { return m_instance; }
        }

        #region constructor

        /// <summary>
        /// private constructor so no one outside the class can create an instance
        /// </summary>
        private TestHelper()
        {
            // start with the unit test DLL. All test data folders will be deployed there by testrun configuration.
            // In order to do that, make sure that "Deployment" section in .testrunconfig file contains the "TestData" folder. If
            // this is the case, then everything in the folder will be copied down right next to unit test DLL.
            DirectoryInfo directoryInfo = new DirectoryInfo(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location));

            // Initialize the input, output and expected folders
            m_inputFolder = Path.Combine(Path.Combine(directoryInfo.FullName, c_unitTestsDataFolder), "Input");
            m_outputFolder = Path.Combine(Path.Combine(directoryInfo.FullName, c_unitTestsDataFolder), "Output");
            m_expectedFolder = Path.Combine(Path.Combine(directoryInfo.FullName, c_unitTestsDataFolder), "Expected");

            // output folder may not exist -- create it if it doesn't
            if (!Directory.Exists(m_outputFolder))
            {
                Directory.CreateDirectory(m_outputFolder);
            }

            // input and expected folders should already exists because we
            // check in files under each one
            Trace.WriteLineIf(!Directory.Exists(m_inputFolder), "Input folder does not exist!");
            Trace.WriteLineIf(!Directory.Exists(m_expectedFolder), "Expected folder does not exist!");
        }

        #endregion

        #region RunTest

        public void RunTest()
        {
            RunTest(null);
        }

        public void RunTest(string extraArguments, params string[] extraInputs)
        {
            RunTest(true, extraArguments, extraInputs);
        }

        public void RunTest(bool inputExpected, string extraArguments, params string[] extraInputs)
        {
            // open the stack trace for this call
            StackTrace stackTrace = new StackTrace();
            string testClass = null;
            string testName = null;

            // save the name of the current method (RunTest)
            string currentMethodName = MethodInfo.GetCurrentMethod().Name;

            // loop from the previous frame up until we get a method name that is not the
            // same as the current method name
            for (int ndx = 1; ndx < stackTrace.FrameCount; ++ndx)
            {
                // get the frame
                StackFrame stackFrame = stackTrace.GetFrame(ndx);

                // we have different entry points with the same name -- we're interested
                // in the first one that ISN'T the same name as our method
                MethodBase methodBase = stackFrame.GetMethod();
                if (methodBase.Name != currentMethodName)
                {
                    // the calling method's name is the test name - we use this as-is for the output file name
                    // and we use any portion before an underscore as the input file
                    testName = methodBase.Name;
                    // get the method's class - we use this as the subfolder under input/output/expected
                    testClass = methodBase.DeclaringType.Name;
                    break;
                }
            }
            // we definitely should be able to find a function on the stack frame that
            // has a different name than this function, but just in case...
            Debug.Assert(testName != null && testClass != null, "Couldn't locate calling stack frame");

            // the output file is just the full test name
            string outputFile = testName;
            List<string> outputFiles = null;

            // the input file is the portion of the test name before the underscore (if any)
            string inputFile = testName.Split('_')[0];
            string inputPath = null;
            int inputCount = 0;

            // create a list we will append all our arguments to
            bool includeAnalysis = true;
            bool specifiesRename = false;
            LinkedList<string> args = new LinkedList<string>();
            if (!string.IsNullOrEmpty(extraArguments))
            {
                // split on spaces
                string[] options = extraArguments.Split(' ');

                // add each one to the args list
                for (int ndx = 0; ndx < options.Length; ++ndx)
                {
                    string option = options[ndx];

                    // ignore empty strings
                    if (option.Length > 0)
                    {
                        args.AddLast(option);
                        if (string.Compare(option, "-analyze", StringComparison.OrdinalIgnoreCase) == 0)
                        {
                            // don't include it -- we already added it
                            includeAnalysis = false;
                        }
                        else if (string.Compare(option, "-xml", StringComparison.OrdinalIgnoreCase) == 0)
                        {
                            // the next option should be an xml file name, so we'll add an option
                            // that is the test name, the .xml suffix, and scope it to the input path.
                            // set the inputPath variable to this path so we know we are going to use it
                            // as the "input"
                            inputPath = BuildFullPath(
                                m_inputFolder,
                                testClass,
                                inputFile,
                                ".xml",
                                true
                                );
                            args.AddLast(inputPath);
                            ++inputCount;
                            outputFiles = ReadXmlForOutputFiles(inputPath, testClass);
                        }
                        else if (string.Compare(option, "-rename", StringComparison.OrdinalIgnoreCase) == 0)
                        {
                            // rename with no param parts (a colon and other stuff) means the next
                            // option should be an xml file name, so we'll add an option
                            // that is the file scoped to the input path.
                            string nextFile = options[++ndx];

                            string renamePath = BuildFullPath(
                                m_inputFolder,
                                testClass,
                                Path.GetFileNameWithoutExtension(nextFile),
                                Path.GetExtension(nextFile),
                                true);

                            // add that scoped path to the arguments
                            args.AddLast(renamePath);
                        }
                        // the -r option can have a subpart, eg: -res:Strings, so only test to see if
                        // the first two characters of the current option are "-res"
                        else if (option.StartsWith("-res", StringComparison.OrdinalIgnoreCase))
                        {
                            // the next option is a resource file name, so we'll need to scope it to the input path
                            // FIRST we'll try to see if there's an existing compiled .RESOURCES file with the same
                            // name as the current test. eg: if test name is "foo_h", look for foo.resources
                            string resourcePath = BuildFullPath(
                                m_inputFolder,
                                testClass,
                                inputFile,
                                ".resources",
                                false
                                );
                            if (!File.Exists(resourcePath))
                            {
                                // if there's not .RESOURCES file, look for a .RESX file with the same
                                // name as the current test. eg: if test name is "foo_h", look for foo.resx
                                resourcePath = BuildFullPath(
                                    m_inputFolder,
                                    testClass,
                                    inputFile,
                                    ".resx",
                                    false
                                    );
                                if (!File.Exists(resourcePath))
                                {
                                    // doesn't exist!
                                    Assert.Fail(
                                        "Expected resource file does not exist for test '{0}' in folder {1}",
                                        inputFile,
                                        Path.Combine(m_inputFolder, testClass)
                                        );
                                }
                            }
                            args.AddLast(resourcePath);
                        }
                        else if (option.StartsWith("-rename:", StringComparison.OrdinalIgnoreCase) 
                            && option.IndexOf('=') < 0 && option.IndexOf("prop", StringComparison.OrdinalIgnoreCase) < 0)
                        {
                            specifiesRename = true;
                        }
                    }
                }
            }

            // if we haven't already specified analyze option
            if (includeAnalysis)
            {
                // add the -a option
                args.AddLast("-analyze");
            }
            // if we haven't already specified a renaming option, we will
            // use -rename:none so we don't have to always figure out what the hypercrunch 
            // should be
            if (!specifiesRename)
            {
                args.AddLast("-rename:none");
            }

            string outputPath = null;

            // if we haven't already set an input path, then we want to calculate the input/output
            // paths automatically from the test name (normal case)
            if (inputPath == null)
            {
                // compute the path to the output file
                outputPath = GetJsPath(
                  m_outputFolder,
                  testClass,
                  outputFile,
                  false
                  );

                // if it exists already, delete it
                if (File.Exists(outputPath))
                {
                    File.Delete(outputPath);
                }

                // add the output parameter to the end
                args.AddLast("-out");
                args.AddLast(outputPath);

                Trace.WriteLine("INPUT FILE(S):");

                // calculate the input path
                inputPath = GetJsPath(
                  m_inputFolder,
                  testClass,
                  inputFile,
                  false
                  );

                // always add the input file to the command line
                args.AddLast(inputPath);
                if (File.Exists(inputPath))
                {
                    // but don't trace its contents unless it actually exists
                    ++inputCount;
                    TraceFileContents(inputPath);
                }
                else
                {
                    Trace.WriteLine("[input file does not exist]");
                }

                // if there are any extra input files, add them now
                if (extraInputs != null && extraInputs.Length > 0)
                {
                    foreach (string extraInput in extraInputs)
                    {
                        if (extraInput.Length > 0)
                        {
                            // get the full path
                            inputPath = GetJsPath(
                              m_inputFolder,
                              testClass,
                              extraInput,
                              true
                              );

                            // add it to the list
                            args.AddLast(inputPath);

                            // output the file contents
                            Trace.WriteLine(string.Empty);
                            TraceFileContents(inputPath);
                            ++inputCount;
                        }
                    }
                }
            }
            else
            {
                Trace.WriteLine("INPUT FILE:");
                TraceFileContents(inputPath);
            }

            // create an array of strings the appropriate size
            string[] mainArguments = new string[args.Count];
            // copy the arguments to the array
            args.CopyTo(mainArguments, 0);

            // show command-line args
            Trace.WriteLine(string.Empty);
            Trace.WriteLine("COMMAND-LINE SWITCHES:");
            foreach (string arg in mainArguments)
            {
                if (arg.IndexOf(' ') >= 0)
                {
                    // at least one space -- enclose the argument in quotes
                    Trace.Write('"');
                    Trace.Write(arg);
                    Trace.Write('"');
                }
                else
                {
                    // no spaces; don't need quotes
                    Trace.Write(arg);
                }
                Trace.Write(' ');
            }
            Trace.WriteLine(string.Empty);

            // call the AjaxMin main function
            Trace.WriteLine(string.Empty);
            Trace.WriteLine("AJAXMIN Debug Spew:");

            // call Main directly
            int retValue = Microsoft.Ajax.Utilities.MainClass.Main(mainArguments);

            Trace.Write("RETURN CODE: ");
            Trace.WriteLine(retValue);

            // after the run, if we had inputs and one output file...
            if (inputCount > 0 && !string.IsNullOrEmpty(outputPath))
            {
                // compute the path to the expected file
                string expectedPath = GetJsPath(
                  m_expectedFolder,
                  testClass,
                  outputFile,
                  false
                  );

                Trace.WriteLine(string.Empty);
                Trace.WriteLine("odd \"" + expectedPath + "\" \"" + outputPath + "\"");

                Trace.WriteLine(string.Empty);
                Trace.WriteLine("EXPECTED OUTPUT FILE:");
                if (File.Exists(expectedPath))
                {
                    // trace output contents
                    TraceFileContents(expectedPath);
                }
                else
                {
                    // no expected file means we expect the output to be empty
                    Trace.WriteLine("File doesn't exist -- expect output file to be empty");
                }

                // the output file BETTER exist (even if it's just empty)...
                if (File.Exists(outputPath))
                {
                    Trace.WriteLine(string.Empty);
                    Trace.WriteLine("ACTUAL OUTPUT FILE:");
                    // trace output contents
                    TraceFileContents(outputPath);

                    // fail the test if the files do not match
                    Assert.IsTrue(CompareTextFiles(outputPath, expectedPath), "The expected output ({1}) and actual output ({0}) do not match!", outputPath, expectedPath);
                    //Assert.IsTrue(retValue == 0, "Run didn't succeed. Return code: {0}", retValue);
                }
                else if (File.Exists(expectedPath))
                {
                    // no output file, but we did expect an output! That is a failure
                    Assert.Fail("Output file does not exist, but one was expected!");
                }
                else
                {
                    // input file(s) and output file, but can't find output
                    Assert.IsTrue(
                        retValue != 0, 
                        "Run shouldn't succeed if no output is generated. Return code: {0}; output file: {1}", 
                        retValue,
                        outputPath
                        );
                }
            }
            else if (inputCount > 0)
            {
                if (outputFiles != null && outputFiles.Count > 0)
                {
                    // for each one...
                    for (int ndx = 0; ndx < outputFiles.Count; ++ndx)
                    {
                        outputPath = outputFiles[ndx];

                        // compute the expected file path from the filename of the output path.
                        string expectedPath = GetJsPath(
                            m_expectedFolder,
                            testClass,
                            Path.GetFileName(outputPath),
                            false
                            );

                        // trace the expected file contents
                        Trace.WriteLine(string.Empty);
                        Trace.WriteLine(string.Format("EXPECTED OUTPUT FILE {0}:", ndx+1));
                        if (File.Exists(expectedPath))
                        {
                            // trace output contents
                            TraceFileContents(expectedPath);
                        }
                        else
                        {
                            // no expected file means we expect the output to be empty
                            Trace.WriteLine("File doesn't exist -- expect output file to be empty");
                        }

                        // trace the output file contents
                        Trace.WriteLine(string.Empty);
                        Trace.WriteLine(string.Format("ACTUAL OUTPUT FILE {0}:", ndx+1));
                        // trace output contents
                        if (File.Exists(outputPath))
                        {
                            TraceFileContents(outputPath);
                        }
                        else
                        {
                            Trace.WriteLine("Output file doesn't exist");
                        }

                        // fail the entire test if the files do not match
                        Assert.IsTrue(CompareTextFiles(outputPath, expectedPath), "The expected output ({1}) and actual output ({0}) do not match!", outputPath, expectedPath);
                    }
                }
                else
                {
                    // input file(s), but no output file
                    Assert.Fail("No output files");
                }
            }
            else
            {
                // no input file(s)
                Trace.WriteLine("No input file(s).");

                // if we expected there to be input files, then we failed
                Assert.IsFalse(inputExpected, "Expected input files to exist");
                // and if we didn't expect the input files to exist, we better have failed
                Assert.IsTrue(retValue != 0, "Run shouldn't succeed if no input file(s). Return code: {0}", retValue);
            }
        }

        #endregion

        private List<string> ReadXmlForOutputFiles(string xmlPath, string subFolder)
        {
            List<string> outputFiles = null;
            try
            {
                // load in the xml file
                XmlDocument xmlDoc = new XmlDocument();
                xmlDoc.Load(xmlPath);

                // there should be at least one output node
                XmlNodeList outputNodes = xmlDoc.SelectNodes("//output");
                if (outputNodes.Count > 0)
                {
                    // create the list now and use the number of nodes as the initial capacity
                    outputFiles = new List<string>(outputNodes.Count);

                    for (int ndx = 0; ndx < outputNodes.Count; ++ndx)
                    {
                        // get the output path attribute
                        XmlAttribute pathAttribute = outputNodes[ndx].Attributes["path"];

                        // must exist and be non-empty for the purposes of this unit test because we
                        // can't really check for stdout files in this batch mode
                        if (pathAttribute == null || string.IsNullOrEmpty(pathAttribute.Value))
                        {
                            Assert.Fail("XML <output> nodes without path attributes not supported in unit tests");
                        }

                        // create the full path from the output folder, the subfolder, and the attribute.
                        // don't check for existence here -- we haven't run the test yet
                        string outputPath = GetJsPath(
                            m_outputFolder,
                            subFolder,
                            pathAttribute.Value,
                            false
                            );

                        // if the output file exists, it must be from a previous run.
                        // delete it now (the Delete method does not fail if the doesn't already exist,
                        // but it WILL fail if the path to the file doesn't)
                        if (File.Exists(outputPath))
                        {
                            File.Delete(outputPath);
                        }

                        // add it to the output files list
                        outputFiles.Add(outputPath);
                    }
                }
                else
                {
                    Assert.Fail("XML input file contains no <output> nodes");
                }
            }
            catch (XmlException e)
            {
                Debug.WriteLine(e.ToString());
                Assert.Fail("XML Exception processing XML input file: {0}", e.Message);
            }
            return outputFiles;
        }

        public void RunErrorTest(string settingsSwitches, params JSError[] expectedErrorArray)
        {
            // open the stack trace for this call
            StackTrace stackTrace = new StackTrace();
            string testClass = null;
            string testName = null;

            // save the name of the current method (RunTest)
            string currentMethodName = MethodInfo.GetCurrentMethod().Name;

            // loop from the previous frame up until we get a method name that is not the
            // same as the current method name
            for (int ndx = 1; ndx < stackTrace.FrameCount; ++ndx)
            {
                // get the frame
                StackFrame stackFrame = stackTrace.GetFrame(ndx);

                // we have different entry points with the same name -- we're interested
                // in the first one that ISN'T the same name as our method
                MethodBase methodBase = stackFrame.GetMethod();
                if (methodBase.Name != currentMethodName)
                {
                    // the calling method's name is the test name - we use this as-is for the output file name
                    // and we use any portion before an underscore as the input file
                    testName = methodBase.Name;
                    // get the method's class - we use this as the subfolder under input/output/expected
                    testClass = methodBase.DeclaringType.Name;
                    break;
                }
            }
            // we definitely should be able to find a function on the stack frame that
            // has a different name than this function, but just in case...
            Debug.Assert(testName != null && testClass != null, "Couldn't locate calling stack frame");

            // the input file is the portion of the test name before the underscore (if any)
            string inputFile = testName.Split('_')[0];

            // get the input and output paths
            string inputPath = GetJsPath(
              m_inputFolder,
              testClass,
              inputFile,
              false);
            Assert.IsTrue(File.Exists(inputPath), "Input File does not exist: {0}", inputPath);

            var outputPath = GetJsPath(
                m_outputFolder,
                testClass,
                testName,
                false);

            if (File.Exists(outputPath))
            {
                // if it exists already, delete it
                File.Delete(outputPath);
            }
            else
            {
                // otherwise make sure the directory exists
                Directory.CreateDirectory(Path.GetDirectoryName(outputPath));
            }

            /*int expectedErrorCode = (int)(0x800A0000 + (int)expectedError);
            Trace.WriteLine(string.Empty);
            Trace.WriteLine(string.Format("Expecting error 0x{0:X}", expectedErrorCode));*/

            // if we were passed a string containing command-line settings...
            var switchParser = new SwitchParser();
            if (!string.IsNullOrEmpty(settingsSwitches))
            {
                // parse the string now
                switchParser.Parse(settingsSwitches);
            }

            // read the input JS
            string jsSource;
            using (StreamReader reader = new StreamReader(inputPath, MainClass.GetJSEncoding(switchParser.EncodingInputName)))
            {
                jsSource = reader.ReadToEnd();
            }

            Trace.Write("INPUT FILE: ");
            Trace.WriteLine(inputPath);
            Trace.WriteLine(jsSource);

            bool testPassed = true;
            List<JSError> expectedErrorList = new List<JSError>(expectedErrorArray);
            ErrorTrap errorTrap = new ErrorTrap();
            string crunchedCode = errorTrap.RunTest(switchParser.JSSettings, jsSource);
            JScriptException[] errors = errorTrap.Errors;

            // output the crunched code using the proper output encoding
            using (var outputStream = new StreamWriter(outputPath, false, MainClass.GetJSEncoding(switchParser.EncodingOutputName)))
            {
                outputStream.Write(crunchedCode);
            }

            Trace.WriteLine(string.Empty);
            Trace.WriteLine("---ERRORS---");
            Trace.Indent();
            foreach (JScriptException ex in errors)
            {
                // log the error
                Trace.WriteLine(string.Empty);
                Trace.WriteLine(string.Format("Error 0x{0:X} at Line {1}, Column {2}: {3}", ex.Error, ex.Line, ex.Column, ex.ErrorSegment));
                Trace.Indent();
                Trace.WriteLine(ex.Message);

                JSError errorCode = (JSError)(ex.Error & 0xffff);
                int index = expectedErrorList.IndexOf(errorCode);
                if (index >= 0)
                {
                    // expected error -- remove it from the list so we can tell what we're missing later
                    expectedErrorList.RemoveAt(index);
                }
                else
                {
                    // unexpected error
                    testPassed = false;
                    Trace.WriteLine("UNEXPECTED");
                }
                Trace.Unindent();
            }
            Trace.Unindent();
            // the list should be empty now -- if it isn't, then there was an expected error that didn't happen
            if (expectedErrorList.Count > 0)
            {
                testPassed = false;
                Trace.WriteLine(string.Empty);
                Trace.WriteLine("---MISSING ERRORS---");
                Trace.Indent();
                foreach (JSError jsError in expectedErrorList)
                {
                    Trace.WriteLine(jsError.ToString());
                }
                Trace.Unindent();
            }

            if (!testPassed)
            {
                Trace.WriteLine("UNEXPECTED ERROR RESULTS");
            }

            // compute the path to the expected file
            string expectedPath = GetJsPath(
                m_expectedFolder,
                testClass,
                testName,
                false);

            Trace.WriteLine(string.Empty);
            Trace.WriteLine("odd \"" + expectedPath + "\" \"" + outputPath + "\"");

            Trace.WriteLine(string.Empty);
            Trace.WriteLine("---Expected Code---");
            TraceFileContents(expectedPath);

            Trace.WriteLine(string.Empty);
            Trace.WriteLine("---Resulting Code---");
            TraceFileContents(outputPath);

            Assert.IsTrue(CompareTextFiles(outputPath, expectedPath), "The expected output ({1}) and actual output ({0}) do not match!", outputPath, expectedPath);
            Assert.IsTrue(testPassed, "Test failed");
        }

        private class ErrorTrap
        {
            private List<JScriptException> m_errorList;
            public JScriptException[] Errors
            {
                get { return m_errorList.ToArray(); }
            }

            public ErrorTrap()
            {
                m_errorList = new List<JScriptException>();
            }

            public string RunTest(CodeSettings codeSettings, string sourceCode)
            {
                JSParser jsParser = new JSParser(sourceCode);
                jsParser.CompilerError += OnCompilerError;

                // kick off the parsing
                Block programBlock = jsParser.Parse(codeSettings);

                // return the crunched code
                return programBlock.ToCode();
            }

            void OnCompilerError(object sender, JScriptExceptionEventArgs ex)
            {
                // add it to the list and keep on truckin'
                m_errorList.Add(ex.Exception);
            }
        }

        #region helper methods

        // start with root folder, add subfolder, then add the file name + ".js" extension.
        private string GetJsPath(string rootFolder, string subfolder, string fileName, bool mustExist)
        {
            var ext = Path.GetExtension(fileName);
            return BuildFullPath(rootFolder, subfolder, fileName, string.IsNullOrEmpty(ext) ? ".js" : ext, mustExist);
        }

        // start with root folder, add subfolder, then add the file name + extension.
        private string BuildFullPath(string rootFolder, string subfolder, string fileName, string extension, bool mustExist)
        {
            string folderPath = Path.Combine(rootFolder, subfolder);
            string fullPath = Path.ChangeExtension(Path.Combine(folderPath, fileName), extension);
            if (mustExist)
            {
                Assert.IsTrue(
                  File.Exists(fullPath),
                  string.Format("Expected file does not exist: {0}", fullPath)
                  );
            }
            return fullPath;
        }

        private void TraceFileContents(string filePath)
        {
            using (StreamReader reader = new StreamReader(filePath))
            {
                string text = reader.ReadToEnd();

                Trace.WriteLine(filePath);
                Trace.WriteLine(text);
                //Trace.WriteLine(string.Empty);
            }
        }

        private bool CompareTextFiles(string leftPath, string rightPath)
        {
            // the left file should always exist
            Assert.IsTrue(File.Exists(leftPath),"File does not exist: {0}", leftPath);

            // right file may not exist -- if it doesn't, the left file must be empty to be the same
            //Assert.IsTrue(File.Exists(rightPath),"File does not exist: {0}",rightPath);

            using (StreamReader leftReader = new StreamReader(leftPath))
            {
                // read the left file in its entirety
                string left = leftReader.ReadToEnd();
                if (File.Exists(rightPath))
                {
                    using (StreamReader rightReader = new StreamReader(rightPath))
                    {
                        string right = rightReader.ReadToEnd();

                        return (string.Compare(left, right) == 0);
                    }
                }
                else
                {
                    // right file doesn't exist -- compare against an empty string
                    return (string.Compare(left, string.Empty) == 0);
                }
            }
        }

        #endregion
    }
}
