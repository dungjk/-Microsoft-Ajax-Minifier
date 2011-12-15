// CssParser.cs
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

namespace Microsoft.Ajax.Utilities
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using System.Runtime.Serialization;
    using System.Security;
    using System.Security.Permissions;
    using System.Text;
    using System.Text.RegularExpressions;

    /// <summary>
    /// Parser takes Tokens and parses them into rules and statements
    /// </summary>
    public class CssParser
    {
        #region state fields

        private CssScanner m_scanner;
        private CssToken m_currentToken;
        private StringBuilder m_parsed;
        private bool m_noOutput;
        private string m_lastOutputString;
        private bool m_mightNeedSpace;
        private bool m_expressionContainsErrors;
        private bool m_skippedSpace;
        private int m_lineLength;

        // this is used to make sure we don't output two newlines in a row.
        // start it as true so we don't start off with a blank line
        private bool m_outputNewLine = true;

        // set this to true to force a newline before any other output
        private bool m_forceNewLine = false;

        public CssSettings Settings
        {
            get; set;
        }

        private readonly List<string> m_namespaces;

        public string FileContext { get; set; }

        private CodeSettings m_jsSettings;

        #endregion

        #region Comment-related fields

        /// <summary>
        /// regular expression for matching css comments
        /// Format: /*(anything or nothing inside)*/
        /// </summary>
        //private static Regex s_regexComments = new Regex(
        //    @"/\*([^*]|(\*+[^*/]))*\*+/",
        //    RegexOptions.IgnoreCase | RegexOptions.Singleline
#if !SILVERLIGHT
        //    | RegexOptions.Compiled
#endif
        //    );

        /// <summary>
        /// regular expression for matching first comment hack
        /// This is the MacIE ignore bug: /*(anything or nothing inside)\*/.../*(anything or nothing inside)*/
        /// </summary>
        private static Regex s_regexHack1 = new Regex(
            @"/\*([^*]|(\*+[^*/]))*\**\\\*/(?<inner>.*?)/\*([^*]|(\*+[^*/]))*\*+/",
            RegexOptions.IgnoreCase | RegexOptions.Singleline
#if !SILVERLIGHT
            | RegexOptions.Compiled
#endif
            );

        /// <summary>
        /// Regular expression for matching second comment hack
        /// Hide from everything EXCEPT Netscape 4 and Opera 5
        /// Format: /*/*//*/.../*(anything or nothing inside)*/
        /// </summary>
        private static Regex s_regexHack2 = new Regex(
            @"/\*/\*//\*/(?<inner>.*?)/\*([^*]|(\*+[^*/]))*\*+/",
            RegexOptions.IgnoreCase | RegexOptions.Singleline
#if !SILVERLIGHT
            | RegexOptions.Compiled
#endif
            );

        /// <summary>
        /// Regular expression for matching third comment hack
        /// Hide from Netscape 4
        /// Format: /*/*/.../*(anything or nothing inside)*/
        /// </summary>
        private static Regex s_regexHack3 = new Regex(
            @"/\*/\*/(?<inner>.*?)/\*([^*]|(\*+[^*/]))*\*+/",
            RegexOptions.IgnoreCase | RegexOptions.Singleline
#if !SILVERLIGHT
            | RegexOptions.Compiled
#endif
            );

        /// <summary>
        /// Regular expression for matching fourth comment hack
        /// Hide from IE6
        /// Format: property /*(anything or nothing inside)*/:value
        /// WARNING: This does not actually parse the property/value -- it simply looks for a
        /// word character followed by at least one whitespace character, followed
        /// by a simple comment, followed by optional space, followed by a colon.
        /// Does not match the simple word, the space or the colon (just the comment) 
        /// </summary>
        private static Regex s_regexHack4 = new Regex(
            @"(?<=\w\s+)/\*([^*]|(\*+[^*/]))*\*+/\s*(?=:)",
            RegexOptions.IgnoreCase | RegexOptions.Singleline
#if !SILVERLIGHT
            | RegexOptions.Compiled
#endif
            );

        /// <summary>
        /// Regular expression for matching fifth comment hack
        /// Hide from IE5.5
        /// Format: property:/* (anything or nothing inside) */value
        /// WARNING: This does not actually parse the property/value -- it simply looks for a
        /// word character followed by optional whitespace character, followed
        /// by a colon, followed by optional whitespace, followed by a simple comment.
        /// Does not match initial word or the colon, just the comment.
        /// </summary>
        private static Regex s_regexHack5 = new Regex(
            @"(?<=[\w/]\s*:)\s*/\*([^*]|(\*+[^*/]))*\*+/",
            RegexOptions.IgnoreCase | RegexOptions.Singleline
#if !SILVERLIGHT
            | RegexOptions.Compiled
#endif
            );

        /// <summary>
        /// Regular expression for matching sixth comment hack -- although not a real hack
        /// Hide from IE6, NOT
        /// Format: property/*(anything or nothing inside)*/:value
        /// NOTE: This doesn't actually hide from IE6; it needs a space before the comment to actually work.
        /// but enoough people code this in their CSS and expect it to be output that I recieved enough
        /// requests to add it to the allowed "hacks"
        /// WARNING: This does not actually parse the property/value -- it simply looks for a
        /// word character followed by a simple comment, followed by optional space, followed by a colon.
        /// Does not match the simple word or the colon (just initial whitespace and comment) 
        /// </summary>
        private static Regex s_regexHack6 = new Regex(
            @"(?<=\w)/\*([^*]|(\*+[^*/]))*\*+/\s*(?=:)",
            RegexOptions.IgnoreCase | RegexOptions.Singleline
#if !SILVERLIGHT
            | RegexOptions.Compiled
#endif
            );

        /// <summary>
        /// Regular expression for empty comments
        /// These comments don't really do anything. But if the developer wrote an empty
        /// comment (/**/ or /* */), then it has no documentation value and might possibly be
        /// an attempted comment hack.
        /// Format: /**/ or /* */ (single space)
        /// </summary>
        private static Regex s_regexHack7 = new Regex(
            @"/\*(\s?)\*/",
            RegexOptions.IgnoreCase | RegexOptions.Singleline
#if !SILVERLIGHT
            | RegexOptions.Compiled
#endif
            );

        #endregion

        #region color-related fields

        /// <summary>
        /// matches 6-digit RGB color value where both r digits are the same, both
        /// g digits are the same, and both b digits are the same (but r, g, and b
        /// values are not necessarily the same). Used to identify #rrggbb values
        /// that can be collapsed to #rgb
        /// </summary>
        private static Regex s_rrggbb = new Regex(
            @"^\#(?<r>[0-9a-fA-F])\k<r>(?<g>[0-9a-fA-F])\k<g>(?<b>[0-9a-fA-F])\k<b>$",
            RegexOptions.IgnoreCase
#if !SILVERLIGHT
            | RegexOptions.Compiled
#endif
            );

        // whether we are currently parsing the value for a property that might
        // use color names
        private bool m_parsingColorValue;

        #endregion

        #region value-replacement fields

        /// <summary>
        /// regular expression for matching css comments containing special formatted identifiers
        /// for value-replacement matching
        /// Format: /*[id]*/
        /// </summary>
        private static Regex s_valueReplacement = new Regex(
          @"/\*\s*\[(?<id>\w+)\]\s*\*/",
          RegexOptions.IgnoreCase | RegexOptions.Singleline
#if !SILVERLIGHT
 | RegexOptions.Compiled
#endif
);

        // this variable will be set whenever we encounter a value-replacement comment
        // and have a string to replace it with
        private string m_valueReplacement;// = null;

        #endregion

        #region token-related properties

        private TokenType CurrentTokenType
        {
            get
            {
                return (
                  m_currentToken != null
                  ? m_currentToken.TokenType
                  : TokenType.None
                  );
            }
        }

        private string CurrentTokenText
        {
            get
            {
                return (
                  m_currentToken != null
                  ? m_currentToken.Text
                  : string.Empty
                  );
            }
        }

        #endregion

        public CssParser()
        {
            // default settings
            Settings = new CssSettings();

            // create the settings we'll use for JS expression minification
            // use the defaults, other than to set the kill switch so that it leaves
            // string literals alone (so we don't inadvertently change any delimiter chars)
            m_jsSettings = new CodeSettings() { KillSwitch = (long)TreeModifications.MinifyStringLiterals };

            // create a list of strings that represent the namespaces declared
            // in a @namespace statement. We will clear this every time we parse a new source string.
            m_namespaces = new List<string>();
        }

        public string Parse(string source)
        {
            // clear out the list of namespaces
            m_namespaces.Clear();

            // pre-process the comments
            var resetToHacks = false;
            try
            {
                if (Settings.CommentMode == CssComment.Hacks)
                {
                    // change the various hacks to important comments so they will be kept
                    // in the output
                    source = s_regexHack1.Replace(source, "/*! \\*/${inner}/*!*/");
                    source = s_regexHack2.Replace(source, "/*!/*//*/${inner}/**/");
                    source = s_regexHack3.Replace(source, "/*!/*/${inner}/*!*/");
                    source = s_regexHack4.Replace(source, "/*!*/");
                    source = s_regexHack5.Replace(source, "/*!*/");
                    source = s_regexHack6.Replace(source, "/*!*/");
                    source = s_regexHack7.Replace(source, "/*!*/");

                    // now that we've changed all our hack comments to important comments, we can
                    // change the flag to Important so all non-important hacks are removed. 
                    // And set a flag to remind us to change it back before we exit, or the NEXT
                    // file we process will have the wrong setting.
                    Settings.CommentMode = CssComment.Important;
                    resetToHacks = true;
                }

                // set up for the parse
                using (StringReader reader = new StringReader(source))
                {
                    m_scanner = new CssScanner(reader);
                    m_scanner.AllowEmbeddedAspNetBlocks = this.Settings.AllowEmbeddedAspNetBlocks;
                    m_scanner.ScannerError += new EventHandler<CssScannerErrorEventArgs>(OnScannerError);

                    // create the string builder into which we will be 
                    // building our crunched stylesheet
                    m_parsed = new StringBuilder();

                    // get the first token
                    NextToken();

                    try
                    {
                        // parse a style sheet!
                        ParseStylesheet();

                        if (!m_scanner.EndOfFile)
                        {
                            string errorMessage = CssStringMgr.GetString(StringEnum.ExpectedEndOfFile);
                            throw new CssScannerException(
                                (int)StringEnum.ExpectedEndOfFile,
                                0,
                                m_currentToken.Context.Start.Line,
                                m_currentToken.Context.Start.Char,
                                errorMessage);
                        }
                    }
                    catch (CssException exc)
                    {
                        // show the error
                        OnCssError(exc);
                    }

                    // get the crunched string and dump the string builder
                    // (we don't need it anymore)
                    source = m_parsed.ToString();
                    m_parsed = null;
                }
            }
            finally
            {
                // if we had changed our setting object...
                if (resetToHacks)
                {
                    // ...be sure to change it BACK for next time.
                    Settings.CommentMode = CssComment.Hacks;
                }
            }

            return source;
        }

        #region Parse... methods

        private Parsed ParseStylesheet()
        {
            Parsed parsed = Parsed.False;
            if (CurrentTokenType == TokenType.CharacterSetSymbol)
            {
                AppendCurrent();
                SkipSpace();

                if (CurrentTokenType != TokenType.String)
                {
                    ReportError(0, StringEnum.ExpectedCharset, CurrentTokenText);
                    SkipToEndOfStatement();
                    AppendCurrent();
                }
                else
                {
                    Append(' ');
                    AppendCurrent();
                    SkipSpace();

                    if (CurrentTokenType != TokenType.Character || CurrentTokenText != ";")
                    {
                        ReportError(0, StringEnum.ExpectedSemicolon, CurrentTokenText);
                        SkipToEndOfStatement();
                        // be sure to append the closing token (; or })
                        AppendCurrent();
                    }
                    else
                    {
                        Append(';');
                        NextToken();
                    }
                }
            }

            // any number of S, Comment, CDO, or CDC elements
            ParseSCDOCDCComments();

            // any number of imports followed by S, Comment, CDO or CDC
            while (ParseImport() == Parsed.True)
            {
                // any number of S, Comment, CDO, or CDC elements
                ParseSCDOCDCComments();
            }

            // any number of namespaces followed by S, Comment, CDO or CDC
            while (ParseNamespace() == Parsed.True)
            {
                // any number of S, Comment, CDO, or CDC elements
                ParseSCDOCDCComments();
            }

            // the main guts of stuff
            while (ParseRule() == Parsed.True
              || ParseMedia() == Parsed.True
              || ParsePage() == Parsed.True
              || ParseFontFace() == Parsed.True
              || ParseKeyFrames() == Parsed.True
              || ParseAtKeyword() == Parsed.True
			  || ParseAspNetBlock() == Parsed.True)
            {
                // any number of S, Comment, CDO or CDC elements
                ParseSCDOCDCComments();
            }

            // if there weren't any errors, we SHOULD be at the EOF state right now.
            // if we're not, we may have encountered an invalid, unexpected character.
            while (!m_scanner.EndOfFile)
            {
                // throw an exception
                ReportError(0, StringEnum.UnexpectedToken, CurrentTokenText);

                // skip the token
                NextToken();

                // might be a comment again; check just in case
                ParseSCDOCDCComments();

                // try the guts again
                while (ParseRule() == Parsed.True
                  || ParseMedia() == Parsed.True
                  || ParsePage() == Parsed.True
                  || ParseFontFace() == Parsed.True
                  || ParseAtKeyword() == Parsed.True
				  || ParseAspNetBlock() == Parsed.True)
                {
                    // any number of S, Comment, CDO or CDC elements
                    ParseSCDOCDCComments();
                }
            }

            return parsed;
        }

        private void ParseSCDOCDCComments()
        {
            while (CurrentTokenType == TokenType.Space
              || CurrentTokenType == TokenType.Comment
              || CurrentTokenType == TokenType.CommentOpen
              || CurrentTokenType == TokenType.CommentClose)
            {
                if (CurrentTokenType != TokenType.Space)
                {
                    AppendCurrent();
                }
                NextToken();
            }
        }

        /*
        private void ParseUnknownBlock()
        {
            // output the opening brace and move to the next
            AppendCurrent();
            // skip space -- there shouldn't need to be space after the opening brace
            SkipSpace();

            // loop until we find the closing breace
            while (!m_scanner.EndOfFile
              && (CurrentTokenType != TokenType.Character || CurrentTokenText != "}"))
            {
                // see if we are recursing unknown blocks
                if (CurrentTokenType == TokenType.Character && CurrentTokenText == "{")
                {
                    // recursive block
                    ParseUnknownBlock();
                }
                else if (CurrentTokenType == TokenType.AtKeyword)
                {
                    // parse the at-keyword
                    ParseAtKeyword();
                }
                else if (CurrentTokenType == TokenType.Character && CurrentTokenText == ";")
                {
                    // append a semi-colon and skip any space after it
                    AppendCurrent();
                    SkipSpace();
                }
                else
                {
                    // whatever -- just append the token and move on
                    AppendCurrent();
                    NextToken();
                }
            }

            // output the closing brace and skip any trailing space
            AppendCurrent();
            SkipSpace();
        }
        */

        private Parsed ParseAtKeyword()
        {
            Parsed parsed = Parsed.False;
            if (CurrentTokenType == TokenType.AtKeyword)
            {
                // only report an unexpected at-keyword IF the identifier doesn't start 
                // with a hyphen, because that would be a vendor-specific at-keyword,
                // which is theoretically okay.
                if (!CurrentTokenText.StartsWith("@-", StringComparison.OrdinalIgnoreCase))
                {
                    ReportError(2, StringEnum.UnexpectedAtKeyword, CurrentTokenText);
                }

                SkipToEndOfStatement();
                AppendCurrent();
                SkipSpace();
                NewLine();
                parsed = Parsed.True;
            }
            return parsed;
        }

		private Parsed ParseAspNetBlock()
		{
			Parsed parsed = Parsed.False;
			if (Settings.AllowEmbeddedAspNetBlocks &&
				CurrentTokenType == TokenType.AspNetBlock)
			{
				AppendCurrent();
				SkipSpace();
				parsed = Parsed.True;
			}
			return parsed;
		}

        private Parsed ParseNamespace()
        {
            Parsed parsed = Parsed.False;
            if (CurrentTokenType == TokenType.NamespaceSymbol)
            {
                NewLine();
                AppendCurrent();
                SkipSpace();

                if (CurrentTokenType == TokenType.Identifier)
                {
                    Append(' ');
                    AppendCurrent();

                    // if the namespace is not already in the list, 
                    // save current text as a declared namespace value 
                    // that can be used in the rest of the code
                    if (!m_namespaces.Contains(CurrentTokenText))
                    {
                        m_namespaces.Add(CurrentTokenText);
                    }
                    else
                    {
                        // error -- we already have this namespace in the list
                        ReportError(1, StringEnum.DuplicateNamespaceDeclaration, CurrentTokenText);
                    }

                    SkipSpace();
                }

                if (CurrentTokenType != TokenType.String
                  && CurrentTokenType != TokenType.Uri)
                {
                    ReportError(0, StringEnum.ExpectedNamespace, CurrentTokenText);
                    SkipToEndOfStatement();
                    AppendCurrent();
                }
                else
                {
                    Append(' ');
                    AppendCurrent();
                    SkipSpace();

                    if (CurrentTokenType == TokenType.Character
                      && CurrentTokenText == ";")
                    {
                        Append(';');
                        SkipSpace();
                        NewLine();
                    }
                    else
                    {
                        ReportError(0, StringEnum.ExpectedSemicolon, CurrentTokenText);
                        SkipToEndOfStatement();
                        AppendCurrent();
                    }
                }

                parsed = Parsed.True;
            }
            return parsed;
        }

        private void ValidateNamespace(string namespaceIdent)
        {
            // check it against list of all declared @namespace names
            if (!string.IsNullOrEmpty(namespaceIdent)
                && namespaceIdent != "*"
                && !m_namespaces.Contains(namespaceIdent))
            {
                ReportError(0, StringEnum.UndeclaredNamespace, namespaceIdent);
            }
        }

        private Parsed ParseKeyFrames()
        {
            // '@keyframes' IDENT '{' keyframes-blocks '}'
            Parsed parsed = Parsed.False;
            if (CurrentTokenType == TokenType.KeyFramesSymbol)
            {
                // found the @keyframes at-rule
                parsed = Parsed.True;

                NewLine();
                AppendCurrent();
                SkipSpace();

                // needs to be followed by an identifier
                if (CurrentTokenType == TokenType.Identifier || CurrentTokenType == TokenType.String)
                {
                    // if this is an identifier, then we need to make sure we output a space
                    // character so the identifier doesn't get attached to the previous @-rule
                    if (CurrentTokenType == TokenType.Identifier || Settings.OutputMode == OutputMode.MultipleLines)
                    {
                        Append(' ');
                    }

                    AppendCurrent();
                    SkipSpace();
                }
                else
                {
                    ReportError(0, StringEnum.ExpectedIdentifier, CurrentTokenText);
                }

                // followed by keyframe blocks surrounded with curly-braces
                if (CurrentTokenType == TokenType.Character && CurrentTokenText == "{")
                {
                    NewLine();
                    AppendCurrent();
                    Indent();
                    NewLine();
                    SkipSpace();

                    ParseKeyFrameBlocks();

                    // better end with a curly-brace
                    Unindent();
                    NewLine();
                    if (CurrentTokenType == TokenType.Character && CurrentTokenText == "}")
                    {
                        NewLine();
                        AppendCurrent();
                        SkipSpace();
                    }
                    else
                    {
                        ReportError(0, StringEnum.ExpectedClosingBrace, CurrentTokenText);
                        SkipToEndOfDeclaration();
                    }
                }
                else
                {
                    ReportError(0, StringEnum.ExpectedOpenBrace, CurrentTokenText);
                    SkipToEndOfStatement();
                }
            }
            return parsed;
        }

        private void ParseKeyFrameBlocks()
        {
            // [ keyframe-selectors block ]*
            while (ParseKeyFrameSelectors() == Parsed.True)
            {
                ParseDeclarationBlock(false);

                // set the force-newline flag to true so that any selectors we may find next
                // will start on a new line
                m_forceNewLine = true;
            }

            // reset the flag
            m_forceNewLine = false;
        }

        private Parsed ParseKeyFrameSelectors()
        {
            // [ 'from' | 'to' | PERCENTAGE ] [ ',' [ 'from' | 'to' | PERCENTAGE ] ]*
            Parsed parsed = Parsed.False;

            // see if we start with a percentage or the words "from" or "to"
            if (CurrentTokenType == TokenType.Percentage)
            {
                AppendCurrent();
                SkipSpace();
                parsed = Parsed.True;
            }
            else if (CurrentTokenType == TokenType.Identifier)
            {
                var upperIdent = CurrentTokenText.ToUpperInvariant();
                if (string.CompareOrdinal(upperIdent, "FROM") == 0
                    || string.CompareOrdinal(upperIdent, "TO") == 0)
                {
                    AppendCurrent();
                    SkipSpace();
                    parsed = Parsed.True;
                }
            }

            // if we found one, keep going as long as there are others comma-separated
            while (parsed == Parsed.True && CurrentTokenType == TokenType.Character && CurrentTokenText == ",")
            {
                // append the comma, and if this is multiline mode, follow it with a space for readability
                AppendCurrent();
                if (Settings.OutputMode == OutputMode.MultipleLines)
                {
                    Append(' ');
                }
                SkipSpace();

                // needs to be either a percentage or "from" or "to"
                if (CurrentTokenType == TokenType.Percentage)
                {
                    AppendCurrent();
                    SkipSpace();
                }
                else if (CurrentTokenType == TokenType.Identifier)
                {
                    var upperIdent = CurrentTokenText.ToUpperInvariant();
                    if (string.CompareOrdinal(upperIdent, "FROM") == 0
                        || string.CompareOrdinal(upperIdent, "TO") == 0)
                    {
                        AppendCurrent();
                        SkipSpace();
                    }
                }
                else
                {
                    ReportError(0, StringEnum.ExpectedPercentageFromOrTo, CurrentTokenText);
                }
            }

            return parsed;
        }

        private Parsed ParseImport()
        {
            Parsed parsed = Parsed.False;
            if (CurrentTokenType == TokenType.ImportSymbol)
            {
                NewLine();
                AppendCurrent();
                SkipSpace();

                if (CurrentTokenType != TokenType.String
                  && CurrentTokenType != TokenType.Uri)
                {
                    ReportError(0, StringEnum.ExpectedImport, CurrentTokenText);
                    SkipToEndOfStatement();
                    AppendCurrent();
                }
                else
                {
                    // only need a space if this is a Uri -- a string starts with a quote delimiter
                    // and won't get parsed as teh end of the @import token
                    if (CurrentTokenType == TokenType.Uri || Settings.OutputMode == OutputMode.MultipleLines)
                    {
                        Append(' ');
                    }

                    // append the file (string or uri)
                    AppendCurrent();
                    SkipSpace();

                    // optional comma-separated list of media queries
                    // won't need a space because the ending is either a quote or a paren
                    ParseMediaQueryList(false);

                    if (CurrentTokenType == TokenType.Character && CurrentTokenText == ";")
                    {
                        Append(';');
                        NewLine();
                    }
                    else
                    {
                        ReportError(0, StringEnum.ExpectedSemicolon, CurrentTokenText);
                        SkipToEndOfStatement();
                        AppendCurrent();
                    }
                }
                SkipSpace();
                parsed = Parsed.True;
            }

            return parsed;
        }

        private Parsed ParseMedia()
        {
            Parsed parsed = Parsed.False;
            if (CurrentTokenType == TokenType.MediaSymbol)
            {
                NewLine();
                AppendCurrent();
                SkipSpace();

                // might need a space because the last token was @media
                if (ParseMediaQueryList(true) == Parsed.True)
                {
                    if (CurrentTokenType == TokenType.Character && CurrentTokenText == "{")
                    {
                        AppendCurrent();
                        SkipSpace();

                        // the main guts of stuff
                        while (ParseRule() == Parsed.True
                          || ParseMedia() == Parsed.True
                          || ParsePage() == Parsed.True
                          || ParseFontFace() == Parsed.True
                          || ParseAtKeyword() == Parsed.True
                          || ParseAspNetBlock() == Parsed.True)
                        {
                            // any number of S, Comment, CDO or CDC elements
                            ParseSCDOCDCComments();
                        }
                    }
                    else
                    {
                        SkipToEndOfStatement();
                    }

                    if (CurrentTokenType == TokenType.Character)
                    {
                        if (CurrentTokenText == ";")
                        {
                            AppendCurrent();
                            Unindent();
                            NewLine();
                        }
                        else if (CurrentTokenText == "}")
                        {
                            Unindent();
                            NewLine();
                            AppendCurrent();
                        }
                        else
                        {
                            SkipToEndOfStatement();
                            AppendCurrent();
                        }
                    }
                    else
                    {
                        SkipToEndOfStatement();
                        AppendCurrent();
                    }

                    SkipSpace();
                    parsed = Parsed.True;
                }
                else
                {
                    SkipToEndOfStatement();
                }
            }

            return parsed;
        }

        private Parsed ParseMediaQueryList(bool mightNeedSpace)
        {
            // see if we have a media query
            Parsed parsed = ParseMediaQuery(mightNeedSpace);

            // it's a comma-separated list, so as long as we find a comma, keep parsing queries
            while(CurrentTokenType == TokenType.Character && CurrentTokenText == ",")
            {
                // output the comma and skip any space
                AppendCurrent();
                SkipSpace();

                if (ParseMediaQuery(false) != Parsed.True)
                {
                    // fail
                    ReportError(0, StringEnum.ExpectedMediaQuery, CurrentTokenText);
                }
            }

            return parsed;
        }

        private Parsed ParseMediaQuery(bool firstQuery)
        {
            var parsed = Parsed.False;
            var mightNeedSpace = firstQuery;

            // we have an optional word ONLY or NOT -- they will show up as identifiers here
            if (CurrentTokenType == TokenType.Identifier &&
                (string.Compare(CurrentTokenText, "ONLY", StringComparison.OrdinalIgnoreCase) == 0
                || string.Compare(CurrentTokenText, "NOT", StringComparison.OrdinalIgnoreCase) == 0))
            {
                // if this is the first query, the last thing we output was @media, which will need a separator.
                // if it's not the first, the last thing was a comma, so no space is needed.
                // but if we're expanding the output, we always want a space
                if (firstQuery || Settings.OutputMode == OutputMode.MultipleLines)
                {
                    Append(' ');
                }

                // output the only/not string and skip any subsequent space
                AppendCurrent();
                SkipSpace();
                
                // we might need a space since the last thing was the only/not
                mightNeedSpace = true;
            }

            // we should be at a either a media type or an expression
            if (CurrentTokenType == TokenType.Identifier)
            {
                // media type
                // if we might need a space, output it now
                if (mightNeedSpace || Settings.OutputMode == OutputMode.MultipleLines)
                {
                    Append(' ');
                }

                // output the media type
                AppendCurrent();
                SkipSpace();

                // the media type is an identifier, so we might need a space
                mightNeedSpace = true;

                // the next item should be either AND or the start of the block
                parsed = Parsed.True;
            }
            else if (CurrentTokenType == TokenType.Character && CurrentTokenText == "(")
            {
                // no media type -- straight to an expression
                ParseMediaQueryExpression();

                // the expression ends in a close paren, so we don't need the space
                mightNeedSpace = false;

                // the next item should be either AND or the start of the block
                parsed = Parsed.True;
            }
            else if (CurrentTokenType != TokenType.Character || CurrentTokenText != ";")
            {
                // expected a media type
                ReportError(0, StringEnum.ExpectedMediaIdentifier, CurrentTokenText);
            }

            // either we have no more and-delimited expressions,
            // OR we have an *identifier* AND (and followed by space)
            // OR we have a *function* AND (and followed by the opening paren, scanned as a function)
            while ((CurrentTokenType == TokenType.Identifier
                && string.Compare(CurrentTokenText, "AND", StringComparison.OrdinalIgnoreCase) == 0)
                || (CurrentTokenType == TokenType.Function
                && string.Compare(CurrentTokenText, "AND(", StringComparison.OrdinalIgnoreCase) == 0))
            {
                // if we might need a space, output it now
                if (mightNeedSpace || Settings.OutputMode == OutputMode.MultipleLines)
                {
                    Append(' ');

                    // the media expression ends in a close-paren, so we never need another space
                    mightNeedSpace = false;
                }

                // output the AND text.
                // MIGHT be AND( if it was a function, so first set a flag so we will know
                // wether or not to expect the opening paren
                if (CurrentTokenType == TokenType.Function)
                {
                    // this is not strictly allowed by the CSS3 spec!
                    // we are going to throw an error
                    ReportError(1, StringEnum.MediaQueryRequiresSpace, CurrentTokenText);

                    //and then fix what the developer wrote and make sure there is a space
                    // between the AND and the (. The CSS3 spec says it is invalid to not have a
                    // space there.
                    Append("and (");
                    SkipSpace();

                    // included the paren
                    ParseMediaQueryExpression();
                }
                else
                {
                    // didn't include the paren -- it BETTER be the next token 
                    // (after we output the AND token)
                    AppendCurrent();
                    SkipSpace();
                    if (CurrentTokenType == TokenType.Character
                        && CurrentTokenText == "(")
                    {
                        // put a space between the AND and the (
                        Append(' ');

                        ParseMediaQueryExpression();
                    }
                    else
                    {
                        // error -- we expected another media query expression
                        ReportError(0, StringEnum.ExpectedMediaQueryExpression, CurrentTokenText);

                        // break out of the loop so we can exit
                        break;
                    }
                }
            }

            return parsed;
        }

        private void ParseMediaQueryExpression()
        {
            // expect current token to be the opening paren when calling
            if (CurrentTokenType == TokenType.Character && CurrentTokenText == "(")
            {
                // output the paren and skip any space
                AppendCurrent();
                SkipSpace();
            }

            // media feature is required, and it's an ident
            if (CurrentTokenType == TokenType.Identifier)
            {
                // output the media feature and skip any space
                AppendCurrent();
                SkipSpace();

                // the next token should either be a colon (followed by an expression) or the closing paren
                if (CurrentTokenType == TokenType.Character && CurrentTokenText == ":")
                {
                    // got an expression.
                    // output the colon and skip any whitespace
                    AppendCurrent();
                    SkipSpace();

                    // if we are expanding the output, we want a space after the colon
                    if (Settings.OutputMode == OutputMode.MultipleLines)
                    {
                        Append(' ');
                    }

                    // parse the expression -- it's not optional
                    if (ParseExpr() != Parsed.True)
                    {
                        ReportError(0, StringEnum.ExpectedExpression, CurrentTokenText);
                    }

                    // better be the closing paren
                    if (CurrentTokenType == TokenType.Character && CurrentTokenText == ")")
                    {
                        // output the closing paren and skip any whitespace
                        AppendCurrent();
                        SkipSpace();
                    }
                    else
                    {
                        ReportError(0, StringEnum.ExpectedClosingParen, CurrentTokenText);
                    }
                }
                else if (CurrentTokenType == TokenType.Character && CurrentTokenText == ")")
                {
                    // end of the expressions -- output the closing paren and skip any whitespace
                    AppendCurrent();
                    SkipSpace();
                }
                else
                {
                    ReportError(0, StringEnum.ExpectedClosingParen, CurrentTokenText);
                }
            }
            else
            {
                ReportError(0, StringEnum.ExpectedMediaFeature, CurrentTokenText);
            }
        }

        private Parsed ParseDeclarationBlock(bool allowMargins)
        {
            Parsed parsed = Parsed.False;
            // expect current token to be the opening brace when calling
            if (CurrentTokenType != TokenType.Character || CurrentTokenText != "{")
            {
                ReportError(0, StringEnum.ExpectedOpenBrace, CurrentTokenText);
                SkipToEndOfStatement();
                AppendCurrent();
                SkipSpace();
            }
            else
            {
                NewLine();
                Append('{');

                Indent();
                SkipSpace();

                while (!m_scanner.EndOfFile)
                {
                    try
                    {
                        // check the line length before each new declaration -- if we're past the threshold, start a new line
                        if (m_lineLength >= Settings.LineBreakThreshold)
                        {
                            AddNewLine();
                        }

                        Parsed parsedDecl = ParseDeclaration();

                        // if we are allowed to have margin at-keywords in this block, and
                        // we didn't find a declaration, check to see if it's a margin
                        var parsedMargin = false;
                        if (allowMargins && parsedDecl == Parsed.Empty)
                        {
                            parsedMargin = ParseMargin() == Parsed.True;
                        }
                        
                        // if we parsed a margin, we DON'T expect there to be a semi-colon.
                        // if we didn't parse a margin, then there better be either a semicolon or a closing brace.
                        if (!parsedMargin)
                        {
                            if (CurrentTokenType != TokenType.Character
                              || (CurrentTokenText != ";" && CurrentTokenText != "}"))
                            {
                                ReportError(0, StringEnum.ExpectedSemicolonOrClosingBrace, CurrentTokenText);

                                // we'll get here if we decide to ignore the error and keep trudging along. But we still
                                // need to skip to the end of the declaration.
                                SkipToEndOfDeclaration();
                            }
                        }

                        // if we're at the end, close it out
                        if (CurrentTokenText == "}")
                        {
                            // if we want terminating semicolons but the source
                            // didn't have one (evidenced by a non-empty declaration)...
                            if (Settings.TermSemicolons && parsedDecl == Parsed.True)
                            {
                                // ...then add one now.
                                Append(';');
                            }
                            // append the closing brace
                            Unindent();
                            NewLine();
                            Append('}');
                            // skip past it
                            SkipSpace();
                            parsed = Parsed.True;
                            break;
                        }
                        else if (CurrentTokenText == ";")
                        {
                            // token is a semi-colon
                            // if we always want to add the semicolons, add it now
                            if (Settings.TermSemicolons)
                            {
                                Append(';');
                                SkipSpace();
                            }
                            else
                            {
                                // we have a semicolon, but we don't know if we can
                                // crunch it out or not. If the NEXT token is a closing brace, then
                                // we can crunch out the semicolon.
                                // PROBLEM: if there's a significant comment AFTER the semicolon, then the 
                                // comment gets output before we output the semicolon, which could
                                // reverse the intended code.

                                // skip any whitespace to see if we need to add a semicolon
                                // to the end, or if we can crunch it out, but use a special function
                                // that doesn't send any comments to the stream yet -- it batches them
                                // up and returns them (if any)
                                string comments = NextSignificantToken();

                                // if the significant token after the 
                                // semicolon is not a cosing brace, then we'll add the semicolon.
                                // if there are two semi-colons in a row, don't add it because we'll double it.
                                // if there's a non-empty comment, it might be a significant hack, so add the semi-colon just in case.
                                if (CurrentTokenType != TokenType.Character
                                  || (CurrentTokenText != "}" && CurrentTokenText != ";")
                                  || (comments.Length > 0 && comments != "/* */" && comments != "/**/"))
                                {
                                    Append(';');
                                }
                                // now that we've possibly added our semi-colon, we're safe
                                // to add any comments we may have found before the current token
                                if (comments.Length > 0)
                                {
                                    Append(comments);

                                    // and comments always end on a new line
                                    m_outputNewLine = true;
                                    m_lineLength = 0;
                                }
                            }
                        }
                    }
                    catch (CssException e)
                    {
                        // show the error
                        OnCssError(e);
                        
                        // skip to the end of the declaration
                        SkipToEndOfDeclaration();
                        if (CurrentTokenType != TokenType.None)
                        {
                            if (Settings.TermSemicolons
                              || CurrentTokenType != TokenType.Character
                              || (CurrentTokenText != "}" && CurrentTokenText != ";"))
                            {
                                Append(';');
                            }
                        }
                    }
                }
            }
            return parsed;
        }

        private Parsed ParsePage()
        {
            Parsed parsed = Parsed.False;
            if (CurrentTokenType == TokenType.PageSymbol)
            {
                NewLine();
                AppendCurrent();
                SkipSpace();

                if (CurrentTokenType == TokenType.Identifier)
                {
                    Append(' ');
                    AppendCurrent();
                    NextToken();
                }
                // optional
                ParsePseudoPage();

                if (CurrentTokenType == TokenType.Space)
                {
                    SkipSpace();
                }

                if (CurrentTokenType == TokenType.Character && CurrentTokenText == "{")
                {
                    // allow margin at-keywords
                    parsed = ParseDeclarationBlock(true);
                    NewLine();
                }
                else
                {
                    SkipToEndOfStatement();
                    AppendCurrent();
                    SkipSpace();
                }
            }
            return parsed;
        }

        private Parsed ParsePseudoPage()
        {
            Parsed parsed = Parsed.False;
            if (CurrentTokenType == TokenType.Character && CurrentTokenText == ":")
            {
                Append(':');
                NextToken();

                if (CurrentTokenType != TokenType.Identifier)
                {
                    ReportError(0, StringEnum.ExpectedIdentifier, CurrentTokenText);
                }

                AppendCurrent();
                NextToken();
                parsed = Parsed.True;
            }
            return parsed;
        }

        private Parsed ParseMargin()
        {
            Parsed parsed = Parsed.Empty;
            switch (CurrentTokenType)
            {
                case TokenType.TopLeftCornerSymbol:
                case TokenType.TopLeftSymbol:
                case TokenType.TopCenterSymbol:
                case TokenType.TopRightSymbol:
                case TokenType.TopRightCornerSymbol:
                case TokenType.BottomLeftCornerSymbol:
                case TokenType.BottomLeftSymbol:
                case TokenType.BottomCenterSymbol:
                case TokenType.BottomRightSymbol:
                case TokenType.BottomRightCornerSymbol:
                case TokenType.LeftTopSymbol:
                case TokenType.LeftMiddleSymbol:
                case TokenType.LeftBottomSymbol:
                case TokenType.RightTopSymbol:
                case TokenType.RightMiddleSymbol:
                case TokenType.RightBottomSymbol:
                    // these are the margin at-keywords
                    NewLine();
                    AppendCurrent();
                    SkipSpace();

                    // don't allow margin at-keywords
                    parsed = ParseDeclarationBlock(false);
                    NewLine();
                    break;

                default:
                    // we're not interested
                    break;
            }
            return parsed;
        }

        private Parsed ParseFontFace()
        {
            Parsed parsed = Parsed.False;
            if (CurrentTokenType == TokenType.FontFaceSymbol)
            {
                NewLine();
                AppendCurrent();
                SkipSpace();

                // don't allow margin at-keywords
                parsed = ParseDeclarationBlock(false);
                NewLine();
            }
            return parsed;
        }

        private Parsed ParseOperator()
        {
            Parsed parsed = Parsed.Empty;
            if (CurrentTokenType == TokenType.Character
              && (CurrentTokenText == "/" || CurrentTokenText == ","))
            {
                AppendCurrent();
                SkipSpace();
                parsed = Parsed.True;
            }
            return parsed;
        }

        private Parsed ParseCombinator()
        {
            Parsed parsed = Parsed.Empty;
            if (CurrentTokenType == TokenType.Character
              && (CurrentTokenText == "+" || CurrentTokenText == ">" || CurrentTokenText == "~"))
            {
                AppendCurrent();
                SkipSpace();
                parsed = Parsed.True;
            }
            return parsed;
        }

        private Parsed ParseRule()
        {
            // check the line length before each new declaration -- if we're past the threshold, start a new line
            if (m_lineLength >= Settings.LineBreakThreshold)
            {
                AddNewLine();
            }

            m_forceNewLine = true;
            Parsed parsed = ParseSelector();
            if (parsed == Parsed.True)
            {
                while (!m_scanner.EndOfFile)
                {
                    try
                    {
                        if (CurrentTokenType != TokenType.Character
                          || (CurrentTokenText != "," && CurrentTokenText != "{"))
                        {
                            ReportError(0, StringEnum.ExpectedCommaOrOpenBrace, CurrentTokenText);
                            SkipToEndOfStatement();
                            AppendCurrent();
                            SkipSpace();
                            break;
                        }
                        if (CurrentTokenText == "{")
                        {
                            // REVIEW: IE6 has an issue where the "first-letter" and "first-line" 
                            // pseudo-classes need to be separated from the opening curly-brace 
                            // of the following rule set by a space or it doesn't get picked up. 
                            // So if the last-outputted word was "first-letter" or "first-line",
                            // add a space now (since we know the next character at this point 
                            // is the opening brace of a rule-set).
                            // Maybe some day this should be removed or put behind an "IE6-compat" switch.
                            if (m_lastOutputString == "first-letter" || m_lastOutputString == "first-line")
                            {
                                Append(' ');
                            }

                            // don't allow margin at-keywords
                            parsed = ParseDeclarationBlock(false);
                            break;
                        }

                        Append(',');

                        // check the line length before each new declaration -- if we're past the threshold, start a new line
                        if (m_lineLength >= Settings.LineBreakThreshold)
                        {
                            AddNewLine();
                        }
                        else if (Settings.OutputMode == OutputMode.MultipleLines)
                        {
                            Append(' ');
                        }

                        SkipSpace();

                        if (ParseSelector() != Parsed.True)
                        {
                            if (CurrentTokenType == TokenType.Character && CurrentTokenText == "{")
                            {
                                // the author ended the last selector with a comma, but didn't include
                                // the next selector before starting the declaration block. Or maybe it's there,
                                // but commented out. Still okay, but flag a style warning.
                                ReportError(4, StringEnum.ExpectedSelector, CurrentTokenText);
                                continue;
                            }
                            else
                            {
                                // not something we know about -- skip the whole statement
                                ReportError(0, StringEnum.ExpectedSelector, CurrentTokenText);
                                SkipToEndOfStatement();
                            }
                            AppendCurrent();
                            SkipSpace();
                            break;
                        }
                    }
                    catch (CssException e)
                    {
                        OnCssError(e);

                        // skip to end of statement and keep on trucking
                        SkipToEndOfStatement();
                        AppendCurrent();
                        SkipSpace();
                    }
                }
            }
            return parsed;
        }

        private Parsed ParseSelector()
        {
            // should start with a selector
            Parsed parsed = ParseSimpleSelector();
            if (parsed == Parsed.False && CurrentTokenType != TokenType.None)
            {
                // no selector? See if it starts with a combinator.
                // common IE-7 hack to start with a combinator, because that browser will assume a beginning *
                var currentContext = m_currentToken.Context;
                var possibleCombinator = CurrentTokenText;
                parsed = ParseCombinator();
                if (parsed == Parsed.True)
                {
                    ReportError(4, StringEnum.HackGeneratesInvalidCSS, currentContext, possibleCombinator);
                }
            }

            if (parsed == Parsed.True)
            {
                // save whether or not we are skipping anything by checking the type before we skip
                bool spaceWasSkipped = SkipIfSpace();

                while (!m_scanner.EndOfFile)
                {
                    Parsed parsedCombinator = ParseCombinator();
                    if (parsedCombinator != Parsed.True)
                    {
                        // we know the selector ends with a comma or an open brace,
                        // so if the next token is one of those, we're done.
                        // otherwise we're going to slap a space in the stream (if we found one)
                        // and look for the next selector
                        if (CurrentTokenType == TokenType.Character
                          && (CurrentTokenText == "," || CurrentTokenText == "{"))
                        {
                            break;
                        }
                        else if (spaceWasSkipped)
                        {
                            Append(' ');
                        }
                    }

                    if (ParseSimpleSelector() == Parsed.False)
                    {
                        ReportError(0, StringEnum.ExpectedSelector, CurrentTokenText);
                        break;
                    }
                    else
                    {
                        // save the "we skipped whitespace" flag before skipping the whitespace
                        spaceWasSkipped = SkipIfSpace();
                    }
                }
            }
            return parsed;
        }

        // does NOT skip whitespace after the selector
        private Parsed ParseSimpleSelector()
        {
            // the element name is optional
            Parsed parsed = ParseElementName();
            while (!m_scanner.EndOfFile)
            {
                if (CurrentTokenType == TokenType.Hash)
                {
                    AppendCurrent();
                    NextToken();
                    parsed = Parsed.True;
                }
                else if (ParseClass() == Parsed.True)
                {
                    parsed = Parsed.True;
                }
                else if (ParseAttrib() == Parsed.True)
                {
                    parsed = Parsed.True;
                }
                else if (ParsePseudo() == Parsed.True)
                {
                    parsed = Parsed.True;
                }
                else
                {
                    break;
                }
            }
            return parsed;
        }

        private Parsed ParseClass()
        {
            Parsed parsed = Parsed.False;
            if (CurrentTokenType == TokenType.Character
              && CurrentTokenText == ".")
            {
                AppendCurrent();
                NextToken();

                if (CurrentTokenType == TokenType.Identifier)
                {
                    AppendCurrent();
                    NextToken();
                    parsed = Parsed.True;
                }
                else
                {
                    ReportError(0, StringEnum.ExpectedIdentifier, CurrentTokenText);
                }
            }
            else if (CurrentTokenType == TokenType.Dimension || CurrentTokenType == TokenType.Number)
            {
                string rawNumber = m_scanner.RawNumber;
                if (rawNumber != null && rawNumber.StartsWith(".", StringComparison.Ordinal))
                {
                    // if we are expecting a class but we got dimension or number that starts with a period,
                    // then what we REALLY have is a class name that starts with a digit. If it's all digits,
                    // it will be a number, and it it's just an identifier that starts with a digit, it will
                    // be a dimension.
                    // The problem here is that both of those those token type format the number, eg: 
                    // .000foo would get shrunk to 0foo.
                    // Be sure to use the RawNumber property on the scanner to get the raw text exactly as
                    // it was from the input
                    parsed = Parsed.True;

                    // but check the next token to see if it's an identifier.
                    // if the next token is an identifier with no whitespace between it and the previous
                    // "number," then it's part of this identifier
                    NextToken();
                    if (CurrentTokenType == TokenType.Identifier)
                    {
                        // add that identifier to the raw number
                        rawNumber += CurrentTokenText;
                        NextToken();
                    }

                    // report a low-sev warning before outputting the raw number text and advancing
                    ReportError(2, StringEnum.PossibleInvalidClassName, rawNumber);
                    Append(rawNumber);
                }
            }
            return parsed;
        }

        private Parsed ParseElementName()
        {
            Parsed parsed = Parsed.False;
            bool foundNamespace = false;

            // if the next character is a pipe, then we have an empty namespace prefix
            if (CurrentTokenType == TokenType.Character && CurrentTokenText == "|")
            {
                foundNamespace = true;
                AppendCurrent();
                NextToken();
            }

            if (CurrentTokenType == TokenType.Identifier
              || (CurrentTokenType == TokenType.Character && CurrentTokenText == "*"))
            {
                // if we already found a namespace, then there was none specified and the
                // element name started with |. Otherwise, save the current ident as a possible
                // namespace identifier
                string identifier = foundNamespace ? null : CurrentTokenText;

                AppendCurrent();
                NextToken();
                parsed = Parsed.True;

                // if the next character is a pipe, then that previous identifier or asterisk
                // was the namespace prefix
                if (!foundNamespace
                    && CurrentTokenType == TokenType.Character && CurrentTokenText == "|")
                {
                    // throw an error if identifier wasn't prevously defined by @namespace statement
                    ValidateNamespace(identifier);

                    // output the pipe and move to the true element name
                    AppendCurrent();
                    NextToken();

                    // a namespace and the bar character should ALWAYS be followed by
                    // either an identifier or an asterisk
                    if (CurrentTokenType == TokenType.Identifier
                        || (CurrentTokenType == TokenType.Character && CurrentTokenText == "*"))
                    {
                        AppendCurrent();
                        NextToken();
                    }
                    else
                    {
                        // we have an error condition
                        parsed = Parsed.False;
                        // handle the error
                        ReportError(0, StringEnum.ExpectedIdentifier, CurrentTokenText);
                    }
                }
            }
            else if (foundNamespace)
            {
                // we had found an empty namespace, but no element or universal following it!
                // handle the error
                ReportError(0, StringEnum.ExpectedIdentifier, CurrentTokenText);
            }

            return parsed;
        }

        private Parsed ParseAttrib()
        {
            Parsed parsed = Parsed.False;
            if (CurrentTokenType == TokenType.Character
              && CurrentTokenText == "[")
            {
                Append('[');
                SkipSpace();

                bool foundNamespace = false;
                
                // must be either an identifier, an asterisk, or a namespace separator
                if (CurrentTokenType == TokenType.Character && CurrentTokenText == "|")
                {
                    // has an empty namespace
                    foundNamespace = true;
                    AppendCurrent();
                    NextToken();
                }

                if (CurrentTokenType == TokenType.Identifier
                    || (CurrentTokenType == TokenType.Character && CurrentTokenText == "*"))
                {
                    // if we already found a namespace, then there was none specified and the
                    // element name started with |. Otherwise, save the current ident as a possible
                    // namespace identifier
                    string identifier = foundNamespace ? null : CurrentTokenText;

                    AppendCurrent();
                    SkipSpace();

                    // check to see if that identifier is actually a namespace because the current
                    // token is a namespace separator
                    if (!foundNamespace 
                        && CurrentTokenType == TokenType.Character && CurrentTokenText == "|")
                    {
                        // namespaced attribute
                        // throw an error if the namespace hasn't previously been defined by a @namespace statement
                        ValidateNamespace(identifier);

                        // output the pipe and move to the next token,
                        // which should be the attribute name
                        AppendCurrent();
                        SkipSpace();

                        // must be either an identifier or an asterisk
                        if (CurrentTokenType == TokenType.Identifier
                            || (CurrentTokenType == TokenType.Character && CurrentTokenText == "*"))
                        {
                            // output the namespaced attribute name
                            AppendCurrent();
                            SkipSpace();
                        }
                        else
                        {
                            ReportError(0, StringEnum.ExpectedIdentifier, CurrentTokenText);
                        }
                    }
                }
                else
                {
                    // neither an identifier nor an asterisk
                    ReportError(0, StringEnum.ExpectedIdentifier, CurrentTokenText);
                }

                // check to see if there's an (optional) attribute operator
                if ((CurrentTokenType == TokenType.Character && CurrentTokenText == "=")
                  || (CurrentTokenType == TokenType.Includes)
                  || (CurrentTokenType == TokenType.DashMatch)
                  || (CurrentTokenType == TokenType.PrefixMatch)
                  || (CurrentTokenType == TokenType.SuffixMatch)
                  || (CurrentTokenType == TokenType.SubstringMatch))
                {
                    AppendCurrent();
                    SkipSpace();

                    if (CurrentTokenType != TokenType.Identifier
                      && CurrentTokenType != TokenType.String)
                    {
                        ReportError(0, StringEnum.ExpectedIdentifierOrString, CurrentTokenText);
                    }

                    AppendCurrent();
                    SkipSpace();
                }

                if (CurrentTokenType != TokenType.Character
                  || CurrentTokenText != "]")
                {
                    ReportError(0, StringEnum.ExpectedClosingBracket, CurrentTokenText);
                }

                // we're done!
                Append(']');
                NextToken();
                parsed = Parsed.True;
            }
            return parsed;
        }

        private Parsed ParsePseudo()
        {
            Parsed parsed = Parsed.False;
            if (CurrentTokenType == TokenType.Character
              && CurrentTokenText == ":")
            {
                Append(':');
                NextToken();

                // CSS3 has pseudo-ELEMENTS that are specified with a double-colon.
                // IF we find a double-colon, we will treat it exactly the same as if it were a pseudo-CLASS.
                if (CurrentTokenType == TokenType.Character && CurrentTokenText == ":")
                {
                    Append(':');
                    NextToken();
                }

                switch (CurrentTokenType)
                {
                    case TokenType.Identifier:
                        AppendCurrent();
                        NextToken();
                        break;

                    case TokenType.Not:
                        AppendCurrent();
                        SkipSpace();
                        // the argument of a NOT operator is a simple selector
                        parsed = ParseSimpleSelector();
                        if (parsed != Parsed.True)
                        {
                            // TODO: error? shouldn't we ALWAYS have a simple select inside a not() function?
                        }

                        // skip any whitespace if we have it
                        SkipIfSpace();

                        // don't forget the closing paren
                        if (CurrentTokenType != TokenType.Character
                          || CurrentTokenText != ")")
                        {
                            ReportError(0, StringEnum.ExpectedIdentifier, CurrentTokenText);
                        }
                        AppendCurrent();
                        NextToken();
                        break;

                    case TokenType.Function:
                        AppendCurrent();
                        SkipSpace();

                        // parse the function argument expression
                        ParseExpression();

                        if (CurrentTokenType != TokenType.Character
                          || CurrentTokenText != ")")
                        {
                            ReportError(0, StringEnum.ExpectedIdentifier, CurrentTokenText);
                        }
                        AppendCurrent();
                        NextToken();
                        break;

                    default:
                        ReportError(0, StringEnum.ExpectedIdentifier, CurrentTokenText);
                        break;
                }
                parsed = Parsed.True;
            }
            return parsed;
        }

        private Parsed ParseExpression()
        {
            Parsed parsed = Parsed.Empty;
            while(true)
            {
                switch(CurrentTokenType)
                {
                    case TokenType.Dimension:
                    case TokenType.Number:
                    case TokenType.String:
                    case TokenType.Identifier:
                        // just output these token types
                        parsed = Parsed.True;
                        AppendCurrent();
                        NextToken();
                        break;

                    case TokenType.Space:
                        // ignore spaces
                        NextToken();
                        break;

                    case TokenType.Character:
                        if (CurrentTokenText == "+" || CurrentTokenText == "-")
                        {
                            parsed = Parsed.True;
                            AppendCurrent();
                            NextToken();
                        }
                        else
                        {
                            // anything else and we exit
                            return parsed;
                        }
                        break;

                    default:
                        // anything else and we bail
                        return parsed;
                }
            }
        }

        private Parsed ParseDeclaration()
        {
            Parsed parsed = Parsed.Empty;

            // see if the developer is using an IE hack of prefacing property names
            // with an asterisk -- IE seems to ignore it; other browsers will recognize
            // the invalid property name and ignore it.
            string prefix = null;
            if (CurrentTokenType == TokenType.Character 
                && (CurrentTokenText == "*" || CurrentTokenText == "."))
            {
                // spot a low-pri error because this is actually invalid CSS
                // taking advantage of an IE "feature"
                ReportError(4, StringEnum.HackGeneratesInvalidCSS, CurrentTokenText);

                // save the prefix and skip it
                prefix = CurrentTokenText;
                NextToken();
            }

            if (CurrentTokenType == TokenType.Identifier)
            {
                // save the property name
                string propertyName = CurrentTokenText;

                NewLine();
                if (prefix != null)
                {
                    Append(prefix);
                }
                AppendCurrent();

                // we want to skip space BUT we want to preserve a space if there is a whitespace character
                // followed by a comment. So don't call the simple SkipSpace method -- that will output the
                // comment but ignore all whitespace.
                SkipSpaceComment();

                if (CurrentTokenType != TokenType.Character
                  || CurrentTokenText != ":")
                {
                    ReportError(0, StringEnum.ExpectedColon, CurrentTokenText);
                    SkipToEndOfDeclaration();
                    return Parsed.True;
                }
                Append(':');
                if (Settings.OutputMode == OutputMode.MultipleLines)
                {
                    Append(' ');
                }
                SkipSpace();

                if (m_valueReplacement != null)
                {
                    // output the replacement string
                    Append(m_valueReplacement);

                    // clear the replacement string
                    m_valueReplacement = null;

                    // set the no-output flag, parse the expression, the reset the flag.
                    // we don't care if ParsExpr actually found an expression or not
                    m_noOutput = true;
                    ParseExpr();
                    m_noOutput = false;
                }
                else 
                {
                    m_parsingColorValue = MightContainColorNames(propertyName);
                    parsed = ParseExpr();
                    m_parsingColorValue = false;

                    if (parsed != Parsed.True)
                    {
                        ReportError(0, StringEnum.ExpectedExpression, CurrentTokenText);
                        SkipToEndOfDeclaration();
                        return Parsed.True;
                    }
                }

                // optional
                ParsePrio();

                parsed = Parsed.True;
            }
            return parsed;
        }

        private Parsed ParsePrio()
        {
            Parsed parsed = Parsed.False;
            if (CurrentTokenType == TokenType.ImportantSymbol)
            {
                if (Settings.OutputMode == OutputMode.MultipleLines)
                {
                    Append(' ');
                }
                AppendCurrent();
                SkipSpace();

                // a common IE7-and-below hack is to append another ! at the end of !important.
                if (CurrentTokenType == TokenType.Character && CurrentTokenText == "!")
                {
                    ReportError(4, StringEnum.HackGeneratesInvalidCSS, CurrentTokenText);
                    AppendCurrent();
                    SkipSpace();
                }

                parsed = Parsed.True;
            }
            else if (CurrentTokenType == TokenType.Character && CurrentTokenText == "!")
            {
                // another common IE7-and-below hack is to use an identifier OTHER than "important". All other browsers will see this
                // as an error, but IE7 and below will keep on processing. A common thing is to put !ie at the end to mark
                // the declaration as only for IE.
                if (Settings.OutputMode == OutputMode.MultipleLines)
                {
                    Append(' ');
                }

                AppendCurrent();
                NextToken();
                if (CurrentTokenType == TokenType.Identifier)
                {
                    ReportError(4, StringEnum.HackGeneratesInvalidCSS, CurrentTokenText);

                    AppendCurrent();
                    SkipSpace();
                    parsed = Parsed.True;
                }
                else
                {
                    // but we need SOME identifier here....
                    ReportError(0, StringEnum.ExpectedIdentifier, CurrentTokenText);
                }
            }
            return parsed;
        }

        private Parsed ParseExpr()
        {
            Parsed parsed = ParseTerm(false);
            if (parsed == Parsed.True)
            {
                while (!m_scanner.EndOfFile)
                {
                    Parsed parsedOp = ParseOperator();
                    if (parsedOp != Parsed.False)
                    {
                        if (ParseTerm(parsedOp == Parsed.Empty) == Parsed.False)
                        {
                            break;
                        }
                    }
                }
            }
            return parsed;
        }

        private Parsed ParseFunctionParameters()
        {
            Parsed parsed = ParseTerm(false);
            if (parsed == Parsed.True)
            {
                while (!m_scanner.EndOfFile)
                {
                    if (CurrentTokenType == TokenType.Character
                      && CurrentTokenText == "=")
                    {
                        AppendCurrent();
                        SkipSpace();
                        ParseTerm(false);
                    }

                    Parsed parsedOp = ParseOperator();
                    if (parsedOp != Parsed.False)
                    {
                        if (ParseTerm(parsedOp == Parsed.Empty) == Parsed.False)
                        {
                            break;
                        }
                    }
                }
            }
            else if (parsed == Parsed.False
              && CurrentTokenType == TokenType.Character
              && CurrentTokenText == ")")
            {
                // it's okay to have no parameters in functions
                parsed = Parsed.Empty;
            }
            return parsed;
        }

        private Parsed ParseTerm(bool wasEmpty)
        {
            Parsed parsed = Parsed.False;
            bool hasUnary = false;
            if (CurrentTokenType == TokenType.Character
              && (CurrentTokenText == "-" || CurrentTokenText == "+"))
            {
                if (wasEmpty)
                {
                    Append(' ');
                    wasEmpty = false;
                }
                AppendCurrent();
                NextToken();
                hasUnary = true;
            }

            switch (CurrentTokenType)
            {
                case TokenType.Hash:
                    if (hasUnary)
                    {
                        ReportError(0, StringEnum.HashAfterUnaryNotAllowed, CurrentTokenText);
                    }

                    if (wasEmpty)
                    {
                        Append(' ');
                        wasEmpty = false;
                    }
                    if (ParseHexcolor() == Parsed.False)
                    {
                        ReportError(0, StringEnum.ExpectedHexColor, CurrentTokenText);

                        // we expected the hash token to be a proper color -- but it's not.
                        // we threw an error -- go ahead and output the token as-is and keep going.
                        AppendCurrent();
                        NextToken();
                    }
                    parsed = Parsed.True;
                    break;

                case TokenType.String:
                case TokenType.Identifier:
                case TokenType.Uri:
                //case TokenType.RGB:
                case TokenType.UnicodeRange:
                    if (hasUnary)
                    {
                        ReportError(0, StringEnum.TokenAfterUnaryNotAllowed, CurrentTokenText);
                    }

                    if (wasEmpty)
                    {
                        Append(' ');
                        wasEmpty = false;
                    }
                    AppendCurrent();
                    SkipSpace();
                    parsed = Parsed.True;
                    break;

                case TokenType.Dimension:
                    ReportError(2, StringEnum.UnexpectedDimension, CurrentTokenText);
                    goto case TokenType.Number;

                case TokenType.Number:
                case TokenType.Percentage:
                case TokenType.AbsoluteLength:
                case TokenType.RelativeLength:
                case TokenType.Angle:
                case TokenType.Time:
                case TokenType.Frequency:
                case TokenType.Resolution:
                    if (wasEmpty)
                    {
                        Append(' ');
                        wasEmpty = false;
                    }

                    AppendCurrent();
                    SkipSpace();
                    parsed = Parsed.True;
                    break;

                case TokenType.ProgId:
                    if (wasEmpty)
                    {
                        Append(' ');
                        wasEmpty = false;
                    }
                    if (ParseProgId() == Parsed.False)
                    {
                        ReportError(0, StringEnum.ExpectedProgId, CurrentTokenText);
                    }
                    parsed = Parsed.True;
                    break;

                case TokenType.Function:
                    if (wasEmpty)
                    {
                        Append(' ');
                        wasEmpty = false;
                    }
                    if (ParseFunction() == Parsed.False)
                    {
                        ReportError(0, StringEnum.ExpectedFunction, CurrentTokenText);
                    }
                    parsed = Parsed.True;
                    break;

                default:
                    if (hasUnary)
                    {
                        ReportError(0, StringEnum.UnexpectedToken, CurrentTokenText);
                    }
                    break;
            }
            return parsed;
        }

        private Parsed ParseProgId()
        {
            Parsed parsed = Parsed.False;
            if (CurrentTokenType == TokenType.ProgId)
            {
                ReportError(4, StringEnum.ProgIdIEOnly);

                // append the progid and opening paren
                AppendCurrent();
                SkipSpace();

                // the rest is a series of parameters: name=value, separated
                // by commas and ending with a close paren
                while (CurrentTokenType == TokenType.Identifier)
                {
                    AppendCurrent();
                    SkipSpace();

                    if (CurrentTokenType != TokenType.Character
                      && CurrentTokenText != "=")
                    {
                        ReportError(0, StringEnum.ExpectedEqualSign, CurrentTokenText);
                    }

                    Append('=');
                    SkipSpace();

                    if (ParseTerm(false) != Parsed.True)
                    {
                        ReportError(0, StringEnum.ExpectedTerm, CurrentTokenText);
                    }

                    if (CurrentTokenType == TokenType.Character
                      && CurrentTokenText == ",")
                    {
                        Append(',');
                        SkipSpace();
                    }
                }

                // make sure we're at the close paren
                if (CurrentTokenType == TokenType.Character
                  && CurrentTokenText == ")")
                {
                    Append(')');
                    SkipSpace();
                }
                else
                {
                    ReportError(0, StringEnum.UnexpectedToken, CurrentTokenText);
                }
                parsed = Parsed.True;
            }
            return parsed;
        }

        private Parsed ParseFunction()
        {
            Parsed parsed = Parsed.False;
            if (CurrentTokenType == TokenType.Function)
            {
                bool crunchedRGB = false;
                if (CurrentTokenText == "rgb(")
                {
                    // rgb function parsing
                    bool useRGB = false;
                    // converting to #rrggbb or #rgb IF we don't find any significant comments!
                    // skip any space or comments
                    int[] rgb = new int[3];

                    // we're going to be building up the rgb function just in case we need it
                    StringBuilder sbRGB = new StringBuilder();
                    sbRGB.Append("rgb(");

                    string comments = NextSignificantToken();
                    if (comments.Length > 0)
                    {
                        // add the comments
                        sbRGB.Append(comments);
                        // and signal that we need to use the RGB function because of them
                        useRGB = true;
                    }
                    for (int ndx = 0; ndx < 3; ++ndx)
                    {
                        // if this isn't the first number, we better find a comma separator
                        if (ndx > 0)
                        {
                            if (CurrentTokenType != TokenType.Character
                              || CurrentTokenText != ",")
                            {
                                ReportError(0, StringEnum.ExpectedComma, CurrentTokenText);
                            }
                            // add it to the rgb string builder
                            sbRGB.Append(',');

                            // skip to the next significant
                            comments = NextSignificantToken();
                            if (comments.Length > 0)
                            {
                                // add the comments
                                sbRGB.Append(comments);
                                // and signal that we need to use the RGB function because of them
                                useRGB = true;
                            }
                        }

                        // although we ALLOW negative numbers here, we'll trim them
                        // later. But in the mean time, save a negation flag.
                        bool negateNumber = false;
                        if (CurrentTokenType == TokenType.Character
                          && CurrentTokenText == "-")
                        {
                            negateNumber = true;
                            comments = NextSignificantToken();
                            if (comments.Length > 0)
                            {
                                // add the comments
                                sbRGB.Append(comments);
                                // and signal that we need to use the RGB function because of them
                                useRGB = true;
                            }
                        }

                        if (CurrentTokenType != TokenType.Number
                          && CurrentTokenType != TokenType.Percentage)
                        {
                            ReportError(0, StringEnum.ExpectedRgbNumberOrPercentage, CurrentTokenText);
                        }

                        // we might adjust the value, so save the token text
                        string tokenText = CurrentTokenText;
                        if (CurrentTokenType == TokenType.Number)
                        {
                            // get the number value
                            float numberValue = System.Convert.ToSingle(tokenText, CultureInfo.InvariantCulture) * (negateNumber ? -1 : 1);
                            // make sure it's between 0 and 255
                            if (numberValue < 0)
                            {
                                tokenText = "0";
                                rgb[ndx] = 0;
                            }
                            else if (numberValue > 255)
                            {
                                tokenText = "255";
                                rgb[ndx] = 255;
                            }
                            else
                            {
                                rgb[ndx] = System.Convert.ToInt32(numberValue);
                            }
                        }
                        else
                        {
                            // percentage
                            float percentageValue = System.Convert.ToSingle(tokenText.Substring(0, tokenText.Length - 1), CultureInfo.InvariantCulture)
                            * (negateNumber ? -1 : 1);
                            if (percentageValue < 0)
                            {
                                tokenText = "0%";
                                rgb[ndx] = 0;
                            }
                            else if (percentageValue > 100)
                            {
                                tokenText = "100%";
                                rgb[ndx] = 255;
                            }
                            else
                            {
                                rgb[ndx] = System.Convert.ToInt32(percentageValue * 255 / 100);
                            }
                        }

                        // add the number to the rgb string builder
                        sbRGB.Append(tokenText);

                        // skip to the next significant
                        comments = NextSignificantToken();
                        if (comments.Length > 0)
                        {
                            // add the comments
                            sbRGB.Append(comments);
                            // and signal that we need to use the RGB function because of them
                            useRGB = true;
                        }
                    }

                    if (useRGB)
                    {
                        // something prevented us from collapsing the rgb function
                        // just output the rgb function we've been building up
                        Append(sbRGB.ToString());
                    }
                    else
                    {
                        // we can collapse it to either #rrggbb or #rgb
                        // calculate the full hex string and crunch it
                        string fullCode = string.Format(CultureInfo.InvariantCulture, "#{0:x2}{1:x2}{2:x2}", rgb[0], rgb[1], rgb[2]);
                        string hexString = CrunchHexColor(fullCode, Settings.ColorNames);
                        Append(hexString);

                        // set the flag so we know we don't want to add the closing paren
                        crunchedRGB = true;
                    }
                }
                else if (CurrentTokenText == "expression(")
                {
                    AppendCurrent();
                    NextToken();

                    // for now, just echo out everything up to the matching closing paren, 
                    // taking into account that there will probably be other nested paren pairs. 
                    // The content of the expression is JavaScript, so we'd really
                    // need a full-blown JS-parser to crunch it properly. Kinda scary.
                    // Start the parenLevel at 0 because the "expression(" token contains the first paren.
                    var jsBuilder = new StringBuilder();
                    int parenLevel = 0;

                    while (!m_scanner.EndOfFile
                      && (CurrentTokenType != TokenType.Character
                        || CurrentTokenText != ")"
                        || parenLevel > 0))
                    {
                        if (CurrentTokenType == TokenType.Function)
                        {
                            // the function token INCLUDES the opening parenthesis,
                            // so up the paren level whenever we find a function.
                            // AND this includes the actual expression( token -- so we'll
                            // hit this branch at the beginning. Make sure the parenLevel
                            // is initialized to take that into account
                            ++parenLevel;
                        }
                        else if (CurrentTokenType == TokenType.Character)
                        {
                            switch (CurrentTokenText)
                            {
                                case "(":
                                    // start a nested paren
                                    ++parenLevel;
                                    break;

                                case ")":
                                    // end a nested paren 
                                    // (we know it's nested because if it wasn't, we wouldn't
                                    // have entered the loop)
                                    --parenLevel;
                                    break;
                            }
                        }
                        jsBuilder.Append(CurrentTokenText);
                        NextToken();
                    }

                    // create a JSParser object with the source we found, crunch it, and send 
                    // the minified script to the output
                    var expressionCode = jsBuilder.ToString();
                    if (Settings.MinifyExpressions)
                    {
                        // we want to minify the javascript expressions.
                        // create a JSParser object from the code we parsed.
                        JSParser jsParser = new JSParser(expressionCode);

                        // copy the file context
                        jsParser.FileContext = this.FileContext;

                        // hook the error handler and set the "contains errors" flag to false.
                        // the handler will set the value to true if it encounters any errors
                        jsParser.CompilerError += OnScriptError;
                        m_expressionContainsErrors = false;

                        // parse the source as an expression using our common JS settings
                        Block block = jsParser.ParseExpression(m_jsSettings);

                        // if we got back a parsed block and there were no errors, output the minified code.
                        // if we didn't get back the block, or if there were any errors at all, just output
                        // the raw expression source.
                        if (block != null && !m_expressionContainsErrors)
                        {
                            Append(block.ToCode());
                        }
                        else
                        {
                            Append(expressionCode);
                        }
                    }
                    else
                    {
                        // we don't want to minify expression code for some reason.
                        // just output the code exactly as we parsed it
                        Append(expressionCode);
                    }
                }
                else
                {
                    // generic function parsing
                    AppendCurrent();
                    SkipSpace();

                    if (ParseFunctionParameters() == Parsed.False)
                    {
                        ReportError(0, StringEnum.ExpectedExpression, CurrentTokenText);
                    }
                }

                if (CurrentTokenType == TokenType.Character
                  && CurrentTokenText == ")")
                {
                    if (!crunchedRGB)
                    {
                        Append(')');
                    }
                    SkipSpace();
                }
                else
                {
                    ReportError(0, StringEnum.UnexpectedToken, CurrentTokenText);
                }
                parsed = Parsed.True;
            }
            return parsed;
        }

        private void OnScriptError(object sender, JScriptExceptionEventArgs ea)
        {
            ReportError(0, StringEnum.ExpressionError, ea.Error.Message);
            m_expressionContainsErrors = true;
        }

        private Parsed ParseHexcolor()
        {
            Parsed parsed = Parsed.False;

            // valid hash colors are #rgb, #rrggbb, and #aarrggbb.
            // we won't do any conversion on the #aarrggbb formats to make them smaller.
            if (CurrentTokenType == TokenType.Hash
              && (CurrentTokenText.Length == 4 || CurrentTokenText.Length == 7 || CurrentTokenText.Length == 9))
            {
                parsed = Parsed.True;

                string hexColor = CrunchHexColor(CurrentTokenText, Settings.ColorNames);

                // this is a dumb error message -- this is the sort of the thing the minimizer should
                // handle automatically. Maybe throw this alert if in analyze mode.
                /*if (hexColor.Length < CurrentTokenText.Length)
                {
                    // report a warning
                    ReportError(4, StringEnum.ColorCanBeCollapsed, CurrentTokenText, hexColor);
                }*/

                Append(hexColor);
                SkipSpace();
            }
            return parsed;
        }

        #endregion

        #region Next... methods

        // skip to the next token, but output any comments we may find as we go along
        private TokenType NextToken()
        {
            m_currentToken = m_scanner.NextToken();
            while (CurrentTokenType == TokenType.Comment)
            {
                // the append statement might not actually append anything.
                // if it doesn't, we don't need to output a newline
                if (AppendCurrent())
                {
                    NewLine();
                }
                m_currentToken = m_scanner.NextToken();
            }
            return CurrentTokenType;
        }

        // just skip to the next token; don't skip over comments
        private TokenType NextRawToken()
        {
            m_currentToken = m_scanner.NextToken();
            return CurrentTokenType;
        }

        private string NextSignificantToken()
        {
            // MOST of the time we won't need to save anything,
            // so don't bother allocating a string builder unless we need it
            StringBuilder sb = null;

            // get the next token
            m_currentToken = m_scanner.NextToken();
            while (CurrentTokenType == TokenType.Space || CurrentTokenType == TokenType.Comment)
            {
                // if this token is a comment, add it to the builder
                if (CurrentTokenType == TokenType.Comment)
                {
                    // check for important comment
                    string commentText = CurrentTokenText;
                    bool importantComment = commentText.StartsWith("/*!", StringComparison.Ordinal);
                    if (importantComment)
                    {
                        // get rid of the exclamation mark in some situations
                        commentText = NormalizeImportantComment(commentText);
                    }

                    // if the comment mode is none, don't ever output it.
                    // if the comment mode is all, always output it.
                    // otherwise only output it if it is an important comment.
                    bool writeComment = Settings.CommentMode == CssComment.All
                        || (importantComment && Settings.CommentMode != CssComment.None);

                    if (!importantComment)
                    {
                        // see if this is a value-replacement id
                        Match match = s_valueReplacement.Match(commentText);
                        if (match.Success)
                        {
                            // check all the resource strings objects to see if one is a match.
                            m_valueReplacement = null;

                            var resourceList = Settings.ResourceStrings;
                            if (resourceList != null && resourceList.Count > 0)
                            {
                                // get the id of the string we want to substitute
                                string ident = match.Result("${id}");

                                // walk the list BACKWARDS so later resource string objects override previous ones
                                for (var ndx = resourceList.Count - 1; ndx >= 0; --ndx)
                                {
                                    m_valueReplacement = resourceList[ndx][ident];
                                    if (m_valueReplacement != null)
                                    {
                                        break;
                                    }
                                }
                            }

                            // if there is such a string, we will have saved the value in the value replacement
                            // variable so it will be substituted for the next value.
                            // if there is no such string, we ALWAYS want to output the comment so we know 
                            // there was a problem (even if the comments mode is to output none)
                            writeComment = m_valueReplacement == null;
                            if (writeComment)
                            {
                                // make sure the comment is normalized
                                commentText = NormalizedValueReplacementComment(commentText);
                            }
                        }
                    }

                    if (writeComment)
                    {
                        // if we haven't yet allocated a string builder, do it now
                        if (sb == null)
                        {
                            sb = new StringBuilder();
                        }

                        // add the comment to the builder
                        sb.Append(commentText);
                    }
                }

                // next token
                m_currentToken = m_scanner.NextToken();
            }

            // return any comments we found in the mean time
            return (sb == null ? string.Empty : sb.ToString());
        }

        #endregion

        #region Skip... methods

        /// <summary>
        /// This method advances to the next token FIRST -- effectively skipping the current one -- 
        /// and then skips any space tokens that FOLLOW it.
        /// </summary>
        private void SkipSpace()
        {
            // reset the skipped-space flag
            m_skippedSpace = false;

            // move to the next token
            NextToken();
            // while space, keep stepping
            while (CurrentTokenType == TokenType.Space)
            {
                m_skippedSpace = true;
                NextToken();
            }
        }

        private void SkipSpaceComment()
        {
            // reset the skipped-space flag
            m_skippedSpace = false;

            // move to the next token
            if (NextRawToken() == TokenType.Space)
            {
                // starts with whitespace! If the next token is a comment, we want to make sure that
                // whitespace is preserved. Keep going until we find something that isn't a space
                m_skippedSpace = true;
                while (NextRawToken() == TokenType.Space)
                {
                    // iteration is in the condition
                }

                // now, if the first thing after space is a comment....
                if (CurrentTokenType == TokenType.Comment)
                {
                    // preserve the space character IF we're going to keep the comment.
                    // SO, if the comment mode is ALL, or if this is an important comment,
                    // (if the comment mode is hacks, then this comment will probably have already
                    // been changed into an important comment), then we output the space
                    // and the comment (don't bother outputting the comment if we already know we
                    // aren't going to)
                    if (Settings.CommentMode == CssComment.All
                        || CurrentTokenText.StartsWith("/*!", StringComparison.Ordinal))
                    {
                        Append(' ');

                        // output the comment
                        AppendCurrent();
                    }

                    // and do normal skip-space logic
                    SkipSpace();
                }
            }
            else if (CurrentTokenType == TokenType.Comment)
            {
                // doesn't start with whitespace.
                // append the comment and then do the normal skip-space logic
                AppendCurrent();
                SkipSpace();
            }
        }

        /// <summary>
        /// This method only skips the space that is already the current token.
        /// </summary>
        /// <returns>true if space was skipped; false if the current token is not space</returns>
        private bool SkipIfSpace()
        {
            // reset the skipped-space flag
            m_skippedSpace = false;

            bool tokenIsSpace = CurrentTokenType == TokenType.Space;
            // while space, keep stepping
            while (CurrentTokenType == TokenType.Space)
            {
                m_skippedSpace = true;
                NextToken();
            }
            return tokenIsSpace;
        }

        private void SkipToEndOfStatement()
        {
            bool possibleSpace = false;
            // skip to next semicolon or next block
            // AND honor opening/closing pairs of (), [], and {}
            while (!m_scanner.EndOfFile
                && (CurrentTokenType != TokenType.Character || CurrentTokenText != ";"))
            {
                // if the token is one of the characters we need to match closing characters...
                if (CurrentTokenType == TokenType.Character
                    && (CurrentTokenText == "(" || CurrentTokenText == "[" || CurrentTokenText == "{"))
                {
                    // see if this is this a block -- if so, we'll bail when we're done
                    bool isBlock = (CurrentTokenText == "{");

                    SkipToClose();

                    // if that was a block, bail now
                    if (isBlock)
                    {
                        return;
                    }
                    possibleSpace = false;
                }
                if (CurrentTokenType == TokenType.Space)
                {
                    possibleSpace = true;
                }
                else
                {
                    if (possibleSpace && NeedsSpaceBefore(CurrentTokenText)
                        && NeedsSpaceAfter(m_lastOutputString))
                    {
                        Append(' ');
                    }
                    AppendCurrent();
                    possibleSpace = false;
                }
                NextToken();
            }
        }

        private void SkipToEndOfDeclaration()
        {
            bool possibleSpace = false;
            // skip to end of declaration: ; or }
            // BUT honor opening/closing pairs of (), [], and {}
            while (!m_scanner.EndOfFile
                && (CurrentTokenType != TokenType.Character
              || (CurrentTokenText != ";" && CurrentTokenText != "}")))
            {
                // if the token is one of the characters we need to match closing characters...
                if (CurrentTokenType == TokenType.Character
                    && (CurrentTokenText == "(" || CurrentTokenText == "[" || CurrentTokenText == "{"))
                {
                    SkipToClose();
                    possibleSpace = false;
                }
                if (CurrentTokenType == TokenType.Space)
                {
                    possibleSpace = true;
                }
                else
                {
                    if (possibleSpace && NeedsSpaceBefore(CurrentTokenText)
                        && NeedsSpaceAfter(m_lastOutputString))
                    {
                        Append(' ');
                    }
                    AppendCurrent();
                    possibleSpace = false;
                }
                NextToken();
            }
        }

        private void SkipToClose()
        {
            bool possibleSpace = false;
            string closingText;
            switch (CurrentTokenText)
            {
                case "(":
                    closingText = ")";
                    break;

                case "[":
                    closingText = "]";
                    break;

                case "{":
                    closingText = "}";
                    break;

                default:
                    throw new ArgumentException("invalid closing match");
            }
            if (m_skippedSpace && CurrentTokenText != "{")
            {
                Append(' ');
            }
            AppendCurrent();
            NextToken();

            while (!m_scanner.EndOfFile
                && (CurrentTokenType != TokenType.Character || CurrentTokenText != closingText))
            {
                // if the token is one of the characters we need to match closing characters...
                if (CurrentTokenType == TokenType.Character
                    && (CurrentTokenText == "(" || CurrentTokenText == "[" || CurrentTokenText == "{"))
                {
                    SkipToClose();
                    possibleSpace = false;
                }
                if (CurrentTokenType == TokenType.Space)
                {
                    possibleSpace = true;
                }
                else
                {
                    if (possibleSpace && NeedsSpaceBefore(CurrentTokenText)
                        && NeedsSpaceAfter(m_lastOutputString))
                    {
                        Append(' ');
                    }
                    AppendCurrent();
                    possibleSpace = false;
                }
                NextToken();
            }
        }

        private static bool NeedsSpaceBefore(string text)
        {
            return !("{}()[],;".Contains(text));
        }

        private static bool NeedsSpaceAfter(string text)
        {
            return !("{}()[],;:".Contains(text));
        }

        #endregion

        #region output methods

        private bool AppendCurrent()
        {
            return Append(
                CurrentTokenText, 
                CurrentTokenType);
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Globalization", "CA1308:NormalizeStringsToUppercase")]
        private bool Append(object obj, TokenType tokenType)
        {
            bool outputText = false;
            bool textEndsInEscapeSequence = false;

            // if the no-output flag is true, don't output anything
            // or process value replacement comments
            if (!m_noOutput)
            {
                string text = obj.ToString();
                if (tokenType == TokenType.Identifier || tokenType == TokenType.Dimension)
                {
                    // need to make sure invalid identifier characters are properly escaped
                    StringBuilder escapedBuilder = null;
                    var startIndex = 0;
                    var protectNextHexCharacter = false;

                    // for identifiers, if the first character is a hyphen or an underscore, then it's a prefix
                    // and we want to look at the next character for nmstart. For dimensions, also ignore the minus
                    // sign and the next character will always be a digit.
                    var firstIndex = text[0] == '_' || text[0] == '-' ? 1 : 0;

                    // if the token type is an identifier, we need to make sure the first character
                    // is a proper identifier start, or is escaped. But if it's a dimension, the first
                    // character will be a numeric digit -- which wouldn't be a valid identifier. So
                    // for dimensions, skip the first character -- subsequent numeric characters will
                    // be okay.
                    if (tokenType == TokenType.Identifier)
                    {
                        // the only valid non-escaped first characters are A-Z (and a-z)
                        var firstChar = text[firstIndex];

                        // anything at or above 0x80 is okay for identifiers
                        if (firstChar < 0x80)
                        {
                            // if it's not an a-z or A-Z, we want to escape it
                            if ((firstChar < 'A' || 'Z' < firstChar)
                                && (firstChar < 'a' || 'z' < firstChar))
                            {
                                // invalid first character -- create the string builder
                                escapedBuilder = new StringBuilder();

                                // if we had a prefix, output it
                                if (firstIndex > 0)
                                {
                                    escapedBuilder.Append(text[0]);
                                }

                                // output the escaped first character
                                protectNextHexCharacter = EscapeCharacter(escapedBuilder, text[firstIndex]);
                                textEndsInEscapeSequence = true;
                                startIndex = firstIndex + 1;
                            }
                        }
                    }

                    // loop through remaining characters, escaping any invalid nmchar characters
                    for(var ndx = firstIndex + 1; ndx < text.Length; ++ndx)
                    {
                        char nextChar = text[ndx];

                        // anything at or above 0x80, then it's okay and doesnt need to be escaped
                        if (nextChar < 0x80)
                        {
                            // only -, _, 0-9, a-z, A-Z are allowed without escapes
                            if (nextChar != '-'
                                && nextChar != '_'
                                && ('0' > nextChar || nextChar > '9')
                                && ('a' > nextChar || nextChar > 'z')
                                && ('A' > nextChar || nextChar > 'Z'))
                            {
                                // need to escape this character -- create the builder if we haven't already
                                if (escapedBuilder == null)
                                {
                                    escapedBuilder = new StringBuilder();
                                }

                                // output any okay characters we have so far
                                if (startIndex < ndx)
                                {
                                    // if the first character of the unescaped string is a valid hex digit,
                                    // then we need to add a space so that characer doesn't get parsed as a
                                    // digit in the previous escaped sequence.
                                    // and if the first character is a space, we need to protect it from the
                                    // previous escaped sequence with another space, too.
                                    string unescapedSubstring = text.Substring(startIndex, ndx - startIndex);
                                    if ((protectNextHexCharacter && CssScanner.IsH(unescapedSubstring[0]))
                                        || (textEndsInEscapeSequence && unescapedSubstring[0] == ' '))
                                    {
                                        escapedBuilder.Append(' ');
                                    }

                                    escapedBuilder.Append(unescapedSubstring);
                                }

                                // output the escape sequence for the current character
                                protectNextHexCharacter = EscapeCharacter(escapedBuilder, text[ndx]);
                                textEndsInEscapeSequence = true;

                                // update the start pointer to the next character
                                startIndex = ndx + 1;
                            }
                        }
                    }

                    // if we escaped anything, get the text from what we built
                    if (escapedBuilder != null)
                    {
                        // append whatever is left over
                        if (startIndex < text.Length)
                        {
                            // if the first character of the unescaped string is a valid hex digit,
                            // then we need to add a space so that characer doesn't get parsed as a
                            // digit in the previous escaped sequence.
                            // same for spaces! a trailing space will be part of the escape, so if we need
                            // a real space to follow, need to make sure there are TWO.
                            string unescapedSubstring = text.Substring(startIndex);
                            if ((protectNextHexCharacter && CssScanner.IsH(unescapedSubstring[0])) 
                                || unescapedSubstring[0] == ' ')
                            {
                                escapedBuilder.Append(' ');
                            }

                            escapedBuilder.Append(unescapedSubstring);
                            textEndsInEscapeSequence = false;
                        }

                        // get the full string
                        text = escapedBuilder.ToString();
                    }
                }

                // if it's not a comment, we're going to output it.
                // if it is a comment, we're not going to SAY we've output anything,
                // even if we end up outputting the comment
                var isImportant = false;
                outputText = (tokenType != TokenType.Comment);
                if (!outputText)
                {
                    // we have a comment.
                    // if the comment mode is none, we never want to output it.
                    // if the comment mode is all, then we always want to output it.
                    // otherwise we only want to output if it's an important /*! */ comment
                    if (text.StartsWith("/*!", StringComparison.Ordinal))
                    {
                        // this is an important comment. We will always output it
                        // UNLESS the comment mode is none. If it IS none, bail now.
                        if (Settings.CommentMode == CssComment.None)
                        {
                            return false;    
                        }

                        // this is an important comment that we always want to output
                        // (after we get rid of the exclamation point in some situations)
                        text = NormalizeImportantComment(text);

                        // find the index of the initial / character
                        var indexSlash = text.IndexOf('/');
                        if (indexSlash > 0)
                        {
                            // it's not the first character!
                            // the only time that should happen is if we put a line-feed in front.
                            // if the string builder is empty, or if the LAST character is a \r or \n,
                            // then trim off everything before that opening slash
                            if (m_outputNewLine)
                            {
                                // trim off everything before it
                                text = text.Substring(indexSlash);
                            }
                        }
                    }
                    else
                    {
                        // check to see if it's a special value-replacement comment
                        Match match = s_valueReplacement.Match(CurrentTokenText);
                        if (match.Success)
                        {
                            m_valueReplacement = null;

                            var resourceList = Settings.ResourceStrings;
                            if (resourceList != null && resourceList.Count > 0)
                            {
                                // it is! see if we have a replacement string
                                string id = match.Result("${id}");

                                // if we have resource strings in the settings, check each one for the
                                // id and set the value replacement field to the value.
                                // walk backwards so later objects override earlier ones.
                                for (var ndx = resourceList.Count - 1; ndx >= 0; --ndx)
                                {
                                    m_valueReplacement = resourceList[ndx][id];
                                    if (m_valueReplacement != null)
                                    {
                                        break;
                                    }
                                }
                            }

                            if (m_valueReplacement != null)
                            {
                                // we do. Don't output the comment. Instead, save the value replacement
                                // for the next time we encounter a value
                                return false;
                            }
                            else
                            {
                                // make sure the comment is normalized
                                text = NormalizedValueReplacementComment(text);
                            }
                        }
                        else if (Settings.CommentMode != CssComment.All)
                        {
                            // don't want to output, bail now
                            return false;
                        }
                    }

                    // see if it's still important
                    isImportant = text.StartsWith("/*!", StringComparison.Ordinal);
                }
                else if (m_parsingColorValue
                    && tokenType == TokenType.Identifier
                    && !text.StartsWith("#", StringComparison.Ordinal))
                {
                    bool nameConvertedToHex = false;
                    string lowerCaseText = text.ToLower(CultureInfo.InvariantCulture);
                    string rgbString;

                    switch (Settings.ColorNames)
                    {
                        case CssColor.Hex:
                            // we don't want any color names in our code.
                            // convert ALL known color names to hex, so see if there is a match on
                            // the set containing all the name-to-hex values
                            if (ColorSlice<AllColorNames>.Data.TryGetValue(lowerCaseText, out rgbString))
                            {
                                text = rgbString;
                                nameConvertedToHex = true;
                            }
                            break;

                        case CssColor.Strict:
                            // we only want strict names in our css.
                            // convert all non-strict name to hex, AND any strict names to hex if the hex is
                            // shorter than the name. So check the set that contains all non-strict name-to-hex
                            // values and all the strict name-to-hex values where hex is shorter than name.
                            if (ColorSlice<StrictHexShorterThanNameAndAllNonStrict>.Data.TryGetValue(lowerCaseText, out rgbString))
                            {
                                text = rgbString;
                                nameConvertedToHex = true;
                            }
                            break;

                        case CssColor.Major:
                            // we don't care if there are non-strict color name. So check the set that only
                            // contains name-to-hex pairs where the hex is shorter than the name.
                            if (ColorSlice<HexShorterThanName>.Data.TryGetValue(lowerCaseText, out rgbString))
                            {
                                text = rgbString;
                                nameConvertedToHex = true;
                            }
                            break;
                    }

                    // if we didn't convert the color name to hex, let's see if it is a color
                    // name -- if so, we want to make it lower-case for readability. We don't need
                    // to do this check if our color name setting is hex-only, because we would
                    // have already converted the name if we know about it
                    if (Settings.ColorNames != CssColor.Hex && !nameConvertedToHex
                        && ColorSlice<AllColorNames>.Data.TryGetValue(lowerCaseText, out rgbString))
                    {
                        // the color exists in the table, so we're pretty sure this is a color.
                        // make sure it's lower case
                        text = lowerCaseText;
                    }
                }

                // if the global might-need-space flag is set and the first character we're going to
                // output if a hex digit or a space, we will need to add a space before our text
                if (m_mightNeedSpace
                    && (CssScanner.IsH(text[0]) || text[0] == ' '))
                {
                    if (m_lineLength >= Settings.LineBreakThreshold)
                    {
                        // we want to add whitespace, but we're over the line-length threshold, so
                        // output a line break instead
                        AddNewLine();
                    }
                    else
                    {
                        // output a space on the same line
                        m_parsed.Append(' ');
                        ++m_lineLength;
                    }
                }

                if (tokenType == TokenType.Comment && isImportant)
                {
                    // don't bother resetting line length after this because 
                    // we're going to follow the comment with another blank line
                    // and we'll reset the length at that time
                    AddNewLine();
                }

                if (text == " ")
                {
                    // we are asking to output a space character. At this point, if we are
                    // over the line-length threshold, we can substitute a line break for a space.
                    if (m_lineLength >= Settings.LineBreakThreshold)
                    {
                        AddNewLine();
                    }
                    else
                    {
                        // just output a space, and don't change the newline flag
                        m_parsed.Append(' ');
                        ++m_lineLength;
                    }
                }
                else
                {
                    // normal text
                    // see if we wanted to force a newline
                    if (m_forceNewLine)
                    {
                        // only output a newline if we aren't already on a new line
                        // AND we are in multiple-line mode
                        if (!m_outputNewLine && Settings.OutputMode == OutputMode.MultipleLines)
                        {
                            AddNewLine();
                        }
                        
                        // reset the flag
                        m_forceNewLine = false;
                    }

                    m_parsed.Append(text);
                    m_outputNewLine = false;

                    if (tokenType == TokenType.Comment && isImportant)
                    {
                        AddNewLine();
                        m_lineLength = 0;
                        m_outputNewLine = true;
                    }
                    else
                    {
                        m_lineLength += text.Length;
                    }
                }

                // if the text we just output ENDS in an escape, we might need a space later
                m_mightNeedSpace = textEndsInEscapeSequence;

                // save a copy of the string so we can check the last output
                // string later if we need to
                m_lastOutputString = text;
            }

            return outputText;
        }

        private static bool EscapeCharacter(StringBuilder sb, char character)
        {
            // output the hex value of the escaped character. If it's less than seven digits
            // (the slash followed by six hex digits), we might
            // need to append a space before the next valid character if it is a valid hex digit.
            // (we will always need to append another space after an escape sequence if the next valid character is a space)
            var hex = string.Format(CultureInfo.InvariantCulture, "\\{0:x}", (int)character);
            sb.Append(hex);
            return hex.Length < 7;
        }

        private bool Append(object obj)
        {
            return Append(obj, TokenType.None);
        }

        private void NewLine()
        {
            // if we've output something other than a newline, output one now
            if (Settings.OutputMode == OutputMode.MultipleLines && !m_outputNewLine)
            {
                AddNewLine();
                m_lineLength = 0;
                m_outputNewLine = true;
            }
        }

        /// <summary>
        /// Always add new line to the stream
        /// </summary>
        /// <param name="sb"></param>
        private void AddNewLine()
        {
            if (!m_outputNewLine)
            {
                if (Settings.OutputMode == OutputMode.MultipleLines)
                {
                    m_parsed.AppendLine();

                    var indentSpaces = Settings.TabSpaces;
                    m_lineLength = indentSpaces.Length;
                    if (m_lineLength > 0)
                    {
                        m_parsed.Append(indentSpaces);
                    }
                }
                else
                {
                    m_parsed.Append('\n');
                    m_lineLength = 0;
                }

                m_outputNewLine = true;
            }
        }

        private void Indent()
        {
            // increase the indent level by one
            Settings.Indent();
        }

        private void Unindent()
        {
            // only decrease the indent level by one IF it's greater than zero
            Settings.Unindent();
        }

        #endregion

        #region color methods

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Globalization", "CA1308:NormalizeStringsToUppercase")]
        private static string CrunchHexColor(string hexColor, CssColor colorNames)
        {
            // see if this is a repeated color (#rrggbb) that we can collapse to #rgb
            Match match = s_rrggbb.Match(hexColor);
            if (match.Success)
            {
                // yes -- collapse it and make sure it's lower-case so we don't 
                // have to do any case-insensitive comparisons
                hexColor = string.Format(
                  CultureInfo.InvariantCulture,
                  "#{0}{1}{2}",
                  match.Result("${r}"),
                  match.Result("${g}"),
                  match.Result("${b}")
                  ).ToLower(CultureInfo.InvariantCulture);
            }
            else
            {
                // make sure it's lower-case so we don't have to do any
                // case-insensitive comparisons
                hexColor = hexColor.ToLower(CultureInfo.InvariantCulture);
            }

            if (colorNames != CssColor.Hex)
            {
                // check for the hex values that can be swapped with the W3C color names to save bytes?
                //      #808080 - gray
                //      #008000 - green
                //      #800000 - maroon
                //      #000080 - navy
                //      #808000 - olive
                //      #ffa500 - orange
                //      #800080 - purple
                //      #f00    - red
                //      #c0c0c0 - silver
                //      #008080 - teal
                // (these are the only colors we can use and still validate)
                // if we don't care about validating, there are even more colors that work in all
                // major browsers that would save up some bytes. But if we convert to those names,
                // we'd really need to be able to convert back to make it validate again.
                //
                // if the map contains an entry for this color, then we
                // should use the name instead because it's smaller.
                string colorName;
                if (ColorSlice<StrictNameShorterThanHex>.Data.TryGetValue(hexColor, out colorName))
                {
                    hexColor = colorName;
                }
                else if (colorNames == CssColor.Major)
                {
                    if (ColorSlice<NameShorterThanHex>.Data.TryGetValue(hexColor, out colorName))
                    {
                        hexColor = colorName;
                    }
                }
            }
            return hexColor;
        }

        private static bool MightContainColorNames(string propertyName)
        {
            bool hasColor = (propertyName.EndsWith("color", StringComparison.Ordinal));
            if (!hasColor)
            {
                switch (propertyName)
                {
                    case "background":
                    case "border-top":
                    case "border-right":
                    case "border-bottom":
                    case "border-left":
                    case "border":
                    case "outline":
                        hasColor = true;
                        break;
                }
            }
            return hasColor;
        }

        #endregion

        #region Error methods

        private void ReportError(int severity, StringEnum errorNumber, CssContext context, params object[] arguments)
        {
            // guide: 0 == syntax error
            //        1 == the programmer probably did not intend to do this
            //        2 == this can lead to problems in the future.
            //        3 == this can lead to performance problems
            //        4 == this is just not right

            string message = CssStringMgr.GetString(errorNumber, arguments);
            CssParserException exc = new CssParserException(
                (int)errorNumber,
                severity,
                (context != null) ? context.Start.Line : 0,
                (context != null) ? context.Start.Char : 0,
                message);

            // but warnings we want to just report and carry on
            OnCssError(exc);
        }

        // just use the current context for the error
        private void ReportError(int severity, StringEnum errorNumber, params object[] arguments)
        {
            ReportError(severity, errorNumber, (m_currentToken != null ? m_currentToken.Context : null), arguments);
        }

        public event EventHandler<CssErrorEventArgs> CssError;

        protected void OnCssError(CssException exception)
        {
            if (CssError != null && exception != null)
            {
                // format our CSS error code
                string errorCode = string.Format(CultureInfo.InvariantCulture, "CSS{0}", (exception.Error & (0xffff)));

                // if we have no errors in our error ignore list, or if we do but this error code is not in
                // that list, fire the event to whomever is listening for it.
                if (Settings.IgnoreErrors == null ||
                    !Settings.IgnoreErrors.Contains(errorCode))
                {
                    CssError(this, new CssErrorEventArgs(exception,
                        new ContextError(
                            exception.Severity < 2, 
                            exception.Severity,
                            GetSeverityString(exception.Severity), 
                            errorCode,
                            exception.HelpLink,
                            FileContext, 
                            exception.Line, 
                            exception.Char, 
                            0, 
                            0, 
                            exception.Message)));
                }
            }
        }

        private static string GetSeverityString(int severity)
        {
            // From jscriptexception.js:
            //
            //guide: 0 == there will be a run-time error if this code executes
            //       1 == the programmer probably did not intend to do this
            //       2 == this can lead to problems in the future.
            //       3 == this can lead to performance problems
            //       4 == this is just not right
            switch (severity)
            {
                case 0:
                    return CssStringMgr.GetString(StringEnum.Severity0);

                case 1:
                    return CssStringMgr.GetString(StringEnum.Severity1);

                case 2:
                    return CssStringMgr.GetString(StringEnum.Severity2);

                case 3:
                    return CssStringMgr.GetString(StringEnum.Severity3);

                case 4:
                    return CssStringMgr.GetString(StringEnum.Severity4);

                default:
                    return CssStringMgr.GetString(StringEnum.SeverityUnknown, severity);
            }
        }

        void OnScannerError(object sender, CssScannerErrorEventArgs e)
        {
            OnCssError(e.Exception);
        }

        #endregion

        #region comment methods

        /// <summary>
        /// regular expression for matching newline characters
        /// </summary>
//        private static Regex s_regexNewlines = new Regex(
//            @"\r\n|\f|\r|\n",
//            RegexOptions.IgnoreCase | RegexOptions.Singleline
//#if !SILVERLIGHT
//            | RegexOptions.Compiled
//#endif
//            );

        static string NormalizedValueReplacementComment(string source)
        {
            return s_valueReplacement.Replace(source, "/*[${id}]*/");
        }

        static bool CommentContainsText(string comment)
        {
            for (var ndx = 0; ndx < comment.Length; ++ndx)
            {
                if (char.IsLetterOrDigit(comment[ndx]))
                {
                    return true;
                }
            }

            // if we get here, we didn't find any text characters
            return false;
        }

        string NormalizeImportantComment(string source)
        {
            // if this important comment does not contain any text, assume it's for a comment hack
            // and return a normalized string without the exclamation mark.
            if (CommentContainsText(source))
            {
                // first check to see if the comment is in the form /*!/ ...text... /**/
                // if so, then it's probably a part of the Opera5&NS4-only comment hack and we want
                // to make SURE that exclamation point does not get in the output because it would
                // mess up the results.
                if (source[3] == '/' && source.EndsWith("/**/", StringComparison.Ordinal))
                {
                    // it is. output the comment as-is EXCEPT without the exclamation mark
                    // (and don't put any line-feeds around it)
                    source = "/*" + source.Substring(3);
                }
            }
            else
            {
                // important comment, but it doesn't contain text. So instead, leave it inline
                // (don't add a newline character before it) but take out the exclamation mark.
                source = "/*" + source.Substring(3);
            }

            // if this is single-line mode, make sure CRLF-pairs are all converted to just CR
            if (Settings.OutputMode == OutputMode.SingleLine)
            {
                source = source.Replace("\r\n", "\n");
            }
            return source;
        }
        #endregion

        #region private enums

        private enum Parsed
        {
            True,
            False,
            Empty
        }

        #endregion
    }

    #region public enums

    public enum CssComment
    {
        Important = 0,
        None,
        All,
        Hacks
    }

    public enum CssColor
    {
        Strict = 0,
        Hex,
        Major
    }

    #endregion

    #region custom exceptions

    /// <summary>
    /// Base class for exceptions thrown by the parser or the scanner
    /// </summary>
#if !SILVERLIGHT
    [Serializable]
#endif
    public class CssException : Exception
    {
        private string m_originator;
        public string Originator { get { return m_originator; } }

        private int m_severity;
        public int Severity { get { return m_severity; } }

        private int m_line;
        public int Line { get { return m_line; } }

        private int m_char;
        public int Char { get { return m_char; } }

        private int m_error;
        public int Error { get { return m_error; } }

        internal CssException(int errorNum, string source, int severity, int line, int pos, string message)
            : base(message)
        {
            m_error = errorNum;
            m_originator = source;
            m_severity = severity;
            m_line = line;
            m_char = pos;
        }

        internal CssException(int errorNum, string source, int severity, int line, int pos, string message, Exception innerException)
            : base(message, innerException)
        {
            m_error = errorNum;
            m_originator = source;
            m_severity = severity;
            m_line = line;
            m_char = pos;
        }
        public CssException()
        {
        }

        public CssException(string message)
            : base(message)
        {
        }

        public CssException(string message, Exception innerException)
            : base(message, innerException)
        {
        }

#if !SILVERLIGHT
        protected CssException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
            // make sure parameters are not null
            if (info == null)
            {
                throw new ArgumentNullException("info");
            }

            // base class already called, now get out custom fields
            m_originator = info.GetString("originator");
            m_severity = info.GetInt32("severity");
            m_line = info.GetInt32("line");
            m_char = info.GetInt32("char");
        }

        [SecurityCritical]
        [SecurityPermission(SecurityAction.Demand, SerializationFormatter = true)]
        public override void GetObjectData(
           SerializationInfo info, StreamingContext context)
        {
            // make sure parameters are not null
            if (info == null)
            {
                throw new ArgumentNullException("info");
            }

            // call base class
            base.GetObjectData(info, context);

            // output our custom fields
            info.AddValue("originator", m_originator);
            info.AddValue("severity", m_severity);
            info.AddValue("line", m_line);
            info.AddValue("char", m_char);
        }
#endif
    }

#if !SILVERLIGHT
    [Serializable]
#endif
    public sealed class CssParserException : CssException
    {
        private static readonly string s_originator = CssStringMgr.GetString(StringEnum.ParserSubsystem);

        internal CssParserException(int error, int severity, int line, int pos, string message)
            : base(error, s_originator, severity, line, pos, message)
        {
        }

        public CssParserException()
            : base((int)StringEnum.UnknownError, s_originator, 1, 0, 0, CssStringMgr.GetString(StringEnum.UnknownError))
        {
        }

        public CssParserException(string message)
            : base((int)StringEnum.UnknownError, s_originator, 1, 0, 0, message)
        {
        }

        public CssParserException(string message, Exception innerException)
            : base((int)StringEnum.UnknownError, s_originator, 1, 0, 0, message, innerException)
        {
        }

#if !SILVERLIGHT
        private CssParserException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
        }
#endif
    }

    #endregion

    public class CssErrorEventArgs : EventArgs
    {
        /// <summary>
        /// The error information with the context info.
        /// Use this property going forward
        /// </summary>
        public ContextError Error { get; private set; }

        /// <summary>
        /// The CSS exception object. Don't use this; might go away in future version. Use the Error property instead.
        /// </summary>
        public CssException Exception { get; private set; }

        internal CssErrorEventArgs(CssException exc, ContextError error)
        {
            Error = error;
            Exception = exc;
        }
    }
}