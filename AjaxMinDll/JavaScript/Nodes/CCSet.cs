// ccset.cs
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
    public class ConditionalCompilationSet : ConditionalCompilationStatement
    {
        private string m_variableName;
        public string VariableName
        {
            get { return m_variableName; }
            set { m_variableName = value; }
        }

        private AstNode m_value;
        public AstNode Value
        {
            get { return m_value; }
            set
            {
                if (value != m_value)
                {
                    if (m_value != null && m_value.Parent == this)
                    {
                        m_value.Parent = null;
                    }
                    m_value = value;
                    if (value != null)
                    {
                        value.Parent = this;
                    }
                }
            }
        }

        public ConditionalCompilationSet(Context context, JSParser parser, string variableName, AstNode value)
            : base(context, parser)
        {
            VariableName = variableName;
            Value = value;
        }

        public override IEnumerable<AstNode> Children
        {
            get
            {
                return EnumerateNonNullNodes(m_value);
            }
        }

        public override void Accept(IVisitor visitor)
        {
            if (visitor != null)
            {
                visitor.Visit(this);
            }
        }

        public override bool ReplaceChild(AstNode oldNode, AstNode newNode)
        {
            if (Value == oldNode)
            {
                Value = newNode;
                return true;
            }
            return false;
        }

        //public override string ToCode(ToCodeFormat format)
        //{
        //    StringBuilder sb = new StringBuilder();
        //    sb.Append("@set@");
        //    sb.Append(m_variableName);
        //    sb.Append('=');

        //    // if the value is an operator of any kind, we need to wrap it in parentheses
        //    // so it gets properly parsed
        //    if (m_value is BinaryOperator || m_value is UnaryOperator)
        //    {
        //        sb.Append('(');
        //        sb.Append(m_value.ToCode());
        //        sb.Append(')');
        //    }
        //    else if (m_value != null)
        //    {
        //        sb.Append(m_value.ToCode());
        //    }
        //    return sb.ToString();
        //}
    }
}
