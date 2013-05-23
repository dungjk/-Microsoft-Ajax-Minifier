// AjaxMinBuild.cs
//
// Copyright 2013 Microsoft Corporation
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
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Xml;
using Microsoft.Ajax.Utilities;
using Microsoft.Ajax.Utilities.Configuration;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

namespace Microsoft.Ajax.Minifier.Tasks
{
    /// <summary>
    /// MSBuild task for AjaxMin manifest files
    /// </summary>
    public class AjaxMinManifestTask : Task
    {
        #region public properties

        /// <summary>
        /// Default AjaxMin switches to use for the project
        /// </summary>
        public string ProjectDefaultSwitches { get; set; }

        /// <summary>
        /// Root folder to manifest input file relative paths (if different than the location of the manifest file)
        /// </summary>
        public string InputFolder { get; set; }

        /// <summary>
        /// Root folder to manifest input file relative paths for original source files for error messages (if different than InputFolder)
        /// </summary>
        public string SourceFolder { get; set; }

        /// <summary>
        /// Root folder to manifest output file relative paths (if different than the location of the manifest file)
        /// </summary>
        public string OutputFolder { get; set; }

        /// <summary>
        /// Whether to treat warnings as errors
        /// </summary>
        public bool TreatWarningsAsErrors { get; set; }

        /// <summary>
        /// Configuration
        /// </summary>
        public string Configuration { get; set; }

        /// <summary>
        /// List of manifest files to process
        /// </summary>
        public ITaskItem[] Manifests { get; set; }

        /// <summary>
        /// Gets or sets whether this execution is a clean operation (delete output files)
        /// or a normal build operation (create output files)
        /// </summary>
        public bool IsCleanOperation { get; set; }

        #endregion

        #region Execute method

        public override bool Execute()
        {
            if (Manifests != null && Manifests.Length > 0)
            {
                // create the project default settings
                var projectDefaultSettings = new SwitchParser();
                if (!ProjectDefaultSwitches.IsNullOrWhiteSpace())
                {
                    projectDefaultSettings.Parse(ProjectDefaultSwitches);
                }

                // each task item represents an ajaxmin manifest file: an XML file that
                // has settings and one or more output files, each comprised of one or more
                // input files. To execute this process, we will read the XML manifest and
                // execute AjaxMin for each output group.
                // won't bother executing AjaxMin is the file time for the output file
                // is greater than all its inputs.
                foreach (var taskItem in Manifests)
                {
                    ProcessManifest(taskItem, projectDefaultSettings);
                }
            }

            // we succeeded if there have been no errors logged
            return !Log.HasLoggedErrors;
        }

        #endregion

        #region manifest processing

        private void ProcessManifest(ITaskItem taskItem, SwitchParser projectDefaultSettings)
        {
            // save the manifest folder - paths within the manifest will be relative to it
            // if there are no InputFolder or OutputFolder values
            var manifestFolder = Path.GetDirectoryName(taskItem.ItemSpec);
            var manifestModifiedTime = File.GetLastWriteTimeUtc(taskItem.ItemSpec);

            // process the XML file into objects
            Manifest manifest = null;
            try
            {
                // read the manifest in
                manifest = ManifestUtilities.ReadManifestFile(taskItem.ItemSpec);

                // if an input folder was specified and it exists, use that as the root
                // for all input files. Otherwise use the manifest folder path.
                var inputFolder = (this.InputFolder.IsNullOrWhiteSpace() || !Directory.Exists(this.InputFolder))
                    ? manifestFolder
                    : this.InputFolder;

                // validate and normalize all paths. 
                manifest.ValidateAndNormalize(inputFolder, this.OutputFolder, !this.IsCleanOperation);
            }
            catch (FileNotFoundException ex)
            {
                Log.LogError(ex.Message + ex.FileName.IfNotNull(s => " " + s).IfNullOrWhiteSpace(string.Empty));
            }
            catch (XmlException ex)
            {
                Log.LogError(ex.Message);
            }

            if (manifest != null)
            {
                // create the default settings for this configuration, if there are any, based on
                // the project default settings.
                var defaultSettings = ParseConfigSettings(manifest.GetConfigArguments(this.Configuration), projectDefaultSettings);

                // for each output group
                foreach (var outputGroup in manifest.Outputs)
                {
                    ProcessOutputGroup(outputGroup, defaultSettings, manifestModifiedTime);
                }
            }
        }

        private void ProcessOutputGroup(OutputGroup outputGroup, SwitchParser defaultSettings, DateTime manifestModifiedTime)
        {
            // get the file info for the output file. It should already be normalized.
            var outputFileInfo = new FileInfo(outputGroup.Path);

            // the symbol map is an OPTIONAL output, so if we don't want one, we ignore it.
            // but if we do, we need to check for its existence and filetimes, just like 
            // the regular output file
            var symbolsFileInfo = outputGroup.SymbolMap.IfNotNull(sm => new FileInfo(sm.Path));

            if (IsCleanOperation)
            {
                // delete the output file(s)
                outputFileInfo.Delete();
                symbolsFileInfo.IfNotNull(fi => fi.Delete());
            }
            else
            {
                // generate the output files
                GenerateOutput(outputFileInfo, symbolsFileInfo, outputGroup, defaultSettings, manifestModifiedTime);
            }
        }

        private void GenerateOutput(FileInfo outputFileInfo, FileInfo symbolsFileInfo, OutputGroup outputGroup, SwitchParser defaultSettings, DateTime manifestModifiedTime)
        {
            // build the output files
            var processGroup = false;
            var codeType = outputGroup.CodeType;

            if (!outputFileInfo.Exists
                || (symbolsFileInfo != null && !symbolsFileInfo.Exists))
            {
                // one or more outputs don't exist, so we need to process this group
                processGroup = true;
            }
            else
            {
                // output exists. we need to check to see if it's older than
                // any of its input files, and if not, there's no need to process
                // this group. get the filetime of the output file.
                var outputFileTime = outputFileInfo.LastWriteTimeUtc;

                // if we don't want a symbol map, then ignore that output. But if we
                // do and it doesn't exist, then we want to process the group. If we
                // do and it does, then check its filetime and set out output filetime
                // to be the earliest of the two (output or symbols)
                if (symbolsFileInfo != null)
                {
                    var symbolsFileTime = symbolsFileInfo.LastWriteTimeUtc;
                    if (symbolsFileTime < outputFileTime)
                    {
                        outputFileTime = symbolsFileTime;
                    }
                }

                // first check the time of the manifest file itself. If it's newer than the output
                // time, then we need to process. Otherwise we need to check each input source file.
                if (manifestModifiedTime > outputFileTime)
                {
                    // the manifest itself has been changed after the last output that was generated,
                    // so yes: we need to process this group.
                    processGroup = true;
                }
                else
                {
                    // check filetime of each input file, and if ANY one is newer, 
                    // then we will want to set the process-group flag and stop checking
                    foreach (var input in outputGroup.Inputs)
                    {
                        var fileInfo = new FileInfo(input.Path);
                        if (fileInfo.Exists)
                        {
                            if (fileInfo.LastWriteTimeUtc > outputFileTime)
                            {
                                processGroup = true;
                                break;
                            }
                        }
                        else
                        {
                            // file doesn't exist -- check to see if it's a directory
                            var folderInfo = new DirectoryInfo(fileInfo.FullName);
                            if (folderInfo.Exists)
                            {
                                // not a FILE, it's a FOLDER of files.
                                // in order to specify an input folder, we need to have had the right type attribute
                                // on the output group so we know what kind of files to look for
                                if (codeType == CodeType.Unknown)
                                {
                                    // log an error, then bail because we won't be able to do anything anyway
                                    // since we don't know what kind of code we are processing and we don't know which
                                    // files to include from this folder.
                                    Log.LogError(Strings.DirectorySourceRequiresCodeType);
                                    return;
                                }
                                else
                                {
                                    // recursively check all the files in the folder with the proper extension for the code type.
                                    // if anything pops positive, we know we want to process the group so bail early.
                                    processGroup = CheckFolderInputFileTimes(folderInfo, ExtensionsFromCodeType(codeType), outputFileTime);
                                    if (processGroup)
                                    {
                                        break;
                                    }
                                }
                            }
                        }
                    }
                }

                // do the same to any resource file, there are any (and we don't already know we
                // want to process this group)
                if (!processGroup && outputGroup.Resources.Count > 0)
                {
                    foreach (var resource in outputGroup.Resources)
                    {
                        var fileInfo = new FileInfo(resource.Path);
                        if (fileInfo.Exists && fileInfo.LastWriteTimeUtc > outputFileTime)
                        {
                            processGroup = true;
                            break;
                        }
                    }
                }
            }

            // we will process this group if the output doesn't exist
            // or if any of the inputs are newer than the output
            if (processGroup)
            {
                // get the settings to use -- take the configuration for this output group
                // and apply them over the default settings
                var settings = ParseConfigSettings(outputGroup.GetConfigArguments(this.Configuration), defaultSettings);

                // create combined input source
                var inputGroups = outputGroup.ReadInputGroups(settings.EncodingInputName);
                if (inputGroups.Count > 0)
                {
                    switch (codeType)
                    {
                        case CodeType.JavaScript:
                            try
                            {
                                // process the resources for this output group into the settings list
                                outputGroup.ProcessResourceStrings(settings.JSSettings.ResourceStrings, null);

                                // then process the javascript output group
                                ProcessJavaScript(
                                    inputGroups,
                                    settings.JSSettings,
                                    outputFileInfo.FullName,
                                    outputGroup.SymbolMap,
                                    outputGroup.GetEncoding(settings.EncodingOutputName));
                            }
                            catch (ArgumentException ex)
                            {
                                // processing the resource strings could throw this exception
                                Log.LogError(ex.Message);
                            }
                            break;

                        case CodeType.StyleSheet:
                            ProcessStylesheet(
                                inputGroups,
                                settings.CssSettings,
                                settings.JSSettings,
                                outputFileInfo.FullName,
                                outputGroup.GetEncoding(settings.EncodingOutputName));
                            break;

                        case CodeType.Unknown:
                            Log.LogError(Strings.UnknownCodeType);
                            break;
                    }
                }
                else
                {
                    // no input! write an empty output file
                    using (var stream = outputFileInfo.Create())
                    {
                        // write nothing; just create the empty file
                    }
                }
            }
            else
            {
                // none of the inputs are newer than the output -- we're good.
                Log.LogMessage(Strings.SkippedOutputFile, outputFileInfo.Name);
            }
        }

        #endregion

        #region code processing methods

        private void ProcessJavaScript(IList<InputGroup> inputGroups, CodeSettings settings, string outputPath, SymbolMap symbolMap, Encoding outputEncoding)
        {
            // if we want a symbols map, we need to set it up now
            TextWriter mapWriter = null;
            ISourceMap sourceMap = null;
            try
            {
                if (symbolMap != null)
                {
                    // if we specified the path, use it. Otherwise just use the output path with
                    // ".map" appended to the end. Eg: output.js => output.js.map
                    var symbolMapPath = symbolMap.Path.IsNullOrWhiteSpace()
                        ? outputPath + ".map"
                        : symbolMap.Path;

                    // create the map writer and the source map implementation.
                    // look at the Name attribute and implement the proper one.
                    // the encoding needs to be UTF-8 WITHOUT a BOM or it won't work.
                    mapWriter = new StreamWriter(symbolMapPath, false, new UTF8Encoding(false));
                    sourceMap = SourceMapFactory.Create(mapWriter, symbolMap.Name);
                    if (sourceMap != null)
                    {
                        // if we get here, the symbol map now owns the stream and we can null it out so
                        // we don't double-close it
                        mapWriter = null;
                        settings.SymbolsMap = sourceMap;

                        // copy some property values
                        sourceMap.SourceRoot = symbolMap.SourceRoot.IfNullOrWhiteSpace(null);
                        sourceMap.SafeHeader = symbolMap.SafeHeader.GetValueOrDefault(false);

                        // start the package
                        sourceMap.StartPackage(outputPath, symbolMapPath);
                    }
                }

                // save the original term settings. We'll make sure to set this back again
                // for the last item in the group, but we'll make sure it's TRUE for all the others.
                bool originalTermSetting = settings.TermSemicolons;

                var outputBuilder = new StringBuilder();
                GlobalScope sharedGlobalScope = null;
                for (var ndx = 0; ndx < inputGroups.Count; ++ndx)
                {
                    var inputGroup = inputGroups[ndx];

                    // create and setup parser
                    var parser = new JSParser(inputGroup.Source);

                    // set the shared global object
                    parser.GlobalScope = sharedGlobalScope;

                    // set up the error handler
                    parser.CompilerError += (sender, ea) =>
                    {
                        // if the input group isn't project, then we only want to report sev-0 errors
                        if (inputGroup.Origin == SourceOrigin.Project || ea.Error.Severity == 0)
                        {
                            LogContextError(ea.Error);
                        }
                    };

                    // for all but the last item, we want the term-semicolons setting to be true.
                    // but for the last entry, set it back to its original value
                    settings.TermSemicolons = ndx < inputGroups.Count - 1 ? true : originalTermSetting;

                    // minify input
                    var block = parser.Parse(settings);
                    if (block != null)
                    {
                        if (ndx > 0)
                        {
                            // not the first group, so output the appropriate newline
                            // sequence before we output the group.
                            outputBuilder.Append(settings.LineTerminator);
                        }

                        outputBuilder.Append(block.ToCode());
                    }

                    // save the global scope for the next group (if any).
                    // we need to do this in case an earlier input group defines some global
                    // functions or variables, and later groups reference them. We don't want the
                    // later parse to say "undefined global"
                    sharedGlobalScope = parser.GlobalScope;
                }

                // write output
                if (!Log.HasLoggedErrors)
                {
                    using (var writer = new StreamWriter(outputPath, false, outputEncoding))
                    {
                        // write the combined minified code
                        writer.Write(outputBuilder.ToString());

                        // give the map (if any) a chance to add something
                        settings.SymbolsMap.IfNotNull(m => m.EndFile(
                            writer,
                            settings.LineTerminator));
                    }
                }
                else
                {
                    Log.LogWarning(Strings.DidNotMinify, outputPath, Strings.ThereWereErrors);
                }
            }
            finally
            {
                if (sourceMap != null)
                {
                    mapWriter = null;
                    settings.SymbolsMap = null;
                    sourceMap.EndPackage();
                    sourceMap.Dispose();
                }

                if (mapWriter != null)
                {
                    mapWriter.Close();
                }
            }
        }

        private void ProcessStylesheet(IList<InputGroup> inputGroups, CssSettings settings, CodeSettings jsSettings, string outputPath, Encoding encoding)
        {
            var outputBuilder = new StringBuilder();
            foreach (var inputGroup in inputGroups)
            {
                // create and setup parser
                var parser = new CssParser();
                parser.Settings = settings;
                parser.JSSettings = jsSettings;
                parser.CssError += (sender, ea) =>
                {
                    // if the input group is not project, then only report sev-0 errors
                    if (inputGroup.Origin == SourceOrigin.Project || ea.Error.Severity == 0)
                    {
                        LogContextError(ea.Error);
                    }
                };

                // minify input
                outputBuilder.Append(parser.Parse(inputGroup.Source));
            }

            // write output
            if (!Log.HasLoggedErrors)
            {
                using (var writer = new StreamWriter(outputPath, false, encoding))
                {
                    writer.Write(outputBuilder.ToString());
                }
            }
            else
            {
                Log.LogWarning(Strings.DidNotMinify, outputPath, Strings.ThereWereErrors);
            }
        }

        #endregion

        #region helper methods

        private static SwitchParser ParseConfigSettings(string arguments, SwitchParser defaults)
        {
            // clone the default switch settings, parse the arguments on top of the clone,
            // and then return the clone.
            var switchParser = defaults.Clone();
            switchParser.Parse(arguments);
            return switchParser;
        }

        private static bool CheckFolderInputFileTimes(DirectoryInfo folderInfo, string extensions, DateTime outputFileTime)
        {
            // get all the files in this folder
            foreach (var fileInfo in folderInfo.GetFiles())
            {
                // check to see if .ext. is in the list of extensions. The trailing period is needed as an "end of extension"
                // marker, since an extension can't have a period anywhere but as the first character. So the list of extensions
                // will be period-delimited and end in a period.
                if (extensions.IndexOf(fileInfo.Extension.ToUpperInvariant() + '.', StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    // extension is good -- check the input time. If it's later than the output,
                    // bail early because we now know we want to process the output group.
                    if (fileInfo.LastWriteTimeUtc > outputFileTime)
                    {
                        return true;
                    }
                }
            }

            // then recurse any subfolders
            foreach (var subFolder in folderInfo.GetDirectories())
            {
                // bail early if anyting returns true (an input is more recent than the output)
                if (CheckFolderInputFileTimes(subFolder, extensions, outputFileTime))
                {
                    return true;
                }
            }

            // if we get here, nothing is newer
            return false;
        }

        private static string ExtensionsFromCodeType(CodeType codeType)
        {
            // list of extensions ends in a period so we can search for .ext. and be sure
            // to not get any substrings. For instance, if we just has ".css" and searched
            // for .cs, we'd get a match. But if the list is ".css." and we search for ".cs."
            // then we wouldn't match.
            switch(codeType)
            {
                case CodeType.JavaScript:
                    return ".JS.";

                case CodeType.StyleSheet:
                    return ".CSS.";

                default:
                    return string.Empty;
            }
        }

        #endregion

        #region Logging methods

        /// <summary>
        /// Call this method to log an error against the build of a particular source file
        /// </summary>
        /// <param name="path">path of the input source file</param>
        /// <param name="messageIdentifier">String resource identifier</param>
        /// <param name="messageArguments">any optional formatting arguments</param>
        private void LogFileError(string path, string message, params object[] messageArguments)
        {
            Log.LogError(
                null,
                null,
                null,
                path,
                0,
                0,
                0,
                0,
                message,
                messageArguments);
        }

        /// <summary>
        /// Call this method to log an error using a ContextError object
        /// </summary>
        /// <param name="error">Error to log</param>
        private void LogContextError(ContextError error)
        {
            // log it either as an error or a warning
            if (TreatWarningsAsErrors || error.Severity < 2)
            {
                Log.LogError(
                    error.Subcategory,  // subcategory 
                    error.ErrorCode,    // error code
                    error.HelpKeyword,  // help keyword
                    error.File,         // file
                    error.StartLine,    // start line
                    error.StartColumn,  // start column
                    error.EndLine > error.StartLine ? error.EndLine : 0,      // end line
                    error.EndLine > error.StartLine || error.EndColumn > error.StartColumn ? error.EndColumn : 0,    // end column
                    error.Message       // message
                    );
            }
            else
            {
                Log.LogWarning(
                    error.Subcategory,  // subcategory 
                    error.ErrorCode,    // error code
                    error.HelpKeyword,  // help keyword
                    error.File,         // file
                    error.StartLine,    // start line
                    error.StartColumn,  // start column
                    error.EndLine > error.StartLine ? error.EndLine : 0,      // end line
                    error.EndLine > error.StartLine || error.EndColumn > error.StartColumn ? error.EndColumn : 0,    // end column
                    error.Message       // message
                    );
            }
        }

        #endregion
    }
}
