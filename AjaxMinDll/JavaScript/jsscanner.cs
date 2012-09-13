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
        #region static fields

        // keyword table
        private static readonly JSKeyword[] s_Keywords = JSKeyword.InitKeywords();

        private static readonly OperatorPrecedence[] s_OperatorsPrec = InitOperatorsPrec();

        #endregion

        #region private fields

        // scanner main data
        private JSScannerState m_scannerState;

        private string m_strSourceCode;

        private int m_endPos;

        private StringBuilder m_identifier;

        private bool m_peekModeOn;

        // a list of strings that we can add new ones to or clear
        // depending on comments we may find in the source
        internal ICollection<string> DebugLookupCollection { get; set; }

        /// <summary>
        /// List of important comment contexts we encountered before we found the 
        /// current token
        /// </summary>
        private List<Context> m_importantComments;

        // for pre-processor
        private Dictionary<string, string> m_defines;

        #endregion

        #region public properties

        public bool UsePreprocessorDefines { get; set; }

        public bool IgnoreConditionalCompilation { get; set; }

        public bool AllowEmbeddedAspNetBlocks { get; set; }

        public bool SkipDebugBlocks { get; set; }

        public bool InComment
        {
            get
            {
                return m_scannerState.InMultipleLineComment || m_scannerState.InSingleLineComment;
            }
        }

        public bool GotEndOfLine
        {
            get
            {
                return m_scannerState.GotEndOfLine;
            }
        }

        public string EscapedString
        {
            get
            {
                return m_scannerState.EscapedString;
            }
        }

        public int CurrentLine
        {
            get
            {
                return m_scannerState.CurrentLine;
            }
        }

        public int StartLinePosition
        {
            get
            {
                return m_scannerState.StartLinePosition;
            }
        }

        /// <summary>
        /// returns true if we found one or more important comments before the current token
        /// </summary>
        public bool HasImportantComments
        {
            get { return m_importantComments != null && m_importantComments.Count > 0; }
        }

        // turning this property on makes the scanner just return raw tokens without any
        // conditional-compilation comment processing.
        public bool RawTokens { get; set; }

        #endregion

        #region public events

        public event EventHandler<GlobalDefineEventArgs> GlobalDefine;

        #endregion

        #region constructors

        public JSScanner(Context sourceContext)
        {
            if (sourceContext == null)
            {
                throw new ArgumentNullException("sourceContext");
            }

            // create the initial scanner state
            m_scannerState = new JSScannerState()
                {
                    PreviousToken = JSToken.None,
                    StartLinePosition = sourceContext.StartLinePosition,
                    CurrentPosition = sourceContext.StartLinePosition,
                    CurrentLine = sourceContext.StartLineNumber,
                    CurrentToken = sourceContext.Clone()
                };

            // by default we want to use preprocessor defines
            UsePreprocessorDefines = true;

            // just hold on to these values
            m_strSourceCode = sourceContext.Document.Source;
            m_endPos = sourceContext.EndPosition;

            // create a string builder that we'll keep reusing as we
            // scan identifiers. We'll build the unescaped name into it
            m_identifier = new StringBuilder(128);
        }

        #endregion

        /// <summary>
        /// main method for the scanner; scans the next token from the input stream.
        /// </summary>
        /// <returns>next token from the input</returns>
        public Context ScanNextToken()
        {
            JSToken token = JSToken.None;
            m_scannerState.GotEndOfLine = false;
            m_importantComments = null;
            try
            {
                int thisCurrentLine = m_scannerState.CurrentLine;

            nextToken:
                // skip any blanks, setting a state flag if we find any
                bool ws = JSScanner.IsBlankSpace(GetChar(m_scannerState.CurrentPosition));
                if (ws && !RawTokens)
                {
                    // we're not looking for war tokens, so just want to eat the whitespace
                    while (JSScanner.IsBlankSpace(GetChar(++m_scannerState.CurrentPosition))) ;
                }

                m_scannerState.CurrentToken.StartPosition = m_scannerState.CurrentPosition;
                m_scannerState.CurrentToken.StartLineNumber = m_scannerState.CurrentLine;
                m_scannerState.CurrentToken.StartLinePosition = m_scannerState.StartLinePosition;
                char c = GetChar(m_scannerState.CurrentPosition++);
                switch (c)
                {
                    case (char)0:
                        if (m_scannerState.CurrentPosition >= m_endPos)
                        {
                            m_scannerState.CurrentPosition--;
                            token = JSToken.EndOfFile;
                            if (m_scannerState.ConditionalCompilationIfLevel > 0)
                            {
                                m_scannerState.CurrentToken.EndLineNumber = m_scannerState.CurrentLine;
                                m_scannerState.CurrentToken.EndLinePosition = m_scannerState.StartLinePosition;
                                m_scannerState.CurrentToken.EndPosition = m_scannerState.CurrentPosition;
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
                        if ('=' == GetChar(m_scannerState.CurrentPosition))
                        {
                            m_scannerState.CurrentPosition++;
                            token = JSToken.Equal;
                            if ('=' == GetChar(m_scannerState.CurrentPosition))
                            {
                                m_scannerState.CurrentPosition++;
                                token = JSToken.StrictEqual;
                            }
                        }

                        break;

                    case '>':
                        token = JSToken.GreaterThan;
                        if ('>' == GetChar(m_scannerState.CurrentPosition))
                        {
                            m_scannerState.CurrentPosition++;
                            token = JSToken.RightShift;
                            if ('>' == GetChar(m_scannerState.CurrentPosition))
                            {
                                m_scannerState.CurrentPosition++;
                                token = JSToken.UnsignedRightShift;
                            }
                        }

                        if ('=' == GetChar(m_scannerState.CurrentPosition))
                        {
                            m_scannerState.CurrentPosition++;
                            token = token == JSToken.GreaterThan
                                ? JSToken.GreaterThanEqual
                                : token == JSToken.RightShift ? JSToken.RightShiftAssign
                                : token == JSToken.UnsignedRightShift ? JSToken.UnsignedRightShiftAssign
                                : token;
                        }

                        break;

                    case '<':
                        if (AllowEmbeddedAspNetBlocks &&
                            '%' == GetChar(m_scannerState.CurrentPosition))
                        {
                            token = ScanAspNetBlock();
                        }
                        else
                        {
                            token = JSToken.LessThan;
                            if ('<' == GetChar(m_scannerState.CurrentPosition))
                            {
                                m_scannerState.CurrentPosition++;
                                token = JSToken.LeftShift;
                            }

                            if ('=' == GetChar(m_scannerState.CurrentPosition))
                            {
                                m_scannerState.CurrentPosition++;
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
                        if ('=' == GetChar(m_scannerState.CurrentPosition))
                        {
                            m_scannerState.CurrentPosition++;
                            token = JSToken.NotEqual;
                            if ('=' == GetChar(m_scannerState.CurrentPosition))
                            {
                                m_scannerState.CurrentPosition++;
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
                        c = GetChar(m_scannerState.CurrentPosition);
                        if (JSScanner.IsDigit(c))
                        {
                            token = ScanNumber('.');
                        }

                        break;

                    case '&':
                        token = JSToken.BitwiseAnd;
                        c = GetChar(m_scannerState.CurrentPosition);
                        if ('&' == c)
                        {
                            m_scannerState.CurrentPosition++;
                            token = JSToken.LogicalAnd;
                        }
                        else if ('=' == c)
                        {
                            m_scannerState.CurrentPosition++;
                            token = JSToken.BitwiseAndAssign;
                        }

                        break;

                    case '|':
                        token = JSToken.BitwiseOr;
                        c = GetChar(m_scannerState.CurrentPosition);
                        if ('|' == c)
                        {
                            m_scannerState.CurrentPosition++;
                            token = JSToken.LogicalOr;
                        }
                        else if ('=' == c)
                        {
                            m_scannerState.CurrentPosition++;
                            token = JSToken.BitwiseOrAssign;
                        }

                        break;

                    case '+':
                        token = JSToken.Plus;
                        c = GetChar(m_scannerState.CurrentPosition);
                        if ('+' == c)
                        {
                            m_scannerState.CurrentPosition++;
                            token = JSToken.Increment;
                        }
                        else if ('=' == c)
                        {
                            m_scannerState.CurrentPosition++;
                            token = JSToken.PlusAssign;
                        }

                        break;

                    case '-':
                        token = JSToken.Minus;
                        c = GetChar(m_scannerState.CurrentPosition);
                        if ('-' == c)
                        {
                            m_scannerState.CurrentPosition++;
                            token = JSToken.Decrement;
                        }
                        else if ('=' == c)
                        {
                            m_scannerState.CurrentPosition++;
                            token = JSToken.MinusAssign;
                        }

                        break;

                    case '*':
                        token = JSToken.Multiply;
                        if ('=' == GetChar(m_scannerState.CurrentPosition))
                        {
                            m_scannerState.CurrentPosition++;
                            token = JSToken.MultiplyAssign;
                        }

                        break;

                    case '\\':
                        // try decoding a unicode escape sequence. We read the backslash and
                        // now the "current" character is the "u"
                        if (PeekUnicodeEscape(m_scannerState.CurrentPosition, ref c))
                        {
                            // advance past the escape characters
                            m_scannerState.CurrentPosition += 5;

                            // valid unicode escape sequence
                            if (IsValidIdentifierStart(c))
                            {
                                // use the unescaped character as the first character of the
                                // decoded identifier, and current character is now the last position
                                // on the builder
                                m_identifier.Append(c);
                                m_scannerState.LastPosOnBuilder = m_scannerState.CurrentPosition;

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
                            if (IsValidIdentifierStart(GetChar(m_scannerState.CurrentPosition)))
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
                        c = GetChar(m_scannerState.CurrentPosition);
                        switch (c)
                        {
                            case '/':
                                m_scannerState.InSingleLineComment = true;
                                c = GetChar(++m_scannerState.CurrentPosition);

                                // see if there is a THIRD slash character
                                if (c == '/')
                                {
                                    // advance past the slash and see if we have one of our special preprocessing directives
                                    ++m_scannerState.CurrentPosition;
                                    if (GetChar(m_scannerState.CurrentPosition) == '#')
                                    {
                                        // scan preprocessing directives
                                        if (ScanPreprocessingDirective())
                                        {
                                            goto nextToken;
                                        }
                                    }
                                }
                                else if (!RawTokens && c == '@' && !IgnoreConditionalCompilation && !m_peekModeOn)
                                {
                                    // we got //@
                                    // if we have not turned on conditional-compilation yet, then check to see if that's
                                    // what we're trying to do now
                                    if (!m_scannerState.ConditionalCompilationOn)
                                    {
                                        // we are currently on the @ -- start peeking from there
                                        if (!CheckSubstring(m_scannerState.CurrentPosition + 1, "cc_on"))
                                        {
                                            // we aren't turning on conditional comments. We need to ignore this comment
                                            // as just another single-line comment
                                            SkipSingleLineComment();
                                            goto nextToken;
                                        }
                                    }

                                    // if the NEXT character is not an identifier character, then we need to skip
                                    // the @ character -- otherwise leave it there
                                    if (!IsValidIdentifierStart(GetChar(m_scannerState.CurrentPosition + 1)))
                                    {
                                        ++m_scannerState.CurrentPosition;
                                    }

                                    // if we aren't already in a conditional comment
                                    if (!m_scannerState.InConditionalComment)
                                    {
                                        // we are now
                                        m_scannerState.InConditionalComment = true;
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
                                    if (!m_scannerState.InMultipleLineComment && m_scannerState.InConditionalComment)
                                    {
                                        m_scannerState.InConditionalComment = false;
                                        token = JSToken.ConditionalCommentEnd;
                                        break;
                                    }

                                    goto nextToken; // read another token this last one was a comment
                                }

                            case '*':
                                m_scannerState.InMultipleLineComment = true;
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
                                    if (GetChar(++m_scannerState.CurrentPosition) == '@' && !IgnoreConditionalCompilation && !m_peekModeOn)
                                    {
                                        // we have /*@
                                        // if we have not turned on conditional-compilation yet, then let's peek to see if the next
                                        // few characters are cc_on -- if so, turn it on.
                                        if (!m_scannerState.ConditionalCompilationOn)
                                        {
                                            // we are currently on the @ -- start peeking from there
                                            if (!CheckSubstring(m_scannerState.CurrentPosition + 1, "cc_on"))
                                            {
                                                // we aren't turning on conditional comments. We need to ignore this comment
                                                // as just another multi-line comment
                                                SkipMultilineComment(false);
                                                goto nextToken;
                                            }
                                        }
                                            
                                        // if the NEXT character is not an identifier character, then we need to skip
                                        // the @ character -- otherwise leave it there
                                        if (!IsValidIdentifierStart(GetChar(m_scannerState.CurrentPosition + 1)))
                                        {
                                            ++m_scannerState.CurrentPosition;
                                        }

                                        // if we aren't already in a conditional comment
                                        if (!m_scannerState.InConditionalComment)
                                        {
                                            // we are in one now
                                            m_scannerState.InConditionalComment = true;
                                            token = JSToken.ConditionalCommentStart;
                                            break;
                                        }

                                        // we were already in a conditional comment, so ignore the superfluous
                                        // conditional comment start
                                        goto nextToken;
                                    }

                                    if (GetChar(m_scannerState.CurrentPosition) == '!')
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
                                if (RawTokens && RegExpCanFollow(m_scannerState.PreviousToken))
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
                                        m_scannerState.CurrentPosition++;
                                        token = JSToken.DivideAssign;
                                    }
                                }
                                else if (c == '=')
                                {
                                    m_scannerState.CurrentPosition++;
                                    token = JSToken.DivideAssign;
                                }
                                break;
                        }

                        break;

                    case '^':
                        token = JSToken.BitwiseXor;
                        if ('=' == GetChar(m_scannerState.CurrentPosition))
                        {
                            m_scannerState.CurrentPosition++;
                            token = JSToken.BitwiseXorAssign;
                        }

                        break;

                    case '%':
                        token = JSToken.Modulo;
                        if ('=' == GetChar(m_scannerState.CurrentPosition))
                        {
                            m_scannerState.CurrentPosition++;
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
                        if (m_scannerState.InConditionalComment && m_scannerState.InSingleLineComment)
                        {
                            token = JSToken.ConditionalCommentEnd;
                            m_scannerState.InConditionalComment = m_scannerState.InSingleLineComment = false;
                            break;
                        }

                        // \r\n is a valid SINGLE line-terminator. So if the \r is
                        // followed by a \n, we only want to process a single line terminator.
                        if (GetChar(m_scannerState.CurrentPosition) == '\n')
                        {
                            m_scannerState.CurrentPosition++;
                        }

                        // drop down into normal line-ending processing
                        goto case '\n';

                    case '\n':
                    case (char)0x2028:
                    case (char)0x2029:
                        // if we are in a single-line conditional comment, then
                        // clean up the flags and return the end of the conditional comment
                        // WITHOUT skipping past the end of line
                        if (m_scannerState.InConditionalComment && m_scannerState.InSingleLineComment)
                        {
                            token = JSToken.ConditionalCommentEnd;
                            m_scannerState.InConditionalComment = m_scannerState.InSingleLineComment = false;
                            break;
                        }

                        m_scannerState.CurrentLine++;
                        m_scannerState.StartLinePosition = m_scannerState.CurrentPosition;

                        m_scannerState.InSingleLineComment = false;
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
                            m_scannerState.CurrentToken.Token = JSToken.PreprocessDirective;
                            break;
                        }

                        // see if the @-sign is immediately followed by an identifier. If it is,
                        // we'll see which one so we can tell if it's a conditional-compilation statement
                        // need to make sure the context INCLUDES the @ sign
                        int startPosition = m_scannerState.CurrentPosition;
                        m_scannerState.CurrentToken.StartPosition = startPosition - 1;
                        m_scannerState.CurrentToken.StartLineNumber = m_scannerState.CurrentLine;
                        m_scannerState.CurrentToken.StartLinePosition = m_scannerState.StartLinePosition;
                        ScanIdentifier();
                        switch (m_scannerState.CurrentPosition - startPosition)
                        {
                            case 0:
                                // look for '@*/'.
                                if (/*ScannerState.ConditionalCompilationOn &&*/ '*' == GetChar(m_scannerState.CurrentPosition) && '/' == GetChar(++m_scannerState.CurrentPosition))
                                {
                                    m_scannerState.CurrentPosition++;
                                    m_scannerState.InMultipleLineComment = false;
                                    m_scannerState.InConditionalComment = false;
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
                                    ++m_scannerState.ConditionalCompilationIfLevel;

                                    // if we're not in a conditional comment and we haven't explicitly
                                    // turned on conditional compilation when we encounter
                                    // a @if statement, then we can implicitly turn it on.
                                    if (!m_scannerState.InConditionalComment && !m_scannerState.ConditionalCompilationOn)
                                    {
                                        m_scannerState.ConditionalCompilationOn = true;
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
                                    if (!m_scannerState.InConditionalComment && !m_scannerState.ConditionalCompilationOn)
                                    {
                                        m_scannerState.ConditionalCompilationOn = true;
                                    }

                                    break;
                                }

                                if (CheckSubstring(startPosition, "end"))
                                {
                                    token = JSToken.ConditionalCompilationEnd;
                                    if (m_scannerState.ConditionalCompilationIfLevel > 0)
                                    {
                                        // down one more @if level
                                        m_scannerState.ConditionalCompilationIfLevel--;
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
                                    if (m_scannerState.ConditionalCompilationIfLevel <= 0)
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
                                    if (m_scannerState.ConditionalCompilationIfLevel <= 0)
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
                                    m_scannerState.ConditionalCompilationOn = true;
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
                                if (!m_scannerState.ConditionalCompilationOn)
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
                            JSKeyword keyword = s_Keywords[c - 'a'];
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
                            while (JSScanner.IsBlankSpace(GetChar(m_scannerState.CurrentPosition)))
                            {
                                ++m_scannerState.CurrentPosition;
                            }
                            token = JSToken.WhiteSpace;
                        }
                        else
                        {
                            m_scannerState.CurrentToken.EndLineNumber = m_scannerState.CurrentLine;
                            m_scannerState.CurrentToken.EndLinePosition = m_scannerState.StartLinePosition;
                            m_scannerState.CurrentToken.EndPosition = m_scannerState.CurrentPosition;

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
                m_scannerState.CurrentToken.EndLineNumber = m_scannerState.CurrentLine;
                m_scannerState.CurrentToken.EndLinePosition = m_scannerState.StartLinePosition;
                m_scannerState.CurrentToken.EndPosition = m_scannerState.CurrentPosition;
                m_scannerState.GotEndOfLine = (m_scannerState.CurrentLine > thisCurrentLine || token == JSToken.EndOfFile) ? true : false;
                if (m_scannerState.GotEndOfLine && token == JSToken.StringLiteral && m_scannerState.CurrentToken.StartLineNumber == thisCurrentLine)
                {
                    m_scannerState.GotEndOfLine = false;
                }
            }
            catch (IndexOutOfRangeException)
            {
                m_scannerState.CurrentToken.Token = JSToken.None;
                m_scannerState.CurrentToken.EndPosition = m_scannerState.CurrentPosition;
                m_scannerState.CurrentToken.EndLineNumber = m_scannerState.CurrentLine;
                m_scannerState.CurrentToken.EndLinePosition = m_scannerState.StartLinePosition;
                throw new ScannerException(JSError.ErrorEndOfFile);
            }

            // this is now the current token
            m_scannerState.CurrentToken.Token = token;

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
                    m_scannerState.PreviousToken = token;
                    break;
            }

            return m_scannerState.CurrentToken;
        }

        internal JSToken PeekToken()
        {
            m_peekModeOn = true;

            // so we need to make sure that the state we restore is using the same
            // current-token object as it is now, because we reuse that object instead of
            // creating a new one every time. And the parser probably has a pointer
            // to it right now, so we don't want to break them by changing the object out
            // from under them and having it be somethinbg completely different when we return.
            // so save THIS state, clone a new one for the peek, then restore THIS state 
            // before we go back.
            var savedState = m_scannerState;
            m_scannerState = m_scannerState.Clone();
            try
            {
                return ScanNextToken().Token;
            }
            finally
            {
                m_scannerState = savedState;
                m_identifier.Length = 0;
                m_peekModeOn = false;
            }
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

        /// <summary>
        /// Set the lis of predefined preprocessor names, but without values
        /// </summary>
        /// <param name="definedNames">list of names only</param>
        public void SetPreprocessorDefines(ICollection<string> definedNames)
        {
            // this is a destructive set, blowing away any previous list
            if (definedNames != null && definedNames.Count > 0)
            {
                // create a new list, case-INsensitive
                m_defines = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                // add an entry for each non-duplicate, valid name passed to us
                foreach (var definedName in definedNames)
                {
                    if (JSScanner.IsValidIdentifier(definedName) && !m_defines.ContainsKey(definedName))
                    {
                        m_defines.Add(definedName, string.Empty);
                    }
                }
            }
            else
            {
                // we have no defined names
                m_defines = null;
            }
        }

        /// <summary>
        /// Set the list of preprocessor defined names and values
        /// </summary>
        /// <param name="defines">dictionary of name/value pairs</param>
        public void SetPreprocessorDefines(IDictionary<string, string> defines)
        {
            // this is a destructive set, blowing away any previous list
            if (defines != null && defines.Count > 0)
            {
                // create a new dictionary, case-INsensitive
                m_defines = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                // add an entry for each unique, valid name passed to us.
                foreach (var nameValuePair in defines)
                {
                    if (JSScanner.IsValidIdentifier(nameValuePair.Key) && !m_defines.ContainsKey(nameValuePair.Key))
                    {
                        m_defines.Add(nameValuePair.Key, nameValuePair.Value);
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

        private bool CheckCaseInsensitiveSubstring(string target)
        {
            var startIndex = m_scannerState.CurrentPosition;
            for (int ndx = 0; ndx < target.Length; ++ndx)
            {
                if (target[ndx] != char.ToUpperInvariant(GetChar(startIndex + ndx)))
                {
                    // no match
                    return false;
                }
            }

            // if we got here, the strings match. Advance the current position over it
            m_scannerState.CurrentPosition += target.Length;
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
                id = m_scannerState.CurrentToken.Code;
            }

            return id;
        }

        private void ScanIdentifier()
        {
            for (;;)
            {
                char c = GetChar(m_scannerState.CurrentPosition);
                if (!IsIdentifierPartChar(c))
                {
                    break;
                }

                ++m_scannerState.CurrentPosition;
            }

            if (AllowEmbeddedAspNetBlocks
                && CheckSubstring(m_scannerState.CurrentPosition, "<%="))
            {
                // the identifier has an ASP.NET <%= ... %> block as part of it.
                // move the current position to the opening % character and call 
                // the method that will parse it from there.
                ++m_scannerState.CurrentPosition;
                ScanAspNetBlock();
            }

            if (m_scannerState.LastPosOnBuilder > 0)
            {
                m_identifier.Append(m_strSourceCode.Substring(m_scannerState.LastPosOnBuilder, m_scannerState.CurrentPosition - m_scannerState.LastPosOnBuilder));
                m_scannerState.LastPosOnBuilder = 0;
            }
        }

        private JSToken ScanKeyword(JSKeyword keyword)
        {
            for (;;)
            {
                char c = GetChar(m_scannerState.CurrentPosition);
                if ('a' <= c && c <= 'z')
                {
                    m_scannerState.CurrentPosition++;
                    continue;
                }

                if (IsIdentifierPartChar(c) 
                    || (AllowEmbeddedAspNetBlocks && CheckSubstring(m_scannerState.CurrentPosition, "<%=")))
                {
                    ScanIdentifier();
                    return JSToken.Identifier;
                }

                break;
            }

            return keyword.GetKeyword(m_scannerState.CurrentToken, m_scannerState.CurrentPosition - m_scannerState.CurrentToken.StartPosition);
        }

        private JSToken ScanNumber(char leadChar)
        {
            bool noMoreDot = '.' == leadChar;
            JSToken token = noMoreDot ? JSToken.NumericLiteral : JSToken.IntegerLiteral;
            bool exponent = false;
            char c;

            if ('0' == leadChar)
            {
                c = GetChar(m_scannerState.CurrentPosition);
                if ('x' == c || 'X' == c)
                {
                    if (!JSScanner.IsHexDigit(GetChar(m_scannerState.CurrentPosition + 1)))
                    {
                        // bump it up two characters to pick up the 'x' and the bad digit
                        m_scannerState.CurrentPosition += 2;
                        HandleError(JSError.BadHexDigit);
                        // bump it down three characters to go back to the 0
                        m_scannerState.CurrentPosition -= 3;
                    }

                    while (JSScanner.IsHexDigit(GetChar(++m_scannerState.CurrentPosition)))
                    {
                        // empty
                    }

                    return token;
                }
            }

            for (;;)
            {
                c = GetChar(m_scannerState.CurrentPosition);
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
                        char e = GetChar(m_scannerState.CurrentPosition - 1);
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

                m_scannerState.CurrentPosition++;
            }

            c = GetChar(m_scannerState.CurrentPosition - 1);
            if ('+' == c || '-' == c)
            {
                m_scannerState.CurrentPosition--;
                c = GetChar(m_scannerState.CurrentPosition - 1);
            }

            if ('e' == c || 'E' == c)
            {
                m_scannerState.CurrentPosition--;
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
            int pos = m_scannerState.CurrentPosition;
            bool isEscape = false;
            bool isInSet = false;
            char c;
            while (!IsEndLineOrEOF(c = GetChar(m_scannerState.CurrentPosition++), 0))
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
                    if (pos == m_scannerState.CurrentPosition)
                    {
                        return null;
                    }

                    m_scannerState.CurrentToken.EndPosition = m_scannerState.CurrentPosition;
                    m_scannerState.CurrentToken.EndLinePosition = m_scannerState.StartLinePosition;
                    m_scannerState.CurrentToken.EndLineNumber = m_scannerState.CurrentLine;
                    return m_strSourceCode.Substring(
                        m_scannerState.CurrentToken.StartPosition + 1,
                        m_scannerState.CurrentToken.EndPosition - m_scannerState.CurrentToken.StartPosition - 2);
                }
                else if (c == '\\')
                {
                    isEscape = true;
                }
            }

            // reset and return null. Assume it is not a reg exp
            m_scannerState.CurrentPosition = pos;
            return null;
        }

        internal String ScanRegExpFlags()
        {
            int pos = m_scannerState.CurrentPosition;
            while (JSScanner.IsAsciiLetter(GetChar(m_scannerState.CurrentPosition)))
            {
                m_scannerState.CurrentPosition++;
            }

            if (pos != m_scannerState.CurrentPosition)
            {
                m_scannerState.CurrentToken.EndPosition = m_scannerState.CurrentPosition;
                m_scannerState.CurrentToken.EndLineNumber = m_scannerState.CurrentLine;
                m_scannerState.CurrentToken.EndLinePosition = m_scannerState.StartLinePosition;
                return m_strSourceCode.Substring(pos, m_scannerState.CurrentToken.EndPosition - pos);
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
            var thirdChar = GetChar(++m_scannerState.CurrentPosition);

            // advance to the next character
            ++m_scannerState.CurrentPosition;

            // loop until we find a > with a % before it (%>)
            while (!(this.GetChar(this.m_scannerState.CurrentPosition - 1) == '%' &&
                     this.GetChar(this.m_scannerState.CurrentPosition) == '>') ||
                     (m_scannerState.CurrentPosition >= m_endPos))
            {
                this.m_scannerState.CurrentPosition++;
            }

            // we should be at the > of the %> right now.
            // set the end point of this token
            m_scannerState.CurrentToken.EndPosition = m_scannerState.CurrentPosition + 1;
            m_scannerState.CurrentToken.EndLineNumber = m_scannerState.CurrentLine;
            m_scannerState.CurrentToken.EndLinePosition = m_scannerState.StartLinePosition;

            // see if we found an unterminated asp.net block
            if (m_scannerState.CurrentPosition >= m_endPos)
            {
                HandleError(JSError.UnterminatedAspNetBlock);
            }
            else
            {
                // Eat the last >.
                this.m_scannerState.CurrentPosition++;

                if (thirdChar == '=')
                {
                    // this is a <%= ... %> token.
                    // we're going to treat this like an identifier
                    tokenType = JSToken.Identifier;

                    // now, if the next character is an identifier part
                    // then skip to the end of the identifier. And if this is
                    // another <%= then skip to the end (%>)
                    if (IsValidIdentifierPart(GetChar(m_scannerState.CurrentPosition))
                        || CheckSubstring(m_scannerState.CurrentPosition, "<%="))
                    {
                        // and do it however many times we need
                        while (true)
                        {
                            if (IsValidIdentifierPart(GetChar(m_scannerState.CurrentPosition)))
                            {
                                // skip to the end of the identifier part
                                while (IsValidIdentifierPart(GetChar(++m_scannerState.CurrentPosition)))
                                {
                                    // loop
                                }

                                // when we get here, the current position is the first
                                // character that ISN"T an identifier-part. That means everything 
                                // UP TO this point must have been on the 
                                // same line, so we only need to update the position
                                m_scannerState.CurrentToken.EndPosition = m_scannerState.CurrentPosition;
                            }
                            else if (CheckSubstring(m_scannerState.CurrentPosition, "<%="))
                            {
                                // skip forward four characters -- the minimum position
                                // for the closing %>
                                m_scannerState.CurrentPosition += 4;

                                // and keep looping until we find it
                                while (!(this.GetChar(this.m_scannerState.CurrentPosition - 1) == '%' &&
                                         this.GetChar(this.m_scannerState.CurrentPosition) == '>') ||
                                         (m_scannerState.CurrentPosition >= m_endPos))
                                {
                                    this.m_scannerState.CurrentPosition++;
                                }

                                // update the end of the token
                                m_scannerState.CurrentToken.EndPosition = m_scannerState.CurrentPosition + 1;
                                m_scannerState.CurrentToken.EndLineNumber = m_scannerState.CurrentLine;
                                m_scannerState.CurrentToken.EndLinePosition = m_scannerState.StartLinePosition;

                                // we should be at the > of the %> right now.
                                // see if we found an unterminated asp.net block
                                if (m_scannerState.CurrentPosition >= m_endPos)
                                {
                                    HandleError(JSError.UnterminatedAspNetBlock);
                                }
                                else
                                {
                                    // skip the > and go around another time
                                    ++m_scannerState.CurrentPosition;
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
            int start = m_scannerState.CurrentPosition;
            m_scannerState.EscapedString = null;
            StringBuilder result = null;
            do
            {
                ch = GetChar(m_scannerState.CurrentPosition++);

                if (ch != '\\')
                {
                    // this is the common non escape case
                    if (IsLineTerminator(ch, 0))
                    {
                        HandleError(JSError.UnterminatedString);
                        --m_scannerState.CurrentPosition;
                        if (GetChar(m_scannerState.CurrentPosition - 1) == '\r')
                        {
                            --m_scannerState.CurrentPosition;
                        }

                        break;
                    }
                    
                    if ((char)0 == ch)
                    {
                        m_scannerState.CurrentPosition--;
                        HandleError(JSError.UnterminatedString);
                        break;
                    }

                    if (AllowEmbeddedAspNetBlocks
                        && ch == '<'
                        && GetChar(m_scannerState.CurrentPosition) == '%')
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
                    if (m_scannerState.CurrentPosition - start - 1 > 0)
                    {
                        // append all the non escape chars to the string builder
                        result.Append(m_strSourceCode, start, m_scannerState.CurrentPosition - start - 1);
                    }

                    // state variable to be reset
                    bool seqOfThree = false;
                    int esc = 0;

                    ch = GetChar(m_scannerState.CurrentPosition++);
                    switch (ch)
                    {
                        // line terminator crap
                        case '\r':
                            if ('\n' == GetChar(m_scannerState.CurrentPosition))
                            {
                                m_scannerState.CurrentPosition++;
                            }

                            goto case '\n';

                        case '\n':
                        case (char)0x2028:
                        case (char)0x2029:
                            m_scannerState.CurrentLine++;
                            m_scannerState.StartLinePosition = m_scannerState.CurrentPosition;
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
                            ch = GetChar(m_scannerState.CurrentPosition++);
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
                                    --m_scannerState.CurrentPosition; // do not skip over this char we have to read it back
                                }

                                break;
                            }

                            ch = GetChar(m_scannerState.CurrentPosition++);
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
                                    --m_scannerState.CurrentPosition; // do not skip over this char we have to read it back
                                }
                                break;
                            }

                            result.Append((char)esc);
                            break;

                        // unicode escape sequence /uHHHH
                        case 'u':
                            ch = GetChar(m_scannerState.CurrentPosition++);
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
                                    --m_scannerState.CurrentPosition; // do not skip over this char we have to read it back
                                }

                                break;
                            }

                            ch = GetChar(m_scannerState.CurrentPosition++);
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
                                    --m_scannerState.CurrentPosition; // do not skip over this char we have to read it back
                                }

                                break;
                            }

                            ch = GetChar(m_scannerState.CurrentPosition++);
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
                                    --m_scannerState.CurrentPosition; // do not skip over this char we have to read it back
                                }

                                break;
                            }

                            ch = GetChar(m_scannerState.CurrentPosition++);
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
                                    --m_scannerState.CurrentPosition; // do not skip over this char we have to read it back
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

                            ch = GetChar(m_scannerState.CurrentPosition++);
                            if (unchecked((UInt32)(ch - '0')) <= '7' - '0')
                            {
                                if (seqOfThree)
                                {
                                    esc |= (ch - '0') << 3;
                                    ch = GetChar(m_scannerState.CurrentPosition++);
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
                                            --m_scannerState.CurrentPosition; // do not skip over this char we have to read it back
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
                                    --m_scannerState.CurrentPosition; // do not skip over this char we have to read it back
                                }
                            }

                            break;

                        default:
                            // not an octal number, ignore the escape '/' and simply append the current char
                            result.Append(ch);
                            break;
                    }

                    start = m_scannerState.CurrentPosition;
                }
            } while (ch != cStringTerminator);

            // update this.escapedString
            if (null != result)
            {
                if (m_scannerState.CurrentPosition - start - 1 > 0)
                {
                    // append all the non escape chars to the string builder
                    result.Append(m_strSourceCode, start, m_scannerState.CurrentPosition - start - 1);
                }
                m_scannerState.EscapedString = result.ToString();
            }
            else
            {
                if (m_scannerState.CurrentPosition <= m_scannerState.CurrentToken.StartPosition + 2)
                {
                    m_scannerState.EscapedString = "";
                }
                else
                {
                    int numDelimiters = (GetChar(m_scannerState.CurrentPosition - 1) == cStringTerminator ? 2 : 1);
                    m_scannerState.EscapedString = m_strSourceCode.Substring(m_scannerState.CurrentToken.StartPosition + 1, m_scannerState.CurrentPosition - m_scannerState.CurrentToken.StartPosition - numDelimiters);
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
            ++m_scannerState.CurrentPosition;

            char ch;
            while ((ch = GetChar(m_scannerState.CurrentPosition++)) != '\0')
            {
                if (ch == '%'
                    && GetChar(m_scannerState.CurrentPosition) == '>')
                {
                    // found the closing delimiter -- the current position in on the >
                    // so we need to advance to the next character and break out of the loop
                    ++m_scannerState.CurrentPosition;
                    break;
                }
            }
        }

        private void SkipSingleLineComment()
        {
            while (!IsEndLineOrEOF(GetChar(m_scannerState.CurrentPosition++), 0)) ;
            m_scannerState.CurrentLine++;
            m_scannerState.StartLinePosition = m_scannerState.CurrentPosition;
            m_scannerState.InSingleLineComment = false;
        }

        private void SkipToEndOfLine()
        {
            var c = GetChar(m_scannerState.CurrentPosition);
            while (c != 0
                && c != '\n'
                && c != '\r'
                && c != '\x2028'
                && c != '\x2029')
            {
                c = GetChar(++m_scannerState.CurrentPosition);
            }
        }

        private void SkipOneLineTerminator()
        {
            var c = GetChar(m_scannerState.CurrentPosition);
            if (c == '\r')
            {
                // skip over the \r; and if it's followed by a \n, skip it, too
                if (GetChar(++m_scannerState.CurrentPosition) == '\n')
                {
                    ++m_scannerState.CurrentPosition;
                }

                m_scannerState.CurrentLine++;
                m_scannerState.StartLinePosition = m_scannerState.CurrentPosition;
            }
            else if (c == '\n'
                || c == '\x2028'
                || c == '\x2029')
            {
                // skip over the single line-feed character
                ++m_scannerState.CurrentPosition;

                m_scannerState.CurrentLine++;
                m_scannerState.StartLinePosition = m_scannerState.CurrentPosition;
            }
        }

        // this method is public because it's used from the authoring code
        public int SkipMultilineComment(bool importantComment)
        {
            for (; ; )
            {
                char c = GetChar(m_scannerState.CurrentPosition);
                while ('*' == c)
                {
                    c = GetChar(++m_scannerState.CurrentPosition);
                    if ('/' == c)
                    {
                        m_scannerState.CurrentPosition++;
                        m_scannerState.InMultipleLineComment = false;
                        if (importantComment)
                        {
                            SaveImportantComment();
                        }
                        return m_scannerState.CurrentPosition;
                    }

                    if ((char)0 == c)
                    {
                        break;
                    }
                    
                    if (IsLineTerminator(c, 1))
                    {
                        c = GetChar(++m_scannerState.CurrentPosition);
                        m_scannerState.CurrentLine++;
                        m_scannerState.StartLinePosition = m_scannerState.CurrentPosition + 1;
                    }
                }

                if ((char)0 == c && m_scannerState.CurrentPosition >= m_endPos)
                {
                    break;
                }

                if (IsLineTerminator(c, 1))
                {
                    m_scannerState.CurrentLine++;
                    m_scannerState.StartLinePosition = m_scannerState.CurrentPosition + 1;
                }

                ++m_scannerState.CurrentPosition;
            }

            // if we are here we got EOF
            if (importantComment)
            {
                SaveImportantComment();
            }

            m_scannerState.CurrentToken.EndPosition = m_scannerState.CurrentPosition;
            m_scannerState.CurrentToken.EndLinePosition = m_scannerState.StartLinePosition;
            m_scannerState.CurrentToken.EndLineNumber = m_scannerState.CurrentLine;
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
            Context commentContext = m_scannerState.CurrentToken.Clone();
            commentContext.EndPosition = m_scannerState.CurrentPosition;
            commentContext.EndLineNumber = m_scannerState.CurrentLine;
            commentContext.EndLinePosition = m_scannerState.StartLinePosition;

            m_importantComments.Add(commentContext);
        }

        private void SkipBlanks()
        {
            char c = GetChar(m_scannerState.CurrentPosition);
            while (JSScanner.IsBlankSpace(c))
            {
                c = GetChar(++m_scannerState.CurrentPosition);
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
                    if (0x0A == GetChar(m_scannerState.CurrentPosition + increment))
                    {
                        m_scannerState.CurrentPosition++;
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
            return IsLineTerminator(c, increment) || (char)0 == c && m_scannerState.CurrentPosition >= m_endPos;
        }

        private bool IsAtEndOfLine
        {
            get
            {
                return IsEndLineOrEOF(GetChar(m_scannerState.CurrentPosition), 0);
            }
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
                if ('u' == GetChar(m_scannerState.CurrentPosition + 1))
                {
                    char h1 = GetChar(m_scannerState.CurrentPosition + 2);
                    if (IsHexDigit(h1))
                    {
                        char h2 = GetChar(m_scannerState.CurrentPosition + 3);
                        if (IsHexDigit(h2))
                        {
                            char h3 = GetChar(m_scannerState.CurrentPosition + 4);
                            if (IsHexDigit(h3))
                            {
                                char h4 = GetChar(m_scannerState.CurrentPosition + 5);
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
                int startPosition = (m_scannerState.LastPosOnBuilder > 0) ? m_scannerState.LastPosOnBuilder : m_scannerState.CurrentToken.StartPosition;
                if (m_scannerState.CurrentPosition - startPosition > 0)
                {
                    m_identifier.Append(m_strSourceCode.Substring(startPosition, m_scannerState.CurrentPosition - startPosition));
                }

                m_identifier.Append(c);
                m_scannerState.CurrentPosition += 5;
                m_scannerState.LastPosOnBuilder = m_scannerState.CurrentPosition + 1;
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
            var startPos = m_scannerState.CurrentPosition;

            // see if the first character is a valid identifier start
            if (JSScanner.IsValidIdentifierStart(GetChar(startPos)))
            {
                // it is -- skip to the next character
                ++m_scannerState.CurrentPosition;

                // and keep going as long as we have valid part characters
                while (JSScanner.IsValidIdentifierPart(GetChar(m_scannerState.CurrentPosition)))
                {
                    ++m_scannerState.CurrentPosition;
                }
            }

            // if we advanced at all, return the code we scanned. Otherwise return null
            if (m_scannerState.CurrentPosition > startPos)
            {
                identifier = m_strSourceCode.Substring(startPos, m_scannerState.CurrentPosition - startPos);
                if (forceUpper)
                {
                    identifier = identifier.ToUpperInvariant();
                }
            }

            return identifier;
        }

        private bool PPScanInteger(out int intValue)
        {
            var startPos = m_scannerState.CurrentPosition;
            while (IsDigit(GetChar(m_scannerState.CurrentPosition)))
            {
                ++m_scannerState.CurrentPosition;
            }

            var success = false;
            if (m_scannerState.CurrentPosition > startPos)
            {
                success = int.TryParse(m_strSourceCode.Substring(startPos, m_scannerState.CurrentPosition - startPos), out intValue);
            }
            else
            {
                intValue = 0;
            }

            return success;
        }

        private int PPSkipToDirective(params string[] endStrings)
        {
            // save the current position - if we hit an EOF before we find a directive
            // we're looking for, we'll use this as the end of the error context so we
            // don't have the whole rest of the file printed out with the error.
            var endPosition = m_scannerState.CurrentPosition;
            var endLineNum = m_scannerState.CurrentLine;
            var endLinePos = m_scannerState.StartLinePosition;

            while (true)
            {
                char c = GetChar(m_scannerState.CurrentPosition++);
                switch (c)
                {
                    // EOF
                    case (char)0:
                        if (m_scannerState.CurrentPosition >= m_endPos)
                        {
                            // adjust the scanner state
                            m_scannerState.CurrentPosition--;
                            m_scannerState.CurrentToken.EndPosition = m_scannerState.CurrentPosition;
                            m_scannerState.CurrentToken.EndLineNumber = m_scannerState.CurrentLine;
                            m_scannerState.CurrentToken.EndLinePosition = m_scannerState.StartLinePosition;

                            // create a clone of the current token and set the ending to be the end of the
                            // directive for which we're trying to find an end. Use THAT context for the 
                            // error context. Then throw an exception so we can bail.
                            var contextError = m_scannerState.CurrentToken.Clone();
                            contextError.EndPosition = endPosition;
                            contextError.EndLineNumber = endLineNum;
                            contextError.EndLinePosition = endLinePos;
                            contextError.HandleError(string.CompareOrdinal(endStrings[0], "#ENDDEBUG") == 0 
                                ? JSError.NoEndDebugDirective 
                                : JSError.NoEndIfDirective);
                            throw new ScannerException(JSError.ErrorEndOfFile);
                        }

                        break;

                    // line terminator crap
                    case '\r':
                        if (GetChar(m_scannerState.CurrentPosition) == '\n')
                        {
                            m_scannerState.CurrentPosition++;
                        }

                        m_scannerState.CurrentLine++;
                        m_scannerState.StartLinePosition = m_scannerState.CurrentPosition;
                        break;
                    case '\n':
                        m_scannerState.CurrentLine++;
                        m_scannerState.StartLinePosition = m_scannerState.CurrentPosition;
                        break;
                    case (char)0x2028:
                        m_scannerState.CurrentLine++;
                        m_scannerState.StartLinePosition = m_scannerState.CurrentPosition;
                        break;
                    case (char)0x2029:
                        m_scannerState.CurrentLine++;
                        m_scannerState.StartLinePosition = m_scannerState.CurrentPosition;
                        break;

                    // check for /// (and then followed by any one of the substrings passed to us)
                    case '/':
                        if (CheckSubstring(m_scannerState.CurrentPosition, "//"))
                        {
                            // skip it
                            m_scannerState.CurrentPosition += 2;

                            // check to see if this is the start of ANOTHER preprocessor construct. If it
                            // is, then it's a NESTED statement and we'll need to recursively skip the 
                            // whole thing so everything stays on track
                            if (CheckCaseInsensitiveSubstring("#IFDEF")
                                || CheckCaseInsensitiveSubstring("#IFNDEF")
                                || CheckCaseInsensitiveSubstring("#IF"))
                            {
                                PPSkipToDirective("#ENDIF");
                            }
                            else
                            {
                                // now check each of the ending strings that were passed to us to see if one of
                                // them is a match
                                for (var ndx = 0; ndx < endStrings.Length; ++ndx)
                                {
                                    if (CheckCaseInsensitiveSubstring(endStrings[ndx]))
                                    {
                                        // found the ending string
                                        return ndx;
                                    }
                                }

                                // not something we're looking for -- but is it a simple ///#END?
                                if (CheckCaseInsensitiveSubstring("#END"))
                                {
                                    // it is! Well, we were expecting either #ENDIF or #ENDDEBUG, but we found just an #END.
                                    // that's not how the syntax is SUPPOSED to go. But let's let it fly.
                                    // the ending token is always the first one.
                                    return 0;
                                }
                            }
                        }

                        break;
                }
            }
        }

        private bool ScanPreprocessingDirective()
        {
            // check for some AjaxMin preprocessor comments
            if (CheckCaseInsensitiveSubstring("#DEBUG"))
            {
                return ScanDebugDirective();
            }
            else if (CheckCaseInsensitiveSubstring("#GLOBALS"))
            {
                return ScanGlobalsDirective();
            }
            else if (CheckCaseInsensitiveSubstring("#SOURCE"))
            {
                return ScanSourceDirective();
            }
            else if (UsePreprocessorDefines)
            {
                if (CheckCaseInsensitiveSubstring("#IF"))
                {
                    return ScanIfDirective();
                }
                else if (CheckCaseInsensitiveSubstring("#ELSE") && m_scannerState.IfDirectiveLevel > 0)
                {
                    return ScanElseDirective();
                }
                else if (CheckCaseInsensitiveSubstring("#ENDIF") && m_scannerState.IfDirectiveLevel > 0)
                {
                    return ScanEndIfDirective();
                }
                else if (CheckCaseInsensitiveSubstring("#DEFINE"))
                {
                    return ScanDefineDirective();
                }
                else if (CheckCaseInsensitiveSubstring("#UNDEF"))
                {
                    return ScanUndefineDirective();
                }
            }

            return false;
        }

        private bool ScanGlobalsDirective()
        {
            // found ///#GLOBALS comment
            SkipBlanks();

            // should be one or more space-separated identifiers
            while (!IsLineTerminator(GetChar(m_scannerState.CurrentPosition), 1))
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
                    return true;
                }
            }

            return false;
        }

        private bool ScanSourceDirective()
        {
            // found ///#SOURCE comment
            SkipBlanks();

            // pull the line, the column, and the source path off the line
            var linePos = 0;
            var colPos = 0;

            // line number is first
            if (!PPScanInteger(out linePos))
            {
                // not an integer -- skip the rest of the line and move on
                SkipSingleLineComment();
                return true;
            }

            SkipBlanks();

            // column number is second
            if (!PPScanInteger(out colPos))
            {
                // not an integer -- skip the rest of the line and move on
                SkipSingleLineComment();
                return true;
            }

            SkipBlanks();

            // the path should be the last part of the line.
            // skip to the end and then use the part between.
            var ndxStart = m_scannerState.CurrentPosition;
            SkipToEndOfLine();
            if (m_scannerState.CurrentPosition > ndxStart)
            {
                // there is a non-blank source token.
                // so we have the line and the column and the source.
                // use them. Remember, though: we stopped BEFORE the line terminator,
                // so read ONE line terminator for the end of this line.
                SkipOneLineTerminator();

                // change the file context
                m_scannerState.CurrentToken.ChangeFileContext(m_strSourceCode.Substring(ndxStart, m_scannerState.CurrentPosition - ndxStart).TrimEnd());

                // adjust the line number
                this.m_scannerState.CurrentLine = linePos;

                // the start line position is the current position less the column position.
                // and because the column position in the comment is one-based, add one to get 
                // back to zero-based: current - (col - 1)
                this.m_scannerState.StartLinePosition = m_scannerState.CurrentPosition - colPos + 1;
            }

            return true;
        }

        private bool ScanIfDirective()
        {
            // we know we start with #IF -- see if it's #IFDEF or #IFNDEF
            var isIfDef = CheckCaseInsensitiveSubstring("DEF");
            var isIfNotDef = !isIfDef && CheckCaseInsensitiveSubstring("NDEF");

            // skip past the token and any blanks
            SkipBlanks();

            // if we encountered a line-break here, then ignore this directive
            if (!IsAtEndOfLine)
            {
                // get an identifier from the input
                var identifier = PPScanIdentifier(true);
                if (!string.IsNullOrEmpty(identifier))
                {
                    // set a state so that if we hit an #ELSE directive, we skip to #ENDIF
                    ++m_scannerState.IfDirectiveLevel;

                    // if there is a dictionary AND the identifier is in it, then the identifier IS defined.
                    // if there is not dictionary OR the identifier is NOT in it, then it is NOT defined.
                    var isDefined = (m_defines != null && m_defines.ContainsKey(identifier));

                    // skip any blanks
                    SkipBlanks();

                    // if we are at the end of the line, or if this was an #IFDEF or #IFNDEF, then
                    // we have enough information to act
                    if (isIfDef || isIfNotDef || IsAtEndOfLine)
                    {
                        // either #IFDEF identifier, #IFNDEF identifier, or #IF identifier.
                        // this will simply test for whether or not it's defined
                        var conditionIsTrue = (!isIfNotDef && isDefined) || (isIfNotDef && !isDefined);

                        // if the condition is true, we just keep processing and when we hit the #END we're done,
                        // or if we hit an #ELSE we skip to the #END. But if we are not true, we need to skip to
                        // the #ELSE or #END directly.
                        if (!conditionIsTrue)
                        {
                            // the condition is FALSE!
                            // skip to #ELSE or #ENDIF and continue processing normally.
                            // (make sure the end if always the first one)
                            if (PPSkipToDirective("#ENDIF", "#ELSE") == 0)
                            {
                                // encountered the #ENDIF directive, so we know to reset the flag
                                --m_scannerState.IfDirectiveLevel;
                            }
                        }
                    }
                    else
                    {
                        // this is an #IF and we have something after the identifier.
                        // it better be an operator or we'll ignore this comment.
                        var operation = CheckForOperator(PPOperators.Instance);
                        if (operation != null)
                        {
                            // skip any whitespace
                            SkipBlanks();

                            // save the current index -- this is either a non-whitespace character or the EOL.
                            // if it wasn't the EOL, skip to it now
                            var ndxStart = m_scannerState.CurrentPosition;
                            if (!IsAtEndOfLine)
                            {
                                SkipToEndOfLine();
                            }

                            // the value to compare against is the substring between the start and the current.
                            // (and could be empty)
                            var compareTo = m_strSourceCode.Substring(ndxStart, m_scannerState.CurrentPosition - ndxStart);

                            // now do the comparison and see if it's true. If the identifier isn't even defined, then
                            // the condition is false.
                            var conditionIsTrue = isDefined && operation(m_defines[identifier], compareTo.TrimEnd());

                            // if the condition is true, we just keep processing and when we hit the #END we're done,
                            // or if we hit an #ELSE we skip to the #END. But if we are not true, we need to skip to
                            // the #ELSE or #END directly.
                            if (!conditionIsTrue)
                            {
                                // the condition is FALSE!
                                // skip to #ELSE or #ENDIF and continue processing normally.
                                // (make sure the end if always the first one)
                                if (PPSkipToDirective("#ENDIF", "#ELSE") == 0)
                                {
                                    // encountered the #ENDIF directive, so we know to reset the flag
                                    --m_scannerState.IfDirectiveLevel;
                                }
                            }
                        }
                    }

                    if (RawTokens)
                    {
                        // if we are asking for raw tokens, we DON'T want to return these comments or the code
                        // they may have stripped away.
                        SkipSingleLineComment();
                        return true;
                    }
                }
            }

            return false;
        }

        private Func<string,string,bool> CheckForOperator(SortedDictionary<string, Func<string,string,bool>> operators)
        {
            // we need to make SURE we are checking the longer strings before we check the
            // shorter strings, because if the source is === and we check for ==, we'll pop positive
            // for it and miss that last =. 
            foreach (var entry in operators)
            {
                if (CheckCaseInsensitiveSubstring(entry.Key))
                {
                    // found it! return the comparison function for this text
                    return entry.Value;
                }
            }

            // if we got here, we didn't find anything we were looking for
            return null;
        }

        private bool ScanElseDirective()
        {
            // reset the state that says we were in an #IFDEF construct
            --m_scannerState.IfDirectiveLevel;

            // ...then we now want to skip until the #ENDIF directive
            PPSkipToDirective("#ENDIF");

            // if we are asking for raw tokens, we DON'T want to return these comments or the code
            // they stripped away.
            if (RawTokens)
            {
                SkipSingleLineComment();
                return true;
            }

            return false;
        }

        private bool ScanEndIfDirective()
        {
            // reset the state that says we were in an #IFDEF construct
            --m_scannerState.IfDirectiveLevel;

            // if we are asking for raw tokens, we DON'T want to return this comment.
            if (RawTokens)
            {
                SkipSingleLineComment();
                return true;
            }

            return false;
        }

        private bool ScanDefineDirective()
        {
            // skip past the token and any blanks
            SkipBlanks();

            // if we encountered a line-break here, then ignore this directive
            if (!m_scannerState.GotEndOfLine)
            {
                // get an identifier from the input
                var identifier = PPScanIdentifier(true);
                if (!string.IsNullOrEmpty(identifier))
                {
                    // see if we're assigning a value
                    string value = string.Empty;
                    SkipBlanks();
                    if (GetChar(m_scannerState.CurrentPosition) == '=')
                    {
                        // we are! get the rest of the line as the trimmed string
                        var ndxStart = ++m_scannerState.CurrentPosition;
                        SkipToEndOfLine();
                        value = m_strSourceCode.Substring(ndxStart, m_scannerState.CurrentPosition - ndxStart).Trim();
                    }

                    // if there is no dictionary of defines yet, create one now
                    if (m_defines == null)
                    {
                        m_defines = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    }

                    // if the identifier is not already in the dictionary, add it now
                    if (!m_defines.ContainsKey(identifier))
                    {
                        m_defines.Add(identifier, value);
                    }
                    else
                    {
                        // it already exists -- just set the value
                        m_defines[identifier] = value;
                    }

                    // if we are asking for raw tokens, we DON'T want to return this comment.
                    if (RawTokens)
                    {
                        SkipSingleLineComment();
                        return true;
                    }
                }
            }

            return false;
        }

        private bool ScanUndefineDirective()
        {
            // skip past the token and any blanks
            SkipBlanks();

            // if we encountered a line-break here, then ignore this directive
            if (!m_scannerState.GotEndOfLine)
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
                        return true;
                    }
                }
            }

            return false;
        }

        private bool ScanDebugDirective()
        {
            // advance to the next character. If it's an equal sign, then this
            // debug comment is setting a debug namespace, not marking debug code.
            if (GetChar(m_scannerState.CurrentPosition) == '=')
            {
                // we have ///#DEBUG=
                // get the namespace after the equal sign
                ++m_scannerState.CurrentPosition;
                var identifier = PPScanIdentifier(false);
                if (identifier == null)
                {
                    // nothing. clear the debug namespaces
                    DebugLookupCollection.Clear();
                }
                else
                {
                    // this first identifier is the root namespace for the debug object.
                    // let's also treat it as a known global.
                    OnGlobalDefine(identifier);

                    // see if we have a period and keep looping to get IDENT(.IDENT)*
                    while (GetChar(m_scannerState.CurrentPosition) == '.')
                    {
                        ++m_scannerState.CurrentPosition;
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
                        DebugLookupCollection.Add(identifier);
                    }
                }

                // make sure we skip the rest of the line (if any)
                // and loop back up for a new token
                SkipSingleLineComment();
                return true;
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
                    return true;
                }
            }

            return false;
        }

        private void HandleError(JSError error)
        {
            m_scannerState.CurrentToken.EndPosition = m_scannerState.CurrentPosition;
            m_scannerState.CurrentToken.EndLinePosition = m_scannerState.StartLinePosition;
            m_scannerState.CurrentToken.EndLineNumber = m_scannerState.CurrentLine;
            m_scannerState.CurrentToken.HandleError(error);
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

        /// <summary>
        /// Class for associating an operator with a function for ///#IF directives
        /// in a lazy-loaded manner. Doesn't create and initialize the dictionary
        /// until the scanner actually encounters syntax that needs it.
        /// The keys are sorted by length, decreasing (longest operators first).
        /// </summary>
        private sealed class PPOperators : SortedDictionary<string, Func<string, string, bool>>
        {
            private PPOperators()
                : base(new LengthComparer())
            {
                // add the operator information
                this.Add("==", PPIsEqual);
                this.Add("!=", PPIsNotEqual);
                this.Add("===", PPIsStrictEqual);
                this.Add("!==", PPIsNotStrictEqual);
                this.Add("<", PPIsLessThan);
                this.Add(">", PPIsGreaterThan);
                this.Add("<=", PPIsLessThanOrEqual);
                this.Add(">=", PPIsGreaterThanOrEqual);
            }

            #region thread-safe lazy-loading property and nested class

            public static PPOperators Instance
            {
                get
                {
                    return Nested.Instance;
                }
            }

            private static class Nested
            {
                internal static readonly PPOperators Instance = new PPOperators();
            }

            #endregion

            #region sorting class

            /// <summary>
            /// Sorting class for the sorted dictionary base to make sure the operators are
            /// enumerated with the LONGEST strings first, before the shorter strings.
            /// </summary>
            private class LengthComparer : Comparer<string>
            {
                public override int Compare(string x, string y)
                {
                    var delta = x != null && y != null ? y.Length - x.Length : 0;
                    return delta != 0 ? delta : string.CompareOrdinal(x, y);
                }
            }

            #endregion

            #region condition execution methods

            private static bool PPIsStrictEqual(string left, string right)
            {
                // strict comparison is only a string compare -- no conversion to float if
                // the string comparison fails.
                return string.Compare(left, right, StringComparison.OrdinalIgnoreCase) == 0;
            }

            private static bool PPIsNotStrictEqual(string left, string right)
            {
                // strict comparison is only a string compare -- no conversion to float if
                // the string comparison fails.
                return string.Compare(left, right, StringComparison.OrdinalIgnoreCase) != 0;
            }

            private static bool PPIsEqual(string left, string right)
            {
                // first see if a string compare works
                var isTrue = string.Compare(left, right, StringComparison.OrdinalIgnoreCase) == 0;

                // if not, then try converting both sides to double and doing the comparison
                if (!isTrue)
                {
                    double leftNumeric, rightNumeric;
                    if (ConvertToNumeric(left, right, out leftNumeric, out rightNumeric))
                    {
                        // they both converted successfully
                        isTrue = leftNumeric == rightNumeric;
                    }
                }

                return isTrue;
            }

            private static bool PPIsNotEqual(string left, string right)
            {
                // first see if a string compare works
                var isTrue = string.Compare(left, right, StringComparison.OrdinalIgnoreCase) != 0;

                // if they AREN'T equal, then try converting both sides to double and doing the comparison
                if (isTrue)
                {
                    double leftNumeric, rightNumeric;
                    if (ConvertToNumeric(left, right, out leftNumeric, out rightNumeric))
                    {
                        // they both converted successfully
                        isTrue = leftNumeric != rightNumeric;
                    }
                }

                return isTrue;
            }

            private static bool PPIsLessThan(string left, string right)
            {
                // only numeric comparisons
                bool isTrue = false;
                double leftNumeric, rightNumeric;
                if (ConvertToNumeric(left, right, out leftNumeric, out rightNumeric))
                {
                    // they both converted successfully
                    isTrue = leftNumeric < rightNumeric;
                }

                return isTrue;
            }

            private static bool PPIsGreaterThan(string left, string right)
            {
                // only numeric comparisons
                bool isTrue = false;
                double leftNumeric, rightNumeric;
                if (ConvertToNumeric(left, right, out leftNumeric, out rightNumeric))
                {
                    // they both converted successfully
                    isTrue = leftNumeric > rightNumeric;
                }

                return isTrue;
            }

            private static bool PPIsLessThanOrEqual(string left, string right)
            {
                // only numeric comparisons
                bool isTrue = false;
                double leftNumeric, rightNumeric;
                if (ConvertToNumeric(left, right, out leftNumeric, out rightNumeric))
                {
                    // they both converted successfully
                    isTrue = leftNumeric <= rightNumeric;
                }

                return isTrue;
            }

            private static bool PPIsGreaterThanOrEqual(string left, string right)
            {
                // only numeric comparisons
                bool isTrue = false;
                double leftNumeric, rightNumeric;
                if (ConvertToNumeric(left, right, out leftNumeric, out rightNumeric))
                {
                    // they both converted successfully
                    isTrue = leftNumeric >= rightNumeric;
                }

                return isTrue;
            }

            #endregion

            #region static helper methods

            /// <summary>
            /// Try converting the two strings to doubles
            /// </summary>
            /// <param name="left">first string</param>
            /// <param name="right">second string</param>
            /// <param name="leftNumeric">first string converted to double</param>
            /// <param name="rightNumeric">second string converted to double</param>
            /// <returns>true if the conversion was successful; false otherwise</returns>
            private static bool ConvertToNumeric(string left, string right, out double leftNumeric, out double rightNumeric)
            {
                rightNumeric = default(double);
                return double.TryParse(left, NumberStyles.Any, CultureInfo.InvariantCulture, out leftNumeric)
                    && double.TryParse(right, NumberStyles.Any, CultureInfo.InvariantCulture, out rightNumeric);
            }

            #endregion
        }
    }

    public sealed class JSScannerState
    {
        public int StartLinePosition { get; set; }
        public int CurrentPosition { get; set; }
        public int CurrentLine { get; set; }
        public int LastPosOnBuilder { get; set; }
        public int IfDirectiveLevel { get; set; }
        public int ConditionalCompilationIfLevel { get; set; }
        public bool GotEndOfLine { get; set; }
        public bool ConditionalCompilationOn { get; set; }
        public bool InConditionalComment { get; set; }
        public bool InSingleLineComment { get; set; }
        public bool InMultipleLineComment { get; set; }
        public string EscapedString { get; set; }
        public Context CurrentToken { get; set; }
        public JSToken PreviousToken { get; set; }

        public JSScannerState Clone()
        {
            return new JSScannerState()
            {
                CurrentPosition = this.CurrentPosition,
                CurrentLine = this.CurrentLine,
                StartLinePosition = this.StartLinePosition,
                GotEndOfLine = this.GotEndOfLine,
                IfDirectiveLevel = this.IfDirectiveLevel,
                ConditionalCompilationOn = this.ConditionalCompilationOn,
                ConditionalCompilationIfLevel = this.ConditionalCompilationIfLevel,
                InConditionalComment = this.InConditionalComment,
                InSingleLineComment = this.InSingleLineComment,
                InMultipleLineComment = this.InMultipleLineComment,
                LastPosOnBuilder = this.LastPosOnBuilder,
                EscapedString = this.EscapedString,
                PreviousToken = this.PreviousToken,
                CurrentToken = this.CurrentToken.Clone(),
            };
        }
    }

    public class GlobalDefineEventArgs : EventArgs
    {
        public string Name { get; set; }
    }
}
