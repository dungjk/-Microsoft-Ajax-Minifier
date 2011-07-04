// dowhile.cs
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

    public sealed class DoWhileStatement : AstNode
    {
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

        public DoWhileStatement(Context context, JSParser parser, AstNode body, AstNode condition)
            : base(context, parser)
        {
            Body = ForceToBlock(body);
            Condition = condition;
        }

        public override bool RequiresSeparator
        {
            get
            {
                // do-while is weird -- it doesn't require a statement separator
                // after the condition clause. If you do put it in and the do-while statement
                // if the only statement in a do-while or a if-statement's true-block
                // when there's an else-block, you'll get script errors because the
                // semicolon will be read as the terminator for the if-statement or the outer
                // do-statmenet. The if-statement will then not have an else-clause, and will
                // script error on the else being unassociated with any if. The do-statement
                // will error because you didn't include a while. Weird stuff.
                return false;
            }
        }

        public override void Accept(IVisitor visitor)
        {
            if (visitor != null)
            {
                visitor.Visit(this);
            }
        }

        public override IEnumerable<AstNode> Children
        {
            get
            {
                return EnumerateNonNullNodes(Body, Condition);
            }
        }

        public override bool ReplaceChild(AstNode oldNode, AstNode newNode)
        {
            if (Body == oldNode)
            {
                Body = ForceToBlock(newNode);
                return true;
            }
            if (Condition == oldNode)
            {
                Condition = newNode;
                return true;
            }
            return false;
        }
    }
}
