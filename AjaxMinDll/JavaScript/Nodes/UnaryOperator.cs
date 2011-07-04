// unaryop.cs
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

using Microsoft.Ajax.Utilities.JavaScript;
using Microsoft.Ajax.Utilities.JavaScript.Visitors;

namespace Microsoft.Ajax.Utilities.JavaScript.Nodes
{
    public abstract class UnaryOperator : Expression
    {
        private AstNode m_operand;
        public AstNode Operand
        {
            get { return m_operand; }
            set
            {
                if (value != m_operand)
                {
                    if (m_operand != null && m_operand.Parent == this)
                    {
                        m_operand.Parent = null;
                    }
                    m_operand = value;
                    if (value != null)
                    {
                        value.Parent = this;
                    }
                }
            }
        }

        public JSToken OperatorToken { get; private set; }

        public override OperatorPrecedence OperatorPrecedence
        {
            get
            {
                // unary precedence
                return OperatorPrecedence.Unary;
            }
        }

        protected UnaryOperator(Context context, JSParser parser, AstNode operand, JSToken operatorToken)
            : base(context, parser)
        {
            Operand = operand;
            OperatorToken = operatorToken;
        }

        public override string GetFunctionNameGuess(AstNode target)
        {
            return Operand == null || Operand == target 
                ? Parent.GetFunctionNameGuess(this) 
                : Operand.GetFunctionNameGuess(this);
        }

        public override IEnumerable<AstNode> Children
        {
            get
            {
                return EnumerateNonNullNodes(Operand);
            }
        }

        public override bool ReplaceChild(AstNode oldNode, AstNode newNode)
        {
            if (Operand == oldNode)
            {
                Operand = newNode;
                return true;
            }
            return false;
        }

        protected bool NeedsParentheses
        {
            // binary and conditional operators are all lesser-precedence than unaries
            get
            {
                // we only need parens if the operand is a binary op or a conditional op
                return (Operand is BinaryOperator || Operand is Conditional);
            }
        }
    }
}
