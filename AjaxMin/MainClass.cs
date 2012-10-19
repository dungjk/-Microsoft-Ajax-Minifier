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

        /// <summary>
        /// default set of arguments if this is driven from an XML file
        /// </summary>
        private string m_defaultArguments;

        /// <summary>
        /// configuration mode
        /// </summary>
        private string m_configuration;

        /// <summary>whether to suppress output of the parsed code</summary>
        private bool m_noOutput;

        #endregion

        #region common settings

        /// <summary>
        /// object to turn the command-line into settings object
        /// </summary>
        private SwitchParser m_switchParser;

        // whether to clobber existing output files
        private ClobberType m_clobber; // = ClobberType.Auto

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
        /// Input type hint from the switches: possibly JS or CSS
        /// </summary>
        private InputType m_inputTypeHint = InputType.Unknown;

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

        /// <summary>
        /// Name of the symbols map implementation to use (if any)
        /// </summary>
        private string m_symbolsMapName;

        /// <summary>
        /// clobber type
        /// </summary>
        private enum ClobberType
        {
            Auto = 0,
            Clobber,
            NoClobber
        }

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

            m_switchParser.CssOnlyParameter += (sender, ea) => { InputTypeHint(InputType.Stylesheet); };
            m_switchParser.JSOnlyParameter += (sender, ea) => { InputTypeHint(InputType.JavaScript); };
            m_switchParser.InvalidSwitch += (sender, ea) =>
            {
                if (ea.ParameterPart == null)
                {
                    // if there's no parameter, then the switch required an arg
                    throw new UsageException(m_outputMode, AjaxMin.SwitchRequiresArg.FormatInvariant(ea.SwitchPart));
                }
                else
                {
                    // otherwise the arg was invalid
                    throw new UsageException(m_outputMode, AjaxMin.InvalidSwitchArg.FormatInvariant(ea.ParameterPart, ea.SwitchPart));
                }
            };

            // and go
            m_switchParser.Parse(args);

            // if we are going to use an xml file for input, we don't care about finding out which
            // code path to take (JS or CSS) at this point. The XML file can contain either or both.
            if (string.IsNullOrEmpty(m_xmlInputFile))
            {
                // not XML input; we need to know what type we want to process. check for input file extensions.
                if (m_inputFiles != null && m_inputFiles.Count > 0)
                {
                    // check the extensions of the files -- they can definitively tell us 
                    // what input type we want.
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
                                    m_inputType = InputType.Stylesheet;
                                }
                                break;

                            case InputType.JavaScript:
                                // we know we are JS -- if we find a CSS file, throw an error
                                if (extension == ".CSS")
                                {
                                    throw new UsageException(m_outputMode, AjaxMin.ConflictingInputType);
                                }
                                break;

                            case InputType.Stylesheet:
                                // we know we are CSS -- if we find a JS file, throw an error
                                if (extension == ".JS")
                                {
                                    throw new UsageException(m_outputMode, AjaxMin.ConflictingInputType);
                                }
                                break;
                        }
                    }

                    // if we have input files but we don't know the type by now, 
                    // then throw an exception
                    if (m_inputType == InputType.Unknown)
                    {
                        throw new UsageException(m_outputMode, AjaxMin.UnknownInputType);
                    }
                }
                else
                {
                    // no input files. Check the hint from the switches.
                    if (m_inputTypeHint == InputType.Unknown || m_inputTypeHint == InputType.Mix)
                    {
                        // can't tell; throw an exception
                        throw new UsageException(m_outputMode, AjaxMin.UnknownInputType);
                    }

                    // use the hint
                    m_inputType = m_inputTypeHint;
                }
            }
        }

        private void OnUnknownParameter(object sender, UnknownParameterEventArgs ea)
        {
            bool flag;
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
                            m_clobber = ClobberType.Clobber;
                        }
                        else if (SwitchParser.BooleanSwitch(ea.ParameterPart.ToUpperInvariant(), true, out flag))
                        {
                            m_clobber = flag ? ClobberType.Clobber : ClobberType.Auto;
                        }
                        else
                        {
                            throw new UsageException(m_outputMode, AjaxMin.InvalidSwitchArg.FormatInvariant(ea.SwitchPart, ea.ParameterPart));
                        }

                        break;

                    case "CONFIG":
                        if (ea.ParameterPart == null)
                        {
                            throw new UsageException(m_outputMode, AjaxMin.InvalidSwitchArg.FormatInvariant(ea.SwitchPart, ea.ParameterPart));
                        }

                        m_configuration = ea.ParameterPart;
                        break;

                    case "NOCLOBBER":
                        // putting the noclobber switch on the command line without any arguments
                        // is the same as putting -noclobber:true and perfectly valid.
                        if (ea.ParameterPart == null)
                        {
                            m_clobber = ClobberType.NoClobber;
                        }
                        else if (SwitchParser.BooleanSwitch(ea.ParameterPart.ToUpperInvariant(), true, out flag))
                        {
                            m_clobber = flag ? ClobberType.NoClobber : ClobberType.Auto;
                        }
                        else
                        {
                            throw new UsageException(m_outputMode, AjaxMin.InvalidSwitchArg.FormatInvariant(ea.SwitchPart, ea.ParameterPart));
                        }

                        break;

                    case "ECHO":
                    case "I": // <-- old style
                        // ignore any arguments
                        m_echoInput = true;

                        // -pretty and -echo are not compatible
                        if (m_switchParser.AnalyzeMode)
                        {
                            throw new UsageException(m_outputMode, AjaxMin.PrettyAndEchoArgs);
                        }
                        break;

                    case "HELP":
                    case "?":
                        // just show usage
                        throw new UsageException(m_outputMode);

                    case "OUT":
                    case "O": // <-- old style
                        // cannot have two out arguments. If we've already seen an out statement,
                        // either we will have an output file or the no-output flag will be set
                        if (!string.IsNullOrEmpty(m_outputFile) || m_noOutput)
                        {
                            throw new UsageException(m_outputMode, AjaxMin.MultipleOutputArg);
                        }
                        else
                        {
                            // first instance of the -out switch. 
                            // First check to see if there's a flag on the output switch
                            if (!string.IsNullOrEmpty(ea.ParameterPart))
                            {
                                // there is. See if it's a boolean false. If it is, then we want no output 
                                // and we don't follow this switch with an output path.
                                bool outputSwitch;
                                if (SwitchParser.BooleanSwitch(ea.ParameterPart.ToUpperInvariant(), true, out outputSwitch))
                                {
                                    // the no-output flag is the opposite of the boolean flag
                                    m_noOutput = !outputSwitch;
                                }
                                else
                                {
                                    // invalid argument switch
                                    throw new UsageException(m_outputMode, AjaxMin.InvalidArgument.FormatInvariant(ea.Arguments[ea.Index]));
                                }
                            }

                            // if we still want output, then the next argument is the output path
                            if (!m_noOutput)
                            {
                                if (ea.Index >= ea.Arguments.Count - 1)
                                {
                                    throw new UsageException(m_outputMode, AjaxMin.OutputArgNeedsPath);
                                }

                                m_outputFile = ea.Arguments[++ea.Index];
                            }
                        }
                        break;

                    case "MAP":
                        if (!string.IsNullOrEmpty(m_xmlInputFile))
                        {
                            throw new UsageException(m_outputMode, AjaxMin.MapAndXmlArgs);
                        }

                        // next argument is the output path
                        // cannot have two map arguments
                        if (!string.IsNullOrEmpty(m_symbolsMapFile))
                        {
                            throw new UsageException(m_outputMode, AjaxMin.MultipleMapArg);
                        }

                        if (ea.Index >= ea.Arguments.Count - 1)
                        {
                            throw new UsageException(m_outputMode, AjaxMin.MapArgNeedsPath);
                        }

                        m_symbolsMapFile = ea.Arguments[++ea.Index];

                        // save the map implementation name, if any
                        if (!ea.ParameterPart.IsNullOrWhiteSpace())
                        {
                            m_symbolsMapName = ea.ParameterPart;
                        }
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
                                throw new UsageException(m_outputMode, AjaxMin.RenameArgMissingParameterOrFilePath.FormatInvariant(ea.SwitchPart));
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
                            throw new UsageException(m_outputMode, AjaxMin.ResourceArgNeedsPath);
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
                                    throw new UsageException(m_outputMode, AjaxMin.ResourceArgInvalidName.FormatInvariant(part));
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
                            throw new UsageException(m_outputMode, AjaxMin.MapAndXmlArgs);
                        }

                        if (!string.IsNullOrEmpty(m_xmlInputFile))
                        {
                            throw new UsageException(m_outputMode, AjaxMin.MultipleXmlArgs);
                        }
                        // cannot have input files
                        if (m_inputFiles != null && m_inputFiles.Count > 0)
                        {
                            throw new UsageException(m_outputMode, AjaxMin.XmlArgHasInputFiles);
                        }

                        if (ea.Index >= ea.Arguments.Count - 1)
                        {
                            throw new UsageException(m_outputMode, AjaxMin.XmlArgNeedsPath);
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
                        throw new UsageException(m_outputMode, AjaxMin.InvalidArgument.FormatInvariant(ea.Arguments[ea.Index]));
                }
            }
            else
            {
                // no switch -- then this must be an input file!
                // cannot coexist with XML file
                if (!string.IsNullOrEmpty(m_xmlInputFile))
                {
                    throw new UsageException(m_outputMode, AjaxMin.XmlArgHasInputFiles);
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
                else
                {
                    // duplicates are okay
                    m_inputFiles.Add(fileName);
                }
            }
        }

        private void InputTypeHint(InputType inputTypeHint)
        {
            switch (m_inputTypeHint)
            {
                case InputType.Unknown:
                    // if we don't know yet, make the assumption
                    m_inputTypeHint = inputTypeHint;
                    break;

                case InputType.JavaScript:
                case InputType.Stylesheet:
                    // if what we've had before doesn't mesh with what we have now,
                    // then we have a mix of switches
                    if (m_inputTypeHint != inputTypeHint)
                    {
                        m_inputTypeHint = InputType.Mix;
                    }
                    break;

                case InputType.Mix:
                    // a mix is a mix
                    break;
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
                    throw new UsageException(m_outputMode, AjaxMin.SourceFileIsFolder.FormatInvariant(fileName));
                }
                else
                {
                    // just plain doesn't exist
                    throw new UsageException(m_outputMode, AjaxMin.SourceFileNotExist.FormatInvariant(fileName));
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
                Console.Error.WriteLine(AjaxMin.Usage.FormatInvariant(fileName));
            }
        }

        #endregion

        #region Run method

        private int Run()
        {
            int retVal = 0;
            IList<CrunchGroup> crunchGroups;

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
                crunchGroups = new CrunchGroup[] { 
                    new CrunchGroup(m_inputFiles, m_switchParser.EncodingInputName)
                    {
                        Output = new FileInformation() {Path = m_outputFile, EncodingName = m_switchParser.EncodingOutputName},
                        SymbolMapPath = m_symbolsMapFile,
                        SymbolMapName = m_symbolsMapName,
                        InputType = m_inputType
                    }
                };
            }

            if (crunchGroups.Count > 0)
            {
                // if there are any default arguments, then we are coming from an XML file that has 
                // a default set of arguments. Apply them on top of the arguments we parsed from 
                // the command line
                if (!string.IsNullOrEmpty(m_defaultArguments))
                {
                    // parse the default arguments right on top of the ones we parsed from the command-line
                    m_switchParser.Parse(m_defaultArguments);
                }

                // if any one crunch group is writing to stdout, then we need to make sure
                // that no progress or informational messages go to stdout or we will output 
                // invalid JavaScript/CSS. Loop through the crunch groups and if any one is
                // outputting to stdout, set the appropriate flag.
                for (var ndxGroup = 0; ndxGroup < crunchGroups.Count; ++ndxGroup)
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
                throw new UsageException(ConsoleOutputMode.Console, AjaxMin.NoInput);
            }

            return retVal;
        }

        private int ProcessCrunchGroups(IList<CrunchGroup> crunchGroups)
        {
            var retVal = 0;
            var ndxGroup = 0;

            foreach (var crunchGroup in crunchGroups)
            {
                ++ndxGroup;
                var crunchResult = 1;

                // create clones of the overall settings to which we will then apply
                // our changes for this current crunch group
                var switchParser = m_switchParser.Clone();
                switchParser.Parse(crunchGroup.Arguments);

                TextWriter symbolMapWriter = null;
                try
                {
                    if (!string.IsNullOrEmpty(crunchGroup.SymbolMapPath))
                    {
                        retVal = this.ClobberFileAndExecuteOperation(
                            crunchGroup.SymbolMapPath, (path) =>
                            {
                                // the spec says UTF-8, but Chrome fails to load the map if there's a BOM.
                                // So make sure the BOM doesn't get written.
                                symbolMapWriter = new StreamWriter(path, false, new UTF8Encoding(false));
                            });

                        if (retVal != 0)
                        {
                            return retVal;
                        }
                    }

                    if (symbolMapWriter != null)
                    {
                        // which implementation to instantiate?
                        if (string.Compare(crunchGroup.SymbolMapName, V3SourceMap.ImplementationName, StringComparison.OrdinalIgnoreCase) == 0)
                        {
                            switchParser.JSSettings.SymbolsMap = new V3SourceMap(symbolMapWriter);
                        }
                        else
                        {
                            switchParser.JSSettings.SymbolsMap = new ScriptSharpSourceMap(symbolMapWriter);
                        }

                        // if we get here, the symbol map implementation now owns the stream and we can
                        // set it to null so we don't double-dispose it.
                        symbolMapWriter = null;

                        // start off the package
                        switchParser.JSSettings.SymbolsMap.StartPackage(crunchGroup.Output.Path);
                    }

                    // process the crunch group
                    crunchResult = this.ProcessCrunchGroup(crunchGroup, switchParser);
                }
                finally
                {
                    if (switchParser.JSSettings.SymbolsMap != null)
                    {
                        switchParser.JSSettings.SymbolsMap.EndPackage();
                        switchParser.JSSettings.SymbolsMap.Dispose();
                        switchParser.JSSettings.SymbolsMap = null;
                    }

                    if (symbolMapWriter != null)
                    {
                        symbolMapWriter.Close();
                        symbolMapWriter = null;
                    }
                }

                // if the result contained an error...
                if (crunchResult != 0)
                {
                    // if we're processing more than one group, we should output an
                    // error message indicating that this group encountered an error
                    if (crunchGroups.Count > 1)
                    {
                        // non-localized string, so format is not in the resources
                        string errorCode = "AM{0:D4}".FormatInvariant(crunchResult);

                        // if there is an output file name, use it.
                        if (!string.IsNullOrEmpty(crunchGroup.Output.Path))
                        {
                            this.WriteError(
                                crunchGroup.Output.Path,
                                AjaxMin.OutputFileErrorSubCat,
                                errorCode,
                                AjaxMin.OutputFileError.FormatInvariant(crunchResult));
                        }
                        else if (!string.IsNullOrEmpty(this.m_xmlInputFile))
                        {
                            // use the XML file as the location, and the index of the group for more info
                            // inside the message
                            this.WriteError(
                                this.m_xmlInputFile,
                                AjaxMin.OutputGroupErrorSubCat,
                                errorCode,
                                AjaxMin.OutputGroupError.FormatInvariant(ndxGroup, crunchResult));
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
                                AjaxMin.OutputGroupError.FormatInvariant(ndxGroup, crunchResult));
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
                // nothing specified -- use our default encoding of UTF-8 with no BOM.
                // don't need to set the JS encoder fallback because UTF-8 should be able
                // to output all UNICODE characters.
                encoding = new UTF8Encoding(false);
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
            else if (inputType == InputType.Stylesheet)
            {
                encoding = GetCssEncoding(encodingName);
            }

            if (encoding == null)
            {
                throw new UsageException(m_outputMode, AjaxMin.InvalidOutputEncoding.FormatInvariant(encodingName));
            }

            return encoding;
        }

        private Encoding GetInputEncoding(string encodingName)
        {
            // just get the JS encoding; we're not going to be outputting anything with this encoding
            // object, so it doesn't matter which output encoding fallback object we have on it.
            var encoding = GetJSEncoding(encodingName);
            if (encoding == null)
            {
                throw new UsageException(m_outputMode, AjaxMin.InvalidInputEncoding.FormatInvariant(encodingName));
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
                      AjaxMin.CrunchingFile.FormatInvariant(Path.GetFileName(sourcePath))
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

        private int ProcessCrunchGroup(CrunchGroup crunchGroup, SwitchParser switchParser)
        {
            int retVal = 0;

            // length of all the source files combined
            long sourceLength = 0;

            // if we are echoing the input, then we don't want to echo the assembled input with the
            // added ///#SOURCE comments. So create a second builder in those cases, which won't get
            // the comments added to it.
            StringBuilder echoBuilder = null;
            if (m_echoInput)
            {
                echoBuilder = new StringBuilder();

                // we're just echoing the input -- so if this is a JS output file,
                // we want to output a JS version of all resource dictionaries at the top
                // of the file.
                if (crunchGroup.InputType == InputType.JavaScript
                    && switchParser.JSSettings.ResourceStrings.Count > 0)
                {
                    foreach (var resourceStrings in switchParser.JSSettings.ResourceStrings)
                    {
                        string resourceObject = CreateJSFromResourceStrings(resourceStrings);
                        echoBuilder.Append(resourceObject);
                    }
                }
            }

            // combine all the source files into a single string, delimited with ///#SOURCE comments so we can track
            // back to the original files
            var inputBuilder = new StringBuilder();
            if (crunchGroup.Count == 0)
            {
                // coming from stdin
                var sourceCode = ReadInputFile(string.Empty, null, ref sourceLength);
                inputBuilder.AppendLine("///#SOURCE 1 1 stdin");
                inputBuilder.Append(sourceCode);

                // if we are echoing the input, add it to the echo builder, but without the comment
                if (echoBuilder != null)
                {
                    echoBuilder.Append(sourceCode);
                }
            }
            else
            {
                for (var ndx = 0; ndx < crunchGroup.Count; ++ndx)
                {
                    var sourceCode = ReadInputFile(
                        crunchGroup[ndx].Path, 
                        crunchGroup[ndx].EncodingName ?? switchParser.EncodingInputName, 
                        ref sourceLength);

                    inputBuilder.Append("///#SOURCE 1 1 ");
                    inputBuilder.AppendLine(crunchGroup[ndx].Path);
                    inputBuilder.Append(sourceCode);

                    // if we are echoing the input, add it to the echo builder, but without the comment
                    if (echoBuilder != null)
                    {
                        echoBuilder.Append(sourceCode);
                    }
                }
            }

            var combinedSourceCode = inputBuilder.ToString();

            // if the crunch group has any resource strings objects, we need to add them to the back
            // of the settings list
            var hasCrunchSpecificResources = crunchGroup.ResourceStrings != null && crunchGroup.ResourceStrings.Count > 0;

            // create a string builder we'll dump our output into
            StringBuilder outputBuilder = new StringBuilder();

            switch (crunchGroup.InputType)
            {
                case InputType.Stylesheet:
                    if (hasCrunchSpecificResources)
                    {
                        // add to the CSS list
                        foreach (var resourceStrings in crunchGroup.ResourceStrings)
                        {
                            switchParser.CssSettings.AddResourceStrings(resourceStrings);
                        }
                    }

                    retVal = ProcessCssFile(combinedSourceCode, switchParser, outputBuilder);
                    break;

                case InputType.JavaScript:
                    if (hasCrunchSpecificResources)
                    {
                        // add to the JS list
                        foreach (var resourceStrings in crunchGroup.ResourceStrings)
                        {
                            switchParser.JSSettings.AddResourceStrings(resourceStrings);
                        }
                    }

                    try
                    {
                        if (m_switchParser.JSSettings.PreprocessOnly)
                        {
                            // pre-process the input
                            retVal = PreprocessJSFile(combinedSourceCode, switchParser, outputBuilder);
                        }
                        else if (m_echoInput)
                        {
                            retVal = ProcessJSFileEcho(combinedSourceCode, switchParser, outputBuilder);
                        }
                        else
                        {
                            retVal = ProcessJSFile(combinedSourceCode, switchParser, outputBuilder);
                        }
                    }
                    catch (JScriptException e)
                    {
                        retVal = 1;
                        System.Diagnostics.Debug.WriteLine(e.ToString());
                        WriteError("JS{0}".FormatInvariant((int)e.ErrorCode), e.Message);
                    }

                    break;

                default:
                    throw new UsageException(m_outputMode, AjaxMin.UnknownInputType);
            }

            // if we are pretty-printing, add a newline
            if (switchParser.PrettyPrint)
            {
                outputBuilder.AppendLine();
            }

            string crunchedCode = outputBuilder.ToString();

            // use the crunch-group encoding. If none specified, use the default output encoding.
            // if nothing has been specified, use ASCII if sending to the console (no output file)
            // otherwise UTF-8.
            Encoding encodingOutput = GetOutputEncoding(
                crunchGroup.InputType,
                crunchGroup.Output.EncodingName ?? switchParser.EncodingOutputName
                ?? (string.IsNullOrEmpty(crunchGroup.Output.Path) ? "ASCII" : null));

            // now write the final output file
            if (string.IsNullOrEmpty(crunchGroup.Output.Path))
            {
                // no output file specified - send to STDOUT
                // if the code is empty, don't bother outputting it to the console
                if (!string.IsNullOrEmpty(crunchedCode))
                {
                    // however, for some reason when I set the output encoding it
                    // STILL doesn't call the EncoderFallback to Unicode-escape characters
                    // not supported by the encoding scheme. So instead we need to run the
                    // translation outselves. Still need to set the output encoding, though,
                    // so the translated bytes get displayed properly in the console.
                    byte[] encodedBytes = encodingOutput.GetBytes(crunchedCode);

                    // only output the size analysis if we aren't echoing the input
                    if (!m_echoInput)
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
                            WriteProgress(AjaxMin.SavingsMessage.FormatInvariant(
                                              sourceLength,
                                              encodedBytes.Length,
                                              percentage
                                              ));
                        }
                        else
                        {

                            WriteProgress(AjaxMin.SavingsOutputMessage.FormatInvariant(
                                encodedBytes.Length
                                ));
                        }

                        // calculate how much a simple gzip compression would compress the output
                        long gzipLength = CalculateGzipSize(encodedBytes);

                        // calculate the savings and display the result
                        percentage = Math.Round((1 - ((double)gzipLength) / encodedBytes.Length) * 100, 1);
                        WriteProgress(AjaxMin.SavingsGzipMessage.FormatInvariant(gzipLength, percentage));

                        // blank line after
                        WriteProgress();
                    }

                    // send to console out -- if we even want any output
                    if (!m_noOutput)
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

                        // if we are echoing the input, the get a new stream of bytes
                        if (echoBuilder != null)
                        {
                            encodedBytes = encodingOutput.GetBytes(echoBuilder.ToString());
                        }

                        Console.Out.Write(Console.OutputEncoding.GetChars(encodedBytes));
                    }
                }
            }
            else
            {
                retVal = this.ClobberFileAndExecuteOperation(
                    crunchGroup.Output.Path, (path) =>
                    {
                        // create the output file using the given encoding
                        using (StreamWriter outputStream = new StreamWriter(
                           path,
                           false,
                           encodingOutput
                           ))
                        {
                            if (echoBuilder == null)
                            {
                                outputStream.Write(crunchedCode);
                            }
                            else
                            {
                                // just echo the input
                                outputStream.Write(echoBuilder.ToString());
                            }
                        }

                        // only output the size analysis if there is actually some output to measure
                        // and we're not echoing the input
                        if (File.Exists(path) && !m_echoInput)
                        {
                            // get the size of the resulting file
                            FileInfo crunchedFileInfo = new FileInfo(path);
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
                                    WriteProgress(AjaxMin.SavingsMessage.FormatInvariant(
                                                        sourceLength,
                                                        crunchedLength,
                                                        percentage
                                                        ));
                                }
                                else
                                {

                                    WriteProgress(AjaxMin.SavingsOutputMessage.FormatInvariant(
                                        crunchedLength
                                        ));
                                }

                                // compute how long a simple gzip might compress the resulting file
                                long gzipLength = CalculateGzipSize(File.ReadAllBytes(path));

                                // calculate the percentage of compression and display the results
                                percentage = Math.Round((1 - ((double)gzipLength) / crunchedLength) * 100, 1);
                                WriteProgress(AjaxMin.SavingsGzipMessage.FormatInvariant(gzipLength, percentage));

                                // blank line after
                                WriteProgress();
                            }
                        }
                    });
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

                // if the file doesn't exist, we write.
                // else (it does exist)
                //      determine read-only state
                //      if not readonly and clobber is not noclobber, we write
                //      else if it is readonly and clobber is clobber, we change flag and write
                var doWrite = !File.Exists(filePath);
                if (!doWrite)
                {
                    // file exists. determine read-only status
                    var isReadOnly = (File.GetAttributes(filePath) & FileAttributes.ReadOnly) != 0;
                    if (!isReadOnly && m_clobber != ClobberType.NoClobber)
                    {
                        // file exists, it's not read-only, and we don't have noclobber set.
                        // noclobber will never write over an existing file, but auto will
                        // write over an existing file that doesn't have read-only set.
                        doWrite = true;
                    }
                    else if (isReadOnly && m_clobber == ClobberType.Clobber)
                    {
                        // file exists, it IS read-only, and we want to clobber.
                        // noclobber will never write over an existing file, and auto
                        // won't write over a read-only file. But clobber writes over anything.
                        File.SetAttributes(
                            filePath,
                            (File.GetAttributes(filePath) & ~FileAttributes.ReadOnly));
                        doWrite = true;
                    }
                }

                if (doWrite)
                {
                    operation(filePath);
                }
                else
                {
                    retVal = 1;
                    WriteError("AM-IO", AjaxMin.NoClobberError.FormatInvariant(filePath));
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

        /// <summary>
        /// FileInformation class
        /// </summary>
        private class FileInformation
        {
            public string Path { get; set; }
            public string EncodingName { get; set; }
        }

        /// <summary>
        /// CrunchGroup class
        /// </summary>
        private class CrunchGroup : IEnumerable<FileInformation>
        {
            // the output file for the group. May be empty string.
            public FileInformation Output { get; set; }

            // input type (JavaScript or CSS)
            public InputType InputType { get; set; }

            // optional list of resource string objects specific to this group
            public IList<ResourceStrings> ResourceStrings { get; set; }

            // list of input files -- may not be empty.
            private List<FileInformation> m_sourcePaths;// = null;

            // the count of input files
            public int Count { get { return m_sourcePaths.Count; } }

            // indexer to a grunch group points to a specific input file
            public FileInformation this[int ndx]
            {
                get
                {
                    // return the object (which may throw an index exception itself)
                    return m_sourcePaths[ndx];
                }
            }

            // path to the output symbol map
            public string SymbolMapPath { get; set; }

            // name of the symbol map implementation
            public string SymbolMapName { get; set; }

            // optional crunch group-specific arguments
            public string Arguments { get; set; }

            public CrunchGroup()
            {
                m_sourcePaths = new List<FileInformation>();
            }

            public CrunchGroup(IEnumerable<string> inputFiles, string encodingInputName)
            {
                m_sourcePaths = new List<FileInformation>();

                // add the input file information if there is any
                if (inputFiles != null)
                {
                    foreach (var inputPath in inputFiles)
                    {
                        m_sourcePaths.Add(new FileInformation() { Path = inputPath, EncodingName = encodingInputName });
                    }
                }
            }

            public void Add(string inputPath, string encodingName)
            {
                // add this item to the list
                m_sourcePaths.Add(new FileInformation() { Path = inputPath, EncodingName = encodingName });
            }

            #region IEnumerable<FileInformation> Members

            public IEnumerator<FileInformation> GetEnumerator()
            {
                return m_sourcePaths.GetEnumerator();
            }

            #endregion

            #region IEnumerable Members

            IEnumerator IEnumerable.GetEnumerator()
            {
                return m_sourcePaths.GetEnumerator();
            }

            #endregion
        }

        #endregion

        #region ProcessXmlFile method

        private IList<CrunchGroup> ProcessXmlFile(string xmlPath, string outputFolder)
        {
            // list of crunch groups we're going to create by reading the XML file
            List<CrunchGroup> crunchGroups = new List<CrunchGroup>();
            try
            {
                Configuration.Manifest manifest = null;
                StreamReader fileReader = null;
                try
                {
                    // create the file reader
                    fileReader = new StreamReader(xmlPath);

                    // create the xml reader from the file string using these settings
                    var settings = new XmlReaderSettings()
                    {
                        IgnoreComments = true,
                        IgnoreProcessingInstructions = true,
                        IgnoreWhitespace = true,
                    };
                    using (var reader = XmlReader.Create(fileReader, settings))
                    {
                        fileReader = null;
                        manifest = Configuration.ManifestFactory.Create(reader);
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

                if (manifest != null)
                {
                    // save the XML file's directory name because we'll use it as a root
                    // for all the other paths in the file
                    var rootPath = Path.GetDirectoryName(xmlPath);

                    // save the default arguments (if any)
                    m_defaultArguments = GetConfigArguments(manifest.DefaultArguments);

                    // the output nodes correspond to the crunch groups
                    foreach (var outputNode in manifest.Outputs)
                    {
                        // normalize the output path
                        var outputPath = NormalizePath(outputFolder, rootPath, outputNode.Path);

                        var crunchGroup = new CrunchGroup()
                        {
                            Output = new FileInformation() { Path = outputPath, EncodingName = outputNode.EncodingName },
                            InputType = (InputType)outputNode.CodeType,
                            Arguments = GetConfigArguments(outputNode.Arguments),
                            SymbolMapPath = NormalizePath(outputFolder, rootPath, outputNode.SymbolMap.IfNotNull(s => s.Path)),
                            SymbolMapName = outputNode.SymbolMap.IfNotNull(s => s.Name)
                        };

                        // add resources
                        foreach (var resourceNode in outputNode.Resources)
                        {
                            var resourcePath = resourceNode.Path;
                            if (!string.IsNullOrEmpty(resourcePath))
                            {
                                if (!Path.IsPathRooted(resourcePath))
                                {
                                    resourcePath = Path.Combine(rootPath, resourcePath);
                                }

                                // make sure the resource file actually exists! It's an error if it doesn't.
                                if (!File.Exists(resourcePath))
                                {
                                    throw new XmlException(AjaxMin.XmlResourceNotExist.FormatInvariant(resourceNode.Path));
                                }

                                var resourceStrings = ProcessResources(resourcePath);
                                resourceStrings.Name = resourceNode.Name.IfNullOrWhiteSpace(c_defaultResourceObjectName);

                                if (crunchGroup.ResourceStrings == null)
                                {
                                    crunchGroup.ResourceStrings = new List<ResourceStrings>();
                                }

                                crunchGroup.ResourceStrings.Add(resourceStrings);
                            }
                            else
                            {
                                throw new XmlException(AjaxMin.ResourceNoPathAttr);
                            }
                        }

                        // add inputs
                        foreach (var inputNode in outputNode.Inputs)
                        {
                            var inputPath = inputNode.Path;
                            if (!string.IsNullOrEmpty(inputPath))
                            {
                                // if it's a relative path...
                                if (!Path.IsPathRooted(inputPath))
                                {
                                    // make it relative from the XML file
                                    inputPath = Path.Combine(rootPath, inputPath);
                                }

                                // make sure the input file actually exists! It's an error if it doesn't.
                                if (!File.Exists(inputPath))
                                {
                                    throw new XmlException(AjaxMin.XmlInputNotExist.FormatInvariant(inputNode.Path));
                                }

                                // if we don't know the type yet, let's see if the extension gives us a hint
                                if (crunchGroup.InputType == InputType.Unknown)
                                {
                                    switch (Path.GetExtension(inputPath).ToUpperInvariant())
                                    {
                                        case ".JS":
                                            crunchGroup.InputType = InputType.JavaScript;
                                            break;

                                        case ".CSS":
                                            crunchGroup.InputType = InputType.Stylesheet;
                                            break;
                                    }
                                }

                                // add the input file and its encoding (if any) to the group
                                crunchGroup.Add(inputPath, inputNode.EncodingName);
                            }
                            else
                            {
                                // no required path attribute on the <input> element
                                throw new XmlException(AjaxMin.InputNoPathAttr);
                            }
                        }

                        // add it.
                        crunchGroups.Add(crunchGroup);
                    }
                }
            }
            catch (XmlException e)
            {
                // throw an error indicating the XML error
                System.Diagnostics.Debug.WriteLine(e.ToString());
                throw new UsageException(ConsoleOutputMode.Console, AjaxMin.InputXmlError.FormatInvariant(e.Message));
            }

            // return the list of CrunchGroup objects
            return crunchGroups;
        }

        private static string NormalizePath(string outputFolder, string rootPath, string path)
        {
            // if we have a value and it's a relative path...
            if (!string.IsNullOrEmpty(path) && !Path.IsPathRooted(path))
            {
                if (string.IsNullOrEmpty(outputFolder))
                {
                    // make it relative to the XML file
                    path = Path.Combine(rootPath, path);
                }
                else
                {
                    // make it relative to the output folder
                    path = Path.Combine(outputFolder, path);
                }
            }

            return path;
        }

        private string GetConfigArguments(IDictionary<string, string> configArguments)
        {
            // try getting the current configuration
            string arguments;
            if (!configArguments.TryGetValue(m_configuration ?? string.Empty, out arguments))
            {
                // if we didn't already try getting the current configuration...
                if (!string.IsNullOrEmpty(m_configuration))
                {
                    // try the default (empty configuration)
                    configArguments.TryGetValue(m_configuration, out arguments);
                }
            }

            // make sure we don't return null
            return arguments ?? string.Empty;
        }

        #endregion

        #region resource processing

        private ResourceStrings ProcessResourceFile(string resourceFileName)
        {
            WriteProgress(
                AjaxMin.ReadingResourceFile.FormatInvariant(Path.GetFileName(resourceFileName))
                );

            // which method we call to process the resources depends on the file extension
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
                    throw new UsageException(m_outputMode, AjaxMin.ResourceArgInvalidType);
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

            return "{0}: {1}{2} {3}: {4}".FormatInvariant(
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
            sb.AppendLine("{0} (version {1})".FormatInvariant(string.IsNullOrEmpty(product) ? assemblyName.Name : product, assemblyName.Version));
            if (!string.IsNullOrEmpty(description)) { sb.AppendLine(description); }
            if (!string.IsNullOrEmpty(copyright)) { sb.AppendLine(copyright); }
            return sb.ToString();
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Usage", "CA2202:Do not dispose objects multiple times", Justification="incorrect; gzipstream constructor does not close outer stream when third parameter is true")]
        private static long CalculateGzipSize(byte[] bytes)
        {
            using(var memoryStream = new MemoryStream())
            {
                // the third parameter tells the GZIP stream to leave the base stream open so it doesn't
                // dispose of it when it gets disposed. This is needed because we need to dispose the 
                // GZIP stream before it will write ANY of its data.
                using (var gzipStream = new GZipStream(memoryStream, CompressionMode.Compress, true))
                {
                    gzipStream.Write(bytes, 0, bytes.Length);
                }

                return memoryStream.Position;
            }
        }

        #endregion

        #region usage exception

#if !SILVERLIGHT
        [Serializable]
#endif
        private sealed class UsageException : Exception
        {
            public ConsoleOutputMode OutputMode { get; private set; }

            public UsageException(ConsoleOutputMode outputMode)
                : base(string.Empty)
            {
                OutputMode = outputMode;
            }

            public UsageException(ConsoleOutputMode outputMode, string msg)
                : base(msg)
            {
                OutputMode = outputMode;
            }

#if !SILVERLIGHT
            private UsageException(SerializationInfo info, StreamingContext context)
                : base(info, context)
            {
                if (info == null)
                {
                    throw new ArgumentException(AjaxMin.InternalCompilerError);
                }
                OutputMode = ConsoleOutputMode.Console;
            }
#endif
            public UsageException()
            {
                OutputMode = ConsoleOutputMode.Console;
            }
        }

        #endregion
    }

    #region custom enumeration

    /// <summary>
    /// Method of outputting information
    /// </summary>
    internal enum ConsoleOutputMode
    {
        Silent,
        Console
    }

    internal enum InputType
    {
        Unknown = 0,
        JavaScript,
        Stylesheet,
        Mix,
    }

    #endregion
}