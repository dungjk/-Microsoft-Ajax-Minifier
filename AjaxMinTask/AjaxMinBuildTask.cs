// AjaxMinBuildTask.cs
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
using System.Resources;
using System.Security;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using Microsoft.Ajax.Utilities;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

namespace Microsoft.Ajax.Minifier.Tasks
{
    /// <summary>
    /// Provides the MS Build task for Microsoft Ajax Minifier. Please see the list of supported properties below.
    /// </summary>
    [SecurityCritical]
    public class AjaxMin : Task
    {
        #region private fields

        /// <summary>
        /// AjaxMin Minifier
        /// </summary>
        private readonly Utilities.Minifier m_minifier = new Utilities.Minifier();

        /// <summary>
        /// AjaxMin command-line switch parser
        /// </summary>
        private SwitchParser m_switchParser;

        /// <summary>
        /// An optional file mapping the source and destination files.
        /// </summary>
        private string m_symbolsMapFile;

        #endregion

        #region command-line style switches

        /// <summary>
        /// EXE-style command-line switch string for initializing the CSS and/or JS settings at one time
        /// </summary>
        public string Switches 
        {
            get
            {
                // return an empty string instead of null
                return m_switches ?? string.Empty;
            }
            set
            {
                // parse the switches
                m_switchParser.Parse(value);

                // just so we can grab them later, let's keep track of all the switches we are passed
                if (m_switches == null)
                {
                    // this is the first set of switches
                    m_switches = value;
                }
                else
                {
                    // just appends this set to the previous set
                    m_switches += value;
                }
            }
        }
        private string m_switches = null;

        private void OnUnknownParameter(object sender, UnknownParameterEventArgs ea)
        {
            // we only care about rename, res, and r -- ignore all other switches.
            switch (ea.SwitchPart)
            {
                case "RENAME":
                    // only care if the parameter part is null
                    if (ea.ParameterPart == null)
                    {
                        // needs to be another parameter still left
                        if (ea.Index < ea.Arguments.Count - 1)
                        {
                            // the renaming file is specified as the NEXT argument
                            using (var reader = new StreamReader(ea.Arguments[++ea.Index]))
                            {
                                // read the XML file and parse it into the parameters
                                m_switchParser.ParseRenamingXml(reader.ReadToEnd());
                            }
                        }
                    }
                    break;

                case "RES":
                case "R":
                    // needs to be another parameter still left
                    if (ea.Index < ea.Arguments.Count - 1)
                    {
                        // the resource file path is specified as the NEXT argument
                        var resourceFile = ea.Arguments[++ea.Index];

                        var isValid = true;
                        var objectName = ea.ParameterPart;
                        if (string.IsNullOrEmpty(objectName))
                        {
                            // use the default object name
                            objectName = "Strings";
                        }
                        else
                        {
                            // the parameter part needs to be in the pattern of IDENT[.IDENT]*
                            var parts = objectName.Split('.');

                            // assume it's okay unless we prove otherwise
                            foreach (var part in parts)
                            {
                                if (!JSScanner.IsValidIdentifier(part))
                                {
                                    isValid = false;
                                    break;
                                }
                            }
                        }

                        if (isValid)
                        {
                            ResourceStrings resourceStrings = null;

                            // process the appropriate resource type
                            switch(Path.GetExtension(resourceFile).ToUpperInvariant())
                            {
                                case "RESX":
                                    using (var reader = new ResXResourceReader(resourceFile))
                                    {
                                        // create an object out of the dictionary
                                        resourceStrings = new ResourceStrings(reader.GetEnumerator());
                                    }
                                    break;

                                case "RESOURCES":
                                    using (var reader = new ResourceReader(resourceFile))
                                    {
                                        // create an object out of the dictionary
                                        resourceStrings = new ResourceStrings(reader.GetEnumerator());
                                    }
                                    break;

                                default:
                                    // ignore all other extensions
                                    break;
                            }

                            // add it to the settings objects
                            if (resourceStrings != null)
                            {
                                // set the object name
                                resourceStrings.Name = objectName;

                                // and add it to the parsers
                                m_switchParser.JSSettings.AddResourceStrings(resourceStrings);
                                m_switchParser.CssSettings.AddResourceStrings(resourceStrings);
                            }
                        }
                    }
                    break;

                case "MAP":
                    if (ea.Index < ea.Arguments.Count - 1)
                    {
                        m_symbolsMapFile = ea.Arguments[++ea.Index];
                    }
                    break;
            }
        }

        #endregion

        #region Common properties

        /// <summary>
        /// Warning level threshold for reporting errors. Defalut valus is 0 (syntax/run-time errors)
        /// </summary>
        public int WarningLevel 
        { 
            get
            {
                return m_switchParser.WarningLevel;
            }
            set
            {
                m_switchParser.WarningLevel = value;
            }
        }

        /// <summary>
        /// Whether to treat AjaxMin warnings as build errors (true) or not (false). Default value is false.
        /// </summary>
        public bool TreatWarningsAsErrors { get; set; }

        /// <summary>
        /// Whether to attempt to over-write read-only files (default is false)
        /// </summary>
        public bool Clobber { get; set; }

        /// <summary>
        /// <see cref="CodeSettings.IgnoreErrorList"/> for more information.
        /// </summary>
        public string IgnoreErrorList
        {
            get 
            { 
                // there are technically separate lists for JS and CSS, but we'll set them
                // to the same value, so just use the JS list as the reference here.
                return this.m_switchParser.JSSettings.IgnoreErrorList; 
            }
            set 
            { 
                // there are technically separate lists for JS and CSS, but we'll just set them
                // to the same values.
                this.m_switchParser.JSSettings.IgnoreErrorList = value;
                this.m_switchParser.CssSettings.IgnoreErrorList = value;
            }
        }

        #endregion

        #region JavaScript parameters

        /// <summary>
        /// JavaScript source files to minify.
        /// </summary>
        public ITaskItem[] JsSourceFiles { get; set; }

        /// <summary>
        /// Target extension for individually-minified JS files.
        /// Must use wih JsSourceExtensionPattern; cannot be used with JsCombinedFileName.
        /// </summary>
        public string JsTargetExtension { get; set; }

        /// <summary>
        /// Source extension pattern for individually-minified JS files.
        /// Must use wih JsTargetExtension; cannot be used with JsCombinedFileName.
        /// </summary>
        public string JsSourceExtensionPattern { get; set; }

        /// <summary>
        /// Combine and minify all source files to this name.
        /// Cannot be used with JsTargetExtension/JsSourceExtensionPattern.
        /// </summary>
        public string JsCombinedFileName { get; set; }

        /// <summary>
        /// Ensures the final semicolon in minified JS file.
        /// </summary>
        public bool JsEnsureFinalSemicolon 
        {
            get { return this.m_switchParser.JSSettings.TermSemicolons; }
            set { this.m_switchParser.JSSettings.TermSemicolons = value; }
        }

        /// <summary>
        /// <see cref="CodeSettings.CollapseToLiteral"/> for more information.
        /// </summary>
        public bool JsCollapseToLiteral
        {
            get { return this.m_switchParser.JSSettings.CollapseToLiteral;  }
            set { this.m_switchParser.JSSettings.CollapseToLiteral = value; }
        }
        
        /// <summary>
        /// <see cref="CodeSettings.EvalTreatment"/> for more information.
        /// </summary>
        public string JsEvalTreatment
        {
            get { return this.m_switchParser.JSSettings.EvalTreatment.ToString(); }
            set { this.m_switchParser.JSSettings.EvalTreatment = ParseEnumValue<EvalTreatment>(value); }
        }
        
        /// <summary>
        /// <see cref="CodeSettings.IndentSize"/> for more information.
        /// </summary>
        public int JsIndentSize
        {
            get { return this.m_switchParser.JSSettings.IndentSize; }
            set { this.m_switchParser.JSSettings.IndentSize = value; }
        }
        
        /// <summary>
        /// <see cref="CodeSettings.InlineSafeStrings"/> for more information.
        /// </summary>
        public bool JsInlineSafeStrings
        {
            get { return this.m_switchParser.JSSettings.InlineSafeStrings; }
            set { this.m_switchParser.JSSettings.InlineSafeStrings = value; }
        }
        
        /// <summary>
        /// <see cref="CodeSettings.LocalRenaming"/> for more information.
        /// </summary>
        public string JsLocalRenaming
        {
            get { return this.m_switchParser.JSSettings.LocalRenaming.ToString(); }
            set { this.m_switchParser.JSSettings.LocalRenaming = ParseEnumValue<LocalRenaming>(value); }
        }

        /// <summary>
        /// <see cref="CodeSettings.AddRenamePairs"/> for more information.
        /// </summary>
        public string JsManualRenamePairs
        {
            get { return this.m_switchParser.JSSettings.RenamePairs; }
            set { this.m_switchParser.JSSettings.RenamePairs = value; }
        }

        /// <summary>
        /// <see cref="CodeSettings.SetNoAutoRename"/> for more information.
        /// </summary>
        public string JsNoAutoRename
        {
            get { return this.m_switchParser.JSSettings.NoAutoRenameList; }
            set { this.m_switchParser.JSSettings.NoAutoRenameList = value; }
        }

        /// <summary>
        /// <see cref="CodeSettings.SetKnownGlobalNames"/> for more information.
        /// </summary>
        public string JsKnownGlobalNames
        {
            get { return this.m_switchParser.JSSettings.KnownGlobalNamesList; }
            set { this.m_switchParser.JSSettings.KnownGlobalNamesList = value; }
        }

        /// <summary>
        /// <see cref="CodeSettings.SetKnownGlobalNames"/> for more information.
        /// </summary>
        public string JsDebugLookups
        {
            get { return this.m_switchParser.JSSettings.DebugLookupList; }
            set { this.m_switchParser.JSSettings.DebugLookupList = value; }
        }
        
        /// <summary>
        /// <see cref="CodeSettings.MacSafariQuirks"/> for more information.
        /// </summary>
        public bool JsMacSafariQuirks
        {
            get { return this.m_switchParser.JSSettings.MacSafariQuirks; }
            set { this.m_switchParser.JSSettings.MacSafariQuirks = value; }
        }

        /// <summary>
        /// <see cref="CodeSettings.IgnoreConditionalCompilation"/> for more information.
        /// </summary>
        public bool JsIgnoreConditionalCompilation
        {
            get { return this.m_switchParser.JSSettings.IgnoreConditionalCompilation; }
            set { this.m_switchParser.JSSettings.IgnoreConditionalCompilation = value; }
        }

        /// <summary>
        /// <see cref="CodeSettings.MinifyCode"/> for more information.
        /// </summary>
        public bool JsMinifyCode
        {
            get { return this.m_switchParser.JSSettings.MinifyCode; }
            set { this.m_switchParser.JSSettings.MinifyCode = value; }
        }

        /// <summary>
        /// <see cref="CodeSettings.OutputMode"/> for more information.
        /// </summary>
        public string JsOutputMode
        {
            get { return this.m_switchParser.JSSettings.OutputMode.ToString(); }
            set { this.m_switchParser.JSSettings.OutputMode = ParseEnumValue<OutputMode>(value); }
        }

        /// <summary>
        /// <see cref="CodeSettings.PreserveFunctionNames"/> for more information.
        /// </summary>
        public bool JsPreserveFunctionNames
        {
            get { return this.m_switchParser.JSSettings.PreserveFunctionNames; }
            set { this.m_switchParser.JSSettings.PreserveFunctionNames = value; }
        }

        /// <summary>
        /// <see cref="CodeSettings.RemoveFunctionExpressionNames"/> for more information.
        /// </summary>
        public bool JsRemoveFunctionExpressionNames
        {
            get { return this.m_switchParser.JSSettings.RemoveFunctionExpressionNames; }
            set { this.m_switchParser.JSSettings.RemoveFunctionExpressionNames = value; }
        }
        
        /// <summary>
        /// <see cref="CodeSettings.RemoveUnneededCode"/> for more information.
        /// </summary>
        public bool JsRemoveUnneededCode
        {
            get { return this.m_switchParser.JSSettings.RemoveUnneededCode; }
            set { this.m_switchParser.JSSettings.RemoveUnneededCode = value; }
        }
        
        /// <summary>
        /// <see cref="CodeSettings.StripDebugStatements"/> for more information.
        /// </summary>
        public bool JsStripDebugStatements
        {
            get { return this.m_switchParser.JSSettings.StripDebugStatements; }
            set { this.m_switchParser.JSSettings.StripDebugStatements = value; }
        }

        /// <summary>
        /// <see cref="CodeSettings.AllowEmbeddedAspNetBlocks"/> for more information.
        /// </summary>
        public bool JsAllowEmbeddedAspNetBlocks
        {
            get { return this.m_switchParser.JSSettings.AllowEmbeddedAspNetBlocks; }
            set { this.m_switchParser.JSSettings.AllowEmbeddedAspNetBlocks = value; }
        }

        /// <summary>
        /// <see cref="CodeSettings.PreprocessorDefineList"/> for more information.
        /// </summary>
        public string JsPreprocessorDefines
        {
            get { return this.m_switchParser.JSSettings.PreprocessorDefineList; }
            set { this.m_switchParser.JSSettings.PreprocessorDefineList = value; }
        }

        #endregion

        #region CSS parameters

        /// <summary>
        /// CSS source files to minify.
        /// </summary>
        public ITaskItem[] CssSourceFiles { get; set; }

        /// <summary>
        /// Target extension for minified CSS files.
        /// Cannot use with CssCombinedFileName.
        /// </summary>
        public string CssTargetExtension { get; set; }

        /// <summary>
        /// Source extension pattern for CSS files.
        /// Cannot use with CssCombinedFileName.
        /// </summary>
        public string CssSourceExtensionPattern { get; set; }

        /// <summary>
        /// Combine all source files and minify to this the given file name.
        /// Cannot use with CssTargetExtension/CssSourceExtensionPattern.
        /// </summary>
        public string CssCombinedFileName { get; set; }

        /// <summary>
        /// <see cref="CssSettings.ColorNames"/> for more information.
        /// </summary>
        public string CssColorNames
        {
            get { return this.m_switchParser.CssSettings.ColorNames.ToString(); }
            set { this.m_switchParser.CssSettings.ColorNames = ParseEnumValue<CssColor>(value); }
        }
        
        /// <summary>
        /// <see cref="CssSettings.CommentMode"/> for more information.
        /// </summary>
        public string CssCommentMode
        {
            get { return this.m_switchParser.CssSettings.CommentMode.ToString(); }
            set { this.m_switchParser.CssSettings.CommentMode = ParseEnumValue<CssComment>(value); }
        }
        
        /// <summary>
        /// <see cref="CssSettings.ExpandOutput"/> for more information.
        /// </summary>
        public bool CssExpandOutput
        {
            get { return this.m_switchParser.CssSettings.OutputMode == OutputMode.MultipleLines; }
            set { this.m_switchParser.CssSettings.OutputMode = value ? OutputMode.MultipleLines : OutputMode.SingleLine; }
        }

        /// <summary>
        /// <see cref="CssSettings.IndentSpaces"/> for more information.
        /// </summary>
        public int CssIndentSpaces
        {
            get { return this.m_switchParser.CssSettings.IndentSize; }
            set { this.m_switchParser.CssSettings.IndentSize = value; }
        }
        
        /// <summary>
        /// <see cref="CssSettings.TermSemicolons"/> for more information.
        /// </summary>
        public bool CssTermSemicolons
        {
            get { return this.m_switchParser.CssSettings.TermSemicolons; }
            set { this.m_switchParser.CssSettings.TermSemicolons = value; }
        }

        /// <summary>
        /// <see cref="CssSettings.MinifyExpressions"/> for more information.
        /// </summary>
        public bool CssMinifyExpressions
        {
            get { return this.m_switchParser.CssSettings.MinifyExpressions; }
            set { this.m_switchParser.CssSettings.MinifyExpressions = value; }
        }

        /// <summary>
        /// <see cref="CssSettings.AllowEmbeddedAspNetBlocks"/> for more information.
        /// </summary>
        public bool CssAllowEmbeddedAspNetBlocks
        {
            get { return this.m_switchParser.CssSettings.AllowEmbeddedAspNetBlocks; }
            set { this.m_switchParser.CssSettings.AllowEmbeddedAspNetBlocks = value; }
        }

        #endregion

        /// <summary>
        /// Constructor for <see cref="AjaxMin"/> class. Initializes the default
        /// values for all parameters.
        /// </summary>
        public AjaxMin()
        {
            this.m_switchParser = new SwitchParser();
            this.m_switchParser.UnknownParameter += OnUnknownParameter;
            this.JsEnsureFinalSemicolon = true;
        }

        /// <summary>
        /// Executes the Ajax Minifier build task
        /// </summary>
        /// <returns>True if the build task successfully succeded; otherwise, false.</returns>
        //[SecurityCritical]
        public override bool Execute()
        {
            m_minifier.WarningLevel = this.WarningLevel;

            // Deal with JS minification
            if (this.JsSourceFiles != null && this.JsSourceFiles.Length > 0)
            {
                if (this.JsCombinedFileName.IsNullOrWhiteSpace())
                {
                    // no combined name; the source extension and target extension properties must be set.
                    if (this.JsSourceExtensionPattern.IsNullOrWhiteSpace())
                    {
                        Log.LogError(Strings.RequiredParameterIsEmpty, "JsSourceExtensionPattern");
                        return false;
                    }

                    if (this.JsTargetExtension.IsNullOrWhiteSpace())
                    {
                        Log.LogError(Strings.RequiredParameterIsEmpty, "JsTargetExtension");
                        return false;
                    }
                }
                else
                {
                    // a combined name was specified - must NOT use source/target extension properties
                    if (!this.JsSourceExtensionPattern.IsNullOrWhiteSpace())
                    {
                        Log.LogError(Strings.CannotUseCombinedAndIndividual, "JsSourceExtensionPattern");
                        return false;
                    }

                    if (!this.JsTargetExtension.IsNullOrWhiteSpace())
                    {
                        Log.LogError(Strings.CannotUseCombinedAndIndividual, "JsTargetExtension");
                        return false;
                    }
                }

                if (m_symbolsMapFile != null)
                {
                    if (FileIsWritable(m_symbolsMapFile))
                    {
                        using (XmlWriter writer = XmlWriter.Create(
                            m_symbolsMapFile,
                            new XmlWriterSettings { CloseOutput = true, Indent = true }))
                        {
                            using (m_switchParser.JSSettings.SymbolsMap = new ScriptSharpSourceMap(writer))
                            {
                                MinifyJavaScript();
                            }
                        }
                    }
                    else
                    {
                        // log a WARNING that the symbol map generation was skipped -- don't break the build
                        Log.LogWarning(Strings.MapDestinationIsReadOnly, m_symbolsMapFile);
                        MinifyJavaScript();
                    }
                }
                else
                {
                    // No symbol map. Just minify it.
                    MinifyJavaScript();
                }
            }

            // Deal with CSS minification
            if (this.CssSourceFiles != null && this.CssSourceFiles.Length > 0)
            {
                if (this.CssCombinedFileName.IsNullOrWhiteSpace())
                {
                    if (this.CssSourceExtensionPattern.IsNullOrWhiteSpace())
                    {
                        Log.LogError(Strings.RequiredParameterIsEmpty, "CssSourceExtensionPattern");
                        return false;
                    }

                    if (this.CssTargetExtension.IsNullOrWhiteSpace())
                    {
                        Log.LogError(Strings.RequiredParameterIsEmpty, "CssTargetExtension");
                        return false;
                    }
                }
                else
                {
                    if (!this.CssSourceExtensionPattern.IsNullOrWhiteSpace())
                    {
                        Log.LogError(Strings.CannotUseCombinedAndIndividual, "CssSourceExtensionPattern");
                        return false;
                    }

                    if (!this.CssTargetExtension.IsNullOrWhiteSpace())
                    {
                        Log.LogError(Strings.CannotUseCombinedAndIndividual, "CssTargetExtension");
                        return false;
                    }
                }

                MinifyStyleSheets();
            }

            return !Log.HasLoggedErrors;
        }

        /// <summary>
        /// Minifies JS files provided by the caller of the build task.
        /// </summary>
        private void MinifyJavaScript()
        {
            if (this.JsCombinedFileName.IsNullOrWhiteSpace())
            {
                // individually-minified files
                foreach (ITaskItem item in this.JsSourceFiles)
                {
                    string path = Regex.Replace(item.ItemSpec, this.JsSourceExtensionPattern, this.JsTargetExtension,
                                                RegexOptions.IgnoreCase);

                    if (FileIsWritable(path))
                    {
                        string source = File.ReadAllText(item.ItemSpec);
                        MinifyJavaScript(source, item.ItemSpec, path);
                    }
                    else
                    {
                        // log a WARNING that the minification was skipped -- don't break the build
                        Log.LogWarning(Strings.DestinationIsReadOnly, Path.GetFileName(item.ItemSpec), path);
                    }
                }
            }
            else
            {
                // combine the sources into a single file and minify the results
                if (FileIsWritable(this.JsCombinedFileName))
                {
                    var minifiedJs = MinifyAndConcatenateJavaScript();
                    try
                    {
                        File.WriteAllText(this.JsCombinedFileName, minifiedJs);
                    }
                    catch (UnauthorizedAccessException)
                    {
                        LogFileError(this.JsCombinedFileName, Strings.NoWritePermission, this.JsCombinedFileName);
                    }
                }
                else
                {
                    // log a WARNING that the minification was skipped -- don't break the build
                    Log.LogWarning(Strings.DestinationIsReadOnly, Path.GetFileName(this.JsCombinedFileName), this.JsCombinedFileName);
                }
            }
        }

        /// <summary>
        /// Minifies CSS files provided by the caller of the build task.
        /// </summary>
        private void MinifyStyleSheets()
        {
            if (this.CssCombinedFileName.IsNullOrWhiteSpace())
            {
                // individually-minified files
                foreach (ITaskItem item in this.CssSourceFiles)
                {
                    string path = Regex.Replace(item.ItemSpec, this.CssSourceExtensionPattern, this.CssTargetExtension, RegexOptions.IgnoreCase);
                    if (FileIsWritable(path))
                    {
                        try
                        {
                            string source = File.ReadAllText(item.ItemSpec);
                            MinifyStyleSheet(source, item.ItemSpec, path);
                        }
                        catch (Exception e)
                        {
                            LogFileError(item.ItemSpec, Strings.DidNotMinify, path, e.Message);
                            throw;
                        }
                    }
                    else
                    {
                        // log a WARNING that the minification was skipped -- don't break the build
                        Log.LogWarning(Strings.DestinationIsReadOnly, Path.GetFileName(item.ItemSpec), path);
                    }
                }
            }
            else
            {
                // combine the source files and minify the results to a single file
                if (FileIsWritable(this.CssCombinedFileName))
                {
                    var minifiedResults = MinifyAndConcatenateStyleSheet();
                    try
                    {
                        File.WriteAllText(this.CssCombinedFileName, minifiedResults);
                    }
                    catch (UnauthorizedAccessException)
                    {
                        LogFileError(this.CssCombinedFileName, Strings.NoWritePermission, this.CssCombinedFileName);
                    }
                }
                else
                {
                    // log a WARNING that the minification was skipped -- don't break the build
                    Log.LogWarning(Strings.DestinationIsReadOnly, Path.GetFileName(this.CssCombinedFileName), this.CssCombinedFileName);
                }
            }
        }

        #region methods to minify source code

        /// <summary>
        /// Minify the given source code from the given named source, to the given output path
        /// </summary>
        /// <param name="sourceCode">source code to minify</param>
        /// <param name="sourceName">name of the source</param>
        /// <param name="outputPath">destination path for resulting minified code</param>
        private void MinifyJavaScript(string sourceCode, string sourceName, string outputPath)
        {
            try
            {
                if (m_switchParser.JSSettings.SymbolsMap != null)
                {
                    m_switchParser.JSSettings.SymbolsMap.StartPackage(outputPath);
                }

                this.m_minifier.FileName = sourceName;
                string minifiedJs = this.m_minifier.MinifyJavaScript(sourceCode, this.m_switchParser.JSSettings);
                if (this.m_minifier.ErrorList.Count > 0)
                {
                    foreach (var error in this.m_minifier.ErrorList)
                    {
                        LogContextError(error);
                    }
                }

                try
                {
                    File.WriteAllText(outputPath, minifiedJs);
                }
                catch (UnauthorizedAccessException)
                {
                    LogFileError(sourceName, Strings.NoWritePermission, outputPath);
                }
            }
            catch (Exception e)
            {
                LogFileError(sourceName, Strings.DidNotMinify, outputPath, e.Message);
                throw;
            }
            finally
            {
                if (m_switchParser.JSSettings.SymbolsMap != null)
                {
                    m_switchParser.JSSettings.SymbolsMap.EndPackage();
                }
            }
        }

        private string MinifyAndConcatenateJavaScript()
        {
            var outputBuilder = new StringBuilder();

            // we need to make sure that files 0 through length-1 use the setting
            // value that ensures proper termination of the code so it concatenates
            // properly. So save the setting value, set the value to true, then
            // make sure to restore it for the last file
            var savedSetting = this.m_switchParser.JSSettings.TermSemicolons;
            this.m_switchParser.JSSettings.TermSemicolons = true;

            try
            {
                if (m_switchParser.JSSettings.SymbolsMap != null)
                {
                    m_switchParser.JSSettings.SymbolsMap.StartPackage(this.JsCombinedFileName);
                }

                for(var ndx = 0; ndx < this.JsSourceFiles.Length; ++ndx)
                {
                    var item = this.JsSourceFiles[ndx];
                    try
                    {
                        var sourceCode = File.ReadAllText(item.ItemSpec);

                        this.m_minifier.FileName = item.ItemSpec;

                        // if this is the last file, restore the setting to its original value
                        if (ndx == this.JsSourceFiles.Length - 1)
                        {
                            this.m_switchParser.JSSettings.TermSemicolons = savedSetting;
                        }

                        string minifiedJs = this.m_minifier.MinifyJavaScript(sourceCode, this.m_switchParser.JSSettings);
                        if (this.m_minifier.ErrorList.Count > 0)
                        {
                            foreach (var error in this.m_minifier.ErrorList)
                            {
                                LogContextError(error);
                            }
                        }

                        outputBuilder.Append(minifiedJs);
                        if (this.m_switchParser.JSSettings.OutputMode == OutputMode.MultipleLines)
                        {
                            outputBuilder.AppendLine();
                        }
                    }
                    catch (Exception e)
                    {
                        LogFileError(item.ItemSpec, Strings.DidNotMinify, this.JsCombinedFileName, e.Message);
                        throw;
                    }
                }
            }
            finally
            {
                // make SURE we restore that setting
                this.m_switchParser.JSSettings.TermSemicolons = savedSetting;

                // close the symbol map if we are creating one
                if (m_switchParser.JSSettings.SymbolsMap != null)
                {
                    m_switchParser.JSSettings.SymbolsMap.EndPackage();
                }
            }

            return outputBuilder.ToString();
        }

        /// <summary>
        /// Minify the given CSS source with the given name, to the given output path
        /// </summary>
        /// <param name="sourceCode">CSS source to minify</param>
        /// <param name="sourceName">name of hte source entity</param>
        /// <param name="outputPath">output path for the minified results</param>
        private void MinifyStyleSheet(string sourceCode, string sourceName, string outputPath)
        {
            try
            {
                this.m_minifier.FileName = sourceName;
                string results = this.m_minifier.MinifyStyleSheet(sourceCode, this.m_switchParser.CssSettings);
                if (this.m_minifier.ErrorList.Count > 0)
                {
                    foreach (var error in this.m_minifier.ErrorList)
                    {
                        LogContextError(error);
                    }
                }

                try
                {
                    File.WriteAllText(outputPath, results);
                }
                catch (UnauthorizedAccessException)
                {
                    LogFileError(outputPath, Strings.NoWritePermission, outputPath);
                }
            }
            catch (Exception e)
            {
                LogFileError(sourceName, Strings.DidNotMinify, outputPath, e.Message);
                throw;
            }
        }

        private string MinifyAndConcatenateStyleSheet()
        {
            var outputBuilder = new StringBuilder();

            // minify each input files and send the results to the string builder
            foreach (var item in this.CssSourceFiles)
            {
                try
                {
                    var sourceCode = File.ReadAllText(item.ItemSpec);

                    this.m_minifier.FileName = item.ItemSpec;
                    string results = this.m_minifier.MinifyStyleSheet(sourceCode, this.m_switchParser.CssSettings);
                    if (this.m_minifier.ErrorList.Count > 0)
                    {
                        foreach (var error in this.m_minifier.ErrorList)
                        {
                            LogContextError(error);
                        }
                    }

                    outputBuilder.Append(results);
                    if (this.m_switchParser.CssSettings.OutputMode == OutputMode.MultipleLines)
                    {
                        outputBuilder.AppendLine();
                    }
                }
                catch (Exception e)
                {
                    LogFileError(item.ItemSpec, Strings.DidNotMinify, this.CssCombinedFileName, e.Message);
                    throw;
                }
            }

            return outputBuilder.ToString();
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
            if(TreatWarningsAsErrors || error.IsError)
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

        #region Utility methods

        private bool FileIsWritable(string path)
        {
            // the file is writable if it doesn't exist, or is NOT marked readonly
            var fileInfo = new FileInfo(path);
            var writable = !fileInfo.Exists || !fileInfo.IsReadOnly;

            // BUT, if it exists and isn't writable, check the clobber flag. If we want to clobber
            // the file...
            if (fileInfo.Exists && !writable && Clobber)
            {
                // try resetting the read-only flag
                fileInfo.Attributes &= ~FileAttributes.ReadOnly; 

                // and check again
                writable = !fileInfo.IsReadOnly;
            }

            // return the flag
            return writable;
        }

        /// <summary>
        /// Parses the enum value of the given enum type from the string.
        /// </summary>
        /// <typeparam name="T">Type of the enum.</typeparam>
        /// <param name="strValue">Value of the parameter in the string form.</param>
        /// <returns>Parsed enum value</returns>
        private T ParseEnumValue<T>(string strValue) where T: struct
        {
            if (!strValue.IsNullOrWhiteSpace())
            {
                try
                {
                    return (T)Enum.Parse(typeof(T), strValue, true);
                }
                catch (ArgumentNullException) { }
                catch (ArgumentException) { }
                catch (OverflowException) { }
            }

            // if we cannot parse it for any reason, post the error and stop the task.
            Log.LogError(Strings.InvalidInputParameter, strValue);
            return default(T);
        }

        #endregion
    }
}
