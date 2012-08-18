// MainClass.cs
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
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Resources;
using System.Runtime.Serialization;
using System.Security;
using System.Text;
using System.Xml;

namespace Microsoft.Ajax.Utilities
{
    /// <summary>
    /// Application entry point
    /// </summary>
    public partial class MainClass
    {
        #region common fields

        // default resource object name if not specified
        private const string c_defaultResourceObjectName = "Strings";

        // prefix for usage messages that tell that method to not create an error string
        // from the message, but the output it directly, as-is
        private const string c_rawMessagePrefix = "RAWUSAGE";

        /// <summary>
        /// This field is initially false, and it set to true if any errors were
        /// found parsing the javascript. The return value for the application
        /// will be set to non-zero if this flag is true.
        /// Use the -W argument to limit the severity of errors caught by this flag.
        /// </summary>
        private bool m_errorsFound;// = false;

        /// <summary>
        /// Set to true when header is written
        /// </summary>
        private bool m_headerWritten;

        #endregion

        #region common settings

        /// <summary>
        /// object to turn the command-line into settings object
        /// </summary>
        private SwitchParser m_switchParser;

        // whether to clobber existing output files
        private bool m_clobber; // = false

        // simply echo the input code, not the crunched code
        private bool m_echoInput;// = false;

        /// <summary>
        /// File name of the source file or directory (if in recursive mode)
        /// </summary>
        private List<string> m_inputFiles;// = null;

        /// <summary>
        /// Input type: JS or CSS
        /// </summary>
        private InputType m_inputType = InputType.Unknown;

        /// <summary>
        /// Output mode
        /// </summary>
        private ConsoleOutputMode m_outputMode = ConsoleOutputMode.Console;

        /// <summary>
        /// Whether or not we are outputting the crunched code to one or more files (false) or to stdout (true)
        /// </summary>
        private bool m_outputToStandardOut;// = false;

        /// <summary>
        /// Optional file name of the destination file. Must be blank for in-place processing.
        /// If not in-place, a blank destination output to STDOUT
        /// </summary>
        private string m_outputFile = string.Empty;

        /// <summary>
        /// Optionally specify an XML file that indicates the input and output file(s)
        /// instead of specifying a single output and the input file(s) on the command line.
        /// </summary>
        private string m_xmlInputFile;// = null;

        /// <summary>
        /// An optional file mapping the source and destination files.
        /// </summary>
        private string m_symbolsMapFile;

        #endregion

        #region startup code

        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        [STAThread]
        public static int Main(string[] args)
        {
            int retVal;
            if (args == null || args.Length == 0)
            {
                // no args -- we don't know whether to to parse JS or CSS,
                // so no sense waiting for input from STDIN. Output a simple
                // header that displays a message telling how to get more info.
                Console.Out.WriteLine(GetHeaderString());
                Console.Out.WriteLine(AjaxMin.MiniUsageMessage);
                retVal = 0;
            }
            else
            {
                try
                {
                    MainClass app = new MainClass(args);
                    retVal = app.Run();
                }
                catch (UsageException e)
                {
                    Usage(e);
                    retVal = 1;
                }
            }

            return retVal;
        }

        #endregion

        #region Constructor

        private MainClass(string[] args)
        {
            // process the arguments
            ProcessArgs(args);
        }

        #endregion

        #region ProcessArgs method

        private void ProcessArgs(string[] args)
        {
            // create the switch parser and hook the events we care about
            m_switchParser = new SwitchParser();
            m_switchParser.UnknownParameter += OnUnknownParameter;
            m_switchParser.CssOnlyParameter += OnCssOnlyParameter;
            m_switchParser.JSOnlyParameter += OnJSOnlyParameter;
            m_switchParser.InvalidSwitch += OnInvalidSwitch;

            // and go
            m_switchParser.Parse(args);

            if (m_inputFiles != null)
            {
                // if we didn't specify the type (JS or CSS), then look at the extension of
                // the input files and see if we can divine what we are
                foreach (string path in m_inputFiles)
                {
                    string extension = Path.GetExtension(path).ToUpperInvariant();
                    switch (m_inputType)
                    {
                        case InputType.Unknown:
                            // we don't know yet. If the extension is JS or CSS set to the
                            // appropriate input type
                            if (extension == ".JS")
                            {
                                m_inputType = InputType.JavaScript;
                            }
                            else if (extension == ".CSS")
                            {
                                m_inputType = InputType.Css;
                            }
                            break;

                        case InputType.JavaScript:
                            // we know we are JS -- if we find a CSS file, throw an error
                            if (extension == ".CSS")
                            {
                                throw new UsageException(m_outputMode, "ConflictingInputType");
                            }
                            break;

                        case InputType.Css:
                            // we know we are CSS -- if we find a JS file, throw an error
                            if (extension == ".JS")
                            {
                                throw new UsageException(m_outputMode, "ConflictingInputType");
                            }
                            break;
                    }
                }

                // if we have input files but we don't know the type by now, 
                // then throw an exception
                if (m_inputFiles.Count > 0 && m_inputType == InputType.Unknown)
                {
                    throw new UsageException(m_outputMode, "UnknownInputType");
                }
            }
        }

        private void OnUnknownParameter(object sender, UnknownParameterEventArgs ea)
        {
            if (ea.SwitchPart != null)
            {
                // see if the switch is okay
                switch (ea.SwitchPart)
                {
                    case "CLOBBER":
                        // just putting the clobber switch on the command line without any arguments
                        // is the same as putting -clobber:true and perfectly valid.
                        if (ea.ParameterPart == null)
                        {
                            m_clobber = true;
                        }
                        else if (!SwitchParser.BooleanSwitch(ea.ParameterPart.ToUpperInvariant(), true, out m_clobber))
                        {
                            throw new UsageException(m_outputMode, Extensions.FormatInvariant(AjaxMin.InvalidSwitchArg, ea.SwitchPart, ea.ParameterPart));
                        }
                        break;

                    case "ECHO":
                    case "I": // <-- old style
                        // ignore any arguments
                        m_echoInput = true;

                        // -pretty and -echo are not compatible
                        if (m_switchParser.AnalyzeMode)
                        {
                            throw new UsageException(m_outputMode, "PrettyAndEchoArgs");
                        }
                        break;

                    case "HELP":
                    case "?":
                        // just show usage
                        throw new UsageException(m_outputMode);

                    case "OUT":
                    case "O": // <-- old style
                        // next argument is the output path
                        // cannot have two out arguments
                        if (!string.IsNullOrEmpty(m_outputFile))
                        {
                            throw new UsageException(m_outputMode, "MultipleOutputArg");
                        }
                        else if (ea.Index >= ea.Arguments.Count - 1)
                        {
                            throw new UsageException(m_outputMode, "OutputArgNeedsPath");
                        }

                        m_outputFile = ea.Arguments[++ea.Index];
                        break;

                    case "MAP":
                        if (!string.IsNullOrEmpty(m_xmlInputFile))
                        {
                            throw new UsageException(m_outputMode, "MapAndXmlArgs");
                        }

                        // next argument is the output path
                        // cannot have two map arguments
                        if (!string.IsNullOrEmpty(m_symbolsMapFile))
                        {
                            throw new UsageException(m_outputMode, "MultipleMapArg");
                        }

                        if (ea.Index >= ea.Arguments.Count - 1)
                        {
                            throw new UsageException(m_outputMode, "MapArgNeedsPath");
                        }

                        m_symbolsMapFile = ea.Arguments[++ea.Index];
                        break;

                    case "PPONLY":
                        // just putting the pponly switch on the command line without any arguments
                        // is the same as putting -pponly:true and perfectly valid.
                        if (ea.ParameterPart == null)
                        {
                            m_preprocessOnly = true;
                        }
                        else if (!SwitchParser.BooleanSwitch(ea.ParameterPart.ToUpperInvariant(), true, out m_preprocessOnly))
                        {
                            throw new UsageException(m_outputMode, Extensions.FormatInvariant(AjaxMin.InvalidSwitchArg, ea.SwitchPart, ea.ParameterPart));
                        }

                        // this is a JS-only switch
                        OnJSOnlyParameter(null, null);
                        break;

                    case "RENAME":
                        if (ea.ParameterPart == null)
                        {
                            // there are no other parts after -rename
                            // the next argument should be a filename from which we can pull the
                            // variable renaming information
                            if (ea.Index >= ea.Arguments.Count - 1)
                            {
                                // must be followed by an encoding
                                throw new UsageException(m_outputMode, Extensions.FormatInvariant(AjaxMin.RenameArgMissingParameterOrFilePath, ea.SwitchPart));
                            }

                            // the renaming file is specified as the NEXT argument
                            string renameFilePath = ea.Arguments[++ea.Index];

                            // and it needs to exist
                            EnsureInputFileExists(renameFilePath);

                            // process the renaming file
                            ProcessRenamingFile(renameFilePath);
                        }
                        break;

                    case "RES":
                    case "R": // <-- old style
                        // -res:id path
                        // must have resource file on next parameter
                        if (ea.Index >= ea.Arguments.Count - 1)
                        {
                            throw new UsageException(m_outputMode, "ResourceArgNeedsPath");
                        }

                        // the resource file name is the NEXT argument
                        var resourceFile = ea.Arguments[++ea.Index];
                        EnsureInputFileExists(resourceFile);

                        // create the resource strings object from the file name
                        // will throw an error if not a supported file type
                        var resourceStrings = ProcessResourceFile(resourceFile);

                        // set the object name from the parameter part
                        if (!string.IsNullOrEmpty(ea.ParameterPart))
                        {
                            // must be a series of JS identifiers separated by dots: IDENT(.IDENT)*
                            // if any part doesn't match a JAvaScript identifier, throw an error
                            var parts = ea.ParameterPart.Split('.');
                            foreach (var part in parts)
                            {
                                if (!JSScanner.IsValidIdentifier(part))
                                {
                                    throw new UsageException(m_outputMode, Extensions.FormatInvariant(AjaxMin.ResourceArgInvalidName, part));
                                }
                            }

                            // if we got here, then everything is valid; save the name portion
                            resourceStrings.Name = ea.ParameterPart;
                        }
                        else
                        {
                            // no name specified -- use Strings as the default
                            // (not recommended)
                            resourceStrings.Name = c_defaultResourceObjectName;
                        }

                        // add it to the settings objects
                        m_switchParser.JSSettings.AddResourceStrings(resourceStrings);
                        m_switchParser.CssSettings.AddResourceStrings(resourceStrings);

                        break;

                    case "SILENT":
                    case "S": // <-- old style
                        // ignore any argument part
                        m_outputMode = ConsoleOutputMode.Silent;
                        break;

                    case "VERSION":
                        // the user just wants the version number
                        string version = Assembly.GetExecutingAssembly().GetName().Version.ToString();

                        // the special prefix tells the Usage method to not create an error
                        // out of the text and just output it as-is
                        throw new UsageException(ConsoleOutputMode.Silent, c_rawMessagePrefix + version);

                    case "XML":
                    case "X": // <-- old style
                        if (!string.IsNullOrEmpty(m_symbolsMapFile))
                        {
                            throw new UsageException(m_outputMode, "MapAndXmlArgs");
                        }

                        if (!string.IsNullOrEmpty(m_xmlInputFile))
                        {
                            throw new UsageException(m_outputMode, "MultipleXmlArgs");
                        }
                        // cannot have input files
                        if (m_inputFiles != null && m_inputFiles.Count > 0)
                        {
                            throw new UsageException(m_outputMode, "XmlArgHasInputFiles");
                        }

                        if (ea.Index >= ea.Arguments.Count - 1)
                        {
                            throw new UsageException(m_outputMode, "XmlArgNeedsPath");
                        }

                        // the xml file name is the NEXT argument
                        m_xmlInputFile = ea.Arguments[++ea.Index];

                        // and it must exist
                        EnsureInputFileExists(m_xmlInputFile);
                        break;

                    case "CL":
                    case "CS":
                    case "V":
                    case "3":
                        // just ignore -- for backwards-compatibility
                        break;

                    default:
                        // truly an unknown parameter -- throw a usage error
                        throw new UsageException(m_outputMode, Extensions.FormatInvariant(AjaxMin.InvalidArgument, ea.Arguments[ea.Index]));
                }
            }
            else
            {
                // no switch -- then this must be an input file!
                // cannot coexist with XML file
                if (!string.IsNullOrEmpty(m_xmlInputFile))
                {
                    throw new UsageException(m_outputMode, "XmlArgHasInputFiles");
                }

                // shortcut
                string fileName = ea.Arguments[ea.Index];

                // make sure it exists (will throw an exception if it doesn't)
                EnsureInputFileExists(fileName);

                if (m_inputFiles == null)
                {
                    // if we haven't created it yet, do it now and just add the
                    // file because we know it's empty and won't collide with any dupe
                    m_inputFiles = new List<string>();
                    m_inputFiles.Add(fileName);
                }
                else if (!m_inputFiles.Contains(fileName))
                {
                    // we don't want duplicates
                    m_inputFiles.Add(fileName);
                }
            }
        }

        private void OnInvalidSwitch(object sender, InvalidSwitchEventArgs ea)
        {
            if (ea.ParameterPart == null)
            {
                // if there's no parameter, then the switch required an arg
                throw new UsageException(m_outputMode, Extensions.FormatInvariant(AjaxMin.SwitchRequiresArg, ea.SwitchPart));
            }
            else
            {
                // otherwise the arg was invalid
                throw new UsageException(m_outputMode, Extensions.FormatInvariant(AjaxMin.InvalidSwitchArg, ea.ParameterPart, ea.SwitchPart));
            }
        }

        private void OnCssOnlyParameter(object sender, EventArgs ea)
        {
            // if we don't know by now, assume CSS
            if (m_inputType == InputType.Unknown)
            {
                m_inputType = InputType.Css;
            }
        }

        private void OnJSOnlyParameter(object sender, EventArgs ea)
        {
            // if we don't know by now, assume JS
            if (m_inputType == InputType.Unknown)
            {
                m_inputType = InputType.JavaScript;
            }
        }

        private void EnsureInputFileExists(string fileName)
        {
            // make sure it exists
            if (!File.Exists(fileName))
            {
                // file doesn't exist -- is it a folder?
                if (Directory.Exists(fileName))
                {
                    // cannot be a folder
                    throw new UsageException(m_outputMode, Extensions.FormatInvariant(AjaxMin.SourceFileIsFolder, fileName));
                }
                else
                {
                    // just plain doesn't exist
                    throw new UsageException(m_outputMode, Extensions.FormatInvariant(AjaxMin.SourceFileNotExist, fileName));
                }
            }
        }

        #endregion

        #region Usage method

        private static void Usage(UsageException e)
        {
            string fileName = Path.GetFileName(
              Assembly.GetExecutingAssembly().Location
              );

            // only output the header if we aren't supposed to be silent
            if (e.OutputMode != ConsoleOutputMode.Silent)
            {
                Console.Error.WriteLine(GetHeaderString());
            }

            // if we have a message, then only output the mini-usage message that
            // tells the user how to get the full usage text. It's getting too long and
            // obscuring the error messages
            if (e.Message.Length > 0)
            {
                if (e.OutputMode != ConsoleOutputMode.Silent)
                {
                    Console.Error.WriteLine(AjaxMin.MiniUsageMessage);
                    Console.Error.WriteLine();
                }

                if (e.Message.StartsWith(c_rawMessagePrefix, StringComparison.Ordinal))
                {
                    Console.Out.WriteLine(e.Message.Substring(c_rawMessagePrefix.Length));
                }
                else
                {
                    Console.Error.WriteLine(CreateBuildError(
                        null,
                        null,
                        "AM-USAGE", // NON-LOCALIZABLE error code
                        e.Message));
                }
            }
            else if (e.OutputMode != ConsoleOutputMode.Silent)
            {
                Console.Error.WriteLine(Extensions.FormatInvariant(AjaxMin.Usage, fileName));
            }
        }

        #endregion

        #region Run method

        private int Run()
        {
            int retVal = 0;
            CrunchGroup[] crunchGroups;

            // see if we have an XML file to process
            if (!string.IsNullOrEmpty(m_xmlInputFile))
            {
                // process the XML file, using the output path as an optional output root folder
                crunchGroups = ProcessXmlFile(m_xmlInputFile, m_outputFile);
            }
            else
            {
                // just pass the input and output files specified in the command line
                // to the processing method (normal operation)
                crunchGroups = new CrunchGroup[] { new CrunchGroup(
                    m_outputFile, 
                    m_switchParser.EncodingOutputName,
                    m_symbolsMapFile,
                    m_inputFiles != null ? m_inputFiles.ToArray() : new string[] {}, 
                    m_switchParser.EncodingInputName, 
                    m_inputType) };
            }

            if (crunchGroups.Length > 0)
            {
                // if any one crunch group is writing to stdout, then we need to make sure
                // that no progress or informational messages go to stdout or we will output 
                // invalid JavaScript/CSS. Loop through the crunch groups and if any one is
                // outputting to stdout, set the appropriate flag.
                for (var ndxGroup = 0; ndxGroup < crunchGroups.Length; ++ndxGroup)
                {
                    if (string.IsNullOrEmpty(crunchGroups[ndxGroup].Output.Path))
                    {
                        // set the flag; no need to check any more
                        m_outputToStandardOut = true;
                        break;
                    }
                }

                // loop through all the crunch groups
                retVal = this.ProcessCrunchGroups(crunchGroups);
            }
            else
            {
                // no crunch groups
                throw new UsageException(ConsoleOutputMode.Console, "NoInput");
            }

            return retVal;
        }

        private int ProcessCrunchGroups(CrunchGroup[] crunchGroups)
        {
            int retVal = 0;

            for (int ndxGroup = 0; ndxGroup < crunchGroups.Length; ++ndxGroup)
            {
                // shortcut
                CrunchGroup crunchGroup = crunchGroups[ndxGroup];
               
                XmlWriter symbolMapWriter = null;
                if (!string.IsNullOrEmpty(crunchGroup.SymbolMapPath))
                {
                    retVal = this.ClobberFileAndExecuteOperation(
                        crunchGroup.SymbolMapPath,
                        delegate
                        {
                            symbolMapWriter = XmlWriter.Create(
                                crunchGroup.SymbolMapPath,
                                new XmlWriterSettings { CloseOutput = true, Indent = true });
                        });

                    if (retVal != 0)
                    {
                        return retVal;
                    }
                }                

                int crunchResult;                
                try
                {
                    if (symbolMapWriter != null)
                    {
                        m_switchParser.JSSettings.SymbolsMap = new ScriptSharpSourceMap(symbolMapWriter);
                        m_switchParser.JSSettings.SymbolsMap.StartPackage(crunchGroup.Output.Path);
                    }

                    // process the crunch group
                    crunchResult = this.ProcessCrunchGroup(crunchGroup);
                }
                finally
                {
                    if (m_switchParser.JSSettings.SymbolsMap != null)
                    {
                        m_switchParser.JSSettings.SymbolsMap.EndPackage();
                        m_switchParser.JSSettings.SymbolsMap.Dispose();
                        m_switchParser.JSSettings.SymbolsMap = null;
                    }
                }

                // if the result contained an error...
                if (crunchResult != 0)
                {
                    // if we're processing more than one group, we should output an
                    // error message indicating that this group encountered an error
                    if (crunchGroups.Length > 1)
                    {
                        // non-localized string, so format is not in the resources
                        string errorCode = string.Format(CultureInfo.InvariantCulture, "AM{0:D4}", crunchResult);

                        // if there is an output file name, use it.
                        if (!string.IsNullOrEmpty(crunchGroup.Output.Path))
                        {
                            this.WriteError(
                                crunchGroup.Output.Path,
                                AjaxMin.OutputFileErrorSubCat,
                                errorCode,
                                Extensions.FormatInvariant(AjaxMin.OutputFileError, crunchResult));
                        }
                        else if (!string.IsNullOrEmpty(this.m_xmlInputFile))
                        {
                            // use the XML file as the location, and the index of the group for more info
                            // inside the message
                            this.WriteError(
                                this.m_xmlInputFile,
                                AjaxMin.OutputGroupErrorSubCat,
                                errorCode,
                                Extensions.FormatInvariant(AjaxMin.OutputGroupError, ndxGroup, crunchResult));
                        }
                        else
                        {
                            // no output file name, and not from an XML file. If it's not from an XML
                            // file, then there really should only be one crunch group.
                            // but just in case, use "stdout" as the output file and the index of the group 
                            // in the list (which should probably just be zero)
                            this.WriteError(
                                "stdout",
                                AjaxMin.OutputGroupErrorSubCat,
                                errorCode,
                                Extensions.FormatInvariant(AjaxMin.OutputGroupError, ndxGroup, crunchResult));
                        }
                    }
                    // return the error. Only the last one will be used
                    retVal = crunchResult;
                }
            }

            return retVal;
        }

        #endregion

        #region ProcessCrunchGroup method

        public static Encoding GetJSEncoding(string encodingName)
        {
            return GetEncoding(encodingName, new JSEncoderFallback());
        }

        public static Encoding GetCssEncoding(string encodingName)
        {
            return GetEncoding(encodingName, new CssEncoderFallback());
        }

        private static Encoding GetEncoding(string encodingName, EncoderFallback fallback)
        {
            Encoding encoding = null;
            if (string.IsNullOrEmpty(encodingName))
            {
                // nothing specified -- use our default encoding of UTF-8.
                // clone the utf-8 encoder so we can change the fallback handler
                encoding = (Encoding)Encoding.UTF8.Clone();
                encoding.EncoderFallback = fallback;
            }
            else
            {
                try
                {
                    // try to create an encoding from the encoding argument
                    encoding = Encoding.GetEncoding(
                        encodingName,
                        fallback,
                        new DecoderReplacementFallback("?"));
                }
                catch (ArgumentException e)
                {
                    System.Diagnostics.Debug.WriteLine(e.ToString());
                }
            }

            return encoding;
        }

        private Encoding GetOutputEncoding(InputType inputType, string encodingName)
        {
            // pick the right encoder from our file type
            Encoding encoding = null;

            // set the appropriate encoder fallback
            if (inputType == InputType.JavaScript)
            {
                encoding = GetJSEncoding(encodingName);
            }
            else if (inputType == InputType.Css)
            {
                encoding = GetCssEncoding(encodingName);
            }

            if (encoding == null)
            {
                throw new UsageException(m_outputMode, Extensions.FormatInvariant(AjaxMin.InvalidOutputEncoding, encodingName));
            }

            return encoding;
        }

        private Encoding GetInputEncoding(string encodingName)
        {
            // just get the JS encoding; we're not going to be outputting anything with this encoding
            // object, so it doesn't matter which output encoding fallback object we have on it.
            var encoding = GetJSEncoding(encodingName ?? "UTF-8");
            if (encoding == null)
            {
                throw new UsageException(m_outputMode, Extensions.FormatInvariant(AjaxMin.InvalidInputEncoding, encodingName));
            }

            return encoding;
        }

        private string ReadInputFile(string sourcePath, string encodingName, ref long sourceLength)
        {
            // read our chunk of code
            var encodingInput = GetInputEncoding(encodingName);

            string source;
            if (!string.IsNullOrEmpty(sourcePath))
            {
                using (StreamReader reader = new StreamReader(sourcePath, encodingInput))
                {
                    WriteProgress(
                      Extensions.FormatInvariant(AjaxMin.CrunchingFile, Path.GetFileName(sourcePath))
                      );
                    source = reader.ReadToEnd();
                }

                // add the actual file length in to the input source length
                FileInfo inputFileInfo = new FileInfo(sourcePath);
                sourceLength += inputFileInfo.Length;
            }
            else
            {
                WriteProgress(AjaxMin.CrunchingStdIn);
                try
                {
                    // try setting the input encoding
                    Console.InputEncoding = encodingInput;
                }
                catch (IOException e)
                {
                    // error setting the encoding input; just use whatever the default is
                    Debug.WriteLine(e.ToString());
                }

                source = Console.In.ReadToEnd();

                if (m_switchParser.AnalyzeMode)
                {
                    // calculate the actual number of bytes read using the input encoding
                    // and the string that we just read and
                    // add the number of bytes read into the input length.
                    sourceLength += Console.InputEncoding.GetByteCount(source);
                }
                else
                {
                    // don't bother calculating the actual bytes -- the number of characters
                    // is sufficient if we're not doing the analysis
                    sourceLength += source.Length;
                }
            }

            return source;
        }

        private int ProcessCrunchGroup(CrunchGroup crunchGroup)
        {
            int retVal = 0;

            // length of all the source files combined
            long sourceLength = 0;

            // if the crunch group has any resource strings objects, we need to add them to the back
            // of the settings list
            var hasCrunchSpecificResources = crunchGroup.ResourceStrings != null && crunchGroup.ResourceStrings.Count > 0;

            // create a string builder we'll dump our output into
            StringBuilder outputBuilder = new StringBuilder();

            try
            {
                switch (crunchGroup.InputType)
                {
                    case InputType.Css:
                        if (hasCrunchSpecificResources)
                        {
                            // add to the CSS list
                            foreach (var resourceStrings in crunchGroup.ResourceStrings)
                            {
                                m_switchParser.CssSettings.AddResourceStrings(resourceStrings);
                            }
                        }

                        // see how many input files there are
                        if (crunchGroup.Count == 0)
                        {
                            // no input files -- take from stdin
                            retVal = ProcessCssFile(string.Empty, m_switchParser.EncodingInputName, outputBuilder, ref sourceLength);
                        }
                        else
                        {
                            // process each input file
                            for (int ndx = 0; retVal == 0 && ndx < crunchGroup.Count; ++ndx)
                            {
                                retVal = ProcessCssFile(
                                    crunchGroup[ndx].Path,
                                    crunchGroup[ndx].EncodingName ?? m_switchParser.EncodingInputName,
                                    outputBuilder,
                                    ref sourceLength);
                            }
                        }

                        if (hasCrunchSpecificResources)
                        {
                            // remove from the CSS list
                            foreach (var resourceStrings in crunchGroup.ResourceStrings)
                            {
                                m_switchParser.CssSettings.RemoveResourceStrings(resourceStrings);
                            }
                        }
                        break;

                    case InputType.JavaScript:
                        if (hasCrunchSpecificResources)
                        {
                            // add to the JS list
                            foreach (var resourceStrings in crunchGroup.ResourceStrings)
                            {
                                m_switchParser.JSSettings.AddResourceStrings(resourceStrings);
                            }
                        }

                        if (m_echoInput && m_switchParser.JSSettings.ResourceStrings != null)
                        {
                            // we're just echoing the output -- so output a JS version of the dictionary
                            // create JS from the dictionary and output it to the stream
                            // leave the object null
                            foreach (var resourceStrings in m_switchParser.JSSettings.ResourceStrings)
                            {
                                string resourceObject = CreateJSFromResourceStrings(resourceStrings);
                                outputBuilder.Append(resourceObject);
                            }
                        }

                        try
                        {
                            if (m_preprocessOnly)
                            {
                                // see how many input files there are
                                if (crunchGroup.Count == 0)
                                {
                                    // take input from stdin.
                                    // since that's the ONLY input file, pass TRUE for isLastFile
                                    retVal = PreprocessJSFile(string.Empty, m_switchParser.EncodingInputName, outputBuilder, true, ref sourceLength);
                                }
                                else
                                {
                                    // process each input file in turn. 
                                    for (int ndx = 0; retVal == 0 && ndx < crunchGroup.Count; ++ndx)
                                    {
                                        retVal = PreprocessJSFile(
                                            crunchGroup[ndx].Path,
                                            crunchGroup[ndx].EncodingName ?? m_switchParser.EncodingInputName,
                                            outputBuilder,
                                            ndx == crunchGroup.Count - 1,
                                            ref sourceLength);
                                    }
                                }
                            }
                            else if (m_echoInput)
                            {
                                // see how many input files there are
                                if (crunchGroup.Count == 0)
                                {
                                    // take input from stdin.
                                    // since that's the ONLY input file, pass TRUE for isLastFile
                                    retVal = ProcessJSFileEcho(string.Empty, m_switchParser.EncodingInputName, outputBuilder, ref sourceLength);
                                }
                                else
                                {
                                    // process each input file in turn. 
                                    for (int ndx = 0; retVal == 0 && ndx < crunchGroup.Count; ++ndx)
                                    {
                                        retVal = ProcessJSFileEcho(
                                            crunchGroup[ndx].Path,
                                            crunchGroup[ndx].EncodingName ?? m_switchParser.EncodingInputName,
                                            outputBuilder,
                                            ref sourceLength);
                                    }
                                }
                            }
                            else
                            {
                                using (var writer = new StringWriter(outputBuilder, CultureInfo.InvariantCulture))
                                {
                                    var outputVisitor = new OutputVisitor(writer, m_switchParser.JSSettings);

                                    // see how many input files there are
                                    if (crunchGroup.Count == 0)
                                    {
                                        // take input from stdin.
                                        // since that's the ONLY input file, pass TRUE for isLastFile
                                        retVal = ProcessJSFile(string.Empty, m_switchParser.EncodingInputName, outputVisitor, true, ref sourceLength);
                                    }
                                    else
                                    {
                                        // process each input file in turn. 
                                        for (int ndx = 0; retVal == 0 && ndx < crunchGroup.Count; ++ndx)
                                        {
                                            retVal = ProcessJSFile(
                                                crunchGroup[ndx].Path,
                                                crunchGroup[ndx].EncodingName ?? m_switchParser.EncodingInputName,
                                                outputVisitor,
                                                ndx == crunchGroup.Count - 1,
                                                ref sourceLength);
                                        }
                                    }
                                }
                            }
                        }
                        catch (JScriptException e)
                        {
                            retVal = 1;
                            System.Diagnostics.Debug.WriteLine(e.ToString());
                            WriteError(string.Format(CultureInfo.InvariantCulture, "JS{0}", (int)e.ErrorCode), e.Message);
                        }

                        if (hasCrunchSpecificResources)
                        {
                            // remove from the JS list
                            foreach (var resourceStrings in crunchGroup.ResourceStrings)
                            {
                                m_switchParser.JSSettings.RemoveResourceStrings(resourceStrings);
                            }
                        }
                        break;

                    default:
                        throw new UsageException(m_outputMode, "UnknownInputType");
                }

                // if we are pretty-printing, add a newline
                if (m_switchParser.PrettyPrint)
                {
                    outputBuilder.AppendLine();
                }

                string crunchedCode = outputBuilder.ToString();

                Encoding encodingOutput = GetOutputEncoding(
                    crunchGroup.InputType, 
                    crunchGroup.Output.EncodingName ?? m_switchParser.EncodingOutputName);

                // now write the final output file
                if (string.IsNullOrEmpty(crunchGroup.Output.Path))
                {
                    // no output file specified - send to STDOUT
                    // if the code is empty, don't bother outputting it to the console
                    if (!string.IsNullOrEmpty(crunchedCode))
                    {
                        // set the console encoding
                        try
                        {
                            // try setting the appropriate output encoding
                            Console.OutputEncoding = encodingOutput;
                        }
                        catch (IOException e)
                        {
                            // sometimes they will error, in which case we'll just set it to ascii
                            Debug.WriteLine(e.ToString());
                            Console.OutputEncoding = Encoding.ASCII;
                        }

                        // however, for some reason when I set the output encoding it
                        // STILL doesn't call the EncoderFallback to Unicode-escape characters
                        // not supported by the encoding scheme. So instead we need to run the
                        // translation outselves. Still need to set the output encoding, though,
                        // so the translated bytes get displayed properly in the console.
                        byte[] encodedBytes = encodingOutput.GetBytes(crunchedCode);

                        // only output the size analysis if we are in analyze mode
                        // change: no, output the size analysis all the time.
                        // (unless in silent mode, but WriteProgess will take care of that)
                        ////if (m_analyze)
                        {
                            // output blank line before
                            WriteProgress();

                            // if we are echoing the input, don't bother reporting the
                            // minify savings because we don't have the minified output --
                            // we have the original output
                            double percentage;
                            if (!m_echoInput)
                            {
                                // calculate the percentage saved
                                percentage = Math.Round((1 - ((double)encodedBytes.Length) / sourceLength) * 100, 1);
                                WriteProgress(Extensions.FormatInvariant(AjaxMin.SavingsMessage,
                                                  sourceLength,
                                                  encodedBytes.Length,
                                                  percentage
                                                  ));
                            }
                            else
                            {

                                WriteProgress(Extensions.FormatInvariant(AjaxMin.SavingsOutputMessage,
                                    encodedBytes.Length
                                    ));
                            }

                            // calculate how much a simple gzip compression would compress the output
                            long gzipLength = CalculateGzipSize(encodedBytes);

                            // calculate the savings and display the result
                            percentage = Math.Round((1 - ((double)gzipLength) / encodedBytes.Length) * 100, 1);
                            WriteProgress(Extensions.FormatInvariant(AjaxMin.SavingsGzipMessage, gzipLength, percentage));

                            // blank line after
                            WriteProgress();
                        }

                        // send to console out
                        Console.Out.Write(Console.OutputEncoding.GetChars(encodedBytes));
                        //Console.Out.Write(crunchedCode);
                    }
                }
                else
                {
                    retVal = this.ClobberFileAndExecuteOperation(
                        crunchGroup.Output.Path,
                        delegate
                        {
                            // create the output file using the given encoding
                            using (StreamWriter outputStream = new StreamWriter(
                               crunchGroup.Output.Path,
                               false,
                               encodingOutput
                               ))
                            {
                                outputStream.Write(crunchedCode);
                            }

                            // only output the size analysis if there is actually some output to measure
                            if (File.Exists(crunchGroup.Output.Path))
                            {
                                // get the size of the resulting file
                                FileInfo crunchedFileInfo = new FileInfo(crunchGroup.Output.Path);
                                long crunchedLength = crunchedFileInfo.Length;
                                if (crunchedLength > 0)
                                {
                                    // blank line before
                                    WriteProgress();

                                    // if we are just echoing the input, don't bother calculating
                                    // the minify savings because there aren't any
                                    double percentage;
                                    if (!m_echoInput)
                                    {
                                        // calculate the percentage saved by minification
                                        percentage = Math.Round((1 - ((double)crunchedLength) / sourceLength) * 100, 1);
                                        WriteProgress(Extensions.FormatInvariant(AjaxMin.SavingsMessage,
                                                          sourceLength,
                                                          crunchedLength,
                                                          percentage
                                                          ));
                                    }
                                    else
                                    {

                                        WriteProgress(Extensions.FormatInvariant(AjaxMin.SavingsOutputMessage,
                                            crunchedLength
                                            ));
                                    }

                                    // compute how long a simple gzip might compress the resulting file
                                    long gzipLength = CalculateGzipSize(File.ReadAllBytes(crunchGroup.Output.Path));

                                    // calculate the percentage of compression and display the results
                                    percentage = Math.Round((1 - ((double)gzipLength) / crunchedLength) * 100, 1);
                                    WriteProgress(Extensions.FormatInvariant(AjaxMin.SavingsGzipMessage, gzipLength, percentage));

                                    // blank line after
                                    WriteProgress();
                                }
                            }
                        });
                }
            }
            catch (Exception e)
            {
                retVal = 1;
                System.Diagnostics.Debug.WriteLine(e.ToString());
                WriteError("AM-EXCEPTION", e.Message);
            }

            if (retVal == 0 && m_errorsFound)
            {
                // make sure we report an error
                retVal = 1;
            }
            return retVal;
        }

        private int ClobberFileAndExecuteOperation(string filePath, Action<string> operation)
        {
            int retVal = 0;

            // send output to file
            try
            {
                // make sure the destination folder exists
                FileInfo fileInfo = new FileInfo(filePath);
                DirectoryInfo destFolder = new DirectoryInfo(fileInfo.DirectoryName);
                if (!destFolder.Exists)
                {
                    destFolder.Create();
                }

                if (!File.Exists(filePath) || m_clobber)
                {
                    if (m_clobber
                        && File.Exists(filePath)
                        && (File.GetAttributes(filePath) & FileAttributes.ReadOnly) != 0)
                    {
                        // the file exists, we said we want to clobber it, but it's marked as
                        // read-only. Reset that flag.
                        File.SetAttributes(
                            filePath,
                            (File.GetAttributes(filePath) & ~FileAttributes.ReadOnly)
                            );
                    }

                    operation(filePath);
                }
                else
                {
                    retVal = 1;
                    WriteError("AM-IO", Extensions.FormatInvariant(AjaxMin.NoClobberError, filePath));
                }
            }
            catch (ArgumentException e)
            {
                retVal = 1;
                System.Diagnostics.Debug.WriteLine(e.ToString());
                WriteError("AM-PATH", e.Message);
            }
            catch (UnauthorizedAccessException e)
            {
                retVal = 1;
                System.Diagnostics.Debug.WriteLine(e.ToString());
                WriteError("AM-AUTH", e.Message);
            }
            catch (PathTooLongException e)
            {
                retVal = 1;
                System.Diagnostics.Debug.WriteLine(e.ToString());
                WriteError("AM-PATH", e.Message);
            }
            catch (SecurityException e)
            {
                retVal = 1;
                System.Diagnostics.Debug.WriteLine(e.ToString());
                WriteError("AM-SEC", e.Message);
            }
            catch (IOException e)
            {
                retVal = 1;
                System.Diagnostics.Debug.WriteLine(e.ToString());
                WriteError("AM-IO", e.Message);
            }

            return retVal;
        }

        #endregion

        #region CrunchGroup class

        private class FileInformation
        {
            public string Path { get; set; }
            public string EncodingName { get; set; }

            // implictly convert to string by returning the path
            //public static implicit operator string(FileInformation fi){return fi.Path;}
        }

        private class CrunchGroup
        {
            // the output file for the group. May be empty string.
            public FileInformation Output { get; set; }

            // input type (JavaScript or CSS)
            public InputType InputType { get; set; }

            // optional list of resource string objects specific to this group
            public IList<ResourceStrings> ResourceStrings { get; set; }

            // list of input files -- may not be empty.
            private List<FileInformation> m_sourcePaths;// = null;

            // if we don't even have a list yet, return 0; otherwise the count in the list
            public int Count { get { return m_sourcePaths.Count; } }
            public FileInformation this[int ndx]
            {
                get
                {
                    // return the object (which may throw an index exception itself)
                    return m_sourcePaths[ndx];
                }
            }

            public string SymbolMapPath { get; private set; }

            public CrunchGroup(string outputPath, string encodingOutputName, string symbolMapPath)
            {
                Output = new FileInformation() { Path = outputPath, EncodingName = encodingOutputName };
                SymbolMapPath = symbolMapPath;
                m_sourcePaths = new List<FileInformation>();
            }

            public CrunchGroup(string outputPath, string encodingOutputName, string symbolMapPath, string[] inputFiles, string encodingInputName, InputType inputType)
                : this(outputPath, encodingOutputName, symbolMapPath)
            {
                // save the input type
                InputType = inputType;

                // add the array in one fell swoop
                foreach (var inputPath in inputFiles)
                {
                    m_sourcePaths.Add(new FileInformation() { Path = inputPath, EncodingName = encodingInputName });
                }
            }

            public void Add(string inputPath, string encodingName)
            {
                // add this item to the list
                m_sourcePaths.Add(new FileInformation() { Path = inputPath, EncodingName = encodingName });
            }
        }

        #endregion

        #region ProcessXmlFile method

        private static CrunchGroup[] ProcessXmlFile(string xmlPath, string outputFolder)
        {
            // list of crunch groups we're going to create by reading the XML file
            List<CrunchGroup> crunchGroups = new List<CrunchGroup>();
            try
            {
                // save the XML file's directory name because we'll use it as a root
                // for all the other paths in the file
                string rootPath = Path.GetDirectoryName(xmlPath);

                // open the xml file
                XmlDocument xmlDoc = new XmlDocument();
                xmlDoc.Load(xmlPath);

                // get a list of all <output> nodes
                XmlNodeList outputNodes = xmlDoc.SelectNodes("//output");
                if (outputNodes.Count > 0)
                {
                    // process each <output> node
                    for (int ndxOutput = 0; ndxOutput < outputNodes.Count; ++ndxOutput)
                    {
                        // shortcut
                        XmlNode outputNode = outputNodes[ndxOutput];

                        // get the output file path from the path attribute (if any)
                        // it's okay for ther eto be no output file; if that's the case,
                        // the output is sent to the STDOUT stream
                        XmlAttribute pathAttribute = outputNode.Attributes["path"];
                        var outputPath = NormalizePath(outputFolder, rootPath, pathAttribute);
                        var symbolMapPath = NormalizePath(outputFolder, rootPath, outputNode.Attributes["mappath"]);

                        // see if an encoding override has been specified
                        var encodingAttribute = outputNode.Attributes["encoding"];
                        var encodingOutputName = encodingAttribute != null
                            ? encodingAttribute.Value
                            : null;

                        // create the crunch group
                        CrunchGroup crunchGroup = new CrunchGroup(outputPath, encodingOutputName, symbolMapPath);

                        // see if there's an explicit input type, and if so, set the crunch group type
                        var typeAttribute = outputNode.Attributes["type"];
                        if (typeAttribute != null)
                        {
                            switch (typeAttribute.Value.ToUpperInvariant())
                            {
                                case "JS":
                                case "JAVASCRIPT":
                                case "JSCRIPT":
                                    crunchGroup.InputType = InputType.JavaScript;
                                    break;

                                case "CSS":
                                case "STYLESHEET":
                                case "STYLESHEETS":
                                    crunchGroup.InputType = InputType.Css;
                                    break;
                            }
                        }

                        // see if there are any resource nodes
                        var resourceNodes = outputNode.SelectNodes("./resource");
                        if (resourceNodes != null && resourceNodes.Count > 0)
                        {
                            for (var ndx = 0; ndx < resourceNodes.Count; ++ndx)
                            {
                                var resourceNode = resourceNodes[ndx];

                                // if there is a name attribute, we will use it's value for the object name
                                string objectName = null;
                                XmlAttribute nameAttribute = resourceNode.Attributes["name"];
                                if (nameAttribute != null)
                                {
                                    objectName = nameAttribute.Value;
                                }

                                // if no name was specified, use our default name
                                if (string.IsNullOrEmpty(objectName))
                                {
                                    objectName = c_defaultResourceObjectName;
                                }

                                // the path attribute MUST exist, or we will throw an error
                                pathAttribute = resourceNode.Attributes["path"];
                                if (pathAttribute != null)
                                {
                                    // get the value from the attribute
                                    string resourceFile = pathAttribute.Value;
                                    // if it's a relative path...
                                    if (!Path.IsPathRooted(resourceFile))
                                    {
                                        // make it relative from the XML file
                                        resourceFile = Path.Combine(rootPath, resourceFile);
                                    }

                                    // make sure the resource file actually exists! It's an error if it doesn't.
                                    if (!File.Exists(resourceFile))
                                    {
                                        throw new XmlException(Extensions.FormatInvariant(AjaxMin.XmlResourceNotExist,
                                          pathAttribute.Value
                                          ));
                                    }

                                    // create the resource strings object from the path, and set the name
                                    var resourceStrings = ProcessResources(resourceFile);
                                    resourceStrings.Name = objectName;

                                    // if the crunch group doesn't have a resource strings collection, add one now
                                    if (crunchGroup.ResourceStrings == null)
                                    {
                                        crunchGroup.ResourceStrings = new List<ResourceStrings>();
                                    }

                                    // add it to the list
                                    crunchGroup.ResourceStrings.Add(resourceStrings);
                                }
                                else
                                {
                                    throw new XmlException(AjaxMin.ResourceNoPathAttr);
                                }
                            }
                        }

                        // get a list of <input> nodes
                        XmlNodeList inputNodes = outputNode.SelectNodes("./input");
                        if (inputNodes.Count > 0)
                        {
                            // for each <input> element under the <output> node
                            for (int ndxInput = 0; ndxInput < inputNodes.Count; ++ndxInput)
                            {
                                // add the path attribute value to the string list.
                                // the path attribute MUST exist, or we will throw an error
                                pathAttribute = inputNodes[ndxInput].Attributes["path"];
                                if (pathAttribute != null)
                                {
                                    // get the value from the attribute
                                    string inputFile = pathAttribute.Value;
                                    // if it's a relative path...
                                    if (!Path.IsPathRooted(inputFile))
                                    {
                                        // make it relative from the XML file
                                        inputFile = Path.Combine(rootPath, inputFile);
                                    }

                                    // make sure the input file actually exists! It's an error if it doesn't.
                                    if (!File.Exists(inputFile))
                                    {
                                        throw new XmlException(Extensions.FormatInvariant(AjaxMin.XmlInputNotExist,
                                          pathAttribute.Value
                                          ));
                                    }

                                    // if we don't know the type yet, let's see if the extension gives us a hint
                                    if (crunchGroup.InputType == InputType.Unknown)
                                    {
                                        switch (Path.GetExtension(inputFile).ToUpperInvariant())
                                        {
                                            case ".JS":
                                                crunchGroup.InputType = InputType.JavaScript;
                                                break;

                                            case ".CSS":
                                                crunchGroup.InputType = InputType.Css;
                                                break;
                                        }
                                    }

                                    // see if there is an encoding attribute
                                    encodingAttribute = inputNodes[ndxInput].Attributes["encoding"];
                                    string encodingName = encodingAttribute != null
                                        ? encodingAttribute.Value
                                        : null;

                                    // add the input file and its encoding (if any) to the group
                                    crunchGroup.Add(inputFile, encodingName);
                                }
                                else
                                {
                                    // no required path attribute on the <input> element
                                    throw new XmlException(AjaxMin.InputNoPathAttr);
                                }
                            }
                            // add the crunch group to the list
                            crunchGroups.Add(crunchGroup);
                        }
                        else
                        {
                            // no required <input> nodes inside the <output> node
                            throw new XmlException(AjaxMin.OutputNoInputNodes);
                        }
                    }
                }
                else
                {
                    // no required <output> nodes
                    // throw an error to end all processing
                    throw new UsageException(ConsoleOutputMode.Console, "XmlNoOutputNodes");
                }
            }
            catch (XmlException e)
            {
                // throw an error indicating the XML error
                System.Diagnostics.Debug.WriteLine(e.ToString());
                throw new UsageException(ConsoleOutputMode.Console, Extensions.FormatInvariant(AjaxMin.InputXmlError, e.Message));
            }
            // return an array of CrunchGroup objects
            return crunchGroups.ToArray();
        }

        private static string NormalizePath(string outputFolder, string rootPath, XmlAttribute pathAttribute)
        {
            string outputPath = (pathAttribute == null ? string.Empty : pathAttribute.Value);
            // if we have a value and it's a relative path...
            if (outputPath.Length > 0 && !Path.IsPathRooted(outputPath))
            {
                if (string.IsNullOrEmpty(outputFolder))
                {
                    // make it relative to the XML file
                    outputPath = Path.Combine(rootPath, outputPath);
                }
                else
                {
                    // make it relative to the output folder
                    outputPath = Path.Combine(outputFolder, outputPath);
                }
            }
            return outputPath;
        }

        #endregion

        #region resource processing

        private ResourceStrings ProcessResourceFile(string resourceFileName)
        {
            WriteProgress(
                Extensions.FormatInvariant(AjaxMin.ReadingResourceFile, Path.GetFileName(resourceFileName))
                );

            // which meethod we call to process the resources depends on the file extension
            // of the resources path given to us.
            switch (Path.GetExtension(resourceFileName).ToUpperInvariant())
            {
                case ".RESX":
                    // process the resource file as a RESX xml file
                    return ProcessResXResources(resourceFileName);

                case ".RESOURCES":
                    // process the resource file as a compiles RESOURCES file
                    return ProcessResources(resourceFileName);

                default:
                    // no other types are supported
                    throw new UsageException(m_outputMode, "ResourceArgInvalidType");
            }
        }

        private static ResourceStrings ProcessResources(string resourceFileName)
        {
            // default return object is null, meaning we are outputting the JS code directly
            // and don't want to replace any referenced resources in the sources
            ResourceStrings resourceStrings = null;
            using (ResourceReader reader = new ResourceReader(resourceFileName))
            {
                // get an enumerator so we can itemize all the key/value pairs
                IDictionaryEnumerator enumerator = reader.GetEnumerator();

                // create an object out of the dictionary
                resourceStrings = new ResourceStrings(enumerator);
            }
            return resourceStrings;
        }

        private static ResourceStrings ProcessResXResources(string resourceFileName)
        {
            // default return object is null, meaning we are outputting the JS code directly
            // and don't want to replace any referenced resources in the sources
            ResourceStrings resourceStrings = null;
            using (ResXResourceReader reader = new ResXResourceReader(resourceFileName))
            {
                // get an enumerator so we can itemize all the key/value pairs
                IDictionaryEnumerator enumerator = reader.GetEnumerator();

                // create an object out of the dictionary
                resourceStrings = new ResourceStrings(enumerator);
            }
            return resourceStrings;
        }

        #endregion

        #region Utility methods

        /// <summary>
        /// Write an empty progress line
        /// </summary>
        private void WriteProgress()
        {
            WriteProgress(string.Empty);
        }

        /// <summary>
        /// Writes a progress string to the stderr stream.
        /// if in SILENT mode, writes to debug stream, not stderr!!!!
        /// </summary>
        /// <param name="format">format string</param>
        /// <param name="args">optional arguments</param>
        private void WriteProgress(string format, params object[] args)
        {
            if (m_outputMode != ConsoleOutputMode.Silent)
            {
                // if we are writing all output to one or more files, then progress messages will go
                // to stdout. If we are sending any minified output to stdout, then progress messages will
                // goto stderr; in that case, use the -silent option to suppress progress messages
                // from the stderr stream.
                var outputStream = m_outputToStandardOut ? Console.Error : Console.Out;

                // if we haven't yet output our header, do so now
                if (!m_headerWritten)
                {
                    // the header string will end with its own line-terminator, so we 
                    // don't need to call WriteLine
                    outputStream.Write(GetHeaderString());
                    m_headerWritten = true;
                }

                try
                {
                    outputStream.WriteLine(format, args);
                }
                catch (FormatException)
                {
                    // not enough args -- so don't use any
                    outputStream.WriteLine(format);
                }
            }
            else
            {
                // silent -- output to debug only
                try
                {
                    Debug.WriteLine(string.Format(CultureInfo.CurrentCulture, format, args));
                }
                catch (FormatException)
                {
                    // something wrong with the number of args -- don't use any
                    Debug.WriteLine(format);
                }
            }
        }

        /// <summary>
        /// Always write the string to stderr, even in silent mode
        /// </summary>
        /// <param name="message">text to write</param>
        private void WriteError(string message)
        {
            // don't output the header if in silent mode
            if (m_outputMode != ConsoleOutputMode.Silent && !m_headerWritten)
            {
                // the header string will end with its own line-terminator, so we 
                // don't need to call WriteLine
                Console.Error.Write(GetHeaderString());
                m_headerWritten = true;
            }

            // output the error message
            Console.Error.WriteLine(message);
        }

        /// <summary>
        /// Always writes string to stderr, even if in silent mode
        /// </summary>
        /// <param name="location">optional location string, uses assembly name if not provided</param>
        /// <param name="subcategory">optional subcategory</param>
        /// <param name="code">non-localized error code</param>
        /// <param name="message">localized error message</param>
        private void WriteError(string location, string subcategory, string code, string message)
        {
            // output the formatted error message
            WriteError(CreateBuildError(location, subcategory, code, message));
        }

        /// <summary>
        /// Always writes string to stderr, even if in silent mode.
        /// Use default location and subcategory values.
        /// </summary>
        /// <param name="code">non-localized error code</param>
        /// <param name="message">localized error message</param>
        private void WriteError(string code, string message)
        {
            // output the formatted error message, passing null for location and subcategory
            WriteError(null, null, code, message);
        }

        /// <summary>
        /// Output a build error in a style consistent with MSBuild/Visual Studio standards
        /// so that the error gets properly picked up as a build-breaking error and displayed
        /// in the error pane
        /// </summary>
        /// <param name="location">source file(line,col), or empty for general tool error</param>
        /// <param name="subcategory">optional localizable subcategory (such as severity message)</param>
        /// <param name="code">non-localizable code indicating the error -- cannot contain spaces</param>
        /// <param name="format">localized text for error, can contain format placeholders</param>
        /// <param name="args">optional arguments for the format string</param>
        private static string CreateBuildError(string location, string subcategory, string code, string message)
        {
            // if we didn't specify a location string, just use the name of this tool
            if (string.IsNullOrEmpty(location))
            {
                location = Path.GetFileName(
                    Assembly.GetExecutingAssembly().Location
                    );
            }

            // code cannot contain any spaces. If there are, trim it 
            // and replace any remaining spaces with underscores
            if (code.IndexOf(' ') >= 0)
            {
                code = code.Trim().Replace(' ', '_');
            }

            // if subcategory isn't null or empty and doesn't already end in a space, add it
            if (string.IsNullOrEmpty(subcategory))
            {
                // we are null or empty. empty is okay -- we can leave it along. But let's
                // turn nulls into emptys, too
                if (subcategory == null)
                {
                    subcategory = string.Empty;
                }
            }
            else if (!subcategory.EndsWith(" ", StringComparison.Ordinal))
            {
                // we are not empty and we don't end in a space -- add one now
                subcategory += " ";
            }
            // else we are not empty and we already end in a space, so all is good

            return string.Format(
                CultureInfo.CurrentCulture,
                "{0}: {1}{2} {3}: {4}",
                location, // not localized
                subcategory, // localizable, optional
                "error", // NOT localized
                code, // not localized, cannot contain spaces
                message // localizable with optional arguments
                );
        }

        private static string GetHeaderString()
        {
            var description = string.Empty;
            var copyright = string.Empty;
            var product = string.Empty;

            var assembly = Assembly.GetExecutingAssembly();
            foreach (var attr in assembly.GetCustomAttributes(false))
            {
                var attrType = attr.GetType();
                if (attrType == typeof(AssemblyDescriptionAttribute))
                {
                    description = ((AssemblyDescriptionAttribute)attr).Description;
                }
                else if (attrType == typeof(AssemblyCopyrightAttribute))
                {
                    copyright = ((AssemblyCopyrightAttribute)attr).Copyright;
                    copyright = copyright.Replace("©", "(c)");
                }
                else if (attrType == typeof(AssemblyProductAttribute))
                {
                    product = ((AssemblyProductAttribute)attr).Product;
                }
            }

            var assemblyName = assembly.GetName();

            // combine the information for output
            var sb = new StringBuilder();
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "{0} (version {1})", string.IsNullOrEmpty(product) ? assemblyName.Name : product, assemblyName.Version));
            if (!string.IsNullOrEmpty(description)) { sb.AppendLine(description); }
            if (!string.IsNullOrEmpty(copyright)) { sb.AppendLine(copyright); }
            return sb.ToString();
        }

        private static long CalculateGzipSize(byte[] bytes)
        {
            using (var memoryStream = new MemoryStream())
            {
                var gzipStream = new GZipStream(memoryStream, CompressionMode.Compress, true);
                gzipStream.Write(bytes, 0, bytes.Length);
                gzipStream.Close();
                return memoryStream.Position;
            }
        }

        #endregion
    }

    #region usage exceptions

#if !SILVERLIGHT
    [Serializable]
#endif
    public class UsageException : Exception
    {
#if !SILVERLIGHT
        [NonSerialized]
#endif
        private ConsoleOutputMode m_outputMode;
        public ConsoleOutputMode OutputMode { get { return m_outputMode; } }

        public UsageException(ConsoleOutputMode outputMode)
            : base(string.Empty)
        {
            m_outputMode = outputMode;
        }

        public UsageException(ConsoleOutputMode outputMode, string msg)
            : base(msg)
        {
            m_outputMode = outputMode;
        }

#if !SILVERLIGHT
        protected UsageException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
            if (info == null)
            {
                throw new ArgumentException(AjaxMin.InternalCompilerError);
            }
            m_outputMode = ConsoleOutputMode.Console;
        }
#endif
        public UsageException()
        {
            m_outputMode = ConsoleOutputMode.Console;
        }

        public UsageException(string message)
            : this(ConsoleOutputMode.Console, message)
        {
        }

        public UsageException(string message, Exception innerException)
            : base(message, innerException)
        {
            m_outputMode = ConsoleOutputMode.Console;
        }
    }
    #endregion

    #region custom enumeration

    /// <summary>
    /// Method of outputting information
    /// </summary>
    public enum ConsoleOutputMode
    {
        Silent,
        Console
    }

    public enum InputType
    {
        Unknown = 0,
        JavaScript,
        Css
    }

    #endregion
}