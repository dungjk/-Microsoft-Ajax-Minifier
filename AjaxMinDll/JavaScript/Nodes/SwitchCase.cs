// switchcase.cs
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

using System.Collections.Generic;
using System.Text;

using Microsoft.Ajax.Utilities.JavaScript;
using Microsoft.Ajax.Utilities.JavaScript.Visitors;

namespace Microsoft.Ajax.Utilities.JavaScript.Nodes
{
    public sealed class SwitchCase : AstNode
    {
        private AstNode m_caseValue;
        public AstNode CaseValue
        {
            get { return m_caseValue; }
            set
            {
                if (value != m_caseValue)
                {
                    if (m_caseValue != null && m_caseValue.Parent == this)
                    {
                        m_caseValue.Parent = null;
                    }
                    m_caseValue = value;
                    if (value != null)
                    {
                        value.Parent = this;
                    }
                }
            }
        }

        private Block m_statements;
        public Block Statements
        {
            get { return m_statements; }
            set
            {
                if (value != m_statements)
                {
                    if (m_statements != null && m_statements.Parent == this)
                    {
                        m_statements.Parent = null;
                    }
                    m_statements = value;
                    if (value != null)
                    {
                        value.Parent = this;
                    }
                }
            }
        }

        internal bool IsDefault
        {
            get { return (CaseValue == null); }
        }

        public SwitchCase(Context context, JSParser parser, AstNode caseValue, Block statements)
            : base(context, parser)
        {
            CaseValue = caseValue;
            Statements = statements;
        }

        public override void Accept(IVisitor visitor)
        {
            if (visitor != null)
            {
                visitor.Visit(this);
            }
        }

        public override string GetFunctionNameGuess(AstNode target)
        {
            return CaseValue == null ? string.Empty : CaseValue.GetFunctionNameGuess(this);
        }

        public override IEnumerable<AstNode> Children
        {
            get
            {
                return EnumerateNonNullNodes(CaseValue, Statements);
            }
        }

        public override bool ReplaceChild(AstNode oldNode, AstNode newNode)
        {
            if (CaseValue == oldNode)
            {
                CaseValue = newNode;
                return true;
            }
            if (Statements == oldNode)
            {
                Statements = ForceToBlock(newNode);
                return true;
            }
            return false;
        }

        public override bool RequiresSeparator
        {
            get
            {
                // no statements doesn't require a separator.
                // otherwise only if statements require it
                if (Statements == null)
                {
                    return false;
                }

                // if there are no statements, then we don't require a separator.
                // otherwise ask the LAST statement if it needs one.
                return Statements.Count == 0
                    ? false
                    : Statements[Statements.Count - 1].RequiresSeparator;
            }
        }

        //public override string ToCode(ToCodeFormat format)
        //{
        //    StringBuilder sb = new StringBuilder();
        //    // the label should be indented
        //    Parser.Settings.Indent();
        //    // start a new line
        //    Parser.Settings.NewLine(sb);
        //    if (CaseValue != null)
        //    {
        //        sb.Append("case");

        //        string caseValue = CaseValue.ToCode();
        //        if (JSScanner.StartsWithIdentifierPart(caseValue))
        //        {
        //            sb.Append(' ');
        //        }
        //        sb.Append(caseValue);
        //    }
        //    else
        //    {
        //        sb.Append("default");
        //    }
        //    sb.Append(':');

        //    // in pretty-print mode, we indent the statements under the label, too
        //    Parser.Settings.Indent();

        //    // output the statements
        //    if (Statements != null && Statements.Count > 0)
        //    {
        //        sb.Append(Statements.ToCode(ToCodeFormat.NoBraces));
        //    }

        //    // if we are pretty-printing, we need to unindent twice:
        //    // once for the label, and again for the statements
        //    Parser.Settings.Unindent();
        //    Parser.Settings.Unindent();
        //    return sb.ToString();
        //}
    }
}