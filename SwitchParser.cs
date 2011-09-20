// SwitchParser.cs
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
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Xml;

namespace Microsoft.Ajax.Utilities
{
    public class InvalidSwitchEventArgs : EventArgs
    {
        public string SwitchPart { get; set; }
        public string ParameterPart { get; set; }
    }

    public class UnknownParameterEventArgs : EventArgs
    {
        public IList<string> Arguments { get; private set; }

        public int Index { get; set; }
        public string SwitchPart { get; set; }
        public string ParameterPart { get; set; }

        public UnknownParameterEventArgs(IList<string> arguments)
        {
            Arguments = arguments;
        }
    }

    public class SwitchParser
    {
        #region properties

        // settings objects that get set up by parsing a command line
        public CodeSettings JSSettings { get; private set; }
        public CssSettings CssSettings { get; private set; }

        // other parameters we will parse off the command line
        public bool AnalyzeMode { get; private set; }
        public bool PrettyPrint { get; private set; }
        public int WarningLevel { get; private set; }
        public string EncodingOutputName { get; private set; }
        public string EncodingInputName { get; private set; }

        #endregion

        #region events

        // events that are fired under different circumstances while parsing the switches
        public event EventHandler<InvalidSwitchEventArgs> InvalidSwitch;
        public event EventHandler<UnknownParameterEventArgs> UnknownParameter;
        public event EventHandler JSOnlyParameter;
        public event EventHandler CssOnlyParameter;

        #endregion

        public SwitchParser()
        {
            // initialize with default values
            JSSettings = new CodeSettings();
            CssSettings = new CssSettings();
        }

        public SwitchParser(CodeSettings jsSettings, CssSettings cssSettings)
        {
            // apply the switches to these two settings objects
            JSSettings = jsSettings;
            CssSettings = cssSettings;
        }

        #region command line to argument array

        public static string[] ToArguments(string commandLine)
        {
            List<string> args = new List<string>();

            if (!string.IsNullOrEmpty(commandLine))
            {
                var length = commandLine.Length;
                for (var ndx = 0; ndx < length; ++ndx)
                {
                    // skip initial spaces
                    while (ndx < length && char.IsWhiteSpace(commandLine[ndx]))
                    {
                        ++ndx;
                    }

                    // don't create it if we don't need it yet
                    StringBuilder sb = null;

                    // if not at the end yet
                    if (ndx < length)
                    {
                        // grab the first character
                        var firstCharacter = commandLine[ndx];

                        // see if starts with a double-quote
                        var inDelimiter = firstCharacter == '"';
                        if (inDelimiter)
                        {
                            // we found a delimiter -- we're going to need one
                            sb = new StringBuilder();
                        }

                        // if it is, start at the NEXT character
                        var start = inDelimiter ? ndx + 1 : ndx;

                        // skip the first character -- we already know it's not whitespace or a delimiter,
                        // so we don't really care what the heck it is at this point.
                        while (++ndx < length)
                        {
                            // get the current character
                            var ch = commandLine[ndx];

                            if (inDelimiter)
                            {
                                // in delimiter mode.
                                // we only care if we found the closing delimiter
                                if (ch == '"')
                                {
                                    // BUT if it's a double double-quote, then treat those two characters as
                                    // a single double-quote
                                    if (ndx + 1 < length && commandLine[ndx + 1] == '"')
                                    {
                                        // add what we have so far (if anything)
                                        if (ndx > start)
                                        {
                                            sb.Append(commandLine.Substring(start, ndx - start));
                                        }

                                        // insert a single double-quote into the string builder
                                        sb.Append('"');
                                        
                                        // skip over the quote and start on the NEXT character
                                        start = ++ndx + 1;
                                    }
                                    else
                                    {
                                        // found it; end delimiter mode
                                        inDelimiter = false;

                                        if (ndx > start)
                                        {
                                            // add what we have so far
                                            sb.Append(commandLine.Substring(start, ndx - start));
                                        }

                                        // start is the NEXT character after the quote
                                        start = ndx + 1;
                                    }
                                }
                            }
                            else
                            {
                                // not in delimiter mode.
                                // if it's a whitespace, stop looping -- we found the end
                                if (char.IsWhiteSpace(ch))
                                {
                                    break;
                                }
                                else if (ch == '"')
                                {
                                    // we found a start delimiter
                                    inDelimiter = true;

                                    // create the string builder now if we haven't already
                                    if (sb == null)
                                    {
                                        sb = new StringBuilder();
                                    }

                                    // add what we have up to the start delimiter into the string builder
                                    // because we're going to have to add this escaped string to it WITHOUT
                                    // the double-quotes
                                    sb.Append(commandLine.Substring(start, ndx - start));

                                    // and start this one at the next character -- not counting the quote
                                    start = ndx + 1;
                                }
                            }
                        }

                        // we now have the start end end of the argument
                        // if the start and end character are the same delimiter characters, trim them off
                        // otherwise just use what's between them
                        if (sb != null)
                        {
                            // add what we have left (if any)
                            if (ndx > start)
                            {
                                sb.Append(commandLine.Substring(start, ndx - start));
                            }

                            // and send the whole shebang to the list
                            args.Add(sb.ToString());
                        }
                        else
                        {
                            // no double-quotes encountered, so just pull the substring
                            // directly from the command line
                            args.Add(commandLine.Substring(start, ndx - start));
                        }
                    }
                }
            }

            return args.ToArray();
        }

        #endregion

        #region Parse command line

        /// <summary>
        /// Takes a full command-line string and parses the switches into the appropriate settings objects
        /// </summary>
        /// <param name="commandLine"></param>
        public void Parse(string commandLine)
        {
            // no command line, then nothing to parse
            if (!string.IsNullOrEmpty(commandLine))
            {
                // convert the command line to an argument list and pass it
                // to the appropriate override
                Parse(ToArguments(commandLine));
            }
        }

        #endregion

        #region parse arguments

        /// <summary>
        /// Takes an array of arguments and parses the switches into the appropriate settings objects
        /// </summary>
        /// <param name="args"></param>
        public void Parse(string[] args)
        {
            if (args != null)
            {
                // these lists will only be created if needed
                List<string> defines = null;
                List<string> debugLookups = null;
                List<string> globals = null;
                List<string> ignoreErrors = null;
                List<string> noAutoRename = null;
                Dictionary<string, string> renameMap = null;

                var levelSpecified = false;
                var renamingSpecified = false;
                bool parameterFlag;
                for (var ndx = 0; ndx < args.Length; ++ndx)
                {
                    // parameter switch
                    var thisArg = args[ndx];
                    if (thisArg.Length > 1
                      && (thisArg.StartsWith("-", StringComparison.Ordinal) // this is a normal hyphen (minus character)
                      || thisArg.StartsWith("–", StringComparison.Ordinal) // this character is what Word will convert a hyphen to
                      || thisArg.StartsWith("/", StringComparison.Ordinal)))
                    {
                        // general switch syntax is -switch:param
                        var parts = thisArg.Substring(1).Split(':');
                        var switchPart = parts[0].ToUpper(CultureInfo.InvariantCulture);
                        var paramPart = parts.Length == 1 ? null : parts[1];
                        var paramPartUpper = paramPart == null ? null : paramPart.ToUpper(CultureInfo.InvariantCulture);

                        // switch off the switch part
                        switch (switchPart)
                        {
                            case "ANALYZE":
                            case "A": // <-- old-style
                                // ignore any arguments
                                AnalyzeMode = true;

                                // if analyze was specified but no warning level, jack up the warning level
                                // so everything is shown
                                if (!levelSpecified)
                                {
                                    // we want to analyze, and we didn't specify a particular warning level.
                                    // go ahead and report all errors
                                    WarningLevel = int.MaxValue;
                                }

                                break;

                            case "ASPNET":
                                if (BooleanSwitch(paramPartUpper, false, out parameterFlag))
                                {
                                    // same setting for both CSS and JS
                                    JSSettings.AllowEmbeddedAspNetBlocks =
                                        CssSettings.AllowEmbeddedAspNetBlocks = parameterFlag;
                                }
                                else
                                {
                                    OnInvalidSwitch(switchPart, paramPart);
                                }
                                break;

                            case "CC":
                                if (BooleanSwitch(paramPartUpper, true, out parameterFlag))
                                {
                                    // actually, the flag is the opposite of the member -- turn CC ON and we DON'T
                                    // want to ignore them; turn CC OFF and we DO want to ignore them
                                    JSSettings.IgnoreConditionalCompilation = !parameterFlag;
                                }
                                else
                                {
                                    OnInvalidSwitch(switchPart, paramPart);
                                }
                                OnJSOnlyParameter();
                                break;

                            case "COLORS":
                                // two options: hex or names
                                if (paramPartUpper == "HEX")
                                {
                                    CssSettings.ColorNames = CssColor.Hex;
                                }
                                else if (paramPartUpper == "STRICT")
                                {
                                    CssSettings.ColorNames = CssColor.Strict;
                                }
                                else if (paramPartUpper == "MAJOR")
                                {
                                    CssSettings.ColorNames = CssColor.Major;
                                }
                                else
                                {
                                    OnInvalidSwitch(switchPart, paramPart);
                                }
                                OnCssOnlyParameter();
                                break;

                            case "COMMENTS":
                                // four options for css: none, all, important, or hacks
                                // two options for js: none, important
                                // (default is important)
                                if (paramPartUpper == "NONE")
                                {
                                    CssSettings.CommentMode = CssComment.None;
                                    JSSettings.PreserveImportantComments = false;
                                }
                                else if (paramPartUpper == "ALL")
                                {
                                    CssSettings.CommentMode = CssComment.All;
                                    OnCssOnlyParameter();
                                }
                                else if (paramPartUpper == "IMPORTANT")
                                {
                                    CssSettings.CommentMode = CssComment.Important;
                                    JSSettings.PreserveImportantComments = true;
                                }
                                else if (paramPartUpper == "HACKS")
                                {
                                    CssSettings.CommentMode = CssComment.Hacks;
                                    OnCssOnlyParameter();
                                }
                                else
                                {
                                    OnInvalidSwitch(switchPart, paramPart);
                                }

                                break;

                            case "CSS":
                                OnCssOnlyParameter();
                                break;

                            case "DEBUG":
                                // see if the param part is a comma-delimited list
                                if (paramPartUpper != null && paramPartUpper.IndexOf(',') >= 0)
                                {
                                    // we have a comma-separated list.
                                    // the first item is the flag (if any), and the rest (if any) are the "debug" lookup names
                                    var items = paramPart.Split(',');

                                    // use the first value as the debug boolean switch
                                    if (BooleanSwitch(items[0], true, out parameterFlag))
                                    {
                                        // actually the inverse - a TRUE on the -debug switch means we DON'T want to
                                        // strip debug statements, and a FALSE means we DO want to strip them
                                        JSSettings.StripDebugStatements = !parameterFlag;
                                    }
                                    else
                                    {
                                        OnInvalidSwitch(switchPart, paramPart);
                                    }

                                    // and the rest as names.
                                    if (debugLookups == null)
                                    {
                                        debugLookups = new List<string>();
                                    }

                                    // start with index 1, since index 0 was the flag
                                    for (var item = 1; item < items.Length; ++item)
                                    {
                                        // get the identifier that was specified
                                        var identifier = items[item];

                                        // a blank identifier is okay -- we just ignore it
                                        if (!string.IsNullOrEmpty(identifier))
                                        {
                                            // but if it's not blank, it better be a valid JavaScript identifier or member chain
                                            var isValid = true;
                                            if (identifier.IndexOf('.') > 0)
                                            {
                                                // it's a member chain -- check that each part is a valid JS identifier
                                                var names = identifier.Split('.');
                                                foreach (var name in names)
                                                {
                                                    if (!JSScanner.IsValidIdentifier(name))
                                                    {
                                                        OnInvalidSwitch(switchPart, name);
                                                        isValid = false;
                                                    }
                                                }
                                            }
                                            else
                                            {
                                                // no dot -- just an identifier
                                                if (!JSScanner.IsValidIdentifier(identifier))
                                                {
                                                    OnInvalidSwitch(switchPart, identifier);
                                                    isValid = false;
                                                }
                                            }

                                            // don't add duplicates or invalid identifiers
                                            if (isValid && !debugLookups.Contains(identifier))
                                            {
                                                debugLookups.Add(identifier);
                                            }
                                        }
                                    }
                                }
                                else if (BooleanSwitch(paramPartUpper, true, out parameterFlag))
                                {
                                    // no commas -- just use the entire param part as the boolean value.
                                    // just putting the debug switch on the command line without any arguments
                                    // is the same as putting -debug:true and perfectly valid.

                                    // actually the inverse - a TRUE on the -debug switch means we DON'T want to
                                    // strip debug statements, and a FALSE means we DO want to strip them
                                    JSSettings.StripDebugStatements = !parameterFlag;
                                }

                                // this is a JS-only switch
                                OnJSOnlyParameter();
                                break;

                            case "DEFINE":
                                // the parts can be a comma-separate list of identifiers
                                if (string.IsNullOrEmpty(paramPartUpper))
                                {
                                    OnInvalidSwitch(switchPart, paramPart);
                                }
                                else
                                {
                                    // use paramPart because it has been forced to upper-case and these identifiers are
                                    // supposed to be case-insensitive
                                    foreach (string upperCaseName in paramPartUpper.Split(','))
                                    {
                                        // better be a valid JavaScript identifier
                                        if (!JSScanner.IsValidIdentifier(upperCaseName))
                                        {
                                            OnInvalidSwitch(switchPart, upperCaseName);
                                        }
                                        else if (defines == null)
                                        {
                                            // if we haven't created the list yet, do it now
                                            defines = new List<string>();
                                            defines.Add(upperCaseName);
                                        }
                                        else if (!defines.Contains(upperCaseName))
                                        {
                                            // don't add duplicates
                                            defines.Add(upperCaseName);
                                        }
                                    }
                                }

                                break;

                            case "ENC":
                                // the encoding is the next argument
                                if (ndx >= args.Length - 1)
                                {
                                    // must be followed by an encoding
                                    OnInvalidSwitch(switchPart, paramPart);
                                }
                                else
                                {
                                    string encoding = args[++ndx];

                                    // whether this is an in or an out encoding
                                    if (paramPartUpper == "IN")
                                    {
                                        // save the name -- we'll create the encoding later because we may
                                        // override it on a file-by-file basis in an XML file
                                        EncodingInputName = encoding;
                                    }
                                    else if (paramPartUpper == "OUT")
                                    {
                                        // just save the name -- we'll create the encoding later because we need
                                        // to know whether we are JS or CSS to pick the right encoding fallback
                                        EncodingOutputName = encoding;
                                    }
                                    else
                                    {
                                        OnInvalidSwitch(switchPart, paramPart);
                                    }
                                }
                                break;

                            case "EVALS":
                                // three options: ignore, make immediate scope safe, or make all scopes safe
                                if (paramPartUpper == "IGNORE")
                                {
                                    JSSettings.EvalTreatment = EvalTreatment.Ignore;
                                }
                                else if (paramPartUpper == "IMMEDIATE")
                                {
                                    JSSettings.EvalTreatment = EvalTreatment.MakeImmediateSafe;
                                }
                                else if (paramPartUpper == "SAFEALL")
                                {
                                    JSSettings.EvalTreatment = EvalTreatment.MakeAllSafe;
                                }
                                else
                                {
                                    OnInvalidSwitch(switchPart, paramPart);
                                }

                                // this is a JS-only switch
                                OnJSOnlyParameter();
                                break;

                            case "EXPR":
                                // two options: minify (default) or raw
                                if (paramPartUpper == "MINIFY")
                                {
                                    CssSettings.MinifyExpressions = true;
                                }
                                else if (paramPartUpper == "RAW")
                                {
                                    CssSettings.MinifyExpressions = false;
                                }
                                else
                                {
                                    OnInvalidSwitch(switchPart, paramPart);
                                }

                                OnCssOnlyParameter();
                                break;

                            case "FNAMES":
                                // three options: 
                                // LOCK    -> keep all NFE names, don't allow renaming of function names
                                // KEEP    -> keep all NFE names, but allow function names to be renamed
                                // ONLYREF -> remove unref'd NFE names, allow function named to be renamed (DEFAULT)
                                if (paramPartUpper == "LOCK")
                                {
                                    // don't remove function expression names
                                    JSSettings.RemoveFunctionExpressionNames = false;

                                    // and preserve the names (don't allow renaming)
                                    JSSettings.PreserveFunctionNames = true;
                                }
                                else if (paramPartUpper == "KEEP")
                                {
                                    // don't remove function expression names
                                    JSSettings.RemoveFunctionExpressionNames = false;

                                    // but it's okay to rename them
                                    JSSettings.PreserveFunctionNames = false;
                                }
                                else if (paramPartUpper == "ONLYREF")
                                {
                                    // remove function expression names if they aren't referenced
                                    JSSettings.RemoveFunctionExpressionNames = true;

                                    // and rename them if we so desire
                                    JSSettings.PreserveFunctionNames = false;
                                }
                                else
                                {
                                    OnInvalidSwitch(switchPart, paramPart);
                                }

                                // this is a JS-only switch
                                OnJSOnlyParameter();
                                break;

                            case "GLOBAL":
                            case "G": // <-- old style
                                // the parts can be a comma-separate list of identifiers
                                if (string.IsNullOrEmpty(paramPartUpper))
                                {
                                    OnInvalidSwitch(switchPart, paramPart);
                                }
                                else
                                {
                                    foreach (string global in paramPart.Split(','))
                                    {
                                        // better be a valid JavaScript identifier
                                        if (!JSScanner.IsValidIdentifier(global))
                                        {
                                            OnInvalidSwitch(switchPart, global);
                                        }
                                        else if (globals == null)
                                        {
                                            // if we haven't created the list yet, do it now
                                            globals = new List<string>();
                                            globals.Add(global);
                                        }
                                        else if (!globals.Contains(global))
                                        {
                                            // don't add duplicates
                                            globals.Add(global);
                                        }
                                    }
                                }

                                // this is a JS-only switch
                                OnJSOnlyParameter();
                                break;

                            case "IGNORE":
                                // list of error codes to ignore (not report)
                                // the parts can be a comma-separate list of identifiers
                                if (string.IsNullOrEmpty(paramPartUpper))
                                {
                                    OnInvalidSwitch(switchPart, paramPart);
                                }
                                else
                                {
                                    foreach (string errorCode in paramPart.Split(','))
                                    {
                                        // if we haven't created the list yet, do it now
                                        if (ignoreErrors == null)
                                        {
                                            ignoreErrors = new List<string>();
                                        }

                                        // don't add duplicates
                                        if (!ignoreErrors.Contains(errorCode))
                                        {
                                            ignoreErrors.Add(errorCode);
                                        }
                                    }
                                }
                                break;

                            case "INLINE":
                                // set safe for inline to the same boolean.
                                // if no param part, will return false (indicating the default)
                                // if invalid param part, will throw error
                                if (BooleanSwitch(paramPartUpper, true, out parameterFlag))
                                {
                                    JSSettings.InlineSafeStrings = parameterFlag;
                                }
                                else
                                {
                                    OnInvalidSwitch(switchPart, paramPart);
                                }

                                // this is a JS-only switch
                                OnJSOnlyParameter();
                                break;

                            case "JS":
                                OnJSOnlyParameter();
                                break;

                            case "KILL":
                                // optional integer switch argument
                                if (paramPartUpper == null)
                                {
                                    OnInvalidSwitch(switchPart, paramPart);
                                }
                                else
                                {
                                    // get the numeric portion
                                    long killSwitch;
                                    if (paramPartUpper.StartsWith("0X", StringComparison.OrdinalIgnoreCase))
                                    {
                                        // it's hex -- convert the number after the "0x"
                                        if (long.TryParse(paramPartUpper.Substring(2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out killSwitch))
                                        {
                                            // save the switch for both JS and Css
                                            JSSettings.KillSwitch = CssSettings.KillSwitch = killSwitch;

                                            // for CSS, we only look at the first bit: preeserve important comments
                                            if ((killSwitch & 1) != 0)
                                            {
                                                // we set the kill, so make sure the comments are set to none
                                                CssSettings.CommentMode = CssComment.None;
                                            }
                                        }
                                        else
                                        {
                                            OnInvalidSwitch(switchPart, paramPart);
                                        }
                                    }
                                    else if (long.TryParse(paramPartUpper, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out killSwitch))
                                    {
                                        // save the switch for both JS and CSS
                                        JSSettings.KillSwitch = CssSettings.KillSwitch = killSwitch;

                                        // for CSS, we only look at the first bit: preeserve important comments
                                        if ((killSwitch & 1) != 0)
                                        {
                                            // we set the kill, so make sure the comments are set to none
                                            CssSettings.CommentMode = CssComment.None;
                                        }
                                    }
                                    else
                                    {
                                        OnInvalidSwitch(switchPart, paramPart);
                                    }
                                }

                                break;

                            case "LITERALS":
                                // two options: keep or combine
                                if (paramPartUpper == "KEEP")
                                {
                                    JSSettings.CombineDuplicateLiterals = false;
                                }
                                else if (paramPartUpper == "COMBINE")
                                {
                                    JSSettings.CombineDuplicateLiterals = true;
                                }
                                else if (paramPartUpper == "EVAL")
                                {
                                    JSSettings.EvalLiteralExpressions = true;
                                }
                                else if (paramPartUpper == "NOEVAL")
                                {
                                    JSSettings.EvalLiteralExpressions = false;
                                }
                                else
                                {
                                    OnInvalidSwitch(switchPart, paramPart);
                                }

                                // this is a JS-only switch
                                OnJSOnlyParameter();
                                break;

                            case "MAC":
                                // optional boolean switch
                                // no arg is valid scenario (default is true)
                                if (BooleanSwitch(paramPartUpper, true, out parameterFlag))
                                {
                                    JSSettings.MacSafariQuirks = parameterFlag;
                                }
                                else
                                {
                                    OnInvalidSwitch(switchPart, paramPart);
                                }

                                // this is a JS-only switch
                                OnJSOnlyParameter();
                                break;

                            case "MINIFY":
                                // optional boolean switch
                                // no arg is a valid scenario (default is true)
                                if (BooleanSwitch(paramPartUpper, true, out parameterFlag))
                                {
                                    JSSettings.MinifyCode = parameterFlag;
                                }
                                else
                                {
                                    OnInvalidSwitch(switchPart, paramPart);
                                }

                                // this is a JS-only switch
                                OnJSOnlyParameter();
                                break;

                            case "NEW":
                                // two options: keep and collapse
                                if (paramPartUpper == "KEEP")
                                {
                                    JSSettings.CollapseToLiteral = false;
                                }
                                else if (paramPartUpper == "COLLAPSE")
                                {
                                    JSSettings.CollapseToLiteral = true;
                                }
                                else
                                {
                                    OnInvalidSwitch(switchPart, paramPart);
                                }

                                // this is a JS-only switch
                                OnJSOnlyParameter();
                                break;

                            case "NFE": // <-- deprecate; use FNAMES option instead
                                if (paramPartUpper == "KEEPALL")
                                {
                                    JSSettings.RemoveFunctionExpressionNames = false;
                                }
                                else if (paramPartUpper == "ONLYREF")
                                {
                                    JSSettings.RemoveFunctionExpressionNames = true;
                                }
                                else
                                {
                                    OnInvalidSwitch(switchPart, paramPart);
                                }

                                // this is a JS-only switch
                                OnJSOnlyParameter();
                                break;

                            case "NORENAME":
                                // the parts can be a comma-separate list of identifiers
                                if (string.IsNullOrEmpty(paramPartUpper))
                                {
                                    OnInvalidSwitch(switchPart, paramPart);
                                }
                                else
                                {
                                    foreach (string ident in paramPart.Split(','))
                                    {
                                        // better be a valid JavaScript identifier
                                        if (!JSScanner.IsValidIdentifier(ident))
                                        {
                                            OnInvalidSwitch(switchPart, ident);
                                        }
                                        else if (noAutoRename == null)
                                        {
                                            // if we haven't created the list yet, do it now
                                            noAutoRename = new List<string>();
                                            noAutoRename.Add(ident);
                                        }
                                        else if (!noAutoRename.Contains(ident))
                                        {
                                            // don't add duplicates
                                            noAutoRename.Add(ident);
                                        }
                                    }
                                }

                                // this is a JS-only switch
                                OnJSOnlyParameter();
                                break;

                            case "PRETTY":
                            case "P": // <-- old style
                                // doesn't take a flag -- just set to pretty
                                PrettyPrint = true;
                                JSSettings.OutputMode = 
                                    CssSettings.OutputMode = OutputMode.MultipleLines;

                                // if renaming hasn't been specified yet, turn it off for prety-print
                                if (!renamingSpecified)
                                {
                                    JSSettings.LocalRenaming = LocalRenaming.KeepAll;
                                }

                                // optional integer switch argument
                                if (paramPartUpper != null)
                                {
                                    // get the numeric portion; must be a decimal integer
                                    int indentSize;
                                    if (int.TryParse(paramPart, NumberStyles.None, CultureInfo.InvariantCulture, out indentSize))
                                    {
                                        // same value for JS and CSS.
                                        // don't need to check for negative, because the tryparse method above does NOT
                                        // allow for a sign -- no sign, no negative.
                                        JSSettings.IndentSize = CssSettings.IndentSize = indentSize;
                                    }
                                    else 
                                    {
                                        OnInvalidSwitch(switchPart, paramPart);
                                    }
                                }
                                break;

                            case "RENAME":
                                if (paramPartUpper == null)
                                {
                                    // treat as if it's unknown
                                    ndx = OnUnknownParameter(args, ndx, switchPart, paramPart);
                                }
                                else if (paramPartUpper.IndexOf('=') > 0)
                                {
                                    // there is at least one equal sign -- treat this as a set of JS identifier
                                    // pairs. split on commas -- multiple pairs can be specified
                                    var paramPairs = paramPart.Split(',');
                                    foreach (var paramPair in paramPairs)
                                    {
                                        // split on the equal sign -- each pair needs to have an equal sige
                                        var pairParts = paramPair.Split('=');
                                        if (pairParts.Length == 2)
                                        {
                                            // there is an equal sign. The first part is the source name and the
                                            // second part is the new name to which to rename those entities.
                                            string fromIdentifier = pairParts[0];
                                            string toIdentifier = pairParts[1];

                                            // make sure both parts are valid JS identifiers
                                            var fromIsValid = JSScanner.IsValidIdentifier(fromIdentifier);
                                            var toIsValid = JSScanner.IsValidIdentifier(toIdentifier);
                                            if (fromIsValid && toIsValid)
                                            {
                                                // create the map if it hasn't been created yet.
                                                if (renameMap == null)
                                                {
                                                    // create the map and add the first entry
                                                    renameMap = new Dictionary<string, string>();
                                                    renameMap.Add(fromIdentifier, toIdentifier);
                                                }
                                                else if (renameMap.ContainsKey(fromIdentifier)
                                                    && string.CompareOrdinal(toIdentifier, renameMap[fromIdentifier]) != 0)
                                                {
                                                    // from-identifier already exists, and the to-identifier doesn't match.
                                                    // can't rename the same name to two different names!
                                                    OnInvalidSwitch(switchPart, fromIdentifier);
                                                }
                                                else
                                                {
                                                    // add it
                                                    renameMap.Add(fromIdentifier, toIdentifier);
                                                }
                                            }
                                            else
                                            {
                                                if (fromIsValid)
                                                {
                                                    // the toIdentifier is invalid!
                                                    OnInvalidSwitch(switchPart, toIdentifier);
                                                }

                                                if (toIsValid)
                                                {
                                                    // the fromIdentifier is invalid!
                                                    OnInvalidSwitch(switchPart, fromIdentifier);
                                                }
                                            }
                                        }
                                        else
                                        {
                                            // either zero or more than one equal sign. Invalid.
                                            OnInvalidSwitch(switchPart, paramPart);
                                        }
                                    }
                                }
                                else
                                {
                                    // no equal sign; just a plain option
                                    // three options: all, localization, none
                                    if (paramPartUpper == "ALL")
                                    {
                                        JSSettings.LocalRenaming = LocalRenaming.CrunchAll;

                                        // automatic renaming strategy has been specified by this option
                                        renamingSpecified = true;
                                    }
                                    else if (paramPartUpper == "LOCALIZATION")
                                    {
                                        JSSettings.LocalRenaming = LocalRenaming.KeepLocalizationVars;

                                        // automatic renaming strategy has been specified by this option
                                        renamingSpecified = true;
                                    }
                                    else if (paramPartUpper == "NONE")
                                    {
                                        JSSettings.LocalRenaming = LocalRenaming.KeepAll;

                                        // automatic renaming strategy has been specified by this option
                                        renamingSpecified = true;
                                    }
                                    else if (paramPartUpper == "NOPROPS")
                                    {
                                        // manual-renaming does not change property names
                                        JSSettings.ManualRenamesProperties = false;
                                    }
                                    else
                                    {
                                        OnInvalidSwitch(switchPart, paramPart);
                                    }
                                }

                                // this is a JS-only switch
                                OnJSOnlyParameter();
                                break;

                            case "REORDER":
                                // default is true
                                if (BooleanSwitch(paramPartUpper, true, out parameterFlag))
                                {
                                    JSSettings.ReorderScopeDeclarations = parameterFlag;
                                }
                                else
                                {
                                    OnInvalidSwitch(switchPart, paramPart);
                                }

                                // this is a JS-only switch
                                OnJSOnlyParameter();
                                break;

                            case "TERM":
                                // optional boolean argument, defaults to true
                                if (BooleanSwitch(paramPartUpper, true, out parameterFlag))
                                {
                                    JSSettings.TermSemicolons =
                                        CssSettings.TermSemicolons = parameterFlag;
                                }
                                else
                                {
                                    OnInvalidSwitch(switchPart, paramPart);
                                }
                                break;

                            case "UNUSED":
                                // two options: keep and remove
                                if (paramPartUpper == "KEEP")
                                {
                                    JSSettings.RemoveUnneededCode = false;
                                }
                                else if (paramPartUpper == "REMOVE")
                                {
                                    JSSettings.RemoveUnneededCode = true;
                                }
                                else
                                {
                                    OnInvalidSwitch(switchPart, paramPart);
                                }

                                // this is a JS-only switch
                                OnJSOnlyParameter();
                                break;

                            case "WARN":
                            case "W": // <-- old style
                                if (string.IsNullOrEmpty(paramPartUpper))
                                {
                                    // just "-warn" without anything else means all errors and warnings
                                    WarningLevel = int.MaxValue;
                                }
                                else
                                {
                                    // must be an unsigned decimal integer value
                                    int warningLevel;
                                    if (int.TryParse(paramPart, NumberStyles.None, CultureInfo.InvariantCulture, out warningLevel))
                                    {
                                        WarningLevel = warningLevel;
                                    }
                                    else
                                    {
                                        OnInvalidSwitch(switchPart, paramPart);
                                    }
                                }
                                levelSpecified = true;
                                break;

                            // Backward-compatibility switches different from new switches

                            case "D":
                                // equivalent to -debug:false (default behavior)
                                JSSettings.StripDebugStatements = true;
                                OnJSOnlyParameter();
                                break;

                            case "E":
                            case "EO":
                                // equivalent to -enc:out <encoding>
                                if (parts.Length < 2)
                                {
                                    // must be followed by an encoding
                                    OnInvalidSwitch(switchPart, paramPart);
                                }

                                // just save the name -- we'll create the encoding later because we need
                                // to know whether we are JS or CSS to pick the right encoding fallback
                                EncodingOutputName = paramPart;
                                break;

                            case "EI":
                                // equivalent to -enc:in <encoding>
                                if (parts.Length < 2)
                                {
                                    // must be followed by an encoding
                                    OnInvalidSwitch(switchPart, paramPart);
                                }

                                // save the name
                                EncodingInputName = paramPart;
                                break;

                            case "H":
                                // equivalent to -rename:all -unused:remove (default behavior)
                                JSSettings.LocalRenaming = LocalRenaming.CrunchAll;
                                JSSettings.RemoveUnneededCode = true;
                                OnJSOnlyParameter();

                                // renaming is specified by this option
                                renamingSpecified = true;
                                break;

                            case "HL":
                                // equivalent to -rename:localization -unused:remove
                                JSSettings.LocalRenaming = LocalRenaming.KeepLocalizationVars;
                                JSSettings.RemoveUnneededCode = true;
                                OnJSOnlyParameter();

                                // renaming is specified by this option
                                renamingSpecified = true;
                                break;

                            case "HC":
                                // equivalent to -literals:combine -rename:all -unused:remove
                                JSSettings.CombineDuplicateLiterals = true;
                                goto case "H";

                            case "HLC":
                            case "HCL":
                                // equivalent to -literals:combine -rename:localization -unused:remove
                                JSSettings.CombineDuplicateLiterals = true;
                                goto case "HL";

                            case "J":
                                // equivalent to -evals:ignore (default behavior)
                                JSSettings.EvalTreatment = EvalTreatment.Ignore;
                                OnJSOnlyParameter();
                                break;

                            case "K":
                                // equivalent to -inline:true (default behavior)
                                JSSettings.InlineSafeStrings = true;
                                OnJSOnlyParameter();
                                break;

                            case "L":
                                // equivalent to -new:keep (default is collapse)
                                JSSettings.CollapseToLiteral = false;
                                OnJSOnlyParameter();
                                break;

                            case "M":
                                // equivalent to -mac:true (default behavior)
                                JSSettings.MacSafariQuirks = true;
                                OnJSOnlyParameter();
                                break;

                            case "Z":
                                // equivalent to -term:true (default is false)
                                JSSettings.TermSemicolons =
                                    CssSettings.TermSemicolons = true;
                                break;

                            // end backward-compatible section

                            default:
                                ndx = OnUnknownParameter(args, ndx, switchPart, paramPart);
                                break;
                        }
                    }
                    else
                    {
                        // not a switch -- it's an unknown parameter
                        ndx = OnUnknownParameter(args, ndx, null, null);
                    }
                }

                // now check the collections we may have parsed. If any of them are non-null,
                // then set the appropriate property in the settings object(s)
                if (defines != null)
                {
                    var defineList = defines.ToArray();
                    JSSettings.SetPreprocessorDefines(defineList);
                    CssSettings.SetPreprocessorDefines(defineList);
                }

                if (debugLookups != null)
                {
                    JSSettings.SetDebugLookups(debugLookups.ToArray());
                }

                if (globals != null)
                {
                    JSSettings.SetKnownGlobalNames(globals.ToArray());
                }

                if (ignoreErrors != null)
                {
                    var errors = ignoreErrors.ToArray();
                    JSSettings.SetIgnoreErrors(errors);
                    CssSettings.SetIgnoreErrors(errors);
                }

                if (noAutoRename != null)
                {
                    JSSettings.SetNoAutoRename(noAutoRename.ToArray());
                }

                if (renameMap != null)
                {
                    foreach (var fromIdentifier in renameMap.Keys)
                    {
                        JSSettings.AddRenamePair(fromIdentifier, renameMap[fromIdentifier]);
                    }
                }
            }
        }

        #endregion

        #region parse renaming XML

        public void ParseRenamingXml(string xml)
        {
            var xmlDoc = new XmlDocument();
            xmlDoc.LoadXml(xml);

            // get all the <rename> nodes in the document
            var renameNodes = xmlDoc.SelectNodes("//rename");

            // not an error if there are no variables to rename; but if there are no nodes, then
            // there's nothing to process
            if (renameNodes.Count > 0)
            {
                // we have rename nodes to process, so clear out the map
                JSSettings.ClearRenamePairs();

                // process each <rename> node
                for (var ndx = 0; ndx < renameNodes.Count; ++ndx)
                {
                    var renameNode = renameNodes[ndx];

                    // get the from and to attributes
                    var fromAttribute = renameNode.Attributes["from"];
                    var toAttribute = renameNode.Attributes["to"];

                    // need to have both, and their values both need to be non-null and non-empty,
                    // otherwise ignore this node
                    if (fromAttribute != null && !string.IsNullOrEmpty(fromAttribute.Value)
                        && toAttribute != null && !string.IsNullOrEmpty(toAttribute.Value))
                    {
                        JSSettings.AddRenamePair(fromAttribute.Value, toAttribute.Value);
                    }
                }
            }

            // get all the <norename> nodes in the document
            var norenameNodes = xmlDoc.SelectNodes("//norename");

            // not an error if there aren't any
            if (norenameNodes.Count > 0)
            {
                // create a list -- we're going to weed out duplicates
                var noAutoRename = new List<string>(norenameNodes.Count);

                for (var ndx = 0; ndx < norenameNodes.Count; ++ndx)
                {
                    var node = norenameNodes[ndx];
                    var idAttribute = node.Attributes["id"];
                    if (idAttribute != null && !string.IsNullOrEmpty(idAttribute.Value))
                    {
                        // if we haven't created it yet, do it now
                        if (noAutoRename == null)
                        {
                            noAutoRename = new List<string>();
                            noAutoRename.Add(idAttribute.Value);
                        }
                        else if (!noAutoRename.Contains(idAttribute.Value))
                        {
                            noAutoRename.Add(idAttribute.Value);
                        }
                    }
                }

                // and set the values
                JSSettings.SetNoAutoRename(noAutoRename.ToArray());
            }
        }

        #endregion

        #region event handler overrides

        protected virtual int OnUnknownParameter(IList<string> arguments, int index, string switchPart, string parameterPart)
        {
            if (UnknownParameter != null)
            {
                // create our event args that we'll pass to the listeners and read the index field back from
                var ea = new UnknownParameterEventArgs(arguments)
                {
                    Index = index,
                    SwitchPart = switchPart,
                    ParameterPart = parameterPart,
                };

                // fire the event
                UnknownParameter(this, ea);

                // get the index from the event args, in case the listeners changed it
                // BUT ONLY if it's become greater. Can go backwards and get into an infinite loop.
                if (ea.Index > index)
                {
                    index = ea.Index;
                }
            }
            return index;
        }

        protected virtual void OnInvalidSwitch(string switchPart, string parameterPart)
        {
            if (InvalidSwitch != null)
            {
                InvalidSwitch(this, new InvalidSwitchEventArgs() { SwitchPart = switchPart, ParameterPart = parameterPart });
            }
        }

        protected virtual void OnJSOnlyParameter()
        {
            if (JSOnlyParameter != null)
            {
                JSOnlyParameter(this, new EventArgs());
            }
        }

        protected virtual void OnCssOnlyParameter()
        {
            if (CssOnlyParameter != null)
            {
                CssOnlyParameter(this, new EventArgs());
            }
        }

        #endregion

        #region helper methods

        public static bool BooleanSwitch(string booleanText, bool defaultValue, out bool booleanValue)
        {
            // assume it's valid unless proven otherwise
            var isValid = true;

            switch (booleanText)
            {
                case "Y":
                case "YES":
                case "T":
                case "TRUE":
                case "ON":
                case "1":
                    booleanValue = true;
                    break;

                case "N":
                case "NO":
                case "F":
                case "FALSE":
                case "OFF":
                case "0":
                    booleanValue = false;
                    break;

                case "":
                case null:
                    booleanValue = defaultValue;
                    break;

                default:
                    // not a valid value
                    booleanValue = defaultValue;
                    isValid = false;
                    break;
            }

            return isValid;
        }

        #endregion
    }
}
