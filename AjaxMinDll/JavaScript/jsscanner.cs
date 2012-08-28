// jsscanner.cs
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
using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;

namespace Microsoft.Ajax.Utilities
{
    public sealed class JSScanner
    {
        // scanner main data
        private string m_strSourceCode;

        private int m_startPos;

        private int m_endPos;

        private int m_currentPos;

        public int CurrentLine { get; private set; }

        public int StartLinePosition { get; private set; }

        // token information
        private Context m_currentToken;

        private String m_escapedString;

        private StringBuilder m_identifier;

        private int m_idLastPosOnBuilder;

        // flags
        public bool GotEndOfLine { get; private set; }

        private bool m_peekModeOn;

        // keyword table
        private JSKeyword[] m_keywords;

        private static readonly JSKeyword[] s_Keywords = JSKeyword.InitKeywords();

        // pre process information
        private bool m_preProcessorOn;

        private int m_ccIfLevel;

        // for pre-processor
        private Dictionary<string, string> m_defines;

        private int m_inIfDefDirectiveLevel;

        public bool UsePreprocessorDefines { get; set; }

        private bool m_inConditionalComment;

        private bool m_inSingleLineComment;

        private bool m_inMultipleLineComment;

        public bool InComment
        {
            get
            {
                return m_inMultipleLineComment || m_inSingleLineComment;
            }
        }

        public event EventHandler<GlobalDefineEventArgs> GlobalDefine;

        /// <summary>
        /// List of important comment contexts we encountered before we found the 
        /// current token
        /// </summary>
        private List<Context> m_importantComments;

        /// <summary>
        /// returns true if we found one or more important comments before the current token
        /// </summary>
        public bool HasImportantComments
        {
            get { return m_importantComments != null && m_importantComments.Count > 0; }
        }

        // a list of strings that we can add new ones to or clear
        // depending on comments we may find in the source
        private IList<string> m_debugLookups;

        public void SetDebugLookupList(IList<string> debugLookups)
        {
            m_debugLookups = debugLookups;
        }

        /// <summary>
        /// Pop the first important comment context off the queue and return it (if any)
        /// </summary>
        /// <returns>next important comment context, null if no more</returns>
        public Context PopImportantComment()
        {
            Context commentContext = null;
            if (HasImportantComments)
            {
                commentContext = m_importantComments[0];
                m_importantComments.RemoveAt(0);
            }
            return commentContext;
        }

        public bool IgnoreConditionalCompilation { get; set; }

        public bool AllowEmbeddedAspNetBlocks { get; set; }

        // turning this property on makes the scanner just return raw tokens without any
        // conditional-compilation comment processing.
        public bool RawTokens { get; set; }

        private JSToken m_previousToken;

        //public Dictionary<JSToken, int> TokenCounts;

        public JSScanner(Context sourceContext)
        {
            m_keywords = s_Keywords;
            m_previousToken = JSToken.None;
            UsePreprocessorDefines = true;
            SetSource(sourceContext);
        }

        public bool SkipDebugBlocks { get; set; }

        public static bool IsKeyword(string name, bool strictMode)
        {
            bool isKeyword = false;

            // get the index into the keywords array by taking the first letter of the string
            // and subtracting the character 'a' from it. Use a negative number if the string
            // is null or empty
            if (!string.IsNullOrEmpty(name))
            {
                int index = name[0] - 'a';

                // only proceed if the index is within the array length
                if (0 <= index && index < s_Keywords.Length)
                {
                    // get the head of the list for this index (if any)
                    JSKeyword keyword = s_Keywords[name[0] - 'a'];
                    if (keyword != null)
                    {
                        // switch off the token
                        switch (keyword.GetKeyword(name, 0, name.Length))
                        {
                            case JSToken.Get:
                            case JSToken.Set:
                            case JSToken.Identifier:
                                // never considered keywords
                                isKeyword = false;
                                break;

                            case JSToken.Implements:
                            case JSToken.Interface:
                            case JSToken.Let:
                            case JSToken.Package:
                            case JSToken.Private:
                            case JSToken.Protected:
                            case JSToken.Public:
                            case JSToken.Static:
                            case JSToken.Yield:
                                // in strict mode, these ARE keywords, otherwise they are okay
                                // to be identifiers
                                isKeyword = strictMode;
                                break;

                            case JSToken.Native:
                            default:
                                // no other tokens can be identifiers.
                                // apparently never allowed for Chrome, so we want to treat it
                                // differently, too
                                isKeyword = true;
                                break;
                        }
                    }
                }
            }

            return isKeyword;
        }

        public void SetSource(Context sourceContext)
        {
            if (sourceContext == null)
            {
                throw new ArgumentException(JScript.InternalCompilerError);
            }

            m_strSourceCode = sourceContext.Document.Source;
            m_startPos = sourceContext.StartPosition;
            StartLinePosition = sourceContext.StartLinePosition;
            m_endPos = (0 < sourceContext.EndPosition && sourceContext.EndPosition < m_strSourceCode.Length) 
                ? sourceContext.EndPosition 
                : m_strSourceCode.Length;
            m_currentToken = sourceContext;
            m_escapedString = null;
            m_identifier = new StringBuilder(128);
            m_idLastPosOnBuilder = 0;
            m_currentPos = m_startPos;
            CurrentLine = (sourceContext.StartLineNumber > 0) ? sourceContext.StartLineNumber : 1;
            GotEndOfLine = false;

            //TokenCounts = new Dictionary<JSToken, int>();
        }

        public void SetPreprocessorDefines(ReadOnlyCollection<string> definedNames)
        {
            // this is a destructive set, blowing away any previous list
            if (definedNames != null && definedNames.Count > 0)
            {
                // create a new list
                m_defines = new Dictionary<string, string>();

                // add an entrty for each non-duplicate, valid name passed to us
                foreach (var definedName in definedNames)
                {
                    var upperCaseName = definedName.ToUpperInvariant();
                    if (JSScanner.IsValidIdentifier(upperCaseName) && !m_defines.ContainsKey(upperCaseName))
                    {
                        m_defines.Add(upperCaseName, upperCaseName);
                    }
                }
            }
            else
            {
                // we have no defined names
                m_defines = null;
            }
        }

        private void OnGlobalDefine(string name)
        {
            if (GlobalDefine != null)
            {
                GlobalDefine(this, new GlobalDefineEventArgs() { Name = name });
            }
        }

        internal JSToken PeekToken()
        {
            int thisCurrentPos = m_currentPos;
            int thisCurrentLine = CurrentLine;
            int thisStartLinePos = StartLinePosition;
            bool thisGotEndOfLine = GotEndOfLine;
            int thisLastPosOnBuilder = m_idLastPosOnBuilder;
            m_peekModeOn = true;
            JSToken token;

            // temporary switch the token
            Context thisCurrentToken = m_currentToken;
            m_currentToken = m_currentToken.Clone();
            try
            {
                GetNextToken();
                token = m_currentToken.Token;
            }
            finally
            {
                m_currentToken = thisCurrentToken;
                m_currentPos = thisCurrentPos;
                CurrentLine = thisCurrentLine;
                StartLinePosition = thisStartLinePos;
                GotEndOfLine = thisGotEndOfLine;
                m_identifier.Length = 0;
                m_idLastPosOnBuilder = thisLastPosOnBuilder;
                m_peekModeOn = false;
                m_escapedString = null;
            }

            return token;
        }

        public void GetNextToken()
        {
            JSToken token = JSToken.None;
            GotEndOfLine = false;
            m_importantComments = null;
            try
            {
                int thisCurrentLine = CurrentLine;

            nextToken:
                // skip any blanks, setting a state flag if we find any
                bool ws = JSScanner.IsBlankSpace(GetChar(m_currentPos));
                if (ws && !RawTokens)
                {
                    // we're not looking for war tokens, so just want to eat the whitespace
                    while (JSScanner.IsBlankSpace(GetChar(++m_currentPos))) ;
                }

                m_currentToken.StartPosition = m_startPos = m_currentPos;
                m_currentToken.StartLineNumber = CurrentLine;
                m_currentToken.StartLinePosition = StartLinePosition;
                char c = GetChar(m_currentPos++);
                switch (c)
                {
                    case (char)0:
                        if (m_currentPos >= m_endPos)
                        {
                            m_currentPos--;
                            token = JSToken.EndOfFile;
                            if (m_ccIfLevel > 0)
                            {
                                m_currentToken.EndLineNumber = CurrentLine;
                                m_currentToken.EndLinePosition = StartLinePosition;
                                m_currentToken.EndPosition = m_currentPos;
                                HandleError(JSError.NoCCEnd);
                            }

                            break;
                        }

                        if (RawTokens)
                        {
                            // if we are just looking for raw tokens, return this one as an error token
                            token = JSToken.Error;
                            break;
                        }
                        else
                        {
                            // otherwise eat it
                            goto nextToken;
                        }

                    case '=':
                        token = JSToken.Assign;
                        if ('=' == GetChar(m_currentPos))
                        {
                            m_currentPos++;
                            token = JSToken.Equal;
                            if ('=' == GetChar(m_currentPos))
                            {
                                m_currentPos++;
                                token = JSToken.StrictEqual;
                            }
                        }

                        break;

                    case '>':
                        token = JSToken.GreaterThan;
                        if ('>' == GetChar(m_currentPos))
                        {
                            m_currentPos++;
                            token = JSToken.RightShift;
                            if ('>' == GetChar(m_currentPos))
                            {
                                m_currentPos++;
                                token = JSToken.UnsignedRightShift;
                            }
                        }

                        if ('=' == GetChar(m_currentPos))
                        {
                            m_currentPos++;
                            token = token == JSToken.GreaterThan
                                ? JSToken.GreaterThanEqual
                                : token == JSToken.RightShift ? JSToken.RightShiftAssign
                                : token == JSToken.UnsignedRightShift ? JSToken.UnsignedRightShiftAssign
                                : token;
                        }

                        break;

                    case '<':
                        if (AllowEmbeddedAspNetBlocks &&
                            '%' == GetChar(m_currentPos))
                        {
                            token = ScanAspNetBlock();
                        }
                        else
                        {
                            token = JSToken.LessThan;
                            if ('<' == GetChar(m_currentPos))
                            {
                                m_currentPos++;
                                token = JSToken.LeftShift;
                            }

                            if ('=' == GetChar(m_currentPos))
                            {
                                m_currentPos++;
                                if (token == JSToken.LessThan)
                                {
                                    token = JSToken.LessThanEqual;
                                }
                                else
                                {
                                    token = JSToken.LeftShiftAssign;
                                }
                            }
                        }

                        break;

                    case '!':
                        token = JSToken.LogicalNot;
                        if ('=' == GetChar(m_currentPos))
                        {
                            m_currentPos++;
                            token = JSToken.NotEqual;
                            if ('=' == GetChar(m_currentPos))
                            {
                                m_currentPos++;
                                token = JSToken.StrictNotEqual;
                            }
                        }

                        break;

                    case ',':
                        token = JSToken.Comma;
                        break;

                    case '~':
                        token = JSToken.BitwiseNot;
                        break;

                    case '?':
                        token = JSToken.ConditionalIf;
                        break;

                    case ':':
                        token = JSToken.Colon;
                        break;

                    case '.':
                        token = JSToken.AccessField;
                        c = GetChar(m_currentPos);
                        if (JSScanner.IsDigit(c))
                        {
                            token = ScanNumber('.');
                        }

                        break;

                    case '&':
                        token = JSToken.BitwiseAnd;
                        c = GetChar(m_currentPos);
                        if ('&' == c)
                        {
                            m_currentPos++;
                            token = JSToken.LogicalAnd;
                        }
                        else if ('=' == c)
                        {
                            m_currentPos++;
                            token = JSToken.BitwiseAndAssign;
                        }

                        break;

                    case '|':
                        token = JSToken.BitwiseOr;
                        c = GetChar(m_currentPos);
                        if ('|' == c)
                        {
                            m_currentPos++;
                            token = JSToken.LogicalOr;
                        }
                        else if ('=' == c)
                        {
                            m_currentPos++;
                            token = JSToken.BitwiseOrAssign;
                        }

                        break;

                    case '+':
                        token = JSToken.Plus;
                        c = GetChar(m_currentPos);
                        if ('+' == c)
                        {
                            m_currentPos++;
                            token = JSToken.Increment;
                        }
                        else if ('=' == c)
                        {
                            m_currentPos++;
                            token = JSToken.PlusAssign;
                        }

                        break;

                    case '-':
                        token = JSToken.Minus;
                        c = GetChar(m_currentPos);
                        if ('-' == c)
                        {
                            m_currentPos++;
                            token = JSToken.Decrement;
                        }
                        else if ('=' == c)
                        {
                            m_currentPos++;
                            token = JSToken.MinusAssign;
                        }

                        break;

                    case '*':
                        token = JSToken.Multiply;
                        if ('=' == GetChar(m_currentPos))
                        {
                            m_currentPos++;
                            token = JSToken.MultiplyAssign;
                        }

                        break;

                    case '\\':
                        // try decoding a unicode escape sequence. We read the backslash and
                        // now the "current" character is the "u"
                        if (PeekUnicodeEscape(m_currentPos, ref c))
                        {
                            // advance past the escape characters
                            m_currentPos += 5;

                            // valid unicode escape sequence
                            if (IsValidIdentifierStart(c))
                            {
                                // use the unescaped character as the first character of the
                                // decoded identifier, and current character is now the last position
                                // on the builder
                                m_identifier.Append(c);
                                m_idLastPosOnBuilder = m_currentPos;

                                // scan the rest of the identifier
                                ScanIdentifier();

                                // because it STARTS with an escaped character it cannot be a keyword
                                token = JSToken.Identifier;
                                break;
                            }
                        }
                        else
                        {
                            // not a valid unicode escape sequence
                            // see if the next character is a valid identifier character
                            if (IsValidIdentifierStart(GetChar(m_currentPos)))
                            {
                                // we're going to just assume this is an escaped identifier character
                                // because some older browsers allow things like \foo ("foo") and 
                                // \while to be an identifer "while" and not the reserved word
                                ScanIdentifier();
                                token = JSToken.Identifier;
                                break;
                            }
                        }

                        HandleError(JSError.IllegalChar);
                        break;

                    case '/':
                        token = JSToken.Divide;
                        c = GetChar(m_currentPos);
                        switch (c)
                        {
                            case '/':
                                m_inSingleLineComment = true;
                                c = GetChar(++m_currentPos);

                                // see if there is a THIRD slash character
                                if (c == '/')
                                {
                                    // advance past the slash
                                    ++m_currentPos;

                                    // check for some AjaxMin preprocessor comments
                                    if (CheckCaseInsensitiveSubstring(m_currentPos, "#DEBUG"))
                                    {
                                        // advance to the next character. If it's an equal sign, then this
                                        // debug comment is setting a debug namespace, not marking debug code.
                                        if (GetChar(m_currentPos += 6) == '=')
                                        {
                                            // we have ///#DEBUG=
                                            // if the debug lookup list is null, 
                                            // then we don't care about trying to add new ones or clear 
                                            // existing list, so just skip over the comment.
                                            if (m_debugLookups != null)
                                            {
                                                // get the namespace after the equal sign
                                                ++m_currentPos;
                                                var identifier = PPScanIdentifier(false);
                                                if (identifier == null)
                                                {
                                                    // nothing. clear the debug namespaces
                                                    m_debugLookups.Clear();
                                                }
                                                else
                                                {
                                                    // see if we have a period and keep looping to get IDENT(.IDENT)*
                                                    while (GetChar(m_currentPos) == '.')
                                                    {
                                                        ++m_currentPos;
                                                        var nextIdentifier = PPScanIdentifier(false);
                                                        if (nextIdentifier != null)
                                                        {
                                                            identifier += '.' + nextIdentifier;
                                                        }
                                                        else
                                                        {
                                                            // problem with the formatting -- ignore this comment
                                                            identifier = null;
                                                            break;
                                                        }
                                                    }

                                                    if (identifier != null)
                                                    {
                                                        // add the identifier to the debug list
                                                        m_debugLookups.Add(identifier);
                                                    }
                                                }
                                            }

                                            // make sure we skip the rest of the line (if any)
                                            // and loop back up for a new token
                                            SkipSingleLineComment();
                                            goto nextToken;
                                        }
                                        
                                        // NOT a debug namespace assignment comment, so this is the start
                                        // of a debug block. If we are skipping debug blocks, start skipping now.
                                        if (SkipDebugBlocks)
                                        {
                                            // skip until we hit ///#ENDDEBUG, but only if we are stripping debug statements
                                            PPSkipToDirective("#ENDDEBUG");

                                            // if we are asking for raw tokens, we DON'T want to return these comments or the code
                                            // they stripped away.
                                            if (RawTokens)
                                            {
                                                SkipSingleLineComment();
                                                goto nextToken;
                                            }
                                        }
                                    }
                                    else if (CheckCaseInsensitiveSubstring(m_currentPos, "#GLOBALS"))
                                    {
                                        // found ///#GLOBALS comment
                                        m_currentPos += 8;
                                        SkipBlanks();

                                        // should be one or more space-separated identifiers
                                        while (!IsLineTerminator(GetChar(m_currentPos), 1))
                                        {
                                            var identifier = PPScanIdentifier(false);
                                            if (identifier != null)
                                            {
                                                OnGlobalDefine(identifier);
                                                SkipBlanks();
                                            }
                                            else
                                            {
                                                // not an identifier -- ignore the rest of the line
                                                SkipSingleLineComment();
                                                goto nextToken;
                                            }
                                        }
                                    }
                                    else if (CheckCaseInsensitiveSubstring(m_currentPos, "#SOURCE"))
                                    {
                                        // found ///#SOURCE comment
                                        m_currentPos += 7;
                                        SkipBlanks();

                                        // pull the line, the column, and the source path off the line
                                        var linePos = 0;
                                        var colPos = 0;

                                        // line number is first
                                        if (!IsLineTerminator(GetChar(m_currentPos), 0))
                                        {
                                            if (!PPScanInteger(out linePos))
                                            {
                                                // not an integer -- skip the rest of the line and move on
                                                SkipSingleLineComment();
                                                goto nextToken;
                                            }

                                            SkipBlanks();
                                        }

                                        // column number is second
                                        if (!IsLineTerminator(GetChar(m_currentPos), 0))
                                        {
                                            if (!PPScanInteger(out colPos))
                                            {
                                                // not an integer -- skip the rest of the line and move on
                                                SkipSingleLineComment();
                                                goto nextToken;
                                            }

                                            SkipBlanks();
                                        }

                                        // the path should be the last part of the line.
                                        // skip to the end and then use the part between.
                                        var ndxStart = m_currentPos;
                                        SkipToEndOfLine();
                                        if (m_currentPos > ndxStart)
                                        {
                                            // there is a non-blank source token.
                                            // so we have the line and the column and the source.
                                            // use them. Remember, though: we stopped BEFORE the line terminator,
                                            // so as we hit it, we're going to add one to the line, so start one line
                                            // LESS than what we want.
                                            m_currentToken.ChangeFileContext(m_strSourceCode.Substring(ndxStart, m_currentPos - ndxStart).TrimEnd());
                                            this.CurrentLine = linePos - 1;
                                            this.StartLinePosition = linePos - colPos - 1;
                                        }

                                        goto nextToken;
                                    }
                                    else if (UsePreprocessorDefines)
                                    {
                                        var testForNot = false;
                                        if (CheckCaseInsensitiveSubstring(m_currentPos, "#IFDEF")
                                            || (testForNot = CheckCaseInsensitiveSubstring(m_currentPos, "#IFNDEF")))
                                        {
                                            // skip past the token and any blanks
                                            m_currentPos += 6 + (testForNot ? 1 : 0);
                                            SkipBlanks();

                                            // if we encountered a line-break here, then ignore this directive
                                            if (!GotEndOfLine)
                                            {
                                                // get an identifier from the input
                                                var identifier = PPScanIdentifier(true);
                                                if (!string.IsNullOrEmpty(identifier))
                                                {
                                                    // set a state so that if we hit an #ELSE directive, we skip to #ENDIF
                                                    ++m_inIfDefDirectiveLevel;

                                                    // if there is a dictionary AND the identifier is in it, then the identifier IS defined.
                                                    // if there is not dictionary OR the identifier is NOT in it, then it is NOT defined.
                                                    var isDefined = (m_defines != null && m_defines.ContainsKey(identifier));

                                                    // see if the condition is true
                                                    var conditionIsTrue = (!testForNot && isDefined) || (testForNot && !isDefined);
                                                    if (!conditionIsTrue)
                                                    {
                                                        // the condition is FALSE!
                                                        // skip to #ELSE or #ENDIF and continue processing normally.
                                                        if (PPSkipToDirective("#ENDIF", "#ELSE") == 0)
                                                        {
                                                            // encountered the #ENDIF directive, so we know to reset the flag
                                                            --m_inIfDefDirectiveLevel;
                                                        }
                                                    }

                                                    // if we are asking for raw tokens, we DON'T want to return these comments or the code
                                                    // they may have stripped away.
                                                    if (RawTokens)
                                                    {
                                                        SkipSingleLineComment();
                                                        goto nextToken;
                                                    }
                                                }
                                            }
                                        }
                                        else if (CheckCaseInsensitiveSubstring(m_currentPos, "#ELSE") && m_inIfDefDirectiveLevel > 0)
                                        {
                                            // reset the state that says we were in an #IFDEF construct
                                            --m_inIfDefDirectiveLevel;

                                            // ...then we now want to skip until the #ENDIF directive
                                            PPSkipToDirective("#ENDIF");

                                            // if we are asking for raw tokens, we DON'T want to return these comments or the code
                                            // they stripped away.
                                            if (RawTokens)
                                            {
                                                SkipSingleLineComment();
                                                goto nextToken;
                                            }
                                        }
                                        else if (CheckCaseInsensitiveSubstring(m_currentPos, "#ENDIF") && m_inIfDefDirectiveLevel > 0)
                                        {
                                            // reset the state that says we were in an #IFDEF construct
                                            --m_inIfDefDirectiveLevel;

                                            // if we are asking for raw tokens, we DON'T want to return this comment.
                                            if (RawTokens)
                                            {
                                                SkipSingleLineComment();
                                                goto nextToken;
                                            }
                                        }
                                        else if (CheckCaseInsensitiveSubstring(m_currentPos, "#DEFINE"))
                                        {
                                            // skip past the token and any blanks
                                            m_currentPos += 7;
                                            SkipBlanks();

                                            // if we encountered a line-break here, then ignore this directive
                                            if (!GotEndOfLine)
                                            {
                                                // get an identifier from the input
                                                var identifier = PPScanIdentifier(true);
                                                if (!string.IsNullOrEmpty(identifier))
                                                {
                                                    // if there is no dictionary of defines yet, create one now
                                                    if (m_defines == null)
                                                    {
                                                        m_defines = new Dictionary<string, string>();
                                                    }

                                                    // if the identifier is not already in the dictionary, add it now
                                                    if (!m_defines.ContainsKey(identifier))
                                                    {
                                                        m_defines.Add(identifier, identifier);
                                                    }

                                                    // if we are asking for raw tokens, we DON'T want to return this comment.
                                                    if (RawTokens)
                                                    {
                                                        SkipSingleLineComment();
                                                        goto nextToken;
                                                    }
                                                }
                                            }
                                        }
                                        else if (CheckCaseInsensitiveSubstring(m_currentPos, "#UNDEF"))
                                        {
                                            // skip past the token and any blanks
                                            m_currentPos += 6;
                                            SkipBlanks();

                                            // if we encountered a line-break here, then ignore this directive
                                            if (!GotEndOfLine)
                                            {
                                                // get an identifier from the input
                                                var identifier = PPScanIdentifier(true);

                                                // if there was an identifier and we have a dictionary of "defines" and the
                                                // identifier is in that dictionary...
                                                if (!string.IsNullOrEmpty(identifier))
                                                {
                                                    if (m_defines != null && m_defines.ContainsKey(identifier))
                                                    {
                                                        // remove the identifier from the "defines" dictionary
                                                        m_defines.Remove(identifier);
                                                    }

                                                    // if we are asking for raw tokens, we DON'T want to return this comment.
                                                    if (RawTokens)
                                                    {
                                                        SkipSingleLineComment();
                                                        goto nextToken;
                                                    }
                                                }
                                            }
                                        }
                                    }
                                }
                                else if (!RawTokens && c == '@' && !IgnoreConditionalCompilation && !m_peekModeOn)
                                {
                                    // we got //@
                                    // if we have not turned on conditional-compilation yet, then check to see if that's
                                    // what we're trying to do now
                                    if (!m_preProcessorOn)
                                    {
                                        // we are currently on the @ -- start peeking from there
                                        if (!CheckSubstring(m_currentPos + 1, "cc_on"))
                                        {
                                            // we aren't turning on conditional comments. We need to ignore this comment
                                            // as just another single-line comment
                                            SkipSingleLineComment();
                                            goto nextToken;
                                        }
                                    }

                                    // if the NEXT character is not an identifier character, then we need to skip
                                    // the @ character -- otherwise leave it there
                                    if (!IsValidIdentifierStart(GetChar(m_currentPos + 1)))
                                    {
                                        ++m_currentPos;
                                    }

                                    // if we aren't already in a conditional comment
                                    if (!m_inConditionalComment)
                                    {
                                        // we are now
                                        m_inConditionalComment = true;
                                        token = JSToken.ConditionalCommentStart;
                                        break;
                                    }

                                    // already in conditional comment, so just ignore the start of a new
                                    // conditional comment. it's superfluous.
                                    goto nextToken;
                                }

                                SkipSingleLineComment();

                                if (RawTokens)
                                {
                                    // raw tokens -- just return the comment
                                    token = JSToken.Comment;
                                    break;
                                }
                                else
                                {
                                    // if we're still in a multiple-line comment, then we must've been in
                                    // a multi-line CONDITIONAL comment, in which case this normal one-line comment
                                    // won't turn off conditional comments just because we hit the end of line.
                                    if (!m_inMultipleLineComment && m_inConditionalComment)
                                    {
                                        m_inConditionalComment = false;
                                        token = JSToken.ConditionalCommentEnd;
                                        break;
                                    }

                                    goto nextToken; // read another token this last one was a comment
                                }

                            case '*':
                                m_inMultipleLineComment = true;
                                if (RawTokens)
                                {
                                    // if we are looking for raw tokens, we don't care about important comments
                                    // or conditional comments or what-have-you. Scan the comment and return it
                                    // as the current token
                                    SkipMultilineComment(false);
                                    token = JSToken.Comment;
                                    break;
                                }
                                else
                                {
                                    bool importantComment = false;
                                    if (GetChar(++m_currentPos) == '@' && !IgnoreConditionalCompilation && !m_peekModeOn)
                                    {
                                        // we have /*@
                                        // if we have not turned on conditional-compilation yet, then let's peek to see if the next
                                        // few characters are cc_on -- if so, turn it on.
                                        if (!m_preProcessorOn)
                                        {
                                            // we are currently on the @ -- start peeking from there
                                            if (!CheckSubstring(m_currentPos + 1, "cc_on"))
                                            {
                                                // we aren't turning on conditional comments. We need to ignore this comment
                                                // as just another multi-line comment
                                                SkipMultilineComment(false);
                                                goto nextToken;
                                            }
                                        }
                                            
                                        // if the NEXT character is not an identifier character, then we need to skip
                                        // the @ character -- otherwise leave it there
                                        if (!IsValidIdentifierStart(GetChar(m_currentPos + 1)))
                                        {
                                            ++m_currentPos;
                                        }

                                        // if we aren't already in a conditional comment
                                        if (!m_inConditionalComment)
                                        {
                                            // we are in one now
                                            m_inConditionalComment = true;
                                            token = JSToken.ConditionalCommentStart;
                                            break;
                                        }

                                        // we were already in a conditional comment, so ignore the superfluous
                                        // conditional comment start
                                        goto nextToken;
                                    }

                                    if (GetChar(m_currentPos) == '!')
                                    {
                                        // found an "important" comment that we want to preserve
                                        importantComment = true;
                                    }

                                    SkipMultilineComment(importantComment);
                                    goto nextToken; // read another token this last one was a comment
                                }

                            default:
                                // if we're not just returning raw tokens, then we don't need to do this logic.
                                // otherwise if the previous token CAN be before a regular expression....
                                if (RawTokens && RegExpCanFollow(m_previousToken))
                                {
                                    // we think this is probably a regular expression.
                                    // if it is...
                                    if (ScanRegExp() != null)
                                    {
                                        // also scan the flags (if any)
                                        ScanRegExpFlags();
                                        token = JSToken.RegularExpression;
                                    }
                                    else if (c == '=')
                                    {
                                        m_currentPos++;
                                        token = JSToken.DivideAssign;
                                    }
                                }
                                else if (c == '=')
                                {
                                    m_currentPos++;
                                    token = JSToken.DivideAssign;
                                }
                                break;
                        }

                        break;

                    case '^':
                        token = JSToken.BitwiseXor;
                        if ('=' == GetChar(m_currentPos))
                        {
                            m_currentPos++;
                            token = JSToken.BitwiseXorAssign;
                        }

                        break;

                    case '%':
                        token = JSToken.Modulo;
                        if ('=' == GetChar(m_currentPos))
                        {
                            m_currentPos++;
                            token = JSToken.ModuloAssign;
                        }

                        break;

                    case '(':
                        token = JSToken.LeftParenthesis;
                        break;

                    case ')':
                        token = JSToken.RightParenthesis;
                        break;

                    case '{':
                        token = JSToken.LeftCurly;
                        break;

                    case '}':
                        token = JSToken.RightCurly;
                        break;

                    case '[':
                        token = JSToken.LeftBracket;
                        break;

                    case ']':
                        token = JSToken.RightBracket;
                        break;

                    case ';':
                        token = JSToken.Semicolon;
                        break;

                    case '"':
                    case '\'':
                        token = JSToken.StringLiteral;
                        ScanString(c);
                        break;

                    // line terminator crap
                    case '\r':
                        // if we are in a single-line conditional comment, then we want
                        // to return the end of comment token WITHOUT moving past the end of line 
                        // characters
                        if (m_inConditionalComment && m_inSingleLineComment)
                        {
                            token = JSToken.ConditionalCommentEnd;
                            m_inConditionalComment = m_inSingleLineComment = false;
                            break;
                        }

                        // \r\n is a valid SINGLE line-terminator. So if the \r is
                        // followed by a \n, we only want to process a single line terminator.
                        if (GetChar(m_currentPos) == '\n')
                        {
                            m_currentPos++;
                        }

                        // drop down into normal line-ending processing
                        goto case '\n';

                    case '\n':
                    case (char)0x2028:
                    case (char)0x2029:
                        // if we are in a single-line conditional comment, then
                        // clean up the flags and return the end of the conditional comment
                        // WITHOUT skipping past the end of line
                        if (m_inConditionalComment && m_inSingleLineComment)
                        {
                            token = JSToken.ConditionalCommentEnd;
                            m_inConditionalComment = m_inSingleLineComment = false;
                            break;
                        }

                        CurrentLine++;
                        StartLinePosition = m_currentPos;

                        m_inSingleLineComment = false;
                        if (RawTokens)
                        {
                            // if we are looking for raw tokens, then return this as the current token
                            token = JSToken.EndOfLine;
                            break;
                        }
                        else
                        {
                            // otherwise eat it and move on
                            goto nextToken;
                        }

                    case '@':
                        if (IgnoreConditionalCompilation)
                        {
                            // if the switch to ignore conditional compilation is on, then we don't know
                            // anything about conditional-compilation statements, and the @-sign character
                            // is illegal at this spot.
                            HandleError(JSError.IllegalChar);
                            if (RawTokens)
                            {
                                // if we are just looking for raw tokens, return this one as an error token
                                token = JSToken.Error;
                                break;
                            }
                            else
                            {
                                // otherwise eat it
                                goto nextToken;
                            }
                        }

                        // we do care about conditional compilation if we get here
                        if (m_peekModeOn)
                        {
                            // but if we're in peek mode, we just need to know WHAT the 
                            // next token is, and we don't need to go any deeper.
                            m_currentToken.Token = JSToken.PreprocessDirective;
                            break;
                        }

                        // see if the @-sign is immediately followed by an identifier. If it is,
                        // we'll see which one so we can tell if it's a conditional-compilation statement
                        // need to make sure the context INCLUDES the @ sign
                        int startPosition = m_currentPos;
                        m_currentToken.StartPosition = startPosition - 1;
                        m_currentToken.StartLineNumber = CurrentLine;
                        m_currentToken.StartLinePosition = StartLinePosition;
                        ScanIdentifier();
                        switch (m_currentPos - startPosition)
                        {
                            case 0:
                                // look for '@*/'.
                                if (/*m_preProcessorOn &&*/ '*' == GetChar(m_currentPos) && '/' == GetChar(++m_currentPos))
                                {
                                    m_currentPos++;
                                    m_inMultipleLineComment = false;
                                    m_inConditionalComment = false;
                                    token = JSToken.ConditionalCommentEnd;
                                    break;
                                }

                                // otherwise we just have a @ sitting by itself!
                                // throw an error and loop back to the next token.
                                HandleError(JSError.IllegalChar);
                                if (RawTokens)
                                {
                                    // if we are just looking for raw tokens, return this one as an error token
                                    token = JSToken.Error;
                                    break;
                                }
                                else
                                {
                                    // otherwise eat it
                                    goto nextToken;
                                }

                            case 2:
                                if (CheckSubstring(startPosition, "if"))
                                {
                                    token = JSToken.ConditionalCompilationIf;

                                    // increment the if-level
                                    ++m_ccIfLevel;

                                    // if we're not in a conditional comment and we haven't explicitly
                                    // turned on conditional compilation when we encounter
                                    // a @if statement, then we can implicitly turn it on.
                                    if (!m_inConditionalComment && !m_preProcessorOn)
                                    {
                                        m_preProcessorOn = true;
                                    }

                                    break;
                                }

                                // the string isn't a known preprocessor command, so 
                                // fall into the default processing to handle it as a variable name
                                goto default;

                            case 3:
                                if (CheckSubstring(startPosition, "set"))
                                {
                                    token = JSToken.ConditionalCompilationSet;

                                    // if we're not in a conditional comment and we haven't explicitly
                                    // turned on conditional compilation when we encounter
                                    // a @set statement, then we can implicitly turn it on.
                                    if (!m_inConditionalComment && !m_preProcessorOn)
                                    {
                                        m_preProcessorOn = true;
                                    }

                                    break;
                                }

                                if (CheckSubstring(startPosition, "end"))
                                {
                                    token = JSToken.ConditionalCompilationEnd;
                                    if (m_ccIfLevel > 0)
                                    {
                                        // down one more @if level
                                        m_ccIfLevel--;
                                    }
                                    else
                                    {
                                        // not corresponding @if -- invalid @end statement
                                        HandleError(JSError.CCInvalidEnd);
                                    }

                                    break;
                                }

                                // the string isn't a known preprocessor command, so 
                                // fall into the default processing to handle it as a variable name
                                goto default;

                            case 4:
                                if (CheckSubstring(startPosition, "else"))
                                {
                                    token = JSToken.ConditionalCompilationElse;

                                    // if we don't have a corresponding @if statement, then throw and error
                                    // (but keep processing)
                                    if (m_ccIfLevel <= 0)
                                    {
                                        HandleError(JSError.CCInvalidElse);
                                    }

                                    break;
                                }

                                if (CheckSubstring(startPosition, "elif"))
                                {
                                    token = JSToken.ConditionalCompilationElseIf;

                                    // if we don't have a corresponding @if statement, then throw and error
                                    // (but keep processing)
                                    if (m_ccIfLevel <= 0)
                                    {
                                        HandleError(JSError.CCInvalidElseIf);
                                    }

                                    break;
                                }

                                // the string isn't a known preprocessor command, so 
                                // fall into the default processing to handle it as a variable name
                                goto default;

                            case 5:
                                if (CheckSubstring(startPosition, "cc_on"))
                                {
                                    // turn it on and return the @cc_on token
                                    m_preProcessorOn = true;
                                    token = JSToken.ConditionalCompilationOn;
                                    break;
                                }

                                // the string isn't a known preprocessor command, so 
                                // fall into the default processing to handle it as a variable name
                                goto default;

                            default:
                                // we have @[id], where [id] is a valid identifier.
                                // if we haven't explicitly turned on conditional compilation,
                                // we'll keep processing, but we need to fire an error to indicate
                                // that the code should turn it on first.
                                if (!m_preProcessorOn)
                                {
                                    HandleError(JSError.CCOff);
                                }

                                token = JSToken.PreprocessorConstant;
                                break;
                        }

                        break;

                    case '$':
                        goto case '_';

                    case '_':
                        ScanIdentifier();
                        token = JSToken.Identifier;
                        break;

                    default:
                        if ('a' <= c && c <= 'z')
                        {
                            JSKeyword keyword = m_keywords[c - 'a'];
                            if (null != keyword)
                            {
                                token = ScanKeyword(keyword);
                            }
                            else
                            {
                                token = JSToken.Identifier;
                                ScanIdentifier();
                            }
                        }
                        else if (IsDigit(c))
                        {
                            token = ScanNumber(c);
                        }
                        else if (IsValidIdentifierStart(c))
                        {
                            token = JSToken.Identifier;
                            ScanIdentifier();
                        }
                        else if (RawTokens && IsBlankSpace(c))
                        {
                            // we are asking for raw tokens, and this is the start of a stretch of whitespace.
                            // advance to the end of the whitespace, and return that as the token
                            while (JSScanner.IsBlankSpace(GetChar(m_currentPos)))
                            {
                                ++m_currentPos;
                            }
                            token = JSToken.WhiteSpace;
                        }
                        else
                        {
                            m_currentToken.EndLineNumber = CurrentLine;
                            m_currentToken.EndLinePosition = StartLinePosition;
                            m_currentToken.EndPosition = m_currentPos;

                            HandleError(JSError.IllegalChar);
                            if (RawTokens)
                            {
                                // if we are just looking for raw tokens, return this one as an error token
                                token = JSToken.Error;
                                break;
                            }
                            else
                            {
                                // otherwise eat it
                                goto nextToken;
                            }
                        }

                        break;
                }
                m_currentToken.EndLineNumber = CurrentLine;
                m_currentToken.EndLinePosition = StartLinePosition;
                m_currentToken.EndPosition = m_currentPos;
                GotEndOfLine = (CurrentLine > thisCurrentLine || token == JSToken.EndOfFile) ? true : false;
                if (GotEndOfLine && token == JSToken.StringLiteral && m_currentToken.StartLineNumber == thisCurrentLine)
                {
                    GotEndOfLine = false;
                }
            }
            catch (IndexOutOfRangeException)
            {
                m_currentToken.Token = JSToken.None;
                m_currentToken.EndPosition = m_currentPos;
                m_currentToken.EndLineNumber = CurrentLine;
                m_currentToken.EndLinePosition = StartLinePosition;
                throw new ScannerException(JSError.ErrorEndOfFile);
            }

            // this is now the current token
            m_currentToken.Token = token;

            // if this the kind of token we want to know about the next time, then save it
            switch(token)
            {
                case JSToken.WhiteSpace:
                case JSToken.AspNetBlock:
                case JSToken.Comment:
                case JSToken.UnterminatedComment:
                case JSToken.ConditionalCompilationOn:
                case JSToken.ConditionalCompilationSet:
                case JSToken.ConditionalCompilationIf:
                case JSToken.ConditionalCompilationElseIf:
                case JSToken.ConditionalCompilationElse:
                case JSToken.ConditionalCompilationEnd:
                    // don't save these tokens for next time
                    break;

                default:
                    m_previousToken = token;
                    break;
            }

            //if (TokenCounts.ContainsKey(token))
            //{
            //    ++TokenCounts[token];
            //}
            //else
            //{
            //    TokenCounts.Add(token, 1);
            //}
        }

        private bool CheckSubstring(int startIndex, string target)
        {
            for (int ndx = 0; ndx < target.Length; ++ndx)
            {
                if (target[ndx] != GetChar(startIndex + ndx))
                {
                    // no match
                    return false;
                }
            }

            // if we got here, the strings match
            return true;
        }

        private bool CheckCaseInsensitiveSubstring(int startIndex, string target)
        {
            for (int ndx = 0; ndx < target.Length; ++ndx)
            {
                if (target[ndx] != char.ToUpperInvariant(GetChar(startIndex + ndx)))
                {
                    // no match
                    return false;
                }
            }

            // if we got here, the strings match
            return true;
        }

        private char GetChar(int index)
        {
            if (index < m_endPos)
            {
                return m_strSourceCode[index];
            }

            return (char)0;
        }

        public string StringLiteral
        {
            get
            {
                return m_escapedString;
            }
        }

        internal string GetIdentifier()
        {
            string id = null;
            if (m_identifier.Length > 0)
            {
                id = m_identifier.ToString();
                m_identifier.Length = 0;
            }
            else
            {
                id = m_currentToken.Code;
            }

            return id;
        }

        private void ScanIdentifier()
        {
            for (;;)
            {
                char c = GetChar(m_currentPos);
                if (!IsIdentifierPartChar(c))
                {
                    break;
                }

                ++m_currentPos;
            }

            if (AllowEmbeddedAspNetBlocks
                && CheckSubstring(m_currentPos, "<%="))
            {
                // the identifier has an ASP.NET <%= ... %> block as part of it.
                // move the current position to the opening % character and call 
                // the method that will parse it from there.
                ++m_currentPos;
                ScanAspNetBlock();
            }

            if (m_idLastPosOnBuilder > 0)
            {
                m_identifier.Append(m_strSourceCode.Substring(m_idLastPosOnBuilder, m_currentPos - m_idLastPosOnBuilder));
                m_idLastPosOnBuilder = 0;
            }
        }

        private JSToken ScanKeyword(JSKeyword keyword)
        {
            for (;;)
            {
                char c = GetChar(m_currentPos);
                if ('a' <= c && c <= 'z')
                {
                    m_currentPos++;
                    continue;
                }

                if (IsIdentifierPartChar(c) 
                    || (AllowEmbeddedAspNetBlocks && CheckSubstring(m_currentPos, "<%=")))
                {
                    ScanIdentifier();
                    return JSToken.Identifier;
                }

                break;
            }

            return keyword.GetKeyword(m_currentToken, m_currentPos - m_currentToken.StartPosition);
        }

        private JSToken ScanNumber(char leadChar)
        {
            bool noMoreDot = '.' == leadChar;
            JSToken token = noMoreDot ? JSToken.NumericLiteral : JSToken.IntegerLiteral;
            bool exponent = false;
            char c;

            if ('0' == leadChar)
            {
                c = GetChar(m_currentPos);
                if ('x' == c || 'X' == c)
                {
                    if (!JSScanner.IsHexDigit(GetChar(m_currentPos + 1)))
                    {
                        // bump it up two characters to pick up the 'x' and the bad digit
                        m_currentPos += 2;
                        HandleError(JSError.BadHexDigit);
                        // bump it down three characters to go back to the 0
                        m_currentPos -= 3;
                    }

                    while (JSScanner.IsHexDigit(GetChar(++m_currentPos)))
                    {
                        // empty
                    }

                    return token;
                }
            }

            for (;;)
            {
                c = GetChar(m_currentPos);
                if (!JSScanner.IsDigit(c))
                {
                    if ('.' == c)
                    {
                        if (noMoreDot)
                        {
                            break;
                        }

                        noMoreDot = true;
                        token = JSToken.NumericLiteral;
                    }
                    else if ('e' == c || 'E' == c)
                    {
                        if (exponent)
                        {
                            break;
                        }

                        exponent = true;
                        token = JSToken.NumericLiteral;
                    }
                    else if ('+' == c || '-' == c)
                    {
                        char e = GetChar(m_currentPos - 1);
                        if ('e' != e && 'E' != e)
                        {
                            break;
                        }
                    }
                    else
                    {
                        break;
                    }
                }

                m_currentPos++;
            }

            c = GetChar(m_currentPos - 1);
            if ('+' == c || '-' == c)
            {
                m_currentPos--;
                c = GetChar(m_currentPos - 1);
            }

            if ('e' == c || 'E' == c)
            {
                m_currentPos--;
            }

            return token;
        }

        private static bool RegExpCanFollow(JSToken previousToken)
        {
            switch(previousToken)
            {
                case JSToken.Do:
                case JSToken.Return:
                case JSToken.Throw:
                case JSToken.LeftCurly:
                case JSToken.Semicolon:
                case JSToken.LeftParenthesis:
                case JSToken.LeftBracket:
                case JSToken.ConditionalIf:
                case JSToken.Colon:
                case JSToken.Comma:
                case JSToken.Case:
                case JSToken.Else:
                case JSToken.EndOfLine:
                case JSToken.RightCurly:
                case JSToken.LogicalNot:
                case JSToken.BitwiseNot:
                case JSToken.Delete:
                case JSToken.Void:
                case JSToken.New:
                case JSToken.TypeOf:
                case JSToken.Increment:
                case JSToken.Decrement:
                case JSToken.Plus:
                case JSToken.Minus:
                case JSToken.LogicalOr:
                case JSToken.LogicalAnd:
                case JSToken.BitwiseOr:
                case JSToken.BitwiseXor:
                case JSToken.BitwiseAnd:
                case JSToken.Equal:
                case JSToken.NotEqual:
                case JSToken.StrictEqual:
                case JSToken.StrictNotEqual:
                case JSToken.GreaterThan:
                case JSToken.LessThan:
                case JSToken.LessThanEqual:
                case JSToken.GreaterThanEqual:
                case JSToken.LeftShift:
                case JSToken.RightShift:
                case JSToken.UnsignedRightShift:
                case JSToken.Multiply:
                case JSToken.Divide:
                case JSToken.Modulo:
                case JSToken.InstanceOf:
                case JSToken.In:
                case JSToken.Assign:
                case JSToken.PlusAssign:
                case JSToken.MinusAssign:
                case JSToken.MultiplyAssign:
                case JSToken.DivideAssign:
                case JSToken.BitwiseAndAssign:
                case JSToken.BitwiseOrAssign:
                case JSToken.BitwiseXorAssign:
                case JSToken.ModuloAssign:
                case JSToken.LeftShiftAssign:
                case JSToken.RightShiftAssign:
                case JSToken.UnsignedRightShiftAssign:
                case JSToken.None:
                    return true;

                default:
                    return false;
            }
        }

        internal String ScanRegExp()
        {
            int pos = m_currentPos;
            bool isEscape = false;
            bool isInSet = false;
            char c;
            while (!IsEndLineOrEOF(c = GetChar(m_currentPos++), 0))
            {
                if (isEscape)
                {
                    isEscape = false;
                }
                else if (c == '[')
                {
                    isInSet = true;
                }
                else if (isInSet)
                {
                    if (c == ']')
                    {
                        isInSet = false;
                    }
                }
                else if (c == '/')
                {
                    if (pos == m_currentPos)
                    {
                        return null;
                    }

                    m_currentToken.EndPosition = m_currentPos;
                    m_currentToken.EndLinePosition = StartLinePosition;
                    m_currentToken.EndLineNumber = CurrentLine;
                    return m_strSourceCode.Substring(
                        m_currentToken.StartPosition + 1,
                        m_currentToken.EndPosition - m_currentToken.StartPosition - 2);
                }
                else if (c == '\\')
                {
                    isEscape = true;
                }
            }

            // reset and return null. Assume it is not a reg exp
            m_currentPos = pos;
            return null;
        }

        internal String ScanRegExpFlags()
        {
            int pos = m_currentPos;
            while (JSScanner.IsAsciiLetter(GetChar(m_currentPos)))
            {
                m_currentPos++;
            }

            if (pos != m_currentPos)
            {
                m_currentToken.EndPosition = m_currentPos;
                m_currentToken.EndLineNumber = CurrentLine;
                m_currentToken.EndLinePosition = StartLinePosition;
                return m_strSourceCode.Substring(pos, m_currentToken.EndPosition - pos);
            }

            return null;
        }

        /// <summary>
        /// Scans for the end of an Asp.Net block.
        ///  On exit this.currentPos will be at the next char to scan after the asp.net block.
        /// </summary>
        private JSToken ScanAspNetBlock()
        {
            // assume we find an asp.net block
            var tokenType = JSToken.AspNetBlock;

            // the current position is the % that opens the <%.
            // advance to the next character and save it because we will want 
            // to know whether it's an equals-sign later
            var thirdChar = GetChar(++m_currentPos);

            // advance to the next character
            ++m_currentPos;

            // loop until we find a > with a % before it (%>)
            while (!(this.GetChar(this.m_currentPos - 1) == '%' &&
                     this.GetChar(this.m_currentPos) == '>') ||
                     (m_currentPos >= m_endPos))
            {
                this.m_currentPos++;
            }

            // we should be at the > of the %> right now.
            // set the end point of this token
            m_currentToken.EndPosition = m_currentPos + 1;
            m_currentToken.EndLineNumber = CurrentLine;
            m_currentToken.EndLinePosition = StartLinePosition;

            // see if we found an unterminated asp.net block
            if (m_currentPos >= m_endPos)
            {
                HandleError(JSError.UnterminatedAspNetBlock);
            }
            else
            {
                // Eat the last >.
                this.m_currentPos++;

                if (thirdChar == '=')
                {
                    // this is a <%= ... %> token.
                    // we're going to treat this like an identifier
                    tokenType = JSToken.Identifier;

                    // now, if the next character is an identifier part
                    // then skip to the end of the identifier. And if this is
                    // another <%= then skip to the end (%>)
                    if (IsValidIdentifierPart(GetChar(m_currentPos))
                        || CheckSubstring(m_currentPos, "<%="))
                    {
                        // and do it however many times we need
                        while (true)
                        {
                            if (IsValidIdentifierPart(GetChar(m_currentPos)))
                            {
                                // skip to the end of the identifier part
                                while (IsValidIdentifierPart(GetChar(++m_currentPos)))
                                {
                                    // loop
                                }

                                // when we get here, the current position is the first
                                // character that ISN"T an identifier-part. That means everything 
                                // UP TO this point must have been on the 
                                // same line, so we only need to update the position
                                m_currentToken.EndPosition = m_currentPos;
                            }
                            else if (CheckSubstring(m_currentPos, "<%="))
                            {
                                // skip forward four characters -- the minimum position
                                // for the closing %>
                                m_currentPos += 4;

                                // and keep looping until we find it
                                while (!(this.GetChar(this.m_currentPos - 1) == '%' &&
                                         this.GetChar(this.m_currentPos) == '>') ||
                                         (m_currentPos >= m_endPos))
                                {
                                    this.m_currentPos++;
                                }

                                // update the end of the token
                                m_currentToken.EndPosition = m_currentPos + 1;
                                m_currentToken.EndLineNumber = CurrentLine;
                                m_currentToken.EndLinePosition = StartLinePosition;

                                // we should be at the > of the %> right now.
                                // see if we found an unterminated asp.net block
                                if (m_currentPos >= m_endPos)
                                {
                                    HandleError(JSError.UnterminatedAspNetBlock);
                                }
                                else
                                {
                                    // skip the > and go around another time
                                    ++m_currentPos;
                                }
                            }
                            else
                            {
                                // neither an identifer part nor another <%= sequence,
                                // so we're done here
                                break;
                            }
                        }
                    }
                }
            }

            return tokenType;
        }

        //--------------------------------------------------------------------------------------------------
        // ScanString
        //
        //  Scan a string dealing with escape sequences.
        //  On exit this.escapedString will contain the string with all escape sequences replaced
        //  On exit this.currentPos must be at the next char to scan after the string
        //  This method wiil report an error when the string is unterminated or for a bad escape sequence
        //--------------------------------------------------------------------------------------------------
        private void ScanString(char cStringTerminator)
        {
            char ch;
            int start = m_currentPos;
            m_escapedString = null;
            StringBuilder result = null;
            do
            {
                ch = GetChar(m_currentPos++);

                if (ch != '\\')
                {
                    // this is the common non escape case
                    if (IsLineTerminator(ch, 0))
                    {
                        HandleError(JSError.UnterminatedString);
                        --m_currentPos;
                        if (GetChar(m_currentPos - 1) == '\r')
                        {
                            --m_currentPos;
                        }

                        break;
                    }
                    
                    if ((char)0 == ch)
                    {
                        m_currentPos--;
                        HandleError(JSError.UnterminatedString);
                        break;
                    }

                    if (AllowEmbeddedAspNetBlocks
                        && ch == '<'
                        && GetChar(m_currentPos) == '%')
                    {
                        // start of an ASP.NET block INSIDE a string literal.
                        // just skip the entire ASP.NET block -- move forward until
                        // we find the closing %> delimiter, then we'll continue on
                        // with the next character.
                        SkipAspNetReplacement();
                    }
                }
                else
                {
                    // ESCAPE CASE

                    // got an escape of some sort. Have to use the StringBuilder
                    if (null == result)
                    {
                        result = new StringBuilder(128);
                    }

                    // start points to the first position that has not been written to the StringBuilder.
                    // The first time we get in here that position is the beginning of the string, after that
                    // is the character immediately following the escape sequence
                    if (m_currentPos - start - 1 > 0)
                    {
                        // append all the non escape chars to the string builder
                        result.Append(m_strSourceCode, start, m_currentPos - start - 1);
                    }

                    // state variable to be reset
                    bool seqOfThree = false;
                    int esc = 0;

                    ch = GetChar(m_currentPos++);
                    switch (ch)
                    {
                        // line terminator crap
                        case '\r':
                            if ('\n' == GetChar(m_currentPos))
                            {
                                m_currentPos++;
                            }

                            goto case '\n';

                        case '\n':
                        case (char)0x2028:
                        case (char)0x2029:
                            CurrentLine++;
                            StartLinePosition = m_currentPos;
                            break;

                        // classic single char escape sequences
                        case 'b':
                            result.Append((char)8);
                            break;

                        case 't':
                            result.Append((char)9);
                            break;

                        case 'n':
                            result.Append((char)10);
                            break;

                        case 'v':
                            result.Append((char)11);
                            break;

                        case 'f':
                            result.Append((char)12);
                            break;

                        case 'r':
                            result.Append((char)13);
                            break;

                        case '"':
                            result.Append('"');
                            ch = (char)0; // so it does not exit the loop
                            break;

                        case '\'':
                            result.Append('\'');
                            ch = (char)0; // so it does not exit the loop
                            break;

                        case '\\':
                            result.Append('\\');
                            break;

                        // hexadecimal escape sequence /xHH
                        case 'x':
                            ch = GetChar(m_currentPos++);
                            if (unchecked((uint)(ch - '0')) <= '9' - '0')
                            {
                                esc = (ch - '0') << 4;
                            }
                            else if (unchecked((uint)(ch - 'A')) <= 'F' - 'A')
                            {
                                esc = (ch + 10 - 'A') << 4;
                            }
                            else if (unchecked((uint)(ch - 'a')) <= 'f' - 'a')
                            {
                                esc = (ch + 10 - 'a') << 4;
                            }
                            else
                            {
                                HandleError(JSError.BadHexDigit);
                                if (ch != cStringTerminator)
                                {
                                    --m_currentPos; // do not skip over this char we have to read it back
                                }

                                break;
                            }

                            ch = GetChar(m_currentPos++);
                            if (unchecked((uint)(ch - '0')) <= '9' - '0')
                            {
                                esc |= (ch - '0');
                            }
                            else if (unchecked((uint)(ch - 'A')) <= 'F' - 'A')
                            {
                                esc |= (ch + 10 - 'A');
                            }
                            else if (unchecked((uint)(ch - 'a')) <= 'f' - 'a')
                            {
                                esc |= (ch + 10 - 'a');
                            }
                            else
                            {
                                HandleError(JSError.BadHexDigit);
                                if (ch != cStringTerminator)
                                {
                                    --m_currentPos; // do not skip over this char we have to read it back
                                }
                                break;
                            }

                            result.Append((char)esc);
                            break;

                        // unicode escape sequence /uHHHH
                        case 'u':
                            ch = GetChar(m_currentPos++);
                            if (unchecked((uint)(ch - '0')) <= '9' - '0')
                            {
                                esc = (ch - '0') << 12;
                            }
                            else if (unchecked((uint)(ch - 'A')) <= 'F' - 'A')
                            {
                                esc = (ch + 10 - 'A') << 12;
                            }
                            else if (unchecked((uint)(ch - 'a')) <= 'f' - 'a')
                            {
                                esc = (ch + 10 - 'a') << 12;
                            }
                            else
                            {
                                HandleError(JSError.BadHexDigit);
                                if (ch != cStringTerminator)
                                {
                                    --m_currentPos; // do not skip over this char we have to read it back
                                }

                                break;
                            }

                            ch = GetChar(m_currentPos++);
                            if (unchecked((uint)(ch - '0')) <= '9' - '0')
                            {
                                esc |= (ch - '0') << 8;
                            }
                            else if (unchecked((uint)(ch - 'A')) <= 'F' - 'A')
                            {
                                esc |= (ch + 10 - 'A') << 8;
                            }
                            else if (unchecked((uint)(ch - 'a')) <= 'f' - 'a')
                            {
                                esc |= (ch + 10 - 'a') << 8;
                            }
                            else
                            {
                                HandleError(JSError.BadHexDigit);
                                if (ch != cStringTerminator)
                                {
                                    --m_currentPos; // do not skip over this char we have to read it back
                                }

                                break;
                            }

                            ch = GetChar(m_currentPos++);
                            if (unchecked((uint)(ch - '0')) <= '9' - '0')
                            {
                                esc |= (ch - '0') << 4;
                            }
                            else if (unchecked((uint)(ch - 'A')) <= 'F' - 'A')
                            {
                                esc |= (ch + 10 - 'A') << 4;
                            }
                            else if (unchecked((uint)(ch - 'a')) <= 'f' - 'a')
                            {
                                esc |= (ch + 10 - 'a') << 4;
                            }
                            else
                            {
                                HandleError(JSError.BadHexDigit);
                                if (ch != cStringTerminator)
                                {
                                    --m_currentPos; // do not skip over this char we have to read it back
                                }

                                break;
                            }

                            ch = GetChar(m_currentPos++);
                            if (unchecked((uint)(ch - '0')) <= '9' - '0')
                            {
                                esc |= (ch - '0');
                            }
                            else if (unchecked((uint)(ch - 'A')) <= 'F' - 'A')
                            {
                                esc |= (ch + 10 - 'A');
                            }
                            else if (unchecked((uint)(ch - 'a')) <= 'f' - 'a')
                            {
                                esc |= (ch + 10 - 'a');
                            }
                            else
                            {
                                HandleError(JSError.BadHexDigit);
                                if (ch != cStringTerminator)
                                {
                                    --m_currentPos; // do not skip over this char we have to read it back
                                }

                                break;
                            }

                            result.Append((char)esc);
                            break;

                        case '0':
                        case '1':
                        case '2':
                        case '3':
                            seqOfThree = true;
                            esc = (ch - '0') << 6;
                            goto case '4';

                        case '4':
                        case '5':
                        case '6':
                        case '7':
                            // esc is reset at the beginning of the loop and it is used to check that we did not go through the cases 1, 2 or 3
                            if (!seqOfThree)
                            {
                                esc = (ch - '0') << 3;
                            }

                            ch = GetChar(m_currentPos++);
                            if (unchecked((UInt32)(ch - '0')) <= '7' - '0')
                            {
                                if (seqOfThree)
                                {
                                    esc |= (ch - '0') << 3;
                                    ch = GetChar(m_currentPos++);
                                    if (unchecked((UInt32)(ch - '0')) <= '7' - '0')
                                    {
                                        esc |= ch - '0';
                                        result.Append((char)esc);
                                    }
                                    else
                                    {
                                        result.Append((char)(esc >> 3));
                                        if (ch != cStringTerminator)
                                        {
                                            --m_currentPos; // do not skip over this char we have to read it back
                                        }
                                    }
                                }
                                else
                                {
                                    esc |= ch - '0';
                                    result.Append((char)esc);
                                }
                            }
                            else
                            {
                                if (seqOfThree)
                                {
                                    result.Append((char)(esc >> 6));
                                }
                                else
                                {
                                    result.Append((char)(esc >> 3));
                                }

                                if (ch != cStringTerminator)
                                {
                                    --m_currentPos; // do not skip over this char we have to read it back
                                }
                            }

                            break;

                        default:
                            // not an octal number, ignore the escape '/' and simply append the current char
                            result.Append(ch);
                            break;
                    }

                    start = m_currentPos;
                }
            } while (ch != cStringTerminator);

            // update this.escapedString
            if (null != result)
            {
                if (m_currentPos - start - 1 > 0)
                {
                    // append all the non escape chars to the string builder
                    result.Append(m_strSourceCode, start, m_currentPos - start - 1);
                }
                m_escapedString = result.ToString();
            }
            else
            {
                if (m_currentPos <= m_currentToken.StartPosition + 2)
                {
                    m_escapedString = "";
                }
                else
                {
                    int numDelimiters = (GetChar(m_currentPos - 1) == cStringTerminator ? 2 : 1);
                    m_escapedString = m_strSourceCode.Substring(m_currentToken.StartPosition + 1, m_currentPos - m_currentToken.StartPosition - numDelimiters);
                }
            }
        }

        private void SkipAspNetReplacement()
        {
            // the current position is on the % of the opening delimiter, so
            // advance the pointer forward to the first character AFTER the opening
            // delimiter, then keep skipping
            // forward until we find the closing %>. Be sure to set the current pointer
            // to the NEXT character AFTER the > when we find it.
            ++m_currentPos;

            char ch;
            while ((ch = GetChar(m_currentPos++)) != '\0')
            {
                if (ch == '%'
                    && GetChar(m_currentPos) == '>')
                {
                    // found the closing delimiter -- the current position in on the >
                    // so we need to advance to the next character and break out of the loop
                    ++m_currentPos;
                    break;
                }
            }
        }

        private void SkipSingleLineComment()
        {
            while (!IsEndLineOrEOF(GetChar(m_currentPos++), 0)) ;
            CurrentLine++;
            StartLinePosition = m_currentPos;
            m_inSingleLineComment = false;
        }

        private void SkipToEndOfLine()
        {
            var c = GetChar(m_currentPos);
            while (c != 0
                && c != '\n'
                && c != '\r'
                && c != '\x2028'
                && c != '\x2029')
            {
                c = GetChar(++m_currentPos);
            }
        }

        // this method is public because it's used from the authoring code
        public int SkipMultilineComment(bool importantComment)
        {
            for (; ; )
            {
                char c = GetChar(m_currentPos);
                while ('*' == c)
                {
                    c = GetChar(++m_currentPos);
                    if ('/' == c)
                    {
                        m_currentPos++;
                        m_inMultipleLineComment = false;
                        if (importantComment)
                        {
                            SaveImportantComment();
                        }
                        return m_currentPos;
                    }

                    if ((char)0 == c)
                    {
                        break;
                    }
                    
                    if (IsLineTerminator(c, 1))
                    {
                        c = GetChar(++m_currentPos);
                        CurrentLine++;
                        StartLinePosition = m_currentPos + 1;
                    }
                }

                if ((char)0 == c && m_currentPos >= m_endPos)
                {
                    break;
                }

                if (IsLineTerminator(c, 1))
                {
                    CurrentLine++;
                    StartLinePosition = m_currentPos + 1;
                }

                ++m_currentPos;
            }

            // if we are here we got EOF
            if (importantComment)
            {
                SaveImportantComment();
            }

            m_currentToken.EndPosition = m_currentPos;
            m_currentToken.EndLinePosition = StartLinePosition;
            m_currentToken.EndLineNumber = CurrentLine;
            throw new ScannerException(JSError.NoCommentEnd);
        }

        private void SaveImportantComment()
        {
            // if we already found one important comment, we need to append this one 
            // to the end of the existing one(s) so we don't lose them. So if we don't
            // have one already, clone the current context. Otherwise continue with what
            // we have already found.
            if (m_importantComments == null)
            {
                m_importantComments = new List<Context>();
            }

            // save the context of the important comment
            Context commentContext = m_currentToken.Clone();
            commentContext.EndPosition = m_currentPos;
            commentContext.EndLineNumber = CurrentLine;
            commentContext.EndLinePosition = StartLinePosition;

            m_importantComments.Add(commentContext);
        }

        private void SkipBlanks()
        {
            char c = GetChar(m_currentPos);
            while (JSScanner.IsBlankSpace(c))
            {
                c = GetChar(++m_currentPos);
            }
        }

        private static bool IsBlankSpace(char c)
        {
            switch (c)
            {
                case (char)0x09:
                case (char)0x0B:
                case (char)0x0C:
                case (char)0x20:
                case (char)0xA0:
                case (char)0xfeff: // BOM - byte order mark
                    return true;

                default:
                    return (c < 128) ? false : char.GetUnicodeCategory(c) == UnicodeCategory.SpaceSeparator;
            }
        }

        private bool IsLineTerminator(char c, int increment)
        {
            switch (c)
            {
                case (char)0x0D:
                    // treat 0x0D0x0A as a single character
                    if (0x0A == GetChar(m_currentPos + increment))
                    {
                        m_currentPos++;
                    }

                    return true;

                case (char)0x0A:
                    return true;

                case (char)0x2028:
                    return true;

                case (char)0x2029:
                    return true;

                default:
                    return false;
            }
        }

        private bool IsEndLineOrEOF(char c, int increment)
        {
            return IsLineTerminator(c, increment) || (char)0 == c && m_currentPos >= m_endPos;
        }

        private static int GetHexValue(char hex)
        {
            int hexValue;
            if ('0' <= hex && hex <= '9')
            {
                hexValue = hex - '0';
            }
            else if ('a' <= hex && hex <= 'f')
            {
                hexValue = hex - 'a' + 10;
            }
            else
            {
                hexValue = hex - 'A' + 10;
            }

            return hexValue;
        }

        // string might contain escaped characters
        public static bool StartsWithIdentifierPart(string text)
        {
            bool startsWithIdentifierPart = false;
            if (!string.IsNullOrEmpty(text))
            {
                char ch = text[0];
                if (ch == '\\')
                {
                    if (text.Length >= 6 && text[1] == 'u')
                    {
                        // unescape the escaped character
                        char h1 = text[2];
                        char h2 = text[3];
                        char h3 = text[4];
                        char h4 = text[5];
                        if (IsHexDigit(h1) && IsHexDigit(h2) && IsHexDigit(h3) && IsHexDigit(h4))
                        {
                            ch = (char)(GetHexValue(h1) << 12
                                | GetHexValue(h2) << 8
                                | GetHexValue(h3) << 4
                                | GetHexValue(h4));
                        }
                    }
                }

                // is it a valid identifier part?
                startsWithIdentifierPart = IsValidIdentifierPart(ch);
            }

            return startsWithIdentifierPart;
        }

        // string might contain escaped characters
        public static bool EndsWithIdentifierPart(string text)
        {
            bool endsWithIdentifierPart = false;
            if (!string.IsNullOrEmpty(text))
            {
                // get last character. If it's not an identifier part,
                // then we know it's not an identifier part and we can
                // stop looking. 
                // But if it is an identifier, AND it's a hex digit,
                // we need to step back and make sure it's not part of a unicode
                // escape sequence. If it is, we need to decode the escape seqence
                // to see if THAT'S an identifier part.
                int lastIndex = text.Length - 1;
                char ch = text[lastIndex];
                endsWithIdentifierPart = IsValidIdentifierPart(ch);
                if (endsWithIdentifierPart && IsHexDigit(ch) 
                    && text.Length >= 6
                    && IsHexDigit(text[lastIndex - 1])
                    && IsHexDigit(text[lastIndex - 2])
                    && IsHexDigit(text[lastIndex - 3])
                    && text[lastIndex-4] == 'u'
                    && text[lastIndex - 5] == '\\')
                {
                    endsWithIdentifierPart = IsValidIdentifierPart((char)
                        (GetHexValue(text[lastIndex - 3]) << 12
                        | GetHexValue(text[lastIndex - 2]) << 8
                        | GetHexValue(text[lastIndex - 1]) << 4
                        | GetHexValue(ch)));
                }
            }

            return endsWithIdentifierPart;
        }

        // assumes all unicode characters in the string -- NO escape sequences
        public static bool IsValidIdentifier(string name)
        {
            bool isValid = false;
            if (!string.IsNullOrEmpty(name))
            {
                if (IsValidIdentifierStart(name[0]))
                {
                    // loop through all the rest
                    for (int ndx = 1; ndx < name.Length; ++ndx)
                    {
                        char ch = name[ndx];
                        if (!IsValidIdentifierPart(ch))
                        {
                            // fail!
                            return false;
                        }
                    }

                    // if we get here, everything is okay
                    isValid = true;
                }
            }

            return isValid;
        }

        // assumes all unicode characters in the string -- NO escape sequences
        public static bool IsSafeIdentifier(string name)
        {
            bool isValid = false;
            if (!string.IsNullOrEmpty(name))
            {
                if (IsSafeIdentifierStart(name[0]))
                {
                    // loop through all the rest
                    for (int ndx = 1; ndx < name.Length; ++ndx)
                    {
                        char ch = name[ndx];
                        if (!IsSafeIdentifierPart(ch))
                        {
                            // fail!
                            return false;
                        }
                    }

                    // if we get here, everything is okay
                    isValid = true;
                }
            }

            return isValid;
        }

        // unescaped unicode characters
        public static bool IsValidIdentifierStart(char letter)
        {
            if (('a' <= letter && letter <= 'z') || ('A' <= letter && letter <= 'Z') || letter == '_' || letter == '$')
            {
                // good
                return true;
            }

            if (letter >= 128)
            {
                // check the unicode category
                UnicodeCategory cat = char.GetUnicodeCategory(letter);
                switch (cat)
                {
                    case UnicodeCategory.UppercaseLetter:
                    case UnicodeCategory.LowercaseLetter:
                    case UnicodeCategory.TitlecaseLetter:
                    case UnicodeCategory.ModifierLetter:
                    case UnicodeCategory.OtherLetter:
                    case UnicodeCategory.LetterNumber:
                        // okay
                        return true;
                }
            }

            return false;
        }

        // unescaped unicode characters.
        // the same as the "IsValid" method, except various browsers have problems with some
        // of the Unicode characters in the ModifierLetter, OtherLetter, and LetterNumber categories.
        public static bool IsSafeIdentifierStart(char letter)
        {
            if (('a' <= letter && letter <= 'z') || ('A' <= letter && letter <= 'Z') || letter == '_' || letter == '$')
            {
                // good
                return true;
            }

            return false;
        }

        public static bool IsValidIdentifierPart(string text)
        {
            var isValid = false;

            // pull the first character from the string, which may be an escape character
            if (!string.IsNullOrEmpty(text))
            {
                char ch = text[0];
                if (ch == '\\')
                {
                    PeekUnicodeEscape(text, ref ch);
                }

                isValid = IsValidIdentifierPart(ch);
            }

            return isValid;
        }

        // unescaped unicode characters
        public static bool IsValidIdentifierPart(char letter)
        {
            // look for valid ranges
            // 0x200c = ZWNJ - zero-width non-joiner
            // 0x200d = ZWJ - zero-width joiner
            if (('a' <= letter && letter <= 'z')
                || ('A' <= letter && letter <= 'Z')
                || ('0' <= letter && letter <= '9')
                || letter == '_'
                || letter == '$'
                || letter == 0x200c    
                || letter == 0x200d)   
            {
                return true;
            }

            if (letter >= 128)
            {
                UnicodeCategory unicodeCategory = Char.GetUnicodeCategory(letter);
                switch (unicodeCategory)
                {
                    case UnicodeCategory.UppercaseLetter:
                    case UnicodeCategory.LowercaseLetter:
                    case UnicodeCategory.TitlecaseLetter:
                    case UnicodeCategory.ModifierLetter:
                    case UnicodeCategory.OtherLetter:
                    case UnicodeCategory.LetterNumber:
                    case UnicodeCategory.NonSpacingMark:
                    case UnicodeCategory.SpacingCombiningMark:
                    case UnicodeCategory.DecimalDigitNumber:
                    case UnicodeCategory.ConnectorPunctuation:
                        return true;
                }
            }

            return false;
        }

        // unescaped unicode characters.
        // the same as the "IsValid" method, except various browsers have problems with some
        // of the Unicode characters in the ModifierLetter, OtherLetter, LetterNumber,
        // NonSpacingMark, SpacingCombiningMark, DecimalDigitNumber, and ConnectorPunctuation categories.
        public static bool IsSafeIdentifierPart(char letter)
        {
            // look for valid ranges
            if (('a' <= letter && letter <= 'z')
                || ('A' <= letter && letter <= 'Z')
                || ('0' <= letter && letter <= '9')
                || letter == '_'
                || letter == '$')
            {
                return true;
            }

            return false;
        }

        // pulling unescaped characters off the input stream
        internal bool IsIdentifierPartChar(char c)
        {
            return IsIdentifierStartChar(ref c) || IsValidIdentifierPart(c);
        }

        private static void PeekUnicodeEscape(string str, ref char ch)
        {
            // if the length isn't at least six characters starting with a backslash, do nothing
            if (!string.IsNullOrEmpty(str) && ch == '\\' && str.Length >= 6)
            {
                if (str[1] == 'u' 
                    && IsHexDigit(str[2])
                    && IsHexDigit(str[3])
                    && IsHexDigit(str[4])
                    && IsHexDigit(str[5]))
                {
                    ch = (char)(GetHexValue(str[2]) << 12 | GetHexValue(str[3]) << 8 | GetHexValue(str[4]) << 4 | GetHexValue(str[5]));
                }
            }
        }

        private bool PeekUnicodeEscape(int index, ref char ch)
        {
            bool isEscapeChar = false;

            // call this only if we had just read a backslash and the pointer is
            // now at the next character, presumably the 'u'
            if ('u' == GetChar(index))
            {
                char h1 = GetChar(index + 1);
                if (IsHexDigit(h1))
                {
                    char h2 = GetChar(index + 2);
                    if (IsHexDigit(h2))
                    {
                        char h3 = GetChar(index + 3);
                        if (IsHexDigit(h3))
                        {
                            char h4 = GetChar(index + 4);
                            if (IsHexDigit(h4))
                            {
                                // this IS a unicode escape, so compute the new character value
                                // and adjust the current position
                                isEscapeChar = true;
                                ch = (char)(GetHexValue(h1) << 12 | GetHexValue(h2) << 8 | GetHexValue(h3) << 4 | GetHexValue(h4));
                            }
                        }
                    }
                }
            }

            return isEscapeChar;
        }

        // pulling unescaped characters off the input stream
        internal bool IsIdentifierStartChar(ref char c)
        {
            bool isEscapeChar = false;
            if ('\\' == c)
            {
                if ('u' == GetChar(m_currentPos + 1))
                {
                    char h1 = GetChar(m_currentPos + 2);
                    if (IsHexDigit(h1))
                    {
                        char h2 = GetChar(m_currentPos + 3);
                        if (IsHexDigit(h2))
                        {
                            char h3 = GetChar(m_currentPos + 4);
                            if (IsHexDigit(h3))
                            {
                                char h4 = GetChar(m_currentPos + 5);
                                if (IsHexDigit(h4))
                                {
                                    isEscapeChar = true;
                                    c = (char)(GetHexValue(h1) << 12 | GetHexValue(h2) << 8 | GetHexValue(h3) << 4 | GetHexValue(h4));
                                }
                            }
                        }
                    }
                }
            }

            if (!IsValidIdentifierStart(c))
            {
                return false;
            }

            // if we get here, we're a good character!
            if (isEscapeChar)
            {
                int startPosition = (m_idLastPosOnBuilder > 0) ? m_idLastPosOnBuilder : m_currentToken.StartPosition;
                if (m_currentPos - startPosition > 0)
                {
                    m_identifier.Append(m_strSourceCode.Substring(startPosition, m_currentPos - startPosition));
                }

                m_identifier.Append(c);
                m_currentPos += 5;
                m_idLastPosOnBuilder = m_currentPos + 1;
            }

            return true;
        }

        internal static bool IsDigit(char c)
        {
            return '0' <= c && c <= '9';
        }

        internal static bool IsHexDigit(char c)
        {
            return ('0' <= c && c <= '9') || ('A' <= c && c <= 'F') || ('a' <= c && c <= 'f');
        }

        internal static bool IsAsciiLetter(char c)
        {
            return ('A' <= c && c <= 'Z') || ('a' <= c && c <= 'z');
        }

        private string PPScanIdentifier(bool forceUpper)
        {
            string identifier = null;

            // start at the current position
            var startPos = m_currentPos;

            // see if the first character is a valid identifier start
            if (JSScanner.IsValidIdentifierStart(GetChar(startPos)))
            {
                // it is -- skip to the next character
                ++m_currentPos;

                // and keep going as long as we have valid part characters
                while (JSScanner.IsValidIdentifierPart(GetChar(m_currentPos)))
                {
                    ++m_currentPos;
                }
            }

            // if we advanced at all, return the code we scanned. Otherwise return null
            if (m_currentPos > startPos)
            {
                identifier = m_strSourceCode.Substring(startPos, m_currentPos - startPos);
                if (forceUpper)
                {
                    identifier = identifier.ToUpperInvariant();
                }
            }

            return identifier;
        }

        private bool PPScanInteger(out int intValue)
        {
            var startPos = m_currentPos;
            while (IsDigit(GetChar(m_currentPos)))
            {
                ++m_currentPos;
            }

            var success = false;
            if ( m_currentPos > startPos)
            {
                success = int.TryParse(m_strSourceCode.Substring(startPos, m_currentPos - startPos), out intValue);
            }
            else
            {
                intValue = 0;
            }

            return success;
        }

        private int PPSkipToDirective(params string[] endStrings)
        {
            while (true)
            {
                char c = GetChar(m_currentPos++);
                switch (c)
                {
                    // EOF
                    case (char)0:
                        if (m_currentPos >= m_endPos)
                        {
                            m_currentPos--;
                            m_currentToken.EndPosition = m_currentPos;
                            m_currentToken.EndLineNumber = CurrentLine;
                            m_currentToken.EndLinePosition = StartLinePosition;
                            HandleError(JSError.NoCCEnd);
                            throw new ScannerException(JSError.ErrorEndOfFile);
                        }

                        break;

                    // line terminator crap
                    case '\r':
                        if (GetChar(m_currentPos) == '\n')
                        {
                            m_currentPos++;
                        }

                        CurrentLine++;
                        StartLinePosition = m_currentPos;
                        break;
                    case '\n':
                        CurrentLine++;
                        StartLinePosition = m_currentPos;
                        break;
                    case (char)0x2028:
                        CurrentLine++;
                        StartLinePosition = m_currentPos;
                        break;
                    case (char)0x2029:
                        CurrentLine++;
                        StartLinePosition = m_currentPos;
                        break;

                    // check for /// (and then followed by any one of the substrings passed to us)
                    case '/':
                        if (CheckSubstring(m_currentPos, "//"))
                        {
                            // skip it
                            m_currentPos += 2;

                            // check to see if this is the start of ANOTHER preprocessor construct. If it
                            // is, then it's a NESTED statement and we'll need to recursively skip the 
                            // whole thing so everything stays on track
                            if (CheckCaseInsensitiveSubstring(m_currentPos, "#IFDEF")
                                || CheckCaseInsensitiveSubstring(m_currentPos, "#IFNDEF"))
                            {
                                PPSkipToDirective("#ENDIF");
                            }
                            else
                            {
                                // now check each of the ending strings that were passed to us to see if one of
                                // them is a match
                                for (var ndx = 0; ndx < endStrings.Length; ++ndx)
                                {
                                    if (CheckCaseInsensitiveSubstring(m_currentPos, endStrings[ndx]))
                                    {
                                        // found the ending string
                                        // skip it and bail
                                        m_currentPos += endStrings[ndx].Length;
                                        return ndx;
                                    }
                                }
                            }
                        }

                        break;
                }
            }
        }

        private void HandleError(JSError error)
        {
            m_currentToken.EndPosition = m_currentPos;
            m_currentToken.EndLinePosition = StartLinePosition;
            m_currentToken.EndLineNumber = CurrentLine;
            m_currentToken.HandleError(error);
        }

        internal static bool IsAssignmentOperator(JSToken token)
        {
            return JSToken.Assign <= token && token <= JSToken.LastAssign;
        }

        internal static bool IsRightAssociativeOperator(JSToken token)
        {
            return JSToken.Assign <= token && token <= JSToken.ConditionalIf;
        }

        // This function return whether an operator is processable in ParseExpression.
        // Comma is out of this list and so are the unary ops
        internal static bool IsProcessableOperator(JSToken token)
        {
            return JSToken.FirstBinaryOperator <= token && token <= JSToken.ConditionalIf;
        }

        private static readonly OperatorPrecedence[] s_OperatorsPrec = InitOperatorsPrec();

        internal static OperatorPrecedence GetOperatorPrecedence(JSToken token)
        {
            return token == JSToken.None ? OperatorPrecedence.None : JSScanner.s_OperatorsPrec[token - JSToken.FirstBinaryOperator];
        }

        private static OperatorPrecedence[] InitOperatorsPrec()
        {
            OperatorPrecedence[] operatorsPrec = new OperatorPrecedence[JSToken.LastOperator - JSToken.FirstBinaryOperator + 1];

            operatorsPrec[JSToken.Plus - JSToken.FirstBinaryOperator] = OperatorPrecedence.Additive;
            operatorsPrec[JSToken.Minus - JSToken.FirstBinaryOperator] = OperatorPrecedence.Additive;

            operatorsPrec[JSToken.LogicalOr - JSToken.FirstBinaryOperator] = OperatorPrecedence.LogicalOr;
            operatorsPrec[JSToken.LogicalAnd - JSToken.FirstBinaryOperator] = OperatorPrecedence.LogicalAnd;
            operatorsPrec[JSToken.BitwiseOr - JSToken.FirstBinaryOperator] = OperatorPrecedence.BitwiseOr;
            operatorsPrec[JSToken.BitwiseXor - JSToken.FirstBinaryOperator] = OperatorPrecedence.BitwiseXor;
            operatorsPrec[JSToken.BitwiseAnd - JSToken.FirstBinaryOperator] = OperatorPrecedence.BitwiseAnd;

            operatorsPrec[JSToken.Equal - JSToken.FirstBinaryOperator] = OperatorPrecedence.Equality;
            operatorsPrec[JSToken.NotEqual - JSToken.FirstBinaryOperator] = OperatorPrecedence.Equality;
            operatorsPrec[JSToken.StrictEqual - JSToken.FirstBinaryOperator] = OperatorPrecedence.Equality;
            operatorsPrec[JSToken.StrictNotEqual - JSToken.FirstBinaryOperator] = OperatorPrecedence.Equality;

            operatorsPrec[JSToken.InstanceOf - JSToken.FirstBinaryOperator] = OperatorPrecedence.Relational;
            operatorsPrec[JSToken.In - JSToken.FirstBinaryOperator] = OperatorPrecedence.Relational;
            operatorsPrec[JSToken.GreaterThan - JSToken.FirstBinaryOperator] = OperatorPrecedence.Relational;
            operatorsPrec[JSToken.LessThan - JSToken.FirstBinaryOperator] = OperatorPrecedence.Relational;
            operatorsPrec[JSToken.LessThanEqual - JSToken.FirstBinaryOperator] = OperatorPrecedence.Relational;
            operatorsPrec[JSToken.GreaterThanEqual - JSToken.FirstBinaryOperator] = OperatorPrecedence.Relational;

            operatorsPrec[JSToken.LeftShift - JSToken.FirstBinaryOperator] = OperatorPrecedence.Shift;
            operatorsPrec[JSToken.RightShift - JSToken.FirstBinaryOperator] = OperatorPrecedence.Shift;
            operatorsPrec[JSToken.UnsignedRightShift - JSToken.FirstBinaryOperator] = OperatorPrecedence.Shift;

            operatorsPrec[JSToken.Multiply - JSToken.FirstBinaryOperator] = OperatorPrecedence.Multiplicative;
            operatorsPrec[JSToken.Divide - JSToken.FirstBinaryOperator] = OperatorPrecedence.Multiplicative;
            operatorsPrec[JSToken.Modulo - JSToken.FirstBinaryOperator] = OperatorPrecedence.Multiplicative;

            operatorsPrec[JSToken.Assign - JSToken.FirstBinaryOperator] = OperatorPrecedence.Assignment;
            operatorsPrec[JSToken.PlusAssign - JSToken.FirstBinaryOperator] = OperatorPrecedence.Assignment;
            operatorsPrec[JSToken.MinusAssign - JSToken.FirstBinaryOperator] = OperatorPrecedence.Assignment;
            operatorsPrec[JSToken.MultiplyAssign - JSToken.FirstBinaryOperator] = OperatorPrecedence.Assignment;
            operatorsPrec[JSToken.DivideAssign - JSToken.FirstBinaryOperator] = OperatorPrecedence.Assignment;
            operatorsPrec[JSToken.BitwiseAndAssign - JSToken.FirstBinaryOperator] = OperatorPrecedence.Assignment;
            operatorsPrec[JSToken.BitwiseOrAssign - JSToken.FirstBinaryOperator] = OperatorPrecedence.Assignment;
            operatorsPrec[JSToken.BitwiseXorAssign - JSToken.FirstBinaryOperator] = OperatorPrecedence.Assignment;
            operatorsPrec[JSToken.ModuloAssign - JSToken.FirstBinaryOperator] = OperatorPrecedence.Assignment;
            operatorsPrec[JSToken.LeftShiftAssign - JSToken.FirstBinaryOperator] = OperatorPrecedence.Assignment;
            operatorsPrec[JSToken.RightShiftAssign - JSToken.FirstBinaryOperator] = OperatorPrecedence.Assignment;
            operatorsPrec[JSToken.UnsignedRightShiftAssign - JSToken.FirstBinaryOperator] = OperatorPrecedence.Assignment;

            operatorsPrec[JSToken.ConditionalIf - JSToken.FirstBinaryOperator] = OperatorPrecedence.Conditional;
            operatorsPrec[JSToken.Colon - JSToken.FirstBinaryOperator] = OperatorPrecedence.Conditional;

            operatorsPrec[JSToken.Comma - JSToken.FirstBinaryOperator] = OperatorPrecedence.Comma;

            return operatorsPrec;
        }
    }

    public class GlobalDefineEventArgs : EventArgs
    {
        public string Name { get; set; }
    }
}