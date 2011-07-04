// for.cs
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
using System.Text;

using Microsoft.Ajax.Utilities.JavaScript;
using Microsoft.Ajax.Utilities.JavaScript.Visitors;

namespace Microsoft.Ajax.Utilities.JavaScript.Nodes
{

    public sealed class ForStatement : AstNode
    {
        private AstNode m_initializer;
        public AstNode Initializer 
        {
            get { return m_initializer; }
            set
            {
                if (value != m_initializer)
                {
                    if (m_initializer != null && m_initializer.Parent == this)
                    {
                        m_initializer.Parent = null;
                    }
                    m_initializer = value;
                    if (value != null)
                    {
                        value.Parent = this;
                    }
                }
            }
        }

        private AstNode m_condition;
        public AstNode Condition
        {
            get { return m_condition; }
            set
            {
                if (value != m_condition)
                {
                    if (m_condition != null && m_condition.Parent == this)
                    {
                        m_condition.Parent = null;
                    }
                    m_condition = value;
                    if (value != null)
                    {
                        value.Parent = this;
                    }
                }
            }
        }

        private AstNode m_incrementer;
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Naming", "CA1704:IdentifiersShouldBeSpelledCorrectly", MessageId = "Incrementer")]
        public AstNode Incrementer
        {
            get { return m_incrementer; }
            set
            {
                if (value != m_incrementer)
                {
                    if (m_incrementer != null && m_incrementer.Parent == this)
                    {
                        m_incrementer.Parent = null;
                    }
                    m_incrementer = value;
                    if (value != null)
                    {
                        value.Parent = this;
                    }
                }
            }
        }

        private Block m_body;
        public Block Body
        {
            get { return m_body; }
            set
            {
                if (value != m_body)
                {
                    if (m_body != null && m_body.Parent == this)
                    {
                        m_body.Parent = null;
                    }
                    m_body = value;
                    if (value != null)
                    {
                        value.Parent = this;
                    }
                }
            }
        }

        public ForStatement(Context context, JSParser parser, AstNode initializer, AstNode condition, AstNode increment, AstNode body)
            : base(context, parser)
        {
            Initializer = initializer;
            Condition = condition;
            Incrementer = increment;
            Body = ForceToBlock(body);
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
                // requires a separator if the body does
                return Body == null ? true : Body.RequiresSeparator;
            }
        }

        internal override bool EndsWithEmptyBlock
        {
            get
            {
                return Body == null ? true : Body.EndsWithEmptyBlock;
            }
        }

        public override IEnumerable<AstNode> Children
        {
            get
            {
                return EnumerateNonNullNodes(Initializer, Condition, Incrementer, Body);
            }
        }

        public override bool ReplaceChild(AstNode oldNode, AstNode newNode)
        {
            if (Initializer == oldNode)
            {
                Initializer = newNode;
                return true;
            }
            if (Condition == oldNode)
            {
                Condition = newNode;
                return true;
            }
            if (Incrementer == oldNode)
            {
                Incrementer = newNode;
                return true;
            }
            if (Body == oldNode)
            {
                Body = ForceToBlock(newNode);
                return true;
            }
            return false;
        }

        //public override string ToCode(ToCodeFormat format)
        //{
        //    StringBuilder sb = new StringBuilder();
        //    sb.Append("for(");
        //    if (Initializer != null)
        //    {
        //        sb.Append(Initializer.ToCode());
        //    }
        //    sb.Append(';');
        //    CodeSettings codeSettings = Parser.Settings;
        //    if (codeSettings.OutputMode == OutputMode.MultipleLines && codeSettings.IndentSize > 0)
        //    {
        //        sb.Append(' ');
        //    }
        //    if (Condition != null)
        //    {
        //        sb.Append(Condition.ToCode());
        //    }
        //    sb.Append(';');
        //    if (codeSettings.OutputMode == OutputMode.MultipleLines && codeSettings.IndentSize > 0)
        //    {
        //        sb.Append(' ');
        //    }
        //    if (Incrementer != null)
        //    {
        //        sb.Append(Incrementer.ToCode());
        //    }
        //    sb.Append(')');
        //    string bodyString = (
        //      Body == null
        //      ? string.Empty
        //      : Body.ToCode()
        //      );
        //    sb.Append(bodyString);
        //    return sb.ToString();
        //}
    }
}
