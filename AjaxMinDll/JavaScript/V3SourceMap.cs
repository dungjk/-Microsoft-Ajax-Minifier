// V3SourceMap.cs
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
using System.IO;
using System.Text;

namespace Microsoft.Ajax.Utilities
{
    /// <summary>
    /// Standard JSON source map format, version 3
    /// </summary>
    public sealed class V3SourceMap : ISourceMap, IVisitor
    {
        #region private fields 

        private string m_minifiedPath;

        private TextWriter m_writer;

        private int m_maxMinifiedLine;

        /// <summary>whether we have output a property yet</summary>
        private bool m_hasProperty;

        private HashSet<string> m_sourceFiles;

        private HashSet<string> m_names;

        private List<Segment> m_segments;

        private int m_lastDestinationColumn;

        private int m_lastSourceLine;

        private int m_lastSourceColumn;

        private int m_lastFileIndex;

        private int m_lastNameIndex;

        private static string s_base64 = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/";

        #endregion

        public V3SourceMap(TextWriter writer)
        {
            m_writer = writer;

            // source files are not case sensitive?
            m_sourceFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // names are definitely case-sensitive (JavaScript identifiers)
            m_names = new HashSet<string>();

            // segments is a list
            m_segments = new List<Segment>();

            // set all the "last" values to -1 to indicate that
            // we don't have a value from which to generate an offset.
            m_lastDestinationColumn = -1;
            m_lastSourceLine = -1;
            m_lastSourceColumn = -1;
            m_lastFileIndex = -1;
            m_lastNameIndex = -1;
        }

        #region ISourceMap implementation

        /// <summary>
        /// Called when we start a new minified output file
        /// </summary>
        /// <param name="sourcePath">output file path</param>
        public void StartPackage(string sourcePath)
        {
            m_minifiedPath = sourcePath;
        }

        /// <summary>
        /// Called when we end a minified output file. write all the accumulated 
        /// data to the stream.
        /// </summary>
        public void EndPackage()
        {
            // start the JSON object
            m_writer.WriteLine("{");

            WriteProperty("version", 3);
            WriteProperty("file", m_minifiedPath);

            // line number comes in zero-based, so add one to get the line count
            WriteProperty("lineCount", m_maxMinifiedLine + 1);

            // generate the lists for the names and the source files from the
            // hashsets we built up while traversing the tree
            var fileList = new List<string>(m_sourceFiles);
            var nameList = new List<string>(m_names);

            WriteProperty("mappings", GenerateMappings(fileList, nameList));

            WriteProperty("sources", fileList);
            WriteProperty("names", nameList);

            // close the JSON object
            m_writer.WriteLine();
            m_writer.WriteLine("}");
        }

        public object StartSymbol(AstNode astNode, int startLine, int startColumn)
        {
            if (astNode != null)
            {
                // if this is a newline, the startline will be bigger than the largest line we've had so far
                m_maxMinifiedLine = Math.Max(m_maxMinifiedLine, startLine);

                // save the file context in our list of files
                if (astNode.Context != null && astNode.Context.Document != null
                    && astNode.Context.Document.FileContext != null)
                {
                    m_sourceFiles.Add(astNode.Context.Document.FileContext);
                }

                // perform any node-specific operation. We implement IVisitor, so
                // we can pass ourselves to the Accept method of the node to get the
                // proper typed Visit function called.
                astNode.Accept(this);

                // TODO: create a list node for this item that will translate to a segment
                // in the mapping portion of our
            }

            return astNode;
        }

        public void MarkSegment(AstNode node, int startLine, int startColumn, string name, Context context)
        {
            if (startLine == int.MaxValue)
            {
                throw new ArgumentOutOfRangeException("startLine");
            }

            // create the segment object and add it to the list.
            // the destination line/col numbers are zero-based. The format expects line to be 1-based and col 0-based.
            // the context line is one-based; col is zero-based. The format expected line to be 1-based and col to be 0-based.
            var segment = CreateSegment(
                startLine + 1, 
                startColumn, 
                context == null || context.StartLineNumber < 1 ? -1 : context.StartLineNumber,
                context == null || context.StartColumn < 0 ? -1 : context.StartColumn, 
                context.IfNotNull(c => c.Document.FileContext), 
                name);

            m_segments.Add(segment);
        }

        public void EndSymbol(object symbol, int endLine, int endColumn, string parentContext)
        {
            //var astNode = symbol as AstNode;
            m_maxMinifiedLine = Math.Max(m_maxMinifiedLine, endLine);
        }

        public string Name
        {
            get { return "V3"; }
        }

        public void Dispose()
        {
            if (m_writer != null)
            {
                m_writer.Close();
                m_writer = null;
            }
        }

        #endregion

        #region GenerateMappings method

        private string GenerateMappings(IList<string> fileList, IList<string> nameList)
        {
            var sb = new StringBuilder();
            var currentLine = 1;
            foreach (var segment in m_segments)
            {
                if (currentLine < segment.DestinationLine)
                {
                    // we've jumped forward at least one line in the minified file.
                    // add a semicolon for each line we've advanced
                    do
                    {
                        sb.Append(';');
                    }
                    while (++currentLine < segment.DestinationLine);
                }
                else if (sb.Length > 0)
                {
                    // same line; separate segments by comma. But only
                    // if we've already output something
                    sb.Append(',');
                }

                EncodeNumbers(sb, segment, fileList, nameList);
            }

            return sb.ToString();
        }

        private void EncodeNumbers(StringBuilder sb, Segment segment, IList<string> files, IList<string> names)
        {
            // there should always be a destination column
            EncodeNumber(sb, segment.DestinationColumn);

            // if there's a source file...
            if (!string.IsNullOrEmpty(segment.FileName))
            {
                // get the index from the list and encode it into the builder
                // relative to the last file index.
                var thisIndex = files.IndexOf(segment.FileName);
                EncodeNumber(sb, m_lastFileIndex < 0 ? thisIndex : thisIndex - m_lastFileIndex);
                m_lastFileIndex = thisIndex;

                // add the source line and column
                EncodeNumber(sb, segment.SourceLine);
                EncodeNumber(sb, segment.SourceColumn);

                // if there's a symbol name, get its index and encode it into the builder
                // relative to the last name index.
                if (!string.IsNullOrEmpty(segment.SymbolName))
                {
                    thisIndex = names.IndexOf(segment.SymbolName);
                    EncodeNumber(sb, m_lastNameIndex < 0 ? thisIndex : thisIndex - m_lastNameIndex);
                    m_lastNameIndex = thisIndex;
                }
            }
        }

        private static void EncodeNumber(StringBuilder sb, int value)
        {
            // first get the signed vlq value. it uses bit0 as the sign.
            // if the value is negative, shift the positive version over left one and OR a 1.
            // if the value is zero or positive, just shift it over one (bit0 will be zero).
            value = value < 0 ? (-value << 1) | 1 : (value << 1);

            do
            {
                // pull off the last 5 bits of the value. Because value is guaranteed to be
                // positive at this point, we don't have to worry about the int's sign bit
                // filling in the places as we shift right.
                var digit = value & 0x1f;
                value >>= 5;

                // if there is still something left, then we need to set the
                // continuation bit (bit6) to a 1
                if (value > 0)
                {
                    digit |= 0x20;
                }

                // this leaves us with a 6-bit value (between 0 and 63)
                // which we then BASE64 encode and add to the string builder.
                // and if there's anything left, loop around again.
                sb.Append(s_base64[digit]);
            }
            while (value > 0);
        }

        private Segment CreateSegment(int destinationLine, int destinationColumn, int sourceLine, int sourceColumn, string fileName, string symbolName)
        {
            // create the segment with relative offsets for the destination column, source line, and source column.
            // destination line should be absolute.
            var segment = new Segment()
            {
                DestinationLine = destinationLine,
                DestinationColumn = m_lastDestinationColumn < 0 ? destinationColumn : destinationColumn - m_lastDestinationColumn,
                SourceLine = fileName == null ? -1 : m_lastSourceLine < 0 ? sourceLine : sourceLine - m_lastSourceLine,
                SourceColumn = fileName == null ? -1 : m_lastSourceColumn < 0 ? sourceColumn : sourceColumn - m_lastSourceColumn,
                FileName = fileName,
                SymbolName = symbolName
            };

            // set the new "last" values
            m_lastDestinationColumn = destinationColumn;

            // if there was a source location, set the last source line/col
            if (!string.IsNullOrEmpty(fileName))
            {
                m_lastSourceLine = sourceLine;
                m_lastSourceColumn = sourceColumn;
            }

            return segment;
        }

        #endregion

        #region private helper methods

        private void WriteProperty(string name, int number)
        {
            WritePropertyStart(name);
            m_writer.Write(number.ToStringInvariant());
        }

        private void WriteProperty(string name, string text)
        {
            WritePropertyStart(name);
            OutputEscapedString(text);
        }

        private void WriteProperty(string name, ICollection<string> collection)
        {
            WritePropertyStart(name);
            m_writer.Write('[');

            var first = true;
            foreach (var item in collection)
            {
                if (first)
                {
                    first = false;
                }
                else
                {
                    m_writer.Write(',');
                }

                OutputEscapedString(item);
            }

            m_writer.Write(']');
        }

        private void WritePropertyStart(string name)
        {
            if (m_hasProperty)
            {
                m_writer.WriteLine(',');
            }

            OutputEscapedString(name);
            m_writer.Write(':');
            m_hasProperty = true;
        }

        private void OutputEscapedString(string text)
        {
            m_writer.Write('"');
            for (var ndx = 0; ndx < text.Length; ++ndx)
            {
                var ch = text[ndx];
                switch (ch)
                {
                    case '\"':
                        m_writer.Write("\\\"");
                        break;

                    case '\b':
                        m_writer.Write("\\b");
                        break;

                    case '\f':
                        m_writer.Write("\\f");
                        break;

                    case '\n':
                        m_writer.Write("\\n");
                        break;

                    case '\r':
                        m_writer.Write("\\r");
                        break;

                    case '\t':
                        m_writer.Write("\\t");
                        break;

                    default:
                        if (ch < ' ')
                        {
                            // other control characters must be escaped as \uXXXX
                            m_writer.Write("\\u{0:x4}", (int)ch);
                        }
                        else
                        {
                            // just append it. The output encoding will take care of the rest
                            m_writer.Write(ch);
                        }
                        break;
                }
            }

            m_writer.Write('"');
        }

        #endregion

        #region IVisitor methods 

        public void Visit(ArrayLiteral node)
        {
        }

        public void Visit(AspNetBlockNode node)
        {
        }

        public void Visit(AstNodeList node)
        {
        }

        public void Visit(BinaryOperator node)
        {
        }

        public void Visit(Block node)
        {
        }

        public void Visit(Break node)
        {
        }

        public void Visit(CallNode node)
        {
        }

        public void Visit(ConditionalCompilationComment node)
        {
        }

        public void Visit(ConditionalCompilationElse node)
        {
        }

        public void Visit(ConditionalCompilationElseIf node)
        {
        }

        public void Visit(ConditionalCompilationEnd node)
        {
        }

        public void Visit(ConditionalCompilationIf node)
        {
        }

        public void Visit(ConditionalCompilationOn node)
        {
        }

        public void Visit(ConditionalCompilationSet node)
        {
        }

        public void Visit(Conditional node)
        {
        }

        public void Visit(ConstantWrapper node)
        {
        }

        public void Visit(ConstantWrapperPP node)
        {
        }

        public void Visit(ConstStatement node)
        {
        }

        public void Visit(ContinueNode node)
        {
        }

        public void Visit(CustomNode node)
        {
        }

        public void Visit(DebuggerNode node)
        {
        }

        public void Visit(DirectivePrologue node)
        {
        }

        public void Visit(DoWhile node)
        {
        }

        public void Visit(ForIn node)
        {
        }

        public void Visit(ForNode node)
        {
        }

        public void Visit(FunctionObject node)
        {
            if (node != null)
            {
                m_names.Add(node.Name);
            }
        }

        public void Visit(GetterSetter node)
        {
        }

        public void Visit(IfNode node)
        {
        }

        public void Visit(ImportantComment node)
        {
        }

        public void Visit(LabeledStatement node)
        {
        }

        public void Visit(LexicalDeclaration node)
        {
        }

        public void Visit(Lookup node)
        {
            if (node != null)
            {
                // add the lookup to the names list
                m_names.Add(node.Name);
            }
        }

        public void Visit(Member node)
        {
            if (node != null)
            {
                // add the name to the names list
                m_names.Add(node.Name);
            }
        }

        public void Visit(ObjectLiteral node)
        {
        }

        public void Visit(ObjectLiteralField node)
        {
        }

        public void Visit(ObjectLiteralProperty node)
        {
        }

        public void Visit(ParameterDeclaration node)
        {
            if (node != null)
            {
                m_names.Add(node.Name);
            }
        }

        public void Visit(RegExpLiteral node)
        {
        }

        public void Visit(ReturnNode node)
        {
        }

        public void Visit(Switch node)
        {
        }

        public void Visit(SwitchCase node)
        {
        }

        public void Visit(ThisLiteral node)
        {
        }

        public void Visit(ThrowNode node)
        {
        }

        public void Visit(TryNode node)
        {
        }

        public void Visit(Var node)
        {
        }

        public void Visit(VariableDeclaration node)
        {
            if (node != null)
            {
                m_names.Add(node.Name);
            }
        }

        public void Visit(UnaryOperator node)
        {
        }

        public void Visit(WhileNode node)
        {
        }

        public void Visit(WithNode node)
        {
        }

        #endregion

        private class Segment
        {
            public int DestinationLine { get; set; }
            public int DestinationColumn { get; set; }
            public int SourceLine { get; set; }
            public int SourceColumn { get; set; }

            public string FileName { get; set; }
            public string SymbolName { get; set; }
        }
    }
}
