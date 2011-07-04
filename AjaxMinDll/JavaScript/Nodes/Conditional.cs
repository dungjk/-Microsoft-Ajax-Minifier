// conditional.cs
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

    public sealed class Conditional : Expression
    {
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

        private AstNode m_trueExpression;
        public AstNode TrueExpression 
        {
            get { return m_trueExpression; }
            set
            {
                if (value != m_trueExpression)
                {
                    if (m_trueExpression != null && m_trueExpression.Parent == this)
                    {
                        m_trueExpression.Parent = null;
                    }
                    m_trueExpression = value;
                    if (value != null)
                    {
                        value.Parent = this;
                    }
                }
            }
        }

        private AstNode m_falseExpression;
        public AstNode FalseExpression 
        {
            get { return m_falseExpression; }
            set
            {
                if (value != m_falseExpression)
                {
                    if (m_falseExpression != null && m_falseExpression.Parent == this)
                    {
                        m_falseExpression.Parent = null;
                    }
                    m_falseExpression = value;
                    if (value != null)
                    {
                        value.Parent = this;
                    }
                }
            }
        }

        public override OperatorPrecedence OperatorPrecedence
        {
            get
            {
                // we have our own operator precedence
                return OperatorPrecedence.Conditional;
            }
        }

        public Conditional(Context context, JSParser parser, AstNode condition, AstNode trueExpression, AstNode falseExpression)
            : base(context, parser)
        {
            Condition = condition;
            TrueExpression = trueExpression;
            FalseExpression = falseExpression;
        }

        public void SwapBranches()
        {
            AstNode temp = m_trueExpression;
            m_trueExpression = m_falseExpression;
            m_falseExpression = temp;
        }

        public override PrimitiveType FindPrimitiveType()
        {
            if (TrueExpression != null && FalseExpression != null)
            {
                // if the primitive type of both true and false expressions is the same, then
                // we know the primitive type. Otherwise we do not.
                PrimitiveType trueType = TrueExpression.FindPrimitiveType();
                if (trueType == FalseExpression.FindPrimitiveType())
                {
                    return trueType;
                }
            }

            // nope -- they don't match, so we don't know
            return PrimitiveType.Other;
        }

        public override bool IsEquivalentTo(AstNode otherNode)
        {
            var otherConditional = otherNode as Conditional;
            return otherConditional != null
                && Condition.IsEquivalentTo(otherConditional.Condition)
                && TrueExpression.IsEquivalentTo(otherConditional.TrueExpression)
                && FalseExpression.IsEquivalentTo(otherConditional.FalseExpression);
        }

        public override IEnumerable<AstNode> Children
        {
            get
            {
                return EnumerateNonNullNodes(Condition, TrueExpression, FalseExpression);
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
            if (Condition == oldNode)
            {
                Condition = newNode;
                return true;
            }
            if (TrueExpression == oldNode)
            {
                TrueExpression = newNode;
                return true;
            }
            if (FalseExpression == oldNode)
            {
                FalseExpression = newNode;
                return true;
            }
            return false;
        }

        public override AstNode LeftHandSide
        {
            get
            {
                // the condition is on the left
                return Condition.LeftHandSide;
            }
        }

        //private static bool NeedsParens(AstNode node, JSToken refToken)
        //{
        //    bool needsParens = false;

        //    // assignments and commas are the only operators that need parens
        //    // around them. Conditional is pretty low down the list
        //    BinaryOperator binaryOp = node as BinaryOperator;
        //    if (binaryOp != null)
        //    {
        //        OperatorPrecedence thisPrecedence = JSScanner.GetOperatorPrecedence(refToken);
        //        OperatorPrecedence nodePrecedence = JSScanner.GetOperatorPrecedence(binaryOp.OperatorToken);
        //        needsParens = (nodePrecedence < thisPrecedence);
        //    }

        //    return needsParens;
        //}

        //public override string ToCode(ToCodeFormat format)
        //{
        //    StringBuilder sb = new StringBuilder();
        //    bool parens = NeedsParens(Condition, JSToken.ConditionalIf);
        //    if (parens)
        //    {
        //        sb.Append('(');
        //    }

        //    sb.Append(Condition.ToCode());
        //    if (parens)
        //    {
        //        sb.Append(')');
        //    }

        //    CodeSettings codeSettings = Parser.Settings;
        //    if (codeSettings.OutputMode == OutputMode.MultipleLines && codeSettings.IndentSize > 0)
        //    {
        //        sb.Append(" ? ");
        //    }
        //    else
        //    {
        //        sb.Append('?');
        //    }

        //    // the true and false operands are parsed as assignment operators, so use that token as the
        //    // reference token to compare against for operator precedence to determine if we need parens
        //    parens = NeedsParens(TrueExpression, JSToken.Assign);
        //    if (parens)
        //    {
        //        sb.Append('(');
        //    }

        //    sb.Append(TrueExpression.ToCode());
        //    if (parens)
        //    {
        //        sb.Append(')');
        //    }

        //    if (codeSettings.OutputMode == OutputMode.MultipleLines && codeSettings.IndentSize > 0)
        //    {
        //        sb.Append(" : ");
        //    }
        //    else
        //    {
        //        sb.Append(':');
        //    }

        //    parens = NeedsParens(FalseExpression, JSToken.Assign);
        //    if (parens)
        //    {
        //        sb.Append('(');
        //    }

        //    sb.Append(FalseExpression.ToCode());
        //    if (parens)
        //    {
        //        sb.Append(')');
        //    }
        //    return sb.ToString();
        //}
    }
}