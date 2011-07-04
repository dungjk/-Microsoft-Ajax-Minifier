// switch.cs
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
    public sealed class SwitchStatement : AstNode
    {
        private AstNode m_expression;
        public AstNode Expression
        {
            get { return m_expression; }
            set
            {
                if (value != m_expression)
                {
                    if (m_expression != null && m_expression.Parent == this)
                    {
                        m_expression.Parent = null;
                    }
                    m_expression = value;
                    if (value != null)
                    {
                        value.Parent = this;
                    }
                }
            }
        }

        private AstNodeList m_cases;
        public AstNodeList Cases
        {
            get { return m_cases; }
            set
            {
                if (value != m_cases)
                {
                    if (m_cases != null && m_cases.Parent == this)
                    {
                        m_cases.Parent = null;
                    }
                    m_cases = value;
                    if (value != null)
                    {
                        value.Parent = this;
                    }
                }
            }
        }

        public SwitchStatement(Context context, JSParser parser, AstNode expression, AstNodeList cases)
            : base(context, parser)
        {
            Expression = expression;
            Cases = cases;
        }

        public override void Accept(IVisitor visitor)
        {
            if (visitor != null)
            {
                visitor.Visit(this);
            }
        }

        public override bool RequiresSeparator
        {
            get
            {
                // switch always has curly-braces, so we don't
                // require the separator
                return false;
            }
        }

        public override IEnumerable<AstNode> Children
        {
            get
            {
                return EnumerateNonNullNodes(Expression, Cases);
            }
        }

        public override bool ReplaceChild(AstNode oldNode, AstNode newNode)
        {
            if (Expression == oldNode)
            {
                Expression = newNode;
                return true;
            }
            if (Cases == oldNode)
            {
                Cases = newNode as AstNodeList;
                return true;
            }
            return false;
        }

        //public override string ToCode(ToCodeFormat format)
        //{
        //    StringBuilder sb = new StringBuilder();
        //    // switch and value
        //    sb.Append("switch(");
        //    sb.Append(Expression.ToCode());
        //    sb.Append(')');

        //    // opening brace
        //    Parser.Settings.NewLine(sb);
        //    sb.Append('{');

        //    // cases
        //    sb.Append(Cases.ToCode(ToCodeFormat.Semicolons));

        //    // closing brace
        //    Parser.Settings.NewLine(sb);
        //    sb.Append('}');
        //    return sb.ToString();
        //}
    }
}
