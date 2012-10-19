// AjaxMinBuild.cs
//
// Copyright 2012 Microsoft Corporation
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
using System.Globalization;
using System.IO;
using System.Resources;
using System.Security;
using System.Text;
using System.Text.RegularExpressions;
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
        private static readonly string FolderSeparator = Path.DirectorySeparatorChar.ToString();

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

        public override bool Execute()
        {
            // create the project default settings
            var projectDefaultSettings = new SwitchParser();
            projectDefaultSettings.Parse(ProjectDefaultSwitches);

            // each task item represents an ajaxmin manifest file: an XML file that
            // has settings and one or more output files, each comprised of one or more
            // input files. To execute this process, we will read the XML manifest and
            // execute AjaxMin for each output group.
            // won't bother executing AjaxMin is the file time for the output file
            // is greater than all its inputs.
            foreach (var taskItem in Manifests)
            {
                // save the manifest folder - paths within the manifest will be relative to it
                // if there are no InputFolder or OutputFolder values
                var manifestFolder = Path.GetDirectoryName(taskItem.ItemSpec);

                // process the XML file into objects
                Manifest manifest = null;
                var fileReader = new StreamReader(taskItem.ItemSpec);
                try
                {
                    using (var reader = XmlReader.Create(fileReader))
                    {
                        fileReader = null;
                        manifest = ManifestFactory.Create(reader);
                    }
                }
                finally
                {
                    if (fileReader != null)
                    {
                        fileReader.Close();
                        fileReader = null;
                    }
                }

                // create the default settings for this configuration, if there are any, based on
                // the project default settings.
                var defaultSettings = ParseConfigSettings(manifest.DefaultArguments, projectDefaultSettings);

                // for each output group
                foreach (var outputGroup in manifest.Outputs)
                {
                    var processGroup = false;
                    var codeType = outputGroup.CodeType;

                    // get the full path and check for existence
                    var  outputFileInfo = new FileInfo(GetRootedOutput(outputGroup.Path, manifestFolder));

                    // the symbol map is an OPTIONAL output, so if we don't want one, we ignore it.
                    // but if we do, we need to check for its existence and filetimes, just like 
                    // the regular output file
                    FileInfo symbolsFileInfo = null;
                    if (outputGroup.SymbolMap != null)
                    {
                        symbolsFileInfo = new FileInfo(GetRootedOutput(outputGroup.SymbolMap.Path, manifestFolder));
                    }

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

                        // check filetime of each input file, and if ANY one is newer, 
                        // then we will want to set the process-group flag and stop checking
                        foreach (var input in outputGroup.Inputs)
                        {
                            var fileInfo = new FileInfo(GetRootedInput(input.Path, manifestFolder));
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
                                        Log.LogError(Strings.DirectorySourceRequiresCodeType);
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

                        // do the same to any resource file, there are any (and we don't already know we
                        // want to process this group)
                        if (!processGroup && outputGroup.Resources.Count > 0)
                        {
                            foreach (var resource in outputGroup.Resources)
                            {
                                var fileInfo = new FileInfo(GetRootedInput(resource.Path, manifestFolder));
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
                        var settings = ParseConfigSettings(outputGroup.Arguments, defaultSettings);

                        // create combined input source
                        var inputCode = CombineInputs(outputGroup.Inputs, manifestFolder, settings.EncodingInputName, ref codeType);
                        if (inputCode.Length > 0)
                        {
                            switch (codeType)
                            {
                                case CodeType.JavaScript:
                                    ProcessJavaScript(
                                        inputCode,
                                        manifestFolder,
                                        settings.JSSettings,
                                        outputFileInfo.FullName,
                                        outputGroup.SymbolMap,
                                        outputGroup.Resources,
                                        GetJavaScriptEncoding(outputGroup.EncodingName ?? settings.EncodingOutputName));
                                    break;

                                case CodeType.StyleSheet:
                                    ProcessStylesheet(
                                        inputCode,
                                        manifestFolder,
                                        settings.CssSettings,
                                        settings.JSSettings,
                                        outputFileInfo.FullName,
                                        GetStylesheetEncoding(outputGroup.EncodingName ?? settings.EncodingOutputName));
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
            }

            // we succeeded if there have been no errors logged
            return !Log.HasLoggedErrors;
        }

        #region code processing methods

        private void ProcessJavaScript(string inputCode, string manifestFolder, CodeSettings settings, string outputPath, SymbolMap symbolMap, IList<Resource> resourceList, Encoding encoding)
        {
            // create and setup parser
            var parser = new JSParser(inputCode);
            parser.CompilerError += (sender, ea) =>
            {
                LogContextError(ea.Error);
            };

            // if we want a symbols map, we need to set it up now
            TextWriter mapWriter = null;
            try
            {
                try
                {
                    if (symbolMap != null)
                    {
                        // create the map writer and the source map implementation.
                        // look at the Name attribute and implement the proper one.
                        mapWriter = new StreamWriter(GetRootedOutput(symbolMap.Path, manifestFolder), false, Encoding.UTF8);
                        if (string.Compare(symbolMap.Name, V3SourceMap.ImplementationName, StringComparison.OrdinalIgnoreCase) == 0)
                        {
                            settings.SymbolsMap = new V3SourceMap(mapWriter);
                        }
                        else
                        {
                            settings.SymbolsMap = new ScriptSharpSourceMap(mapWriter);
                        }

                        // if we get here, the symbol map now owns the stream and we can null it out so
                        // we don't double-close it
                        mapWriter = null;

                        // start the package
                        settings.SymbolsMap.StartPackage(outputPath);
                    }

                    // if we want to use resource strings, set them up now
                    foreach (var resource in resourceList)
                    {
                        settings.AddResourceStrings(ProcessResourceFile(resource.Name, GetRootedInput(resource.Path, manifestFolder)));
                    }

                    // minify input
                    var block = parser.Parse(settings);
                    var minifiedCode = block.ToCode();

                    // write output
                    if (!Log.HasLoggedErrors)
                    {
                        using (var writer = new StreamWriter(GetRootedOutput(outputPath, manifestFolder), false, encoding))
                        {
                            // write the minified code
                            writer.Write(minifiedCode);

                            // give the map (if any) a chance to add something
                            settings.SymbolsMap.IfNotNull(m => m.EndFile(
                                writer,
                                symbolMap.Path,
                                settings.OutputMode == OutputMode.MultipleLines ? "\r\n" : "\n"));
                        }
                    }
                    else
                    {
                        Log.LogWarning(Strings.DidNotMinify, outputPath, Strings.ThereWereErrors);
                    }
                }
                finally
                {
                    if (settings.SymbolsMap != null)
                    {
                        settings.SymbolsMap.EndPackage();
                        settings.SymbolsMap.Dispose();
                        settings.SymbolsMap = null;
                    }
                }
            }
            finally
            {
                if (mapWriter != null)
                {
                    mapWriter.Close();
                    mapWriter = null;
                }
            }
        }

        private void ProcessStylesheet(string inputCode, string manifestFolder, CssSettings settings, CodeSettings jsSettings, string outputPath, Encoding encoding)
        {
            // create and setup parser
            var parser = new CssParser();
            parser.CssError += (sender, ea) =>
            {
                LogContextError(ea.Error);
            };
            parser.Settings = settings;
            parser.JSSettings = jsSettings;

            // minify input
            var minifiedCode = parser.Parse(inputCode);

            // write output
            if (!Log.HasLoggedErrors)
            {
                using (var writer = new StreamWriter(GetRootedOutput(outputPath, manifestFolder), false, encoding))
                {
                    writer.Write(minifiedCode);
                }
            }
            else
            {
                Log.LogWarning(Strings.DidNotMinify, outputPath, Strings.ThereWereErrors);
            }
        }

        #endregion

        #region helper methods

        private string GetRootedOutput(string path, string manifestFolder)
        {
            return Path.IsPathRooted(path)
                ? path
                : this.OutputFolder.IsNullOrWhiteSpace()
                    ? Path.Combine(manifestFolder, path)
                    : Path.Combine(this.OutputFolder, path);
        }

        private string GetRootedInput(string path, string manifestFolder)
        {
            return Path.IsPathRooted(path)
                ? path
                : this.InputFolder.IsNullOrWhiteSpace()
                    ? Path.Combine(manifestFolder, path)
                    : Path.Combine(this.InputFolder, path);
        }

        private SwitchParser ParseConfigSettings(IDictionary<string, string> configArguments, SwitchParser defaults)
        {
            // first get the appropriate string for this configuration. Check for arguments that
            // match this configuration, and if none exist, check for the defaults (blank config)
            string arguments;
            if (!configArguments.TryGetValue(this.Configuration, out arguments))
            {
                if (!configArguments.TryGetValue(string.Empty, out arguments))
                {
                    // none. Just use default arguments.
                    arguments = string.Empty;
                }
            }

            // now parse them into settings. We don't care about any unrecognized settings; just ignore those.
            // be sure to clone the settings so we don't clobber the defaults and can reuse them the next time.
            var switchParser = defaults.Clone();
            switchParser.Parse(arguments);
            return switchParser;
        }

        private static void CopyInputWithContext(TextWriter writer, string fileContext, string inputPath, Encoding encoding)
        {
            // output a special comment that AjaxMin will pick up so any errors will 
            // have the proper file context
            writer.Write("///#source 1 1 ");
            writer.WriteLine(fileContext);

            // now read all the file source and add it to the combined input.
            // it doesn't matter which encoder fallback we use -- we'll be DECODING, and we always use a simple replacement for that.
            // so just ask for a JS encoding here.
            using (var reader = new StreamReader(inputPath, encoding))
            {
                writer.WriteLine(reader.ReadToEnd());
            }
        }

        private void CopyAllInputWithContext(TextWriter writer, string manifestFolder, DirectoryInfo folderInfo, Encoding encoding, string extensions)
        {
            // get all the files in this folder
            foreach (var fileInfo in folderInfo.GetFiles())
            {
                // check to see if .ext. is in the list of extensions. The trailing period is needed as an "end of extension"
                // marker, since an extension can't have a period anywhere but as the first character. So the list of extensions
                // will be period-delimited and end in a period.
                if (extensions.IndexOf(fileInfo.Extension.ToUpperInvariant() + '.', StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    CopyInputWithContext(writer, this.GetInputFileContext(fileInfo.FullName, manifestFolder), fileInfo.FullName, encoding);
                }
            }

            // then recurse any subfolders
            foreach (var subFolder in folderInfo.GetDirectories())
            {
                CopyAllInputWithContext(writer, manifestFolder, subFolder, encoding, extensions);
            }
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

        private string CombineInputs(IList<InputFile> inputFiles, string manifestFolder, string defaultEncodingName, ref CodeType codeType)
        {
            // create combined input source
            var sb = new StringBuilder();
            using (var writer = new StringWriter(sb, CultureInfo.InvariantCulture))
            {
                foreach (var input in inputFiles)
                {
                    var fileInfo = new FileInfo(GetRootedInput(input.Path, manifestFolder));
                    if (fileInfo.Exists)
                    {
                        // if we don't know the code type yet, try to figure it out from 
                        // the file extensions; first match wins.
                        if (codeType == CodeType.Unknown)
                        {
                            switch (fileInfo.Extension.ToUpperInvariant())
                            {
                                case ".JS":
                                    codeType = CodeType.JavaScript;
                                    break;

                                case ".CSS":
                                    codeType = CodeType.StyleSheet;
                                    break;
                            }
                        }

                        // copy the input file to the output with a special context 
                        // marker so any errors are logged to the apporpriate source file
                        CopyInputWithContext(
                            writer, 
                            this.GetInputFileContext(fileInfo.FullName, manifestFolder), 
                            fileInfo.FullName, 
                            GetJavaScriptEncoding(input.EncodingName ?? defaultEncodingName));
                    }
                    else
                    {
                        // FILE doesn't exist -- see if it's a directory
                        var folderInfo = new DirectoryInfo(fileInfo.FullName);
                        if (folderInfo.Exists)
                        {
                            // AHA! It's a folder, not a file.
                            // if we don't know the code type, then we can't proceed
                            if (codeType == CodeType.Unknown)
                            {
                                Log.LogError(Strings.DirectorySourceRequiresCodeType);
                            }
                            else
                            {
                                // recursively look for all source files of the appropriate extension
                                // assume just JS and CSS at this time.
                                CopyAllInputWithContext(
                                    writer,
                                    manifestFolder,
                                    folderInfo,
                                    GetJavaScriptEncoding(input.EncodingName ?? defaultEncodingName),
                                    ExtensionsFromCodeType(codeType));
                            }
                        }
                        else if (!input.Optional)
                        {
                            // this input file isn't optional, and it doesn't exist. Throw an error.
                            LogFileError(fileInfo.FullName, Strings.RequiredInputDoesntExist, input.Path);
                        }
                    }
                }
            }

            return sb.ToString();
        }

        private string GetInputFileContext(string path, string manifestFolder)
        {
            // if the full path is rooted on the manifest folder or the input folder, trim those folders off
            // and add the sourcefolder (if there is one)
            path = TrimBy(path, manifestFolder, this.SourceFolder);
            path = TrimBy(path, this.InputFolder, this.SourceFolder);

            // finally, trim by the project root (if there is one)
            // TODO: is this the root project, or does it follow the imports?
            if (!string.IsNullOrEmpty(this.BuildEngine.ProjectFileOfTaskNode))
            {
                path = TrimBy(path, new FileInfo(this.BuildEngine.ProjectFileOfTaskNode).DirectoryName, null);
            }

            return path;
        }

        private static string TrimBy(string path, string rootFolder, string newRoot)
        {
            if (path.StartsWith(rootFolder, StringComparison.OrdinalIgnoreCase))
            {
                path = path.Substring(rootFolder.Length + (rootFolder.EndsWith(FolderSeparator, StringComparison.Ordinal) ? 0 : 1));
            }

            if (!string.IsNullOrEmpty(newRoot) && !Path.IsPathRooted(path))
            {
                path = Path.Combine(newRoot, path);
            }

            return path;
        }

        #endregion

        #region encoding helpers

        private Encoding GetJavaScriptEncoding(string encodingName)
        {
            return GetEncoding(encodingName, new JSEncoderFallback());
        }

        private Encoding GetStylesheetEncoding(string encodingName)
        {
            return GetEncoding(encodingName, new CssEncoderFallback());
        }

        private Encoding GetEncoding(string encodingName, EncoderFallback fallback)
        {
            Encoding encoding;
            if (string.IsNullOrEmpty(encodingName))
            {
                encoding = DefaultEncoding(fallback);
            }
            else
            {
                try
                {
                    // try to create an encoding from the encoding argument
                    encoding = Encoding.GetEncoding(
                        encodingName,
                        fallback,
                        new DecoderReplacementFallback("\uFFFD"));
                }
                catch (ArgumentException e)
                {
                    Log.LogError(Strings.InvalidEncodingName, e.Message);

                    // just use the default
                    encoding = DefaultEncoding(fallback);
                }
            }

            return encoding;
        }

        private static Encoding DefaultEncoding(EncoderFallback fallback)
        {
            // default is a clone of UTF8 (so we can set the fallback)
            // with the encoder set
            var encoding = (Encoding)Encoding.UTF8.Clone();
            encoding.EncoderFallback = fallback;
            return encoding;
        }

        #endregion

        #region resource processing

        private ResourceStrings ProcessResourceFile(string resourceName, string resourcePath)
        {
            // which method we call to process the resources depends on the file extension
            // of the resources path given to us.
            ResourceStrings resourceStrings = null;
            switch (Path.GetExtension(resourcePath).ToUpperInvariant())
            {
                case ".RESX":
                    // process the resource file as a RESX xml file
                    resourceStrings = ProcessResXResources(resourcePath);
                    break;

                case ".RESOURCES":
                    // process the resource file as a compiles RESOURCES file
                    resourceStrings = ProcessResources(resourcePath);
                    break;

                default:
                    // no other types are supported
                    Log.LogError(Strings.UnsupportedResourceType, resourcePath);
                    break;
            }

            if (resourceStrings != null)
            {
                resourceStrings.Name = resourceName;
            }

            return resourceStrings;
        }

        private static ResourceStrings ProcessResources(string resourceFileName)
        {
            // default return object is null, meaning we are outputting the JS code directly
            // and don't want to replace any referenced resources in the sources
            using (ResourceReader reader = new ResourceReader(resourceFileName))
            {
                // get an enumerator so we can itemize all the key/value pairs
                // and create an object out of the dictionary
                return new ResourceStrings(reader.GetEnumerator());
            }
        }

        private static ResourceStrings ProcessResXResources(string resourceFileName)
        {
            // default return object is null, meaning we are outputting the JS code directly
            // and don't want to replace any referenced resources in the sources
            using (ResXResourceReader reader = new ResXResourceReader(resourceFileName))
            {
                // get an enumerator so we can itemize all the key/value pairs
                // and create an object out of the dictionary
                return new ResourceStrings(reader.GetEnumerator());
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
